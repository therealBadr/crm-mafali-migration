"""
MAFALI CRM — Historique loader: CSV row data + on-disk attachment files -> Postgres

Loads the full Historique export into the live `historique` table,
replacing whatever's currently there. Safe to re-run: TRUNCATEs the table
before reloading, same pattern as migration/migrate.py's load_table.

Two inputs, matched by filename:
  - CSV_PATH: the row data. 17 columns — the 16 real historique fields
    plus `attachment_file`, which holds a filename like "351037.xlsx"
    when that row has an attachment, empty otherwise. Verified clean
    directly (every one of its 1,083,170 rows has exactly 17 fields; only
    32 have a blank id_histo, a known/expected legacy garbage-row
    category, same as the empty Nom_Franchise row skipped during the
    original migration).
  - ATTACHMENTS_DIR: the folder of actual attachment files, named exactly
    `{id_histo}.{ext}` (e.g. "351037.xlsx"). Cross-checked directly
    against the CSV before writing this script: all 43,355 files on disk
    have a matching CSV row, all 43,355 CSV rows with a non-empty
    attachment_file have a matching file on disk, zero extension
    mismatches. Points at CRM_Export/Attachments now that the folder's on
    this machine. If ATTACHMENTS_DIR is ever empty or missing on a future
    run, this script doesn't crash — every attachment_file lookup just
    misses, gets counted, and prints a one-line summary at the end;
    nothing silently drops data.

Dependency: historique.num_client is a foreign key to
france_optique.cle_opl (NOT VALID only skips checking pre-existing rows
at the moment the constraint was added — every new INSERT, including
this script's, is still checked against it in real time). This load
will fail with a foreign-key error on any row whose client isn't already
in france_optique. Run this AFTER france_optique holds the real full
client data — not the current 489-row test subset — or most inserts
will fail.

Usage:
    python3 load_historique.py
"""

import csv
import datetime
import gc
import os
import resource
import sys

import psycopg2
import psycopg2.extras

from csv_common import make_clean_limited

# ---- Config ----
EXPORT_DIR = "/media/godspeed/DROP BOX/CRM_Export"
CSV_PATH = f"{EXPORT_DIR}/historique_export.csv"
ATTACHMENTS_DIR = f"{EXPORT_DIR}/Attachments"
DSN = "dbname=mafali_crm_db user=godspeed host=/var/run/postgresql"
BATCH_SIZE = 1000  # smaller batches = more frequent checkpoint writes = less redone work per freeze
# Real root cause of the repeated OOM kills (confirmed via dmesg + RSS
# instrumentation): row-count alone doesn't bound a batch's memory — real
# data has several 90MB+ PDF attachments landing in the same 1000-row
# window, and psycopg2 has to hex-encode the whole batch into one SQL
# statement (~2x the raw bytes) before sending it. A batch now also
# flushes early once its attachments alone cross this, regardless of row
# count, so no single execute_values call ever has to hold hundreds of
# MB of binary data at once.
MAX_BATCH_ATTACHMENT_BYTES = 20 * 1024 * 1024
# Lives next to this script (survives /tmp scratchpad resets, unlike a
# checkpoint stored under /tmp) — see the resume-support comment in main().
RESUME_CHECKPOINT_FILE = os.path.join(os.path.dirname(os.path.abspath(__file__)), ".historique_load_checkpoint")

# CSV columns, in file order, mapped to target historique columns.
# attachment_file is handled separately (see load_attachment below), not
# inserted directly — it drives fic_stk/fic_stk_nom instead.
CSV_COLUMNS = [
    'num_client', 'date_saisie', 'heure_saisie', 'assistante_commerciale',
    'date_rappel', 'heure_rappel', 'opération', 'status_vente', 'note',
    'franchise', 'raison_sociale', 'cp', 'ville', 'statuts_clients',
    'magasin_principal', 'id_histo', 'attachment_file',
]

INSERT_COLUMNS = [
    'num_client', 'date_saisie', 'heure_saisie', 'assistante_commercial',
    'date_rappel', 'heure_rappel', 'operation', 'status_vente', 'note',
    'franchise', 'raison_sociale', 'cp', 'ville', 'statuts_clients',
    'magasin_principal', 'id_histo', 'fic_stk', 'fic_stk_nom',
]

# A small number of otherwise-real rows (confirmed by inspection — real
# client numbers, dates, notes — everything except one corrupted field)
# have a single garbled value overflowing its column's VARCHAR limit by a
# few characters. Truncated with a warning rather than dropping the whole
# row or widening the schema for what's confirmed to be corrupted noise,
# not real long-form data (contrast with tel_bis on france_optique, which
# needed widening because the overflow there was genuine data).
clean_assistante = make_clean_limited(50, "assistante_commercial")
clean_operation = make_clean_limited(50, "operation")
clean_status_vente = make_clean_limited(50, "status_vente")
clean_franchise = make_clean_limited(50, "franchise")
clean_raison_sociale = make_clean_limited(60, "raison_sociale")
clean_cp = make_clean_limited(5, "cp")
clean_ville = make_clean_limited(40, "ville")
clean_statuts_clients = make_clean_limited(50, "statuts_clients")
clean_magasin_principal = make_clean_limited(50, "magasin_principal")


def clean(v):
    v = v.strip() if isinstance(v, str) else v
    return v if v else None


def parse_date(v):
    v = clean(v)
    if v is None:
        return None
    try:
        return datetime.date.fromisoformat(v)
    except ValueError:
        print(f"  WARNING: unparseable date {v!r}, storing as NULL")
        return None


def parse_time(v):
    v = clean(v)
    if v is None:
        return None
    try:
        return datetime.time.fromisoformat(v)
    except ValueError:
        print(f"  WARNING: unparseable time {v!r}, storing as NULL")
        return None


def parse_int(v):
    v = clean(v)
    return int(v) if v is not None else None


class AttachmentStats:
    def __init__(self):
        self.attached = 0
        self.missing_file = 0
        self.no_dir_configured = 0


def load_attachment(attachment_file, stats):
    """Returns (fic_stk_bytes_or_None, fic_stk_nom_or_None). Never raises —
    a missing file or unconfigured folder just means this row's attachment
    comes through as NULL, counted in stats and reported at the end,
    rather than crashing a 1M+ row load over one file."""
    attachment_file = clean(attachment_file)
    if attachment_file is None:
        return None, None
    if not ATTACHMENTS_DIR:
        stats.no_dir_configured += 1
        return None, None
    path = os.path.join(ATTACHMENTS_DIR, attachment_file)
    if not os.path.exists(path):
        stats.missing_file += 1
        return None, None
    with open(path, "rb") as f:
        data = f.read()
    stats.attached += 1
    return data, attachment_file


def main():
    if not os.path.exists(CSV_PATH):
        raise SystemExit(f"CSV not found: {CSV_PATH}")

    if not ATTACHMENTS_DIR:
        print("NOTE: ATTACHMENTS_DIR is empty — every attachment will be "
              "skipped (fic_stk left NULL) this run. Fill in the path once "
              "the 53GB folder is on this machine and re-run; TRUNCATE + "
              "reload means a second run picks up every attachment cleanly.")
    elif not os.path.isdir(ATTACHMENTS_DIR):
        raise SystemExit(f"ATTACHMENTS_DIR is set but not a real directory: {ATTACHMENTS_DIR}")

    conn = psycopg2.connect(DSN)
    cur = conn.cursor()

    # Resume support: this step alone reads 54GB of attachment files, and
    # several interruptions in a row — first assumed to be the host
    # machine losing power, later confirmed via dmesg/journalctl to
    # actually be the OOM killer (see flush_batch's periodic-reconnect
    # comment for the real fix) — each cost a full restart from row 0
    # before this existed. A simple id_histo checkpoint, written after
    # every committed batch,
    # means an interruption only loses at most one batch's worth of work
    # instead of everything. Only skips truncating/re-reading when a
    # checkpoint from a genuinely previous, incomplete run exists —
    # deleted automatically on a clean finish, so a fresh intentional
    # re-run (no checkpoint file present) truncates and starts over
    # exactly like every other loader in this migration.
    resume_from = None
    if os.path.exists(RESUME_CHECKPOINT_FILE):
        with open(RESUME_CHECKPOINT_FILE) as f:
            resume_from = int(f.read().strip())
        print(f"Resuming from checkpoint: rows with id_histo <= {resume_from} "
              f"already loaded, skipping them.")
    else:
        print("Truncating historique before reload...")
        cur.execute("TRUNCATE TABLE historique RESTART IDENTITY")
        conn.commit()

    # OVERRIDING SYSTEM VALUE is required: id_histo is GENERATED ALWAYS AS
    # IDENTITY, which rejects an explicit value in a plain INSERT otherwise
    # — same requirement migrate.py's load_table already handles for the
    # other identity columns it preserves (Pays.IDPays, CA.IDCA, etc.).
    insert_sql = (
        f"INSERT INTO historique ({', '.join(INSERT_COLUMNS)}) "
        f"OVERRIDING SYSTEM VALUE VALUES %s"
    )

    stats = AttachmentStats()
    skipped_blank_id = 0
    total_loaded = 0
    batches_flushed = 0
    batch = []
    batch_bytes = 0
    conn_holder = [conn, cur]  # mutable cell so flush_batch can swap the connection

    def flush_batch():
        nonlocal batch, batch_bytes, total_loaded, batches_flushed
        if not batch:
            return
        conn, cur = conn_holder
        try:
            psycopg2.extras.execute_values(cur, insert_sql, batch, page_size=BATCH_SIZE)
            conn.commit()
        except psycopg2.errors.ForeignKeyViolation:
            conn.rollback()
            raise SystemExit(
                "\nForeign-key violation inserting into historique — this "
                "batch references a num_client that doesn't exist yet in "
                "france_optique. This script must run AFTER france_optique "
                "holds the real full client data, not the current test "
                "subset. Load france_optique first, then re-run this script."
            )
        total_loaded += len(batch)
        id_index_local = INSERT_COLUMNS.index('id_histo')
        last_id_in_batch = max(row[id_index_local] for row in batch)
        with open(RESUME_CHECKPOINT_FILE, "w") as f:
            f.write(str(last_id_in_batch))
        print(f"  loaded {total_loaded:,} rows so far...", flush=True)
        batch.clear()
        batch_bytes = 0
        batches_flushed += 1

        # Belt-and-suspenders memory hygiene, kept even though RSS
        # instrumentation later found the real, much bigger cause (see
        # MAX_BATCH_ATTACHMENT_BYTES above: a couple of 90MB+ PDFs landing
        # in the same 1000-row batch, not gradual connection-level growth
        # — RSS was confirmed completely flat for 690k+ rows right up
        # until the batch containing those files). Periodically recycling
        # the connection plus an explicit gc.collect() is still cheap
        # insurance against slower, smaller growth over the full
        # ~1M-row run.
        if batches_flushed % 20 == 0:
            cur.close()
            conn.close()
            gc.collect()
            new_conn = psycopg2.connect(DSN)
            new_cur = new_conn.cursor()
            conn_holder[0] = new_conn
            conn_holder[1] = new_cur

    # Quick pre-count for the progress percentage below. Counts logical
    # CSV rows via csv.reader, not raw physical lines — note/status
    # fields routinely contain real embedded newlines inside quoted
    # values (confirmed in real rows, e.g. multi-line opening-hours
    # notes), so a plain line count would overcount versus what the
    # actual load loop iterates. Still cheap relative to the real load —
    # no attachment files touched here, just a read-through.
    print("Counting total rows for progress reporting...")
    with open(CSV_PATH, "r", encoding="utf-8-sig", newline="") as f:
        precount_reader = csv.reader(f)
        next(precount_reader)  # header
        total_data_rows = sum(1 for _ in precount_reader)
    print(f"  {total_data_rows:,} total data rows in {os.path.basename(CSV_PATH)}")

    rows_read = 0
    progress_report_every = 2000

    with open(CSV_PATH, "r", encoding="utf-8-sig", newline="") as f:
        reader = csv.reader(f)
        header = next(reader)
        if header != CSV_COLUMNS:
            raise SystemExit(
                f"Unexpected CSV header.\nExpected: {CSV_COLUMNS}\nGot:      {header}"
            )

        for row in reader:
            rows_read += 1
            if rows_read % progress_report_every == 0:
                pct = rows_read / total_data_rows * 100
                # RSS instrumentation: two OOM kills so far at ~4.1GB with
                # no confirmed cause yet (ruled out: a single runaway CSV
                # field, connection-level accumulation). Printing memory
                # alongside progress turns the next crash into a real data
                # point — gradual climb across many print lines means a
                # leak somewhere in the loop; a sudden jump between two
                # consecutive lines means one bad row/allocation, and
                # rows_read at that point identifies exactly which one.
                rss_mb = resource.getrusage(resource.RUSAGE_SELF).ru_maxrss / 1024
                print(f"  progress: {pct:.1f}% ({rows_read:,}/{total_data_rows:,} rows read) "
                      f"[RSS: {rss_mb:.0f} MB]", flush=True)

            values = dict(zip(CSV_COLUMNS, row))

            id_histo = parse_int(values['id_histo'])
            # Blank/0/negative/absurdly-large id_histo are all the same
            # class of garbage, confirmed by inspection: real id_histo
            # tops out around 1.1M (matches the real row count), so
            # anything <= 0 or wildly beyond that (some corrupted rows
            # land on huge nonsense numbers like 282583061496065, others
            # on huge negative numbers, others on exactly 0) is not a real
            # row — same corrupted-binary-content signature every time
            # (confirmed directly: these rows also have NULL/garbled
            # num_client, dates, etc., not just a bad id). 2,000,000 is a
            # generous upper bound with headroom above the real max, not a
            # tight cutoff that risks excluding real data.
            if id_histo is None or id_histo <= 0 or id_histo > 2_000_000:
                skipped_blank_id += 1
                continue

            # Resume: skip rows already loaded in a prior, interrupted run
            # — cheaply, before touching num_client validation or (much
            # more importantly) reading any attachment file from disk.
            if resume_from is not None and id_histo <= resume_from:
                continue

            # num_client is NOT NULL — a historique row only means
            # anything if it's attached to a real client. 2 rows in the
            # real data have a plausible id_histo but a blank num_client
            # (checked directly: both are otherwise mostly corrupted
            # garbled content too, just with a couple of stray readable
            # fragments) — not fabricating a fake client to attach them to.
            num_client = parse_int(values['num_client'])
            if num_client is None:
                skipped_blank_id += 1
                print(f"  WARNING: skipping id_histo={id_histo} — blank num_client, "
                      f"can't be attached to any client.")
                continue

            fic_stk, fic_stk_nom = load_attachment(values['attachment_file'], stats)
            if fic_stk is not None:
                batch_bytes += len(fic_stk)

            batch.append((
                num_client,
                parse_date(values['date_saisie']),
                parse_time(values['heure_saisie']),
                clean_assistante(values['assistante_commerciale']),
                parse_date(values['date_rappel']),
                parse_time(values['heure_rappel']),
                clean_operation(values['opération']),
                clean_status_vente(values['status_vente']),
                clean(values['note']),
                clean_franchise(values['franchise']),
                clean_raison_sociale(values['raison_sociale']),
                clean_cp(values['cp']),
                clean_ville(values['ville']),
                clean_statuts_clients(values['statuts_clients']),
                clean_magasin_principal(values['magasin_principal']),
                id_histo,
                psycopg2.Binary(fic_stk) if fic_stk is not None else None,
                fic_stk_nom,
            ))

            # Flush on whichever limit hits first — row count (checkpoint
            # granularity) or accumulated attachment bytes (the actual
            # OOM cause: real data has several 90MB+ PDFs dense enough to
            # land in the same 1000-row window).
            if len(batch) >= BATCH_SIZE or batch_bytes >= MAX_BATCH_ATTACHMENT_BYTES:
                flush_batch()

        flush_batch()

    print(f"  progress: 100.0% ({rows_read:,}/{total_data_rows:,} rows read)", flush=True)

    # Use whatever connection is current — flush_batch periodically
    # swaps it out (see the OOM-mitigation comment there), so the
    # original conn/cur from the top of main() may already be closed.
    conn, cur = conn_holder
    cur.execute("SELECT setval('historique_id_histo_seq', COALESCE((SELECT MAX(id_histo) FROM historique), 1))")
    conn.commit()

    # Real final count, not just this run's total_loaded — a resumed run
    # only counts what IT inserted, not rows a prior interrupted run
    # already committed before dying.
    cur.execute("SELECT count(*) FROM historique")
    grand_total = cur.fetchone()[0]

    cur.close()
    conn.close()

    # Completed cleanly end-to-end — remove the checkpoint so the next
    # invocation is a genuine fresh run (truncate + reload), not an
    # accidental resume from a completed load.
    if os.path.exists(RESUME_CHECKPOINT_FILE):
        os.remove(RESUME_CHECKPOINT_FILE)

    print(f"\nDone. {grand_total:,} total rows in historique "
          f"({total_loaded:,} loaded this run, {skipped_blank_id} skipped "
          f"this run for blank/bad id_histo or num_client).")
    print(f"Attachments this run: {stats.attached:,} attached, "
          f"{stats.missing_file:,} referenced in CSV but not found in "
          f"ATTACHMENTS_DIR, {stats.no_dir_configured:,} skipped because "
          f"ATTACHMENTS_DIR wasn't configured for this run.")


if __name__ == "__main__":
    main()
