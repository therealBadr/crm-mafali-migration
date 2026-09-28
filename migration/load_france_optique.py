"""
MAFALI CRM — loads france_optique_export.csv into Postgres.

Verified structurally sound before this was written: every row's values,
even the short ones, line up correctly against the header from the start
— short rows are missing trailing columns that were empty (the export
tool omits trailing empty fields rather than writing empty commas), not
scrambled or reordered. pad_row() (see csv_common.py) handles this by
re-padding every row to the full 37-column width before mapping.

One row (cle_opl == '0') is a genuine legacy data artifact — its values
are visibly shifted across the wrong columns (a phone number in the
email field, a city name in the portable field, etc.), same class of
issue as the empty Nom_Franchise row and blank id_histo rows already
skipped during earlier migration work. No real client legitimately has
cle_opl 0 (the real minimum seen anywhere in this export is in the
thousands), so it's skipped with a warning rather than loaded as garbage
under a fake client record.

fiche_a_supprimer (present in the source, always 0) is deliberately not
loaded — same exclusion as the original 2026-08-07 migration, it isn't
in the target schema. siret/siren/facturation_electronique/tva have no
legacy source at all (entered fresh in the new app going forward) and
are simply left at their column defaults.

Run this AFTER load_reference_data.py (famille/franchise/pays must
already exist for the foreign keys) and with those foreign keys
temporarily dropped (see run_full_migration.py) — real legacy data is
known to have typos/blanks in these fields that a freshly-added FK would
reject outright.

Usage:
    python3 load_france_optique.py
"""

import psycopg2
import psycopg2.extras

from csv_common import row_reader, pad_row, clean, parse_int, parse_bool, parse_date, parse_time

EXPORT_DIR = "/media/godspeed/DROP BOX/CRM_Export"
DSN = "dbname=mafali_crm_db user=godspeed host=/var/run/postgresql"
BATCH_SIZE = 5000

# (source CSV header, target column, parser). Order doesn't need to match
# the CSV — indices are looked up by name — but does need to match
# INSERT_COLUMNS below 1:1.
FIELD_MAP = [
    ("cle_opl", "cle_opl", parse_int),
    ("famille", "famille", clean),
    ("raison_sociale", "raison_sociale", clean),
    ("complement", "complement", clean),
    ("franchise", "franchise", clean),
    ("franchise2", "franchise2", clean),
    ("franchise3", "franchise3", clean),
    ("franchise4", "franchise4", clean),
    ("rue", "rue", clean),
    ("localisation_1", "localisation_1", clean),
    ("localisation_2", "localisation_2", clean),
    ("cp", "cp", clean),
    ("ville", "ville", clean),
    ("pays", "pays", clean),
    ("telephone", "telephone", clean),
    ("telbis", "tel_bis", clean),
    ("portable", "portable", clean),
    ("fax", "fax", clean),
    ("email", "email", clean),
    ("assistante_commerciale", "assistante_commercial", clean),
    ("responsable_achat", "responsable_achat", clean),
    ("représentant", "representant", clean),
    ("op_en_cours", "op_en_cours", clean),
    ("status_vente", "status_vente", clean),
    ("statuts_clients", "statuts_clients", clean),
    ("etatclient", "etat_client", clean),
    ("production", "production", clean),
    ("magasin_principal", "magasin_principal", clean),
    ("date_saisie", "date_saisie", parse_date),
    ("heure_saisie", "heure_saisie", parse_time),
    ("date_rappel", "date_rappel", parse_date),
    ("heure_rappel", "heure_rappel", parse_time),
    ("note", "note", clean),
    ("noteperm", "note_perm", clean),
    ("bloque", "bloque", parse_bool),
    ("rappelrdv", "rappel_rdv", parse_bool),
]
# fiche_a_supprimer intentionally excluded — see module docstring.

INSERT_COLUMNS = [tgt for _src, tgt, _fn in FIELD_MAP]


def main():
    path = f"{EXPORT_DIR}/france_optique_export.csv"
    reader = row_reader(path)
    header = next(reader)

    missing = [src for src, _tgt, _fn in FIELD_MAP if src not in header]
    if missing:
        raise SystemExit(f"france_optique: expected column(s) not found: {missing}\nActual: {header}")
    indices = [header.index(src) for src, _tgt, _fn in FIELD_MAP]
    cle_opl_pos = [tgt for _src, tgt, _fn in FIELD_MAP].index("cle_opl")

    conn = psycopg2.connect(DSN)
    cur = conn.cursor()

    print("Truncating france_optique before reload...")
    cur.execute("TRUNCATE TABLE france_optique")
    conn.commit()

    insert_sql = f"INSERT INTO france_optique ({', '.join(INSERT_COLUMNS)}) VALUES %s"

    batch = []
    total_loaded = 0
    skipped_bad_key = 0

    def flush():
        nonlocal batch, total_loaded
        if not batch:
            return
        psycopg2.extras.execute_values(cur, insert_sql, batch, page_size=BATCH_SIZE)
        conn.commit()
        total_loaded += len(batch)
        print(f"  loaded {total_loaded:,} rows so far...", flush=True)
        batch = []

    for row in reader:
        row = pad_row(row, len(header))
        values = [fn(row[idx]) for (idx, (_src, _tgt, fn)) in zip(indices, FIELD_MAP)]

        cle_opl = values[cle_opl_pos]
        if cle_opl is None or cle_opl == 0:
            skipped_bad_key += 1
            print(f"  WARNING: skipping row with cle_opl={cle_opl!r} — no real client "
                  f"has this id, likely a shifted/corrupted source row.")
            continue

        batch.append(tuple(values))
        if len(batch) >= BATCH_SIZE:
            flush()

    flush()

    cur.close()
    conn.close()
    print(f"\nDone. {total_loaded:,} rows loaded, {skipped_bad_key} skipped (bad/zero cle_opl).")


if __name__ == "__main__":
    main()
