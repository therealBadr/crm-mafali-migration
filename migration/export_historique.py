"""
MAFALI CRM — Historique export to CSV
HFSQL (via ODBC/pypyodbc) -> single CSV file

Usage:
    pip install pypyodbc
    python export_historique.py

Resumable: tracks the last exported id_histo in a checkpoint file, so
a crash/restart appends from where it left off instead of re-writing
or skipping rows. Safe to re-run.

Pagination note: uses keyset pagination (WHERE id_histo > last_id
ORDER BY id_histo LIMIT N), not OFFSET. That's deliberate — OFFSET
pagination can silently skip or duplicate rows on a live table if
rows are inserted/deleted mid-export. Keyset pagination doesn't care
that id_histo has gaps or a large sentinel-looking value in it — it
only needs id_histo to be unique and sortable, which it is.

ficstk (file attachments) is NOT part of the main CAST(...AS VARCHAR)
column set below — that was tried first and produced a corrupted CSV.
ficstk is binary data (a stored file), and CASTing binary to VARCHAR
just reinterprets the raw bytes as characters rather than encoding
them safely. Any byte in a stored file that happens to match a comma,
quote, or newline breaks the CSV's row/column structure right there,
which is exactly what happened on the first run (rows came back with
9, 1, 17, 3... columns instead of a consistent 18). Fixed by fetching
ficstk in its own isolated query per batch (see fetch_ficstk_map) and
base64-encoding it in Python before it ever reaches the CSV — base64
output is plain ASCII (letters/digits/+//=) and can never collide with
a CSV delimiter, and it's fully reversible back to the original file
bytes at load time.
"""

import base64
import pypyodbc as odbc
import csv
import json
import os
import sys
import time
import datetime

# stdout is block-buffered (not line-buffered) when it's not a TTY — e.g.
# redirected to a file/pipe when run in the background. Without this,
# progress prints below (compute_cast_sizes scan, per-batch export counts)
# don't actually reach the file until the process exits, so a long-running
# export looks silent/stuck even while it's working.
sys.stdout.reconfigure(line_buffering=True)

# pypyodbc hard-codes ascii-decoding for DATE/TIME/TIMESTAMP columns before
# parsing them as text. Some rows in this legacy table have malformed or
# non-ascii bytes in a date/time field, which crashes the default converter.
# These replacements decode more forgivingly and return None (instead of
# crashing) for any value that still doesn't parse as a real date/time —
# those rows get exported with a blank date field rather than losing the
# whole run. _malformed_datetime_count tracks how often this happens so you
# know how much of the data has an issue worth investigating separately.
_malformed_datetime_count = 0


def _safe_decode_buf(x):
    if isinstance(x, bytes):
        try:
            return x.decode('ascii')
        except UnicodeDecodeError:
            return x.decode('cp1252', errors='replace')
    return x


def _safe_dt_cvt(x):
    global _malformed_datetime_count
    x = _safe_decode_buf(x)
    if not x or not x.strip():
        return None
    x = x.strip()
    try:
        # HFSQL returns DATE columns as compact 'YYYYMMDD' (no separators),
        # e.g. '20160602' for 2016-06-02 — not ISO 'YYYY-MM-DD'. Confirmed
        # by querying the raw driver value directly (see diagnostic run).
        return datetime.date(int(x[0:4]), int(x[4:6]), int(x[6:8]))
    except (ValueError, IndexError):
        _malformed_datetime_count += 1
        return None


def _safe_tm_cvt(x):
    global _malformed_datetime_count
    x = _safe_decode_buf(x)
    if not x or not x.strip():
        return None
    try:
        frac = (x[9:] or '0').ljust(6, '0')[:6]
        return datetime.time(int(x[0:2]), int(x[3:5]), int(x[6:8]), int(frac))
    except (ValueError, IndexError):
        _malformed_datetime_count += 1
        return None


def _safe_dttm_cvt(x):
    # Not exercised by this export — Historique has no TIMESTAMP-typed
    # column (confirmed via cursor.description: date_saisie/date_rappel
    # are DATE, heure_saisie/heure_rappel are TIME). Kept registered in
    # case a future column needs it, but its date-portion format below is
    # UNVERIFIED and likely wrong the same way _safe_dt_cvt's was (that one
    # assumed ISO 'YYYY-MM-DD' when HFSQL actually returns 'YYYYMMDD') —
    # confirm against a real raw sample before relying on this.
    global _malformed_datetime_count
    x = _safe_decode_buf(x)
    if not x or not x.strip():
        return None
    try:
        frac = (x[20:] or '0').ljust(6, '0')[:6]
        return datetime.datetime(int(x[0:4]), int(x[5:7]), int(x[8:10]),
                                  int(x[10:13]), int(x[14:16]), int(x[17:19]),
                                  int(frac))
    except (ValueError, IndexError):
        _malformed_datetime_count += 1
        return None


def _b64_encode_binary(value):
    """ficstk comes back as raw bytes (or bytearray) from the driver now
    that it's no longer CAST to VARCHAR — base64-encode it into plain
    ASCII text that's 100% safe inside a CSV cell (only letters, digits,
    '+', '/', '=' — none of which can ever be mistaken for a delimiter).
    None (no attachment on that row) becomes an empty string, same
    convention as every other blank field in this export."""
    if value is None:
        return ""
    if isinstance(value, (bytes, bytearray)):
        return base64.b64encode(bytes(value)).decode("ascii")
    if isinstance(value, str):
        # Unexpected for a binary column, but handle it defensively rather
        # than crash: assume the driver decoded it one-byte-per-character
        # (e.g. latin1), so re-encoding via latin1 recovers the original
        # bytes before base64'ing them.
        try:
            return base64.b64encode(value.encode("latin1")).decode("ascii")
        except UnicodeEncodeError:
            return base64.b64encode(value.encode("utf-8", errors="replace")).decode("ascii")
    return ""

# ---- Config ----
HFSQL_DSN = "DSN=MafaliHFSQL"
OUTPUT_CSV = "historique_export.csv"
CHECKPOINT_FILE = "historique_export_checkpoint.json"
BATCH_SIZE = 5000

# Set TEST_MODE = True and re-run to validate the ficstk base64 change on a
# small slice before committing to another full 1M+ row run. Uses separate
# output/checkpoint filenames so it can never collide with or resume from
# a real run's progress.
TEST_MODE = False
TEST_MODE_LIMIT = 500

if TEST_MODE:
    OUTPUT_CSV = "historique_export_test.csv"
    CHECKPOINT_FILE = "historique_export_checkpoint_test.json"

# HFSQL/legacy Windows data is very often cp1252 (Windows-1252/ANSI),
# not UTF-8. If accented characters look mangled when you open the
# CSV (e.g. "Ã©" instead of "é"), this is why. Check the first batch
# before trusting the rest of the run — flip FIX_ENCODING off if your
# data already comes through clean.
SOURCE_ENCODING = "cp1252"
FIX_ENCODING = True

# ficstk deliberately excluded — see the module docstring. It's fetched
# and base64-encoded separately by fetch_ficstk_map, then appended as
# 'ficstk_base64' when each row is written (see OUTPUT_COLUMNS below).
COLUMNS = [
    'num_client', 'date_saisie', 'heure_saisie', 'assistante_commerciale',
    'date_rappel', 'heure_rappel', 'opération', 'status_vente', 'note',
    'franchise', 'raison_sociale', 'cp', 'ville', 'statuts_clients',
    'magasin_principal', 'id_histo'
]
OUTPUT_COLUMNS = COLUMNS + ['ficstk_base64']

# Columns handled separately (native numeric id, or via the date/time output
# converters above) are excluded from the VARCHAR-casting/length-check logic.
_UNCAST_COLUMNS = {'id_histo', 'date_saisie', 'heure_saisie', 'date_rappel', 'heure_rappel'}
TEXT_COLUMNS = [c for c in COLUMNS if c not in _UNCAST_COLUMNS]

CAST_SIZES_CACHE_FILE = "historique_cast_sizes.json"


def compute_cast_sizes(hf_cur):
    """Determine the real max length of every text column from the actual
    table data, so the VARCHAR casts below are sized to guarantee no
    truncation — not guessed. Cached to disk so a resumed run doesn't
    re-scan the full table every restart.

    Casting to VARCHAR at all is still necessary: pypyodbc has a bug where
    a variable-length ("memo") column, fetched via the driver's
    streamed/chunked path, can silently drop from the row when its value is
    empty or streamed a certain way — shifting every column after it and
    eventually throwing an unrelated-looking IndexError. Casting routes the
    column through the driver's simple, non-chunked text path instead,
    which avoids that bug. Sizing the cast off the real max length (plus a
    buffer) means the cast can never truncate real data.

    ficstk is excluded from this — it's binary, not text, so "cast it to
    VARCHAR" is exactly the move that corrupted the CSV in the first place.
    It gets its own isolated, uncast fetch in fetch_ficstk_map instead.
    """
    if os.path.exists(CAST_SIZES_CACHE_FILE):
        with open(CAST_SIZES_CACHE_FILE) as f:
            cached = json.load(f)
        print(f"Using cached column max-lengths from {CAST_SIZES_CACHE_FILE} "
              f"(delete this file to force a re-scan)")
        return cached

    print("Scanning Historique for actual max length of each text column "
          "(one-time cost, needed to size VARCHAR casts with zero truncation risk)...")

    # One query scanning every column's MAX(LENGTH(...)) at once, instead of
    # a separate full-table scan per column (12 scans -> 1). HFSQL's ODBC
    # driver aggregates client-side rather than pushing MAX down to the
    # server, so each separate scan was pulling the whole 1M+ row table
    # across the wire; bundling the aggregates into one SELECT still pulls
    # the table once, not once per column.
    sizes = None
    last_error = None
    for func in ("LENGTH", "LEN"):
        try:
            select_parts = ', '.join(f"MAX({func}({col}))" for col in TEXT_COLUMNS)
            hf_cur.execute(f"SELECT {select_parts} FROM Historique")
            result = hf_cur.fetchone()
            sizes = {}
            for col, max_len in zip(TEXT_COLUMNS, result):
                max_len = int(max_len) if max_len is not None else 0  # column is entirely NULL/empty
                sizes[col] = max_len + 50  # buffer for safety margin
                print(f"  {col}: max actual length {max_len}, using cast size {sizes[col]}", flush=True)
            break
        except Exception as e:
            last_error = e
            continue

    if sizes is None:
        # Couldn't determine the real lengths via SQL at all. Don't guess
        # a size — that's exactly the truncation risk we're avoiding.
        # Bail loudly instead so this gets fixed rather than silently
        # trusted.
        raise RuntimeError(
            f"Could not determine max lengths for text columns via "
            f"LENGTH()/LEN() — last error: {last_error}. Can't safely "
            f"pick VARCHAR sizes without truncation risk. Either find "
            f"the right length function for this HFSQL ODBC driver, or "
            f"tell me and we'll adjust the query."
        )

    with open(CAST_SIZES_CACHE_FILE, "w") as f:
        json.dump(sizes, f, indent=2)

    return sizes


def build_select_columns(cast_sizes):
    parts = []
    for col in COLUMNS:
        if col in cast_sizes:
            parts.append(f"CAST({col} AS VARCHAR({cast_sizes[col]})) AS {col}")
        else:
            parts.append(col)
    return ', '.join(parts)


def load_checkpoint():
    if os.path.exists(CHECKPOINT_FILE):
        with open(CHECKPOINT_FILE) as f:
            return json.load(f).get("last_id_histo")
    return None


def save_checkpoint(last_id):
    with open(CHECKPOINT_FILE, "w") as f:
        json.dump({"last_id_histo": last_id}, f)


def fix_encoding(value):
    if not FIX_ENCODING or not isinstance(value, str):
        return value
    try:
        return value.encode("latin1").decode(SOURCE_ENCODING)
    except (UnicodeDecodeError, UnicodeEncodeError):
        return value


def sanity_check(hf_cur):
    hf_cur.execute("SELECT COUNT(*), COUNT(DISTINCT id_histo) FROM Historique")
    total, distinct = hf_cur.fetchone()
    dupes = total - distinct
    print(f"Sanity check: {total:,} total rows, {distinct:,} distinct id_histo "
          f"({dupes:,} duplicate id_histo values)")
    if dupes:
        print("id_histo has duplicates — pagination will handle this via the "
              "boundary-group fetch below, but you're relying on that logic "
              "being correct. It's tested against your actual data below.")


def fetch_boundary_group(hf_cur, boundary_id, select_columns):
    """Fetch every row sharing the boundary id_histo value, regardless of
    whether the main batch query already returned some of them. Used to
    guarantee a duplicate-id group is never split across two batches,
    which would otherwise cause the second half to be silently skipped
    once pagination moves past that id."""
    # HFSQL's SQL engine (HExécuteRequêteSQL) rejects "?" bound parameters
    # combined with ORDER BY/LIMIT (see the main loop below); to stay
    # consistent — and because param support here has proven inconsistent
    # across query shapes — id_histo is inlined as a literal instead. Safe:
    # it always comes from our own prior SELECT of this same integer column,
    # never from external input.
    hf_cur.execute(
        f"SELECT {select_columns} FROM Historique WHERE id_histo = {int(boundary_id)}"
    )
    return hf_cur.fetchall()


def fetch_ficstk_map(hf_cur, start_id, end_id):
    """Fetch id_histo/ficstk for exactly the id range this batch covers, in
    its own isolated 2-column query — kept completely separate from the
    other 15 columns so the pypyodbc memo-streaming bug (see
    compute_cast_sizes's docstring) can't shift THOSE columns even if it
    still affects this one. ficstk is left uncast here on purpose (see the
    module docstring for why CASTing it to VARCHAR was the original bug) —
    it comes back as raw bytes and gets base64-encoded in Python instead.

    Uses the same inclusive range as the main batch query (start_id
    exclusive, end_id inclusive) so it captures exactly the same rows,
    boundary duplicates included — no separate IN-list needed, and no
    IN-list size limit to worry about. start_id=None means "from the
    beginning," matching the main query's first-batch behavior.
    """
    if start_id is None:
        hf_cur.execute(
            f"SELECT id_histo, ficstk FROM Historique WHERE id_histo <= {int(end_id)}"
        )
    else:
        hf_cur.execute(
            f"SELECT id_histo, ficstk FROM Historique "
            f"WHERE id_histo > {int(start_id)} AND id_histo <= {int(end_id)}"
        )
    result = {}
    for row in hf_cur.fetchall():
        if len(row) != 2:
            raise RuntimeError(
                f"ficstk fetch returned a row with {len(row)} value(s) "
                f"instead of 2 (id_histo, ficstk) — the memo-streaming bug "
                f"may be affecting this isolated query too, which the "
                f"module docstring's fix assumed wouldn't happen. Row: "
                f"{row!r}. Stopping here rather than silently misalign "
                f"ficstk_base64 against the wrong rows."
            )
        row_id, raw_ficstk = row
        result[row_id] = _b64_encode_binary(raw_ficstk)
    return result


def diagnose_short_row(row, batch_index):
    """A row came back with fewer columns than expected — a pypyodbc bug
    (see compute_cast_sizes docstring) rather than bad data. This can't
    reliably say WHICH column was dropped (that information is already
    gone by the time the row is short), but it prints what we do have so
    the next step is obvious instead of a bare IndexError."""
    print(f"\n--- Short row detected (row {batch_index} in this batch) ---")
    print(f"Expected {len(COLUMNS)} columns, got {len(row)}")
    print(f"Values present: {list(row)}")
    print("This means one of the TEXT_COLUMNS is still hitting the pypyodbc "
          "streamed-column bug even after casting. Compare 'Values present' "
          "against COLUMNS in order to see which value(s) look missing, then "
          "double check that column actually made it into CAST_SIZES_CACHE_FILE "
          "with a real size (delete the cache file to force a re-scan if in doubt).")
    print("---\n")


def main():
    last_id = load_checkpoint()
    resuming = last_id is not None
    print(f"Resuming from id_histo > {last_id}" if resuming else "Starting fresh export")
    if TEST_MODE:
        print(f"TEST_MODE is on — stopping after {TEST_MODE_LIMIT} rows, "
              f"writing to {OUTPUT_CSV} (separate from the real output file).")

    hf_conn = odbc.connect(HFSQL_DSN)
    hf_conn.add_output_converter(odbc.SQL_DATE, _safe_dt_cvt)
    hf_conn.add_output_converter(odbc.SQL_TYPE_DATE, _safe_dt_cvt)
    hf_conn.add_output_converter(odbc.SQL_TIME, _safe_tm_cvt)
    hf_conn.add_output_converter(odbc.SQL_SS_TIME2, _safe_tm_cvt)
    hf_conn.add_output_converter(odbc.SQL_TYPE_TIME, _safe_tm_cvt)
    hf_conn.add_output_converter(odbc.SQL_TIMESTAMP, _safe_dttm_cvt)
    hf_conn.add_output_converter(odbc.SQL_TYPE_TIMESTAMP, _safe_dttm_cvt)
    hf_cur = hf_conn.cursor()

    if not resuming:
        sanity_check(hf_cur)

    cast_sizes = compute_cast_sizes(hf_cur)
    select_columns = build_select_columns(cast_sizes)

    id_index = COLUMNS.index('id_histo')
    total_exported = 0
    start_time = time.time()

    # 'a' (append) so resuming doesn't overwrite what's already written.
    # newline='' is required by csv module on Windows to avoid extra blank lines.
    write_header = not (resuming and os.path.exists(OUTPUT_CSV))
    with open(OUTPUT_CSV, "a", newline="", encoding="utf-8") as f:
        writer = csv.writer(f)
        if write_header:
            writer.writerow(OUTPUT_COLUMNS)

        while True:
            batch_start_id = last_id  # value BEFORE this batch — needed for the ficstk range fetch below

            if last_id is None:
                hf_cur.execute(
                    f"SELECT {select_columns} FROM Historique "
                    f"ORDER BY id_histo LIMIT {BATCH_SIZE}"
                )
            else:
                # Inlined rather than a bound "?" param — see the note in
                # fetch_boundary_group; HFSQL's engine rejects "?" combined
                # with ORDER BY/LIMIT. last_id is always an id_histo value
                # we previously read back from this same integer column.
                hf_cur.execute(
                    f"SELECT {select_columns} FROM Historique "
                    f"WHERE id_histo > {int(last_id)} ORDER BY id_histo LIMIT {BATCH_SIZE}"
                )

            rows = hf_cur.fetchall()
            if not rows:
                break

            for i, row in enumerate(rows):
                if len(row) != len(COLUMNS):
                    diagnose_short_row(row, i)
                    raise RuntimeError(
                        "Stopping here rather than crash blind on an "
                        "IndexError further down — see diagnostic above."
                    )

            boundary_id = rows[-1][id_index]

            # Rows strictly below the boundary id are safe as-is: since
            # last_id was the previous boundary and this batch is ordered,
            # any id below this batch's max is fully contained in this batch.
            below_boundary = [r for r in rows if r[id_index] != boundary_id]

            # For the boundary id itself, re-fetch ALL rows sharing it, in
            # case the batch cut a duplicate-id group in half. Dedupe by
            # full row content in case the batch already had some of them.
            boundary_rows = fetch_boundary_group(hf_cur, boundary_id, select_columns)
            seen = set()
            complete_boundary_group = []
            for r in boundary_rows:
                key = tuple(r)
                if key not in seen:
                    seen.add(key)
                    complete_boundary_group.append(r)

            batch_rows = below_boundary + complete_boundary_group

            clean_rows = [tuple(fix_encoding(v) for v in row) for row in batch_rows]

            # ficstk for exactly this batch's id range, fetched in its own
            # isolated query and base64-encoded — see fetch_ficstk_map.
            ficstk_map = fetch_ficstk_map(hf_cur, batch_start_id, boundary_id)
            final_rows = [
                row + (ficstk_map.get(row[id_index], ""),)
                for row in clean_rows
            ]

            writer.writerows(final_rows)
            f.flush()  # make sure it actually hits disk before checkpointing

            last_id = boundary_id
            save_checkpoint(last_id)

            total_exported += len(final_rows)
            elapsed = time.time() - start_time
            print(f"Exported {total_exported:,} rows so far "
                  f"(last id_histo={last_id}, {elapsed:.0f}s elapsed)")

            if TEST_MODE and total_exported >= TEST_MODE_LIMIT:
                print(f"TEST_MODE limit reached ({TEST_MODE_LIMIT} rows) — stopping.")
                break

    hf_cur.close()
    hf_conn.close()
    print(f"Done. Total rows exported: {total_exported:,} -> {OUTPUT_CSV}")
    if _malformed_datetime_count:
        print(f"Note: {_malformed_datetime_count} malformed date/time values "
              f"were encountered and exported as blank rather than crashing "
              f"the run. Worth spot-checking the CSV for rows with empty "
              f"date_saisie/heure_saisie/date_rappel/heure_rappel before "
              f"treating this as a clean export.")


if __name__ == "__main__":
    main()
