"""
MAFALI CRM — loads the five small reference tables from CRM_Export into
Postgres: type_famille, type_franchise, pays, assistantes,
filtre_operatrice. All five verified structurally clean row-by-row before
this was written (every row's field count matches its header exactly, no
truncation/corruption like france_optique or the original historique
attempt had). Safe to re-run: TRUNCATEs each table before reloading.

Run this BEFORE load_france_optique.py — france_optique's famille/
franchise/pays columns reference these three by foreign key once
run_full_migration.py re-adds the constraints.

Usage:
    python3 load_reference_data.py
"""

import psycopg2

from csv_common import row_reader, pad_row, clean, parse_int, parse_bool

EXPORT_DIR = "/media/godspeed/DROP BOX/CRM_Export"
DSN = "dbname=mafali_crm_db user=godspeed host=/var/run/postgresql"


def load_simple(cur, conn, *, csv_file, table, source_cols, target_cols, pk_cols, transforms=None):
    """source_cols/target_cols are parallel lists (same order, same
    length) — source_cols must match the CSV header exactly (this export's
    lowercase convention, not the original PascalCase migrate.py expects).
    transforms is an optional {source_col: fn} map for non-text columns.

    pk_cols (target column names) drives de-duplication: real data found
    one exact duplicate row (type_franchise's "AUCHAN" appears twice,
    byte-for-byte identical) that a plain INSERT would reject outright as
    a primary-key violation. First occurrence wins, duplicates are
    dropped with a warning rather than silently — this is a real, if
    small, data-quality fact worth Badr seeing, not something to bury."""
    transforms = transforms or {}
    path = f"{EXPORT_DIR}/{csv_file}"
    reader = row_reader(path)
    header = next(reader)
    missing = [c for c in source_cols if c not in header]
    if missing:
        raise SystemExit(f"{table}: expected column(s) not found in {csv_file}: {missing}\nActual: {header}")
    indices = [header.index(c) for c in source_cols]
    pk_positions = [target_cols.index(c) for c in pk_cols]

    rows = []
    seen_keys = set()
    dupes = 0
    for row in reader:
        row = pad_row(row, len(header))
        values = []
        for src, idx in zip(source_cols, indices):
            raw = row[idx]
            fn = transforms.get(src, clean)
            values.append(fn(raw))
        key = tuple(values[p] for p in pk_positions)
        if key in seen_keys:
            dupes += 1
            print(f"  WARNING: {table} — dropping duplicate row for key {key} (first occurrence kept)")
            continue
        seen_keys.add(key)
        rows.append(tuple(values))

    cur.execute(f"TRUNCATE TABLE {table}")
    cols_sql = ", ".join(target_cols)
    placeholders = ", ".join(["%s"] * len(target_cols))
    cur.executemany(f"INSERT INTO {table} ({cols_sql}) VALUES ({placeholders})", rows)
    conn.commit()
    dup_note = f", {dupes} duplicate(s) dropped" if dupes else ""
    print(f"  loaded {table}: {len(rows)} rows (from {csv_file}{dup_note})")
    return len(rows)


def main():
    conn = psycopg2.connect(DSN)
    cur = conn.cursor()

    load_simple(
        cur, conn,
        csv_file="type_famille_export.csv",
        table="type_famille",
        source_cols=["nom_famille"],
        target_cols=["nom_famille"],
        pk_cols=["nom_famille"],
    )

    load_simple(
        cur, conn,
        csv_file="type_franchise_export.csv",
        table="type_franchise",
        source_cols=["nom_franchise"],
        target_cols=["nom_franchise"],
        pk_cols=["nom_franchise"],
    )

    # id_pays is GENERATED ALWAYS AS IDENTITY — preserved via OVERRIDING
    # SYSTEM VALUE (same reasoning as migrate.py's original id_column/
    # id_sequence handling) rather than letting Postgres reassign fresh
    # ids, so any code/data elsewhere that already refers to a specific
    # id_pays value stays correct.
    path = f"{EXPORT_DIR}/pays_export.csv"
    reader = row_reader(path)
    header = next(reader)
    idx = {c: header.index(c) for c in ["idpays", "nompays", "indicatif", "masque"]}
    rows = []
    seen_ids = set()
    pays_dupes = 0
    for row in reader:
        row = pad_row(row, len(header))
        id_pays = parse_int(row[idx["idpays"]])
        if id_pays in seen_ids:
            pays_dupes += 1
            print(f"  WARNING: pays — dropping duplicate row for id_pays={id_pays} (first occurrence kept)")
            continue
        seen_ids.add(id_pays)
        rows.append((
            id_pays,
            clean(row[idx["nompays"]]),
            clean(row[idx["indicatif"]]),
            clean(row[idx["masque"]]),
        ))
    cur.execute("TRUNCATE TABLE pays")
    cur.executemany(
        "INSERT INTO pays (id_pays, nom_pays, indicatif, masque) "
        "OVERRIDING SYSTEM VALUE VALUES (%s, %s, %s, %s)",
        rows,
    )
    cur.execute("SELECT setval('pays_id_pays_seq', COALESCE((SELECT MAX(id_pays) FROM pays), 1))")
    conn.commit()
    dup_note = f", {pays_dupes} duplicate(s) dropped" if pays_dupes else ""
    print(f"  loaded pays: {len(rows)} rows (from pays_export.csv{dup_note})")

    load_simple(
        cur, conn,
        csv_file="assistantes_export.csv",
        table="assistantes",
        source_cols=["prénom_nom", "service", "all_filtres", "commentaire"],
        target_cols=["prenom_nom", "service", "all_filtres", "commentaire"],
        pk_cols=["prenom_nom"],
        transforms={"all_filtres": parse_bool},
    )

    load_simple(
        cur, conn,
        csv_file="filtre_opératrice_export.csv",
        table="filtre_operatrice",
        source_cols=["nom_operateur", "nom_filtre", "filtre_réel"],
        target_cols=["nom_operateur", "nom_filtre", "filtre_reel"],
        pk_cols=["nom_operateur", "nom_filtre"],
    )

    cur.close()
    conn.close()
    print("Done.")


if __name__ == "__main__":
    main()
