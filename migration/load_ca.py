"""
MAFALI CRM — loads ca_export.csv into Postgres. Verified structurally
clean before this was written (every row has exactly 4 fields, matching
the header). Run this AFTER load_france_optique.py and with ca's foreign
key (cle_opl -> france_optique.cle_opl) temporarily dropped — see
run_full_migration.py.

id_ca is GENERATED ALWAYS AS IDENTITY but not part of the real primary
key (that's the composite (cle_opl, annee), same as the original schema
design) — preserved via OVERRIDING SYSTEM VALUE anyway, same reasoning
as pays.id_pays in load_reference_data.py, so nothing referring to a
specific id_ca value elsewhere breaks.

Usage:
    python3 load_ca.py
"""

import psycopg2
import psycopg2.extras

from csv_common import row_reader, pad_row, parse_int

EXPORT_DIR = "/media/godspeed/DROP BOX/CRM_Export"
DSN = "dbname=mafali_crm_db user=godspeed host=/var/run/postgresql"
BATCH_SIZE = 5000


def main():
    path = f"{EXPORT_DIR}/ca_export.csv"
    reader = row_reader(path)
    header = next(reader)
    idx = {c: header.index(c) for c in ["idca", "année", "ca", "cle_opl"]}

    conn = psycopg2.connect(DSN)
    cur = conn.cursor()

    print("Truncating ca before reload...")
    cur.execute("TRUNCATE TABLE ca")
    conn.commit()

    insert_sql = (
        "INSERT INTO ca (id_ca, cle_opl, annee, ca) "
        "OVERRIDING SYSTEM VALUE VALUES %s"
    )

    rows = []
    skipped = 0
    for row in reader:
        row = pad_row(row, len(header))
        cle_opl = parse_int(row[idx["cle_opl"]])
        annee = parse_int(row[idx["année"]])
        if cle_opl is None or annee is None:
            skipped += 1
            continue
        rows.append((
            parse_int(row[idx["idca"]]),
            cle_opl,
            annee,
            parse_int(row[idx["ca"]]),
        ))

    for i in range(0, len(rows), BATCH_SIZE):
        psycopg2.extras.execute_values(cur, insert_sql, rows[i:i + BATCH_SIZE], page_size=BATCH_SIZE)
        conn.commit()
        print(f"  loaded {min(i + BATCH_SIZE, len(rows)):,} / {len(rows):,} rows...", flush=True)

    cur.execute("SELECT setval('ca_id_ca_seq', COALESCE((SELECT MAX(id_ca) FROM ca), 1))")
    conn.commit()

    cur.close()
    conn.close()
    print(f"\nDone. {len(rows):,} rows loaded, {skipped} skipped (blank cle_opl/annee).")


if __name__ == "__main__":
    main()
