#!/usr/bin/env python3
"""
Mafali CRM — HFSQL -> PostgreSQL data migration.

Reads the Excel exports produced by Centre de Contrôle HFSQL (from the .FIC
files) and loads them into mafali_crm_db, following database/01_tables.sql's
schema exactly.

Deliberately excluded / not yet handled, per explicit decisions made while
building this:
  - Filtre_Historique: source file is password-protected, not available yet.
    filtre_historique stays empty until that password is provided.
  - france_optique.Fiche_A_Supprimer: present in the real export but not in
    the original HFSQL documentation or the target schema — left out of this
    migration for now (see PROGRESS.md).
  - database/02_foreign_keys.sql is NOT applied by this script. Run it
    separately, after this script has loaded data and you've reviewed the
    results — see that file's own header comment for why the order matters.

Safe to re-run: every table is TRUNCATEd (CASCADE, RESTART IDENTITY) before
its data is reloaded, so running this script twice produces the same result,
not duplicates.

Usage:
    python3 migrate.py [--source-dir DIR] [--dsn DSN]
"""

import argparse
import datetime
import sys
from pathlib import Path

import pandas as pd
import psycopg2
import psycopg2.extras

DEFAULT_SOURCE_DIR = "/home/godspeed/Downloads/mafali_db_excel"
DEFAULT_DSN = "dbname=mafali_crm_db user=godspeed host=/var/run/postgresql"


def clean_value(v):
    """pandas NaN/NaT -> None; strip whitespace on strings; leave everything else as-is."""
    if pd.isna(v):
        return None
    if isinstance(v, str):
        v = v.strip()
        return v if v != "" else None
    return v


def to_bool(v):
    v = clean_value(v)
    if v is None:
        return None
    return bool(int(v))


def to_date(v):
    """Handles both real Excel date values (France_Optique's export) and
    'DD/MM/YYYY' text strings (Historique's export) — the two source files
    represent the same HFSQL 'Date (aaaammjj)' type differently. Unparseable
    values (real, if rare, data-quality bugs in the source) become NULL with
    a warning rather than crashing the whole load."""
    v = clean_value(v)
    if v is None:
        return None
    try:
        if isinstance(v, str):
            return datetime.datetime.strptime(v, "%d/%m/%Y").date()
        return v.date() if hasattr(v, "date") else v
    except ValueError:
        print(f"  WARNING: could not parse date value {v!r}, storing as NULL")
        return None


def to_time(v):
    """Same situation as to_date: real time values in one export, 'HH:MM'
    text strings in the other, and occasional garbage (e.g. '90:00' — not a
    real hour) in the source data itself."""
    v = clean_value(v)
    if v is None:
        return None
    try:
        if isinstance(v, datetime.time):
            return v
        if isinstance(v, str):
            return datetime.datetime.strptime(v, "%H:%M").time()
        return v.time() if hasattr(v, "time") else v
    except ValueError:
        print(f"  WARNING: could not parse time value {v!r}, storing as NULL")
        return None


def load_sheet(path: Path):
    xl = pd.ExcelFile(path)
    if len(xl.sheet_names) != 1:
        print(f"  WARNING: {path.name} has {len(xl.sheet_names)} sheets ({xl.sheet_names}), using the first")
    return pd.read_excel(path, sheet_name=xl.sheet_names[0])


def load_table(cur, conn, *, table, source_file, columns, id_column=None, id_sequence=None, pk_columns=None):
    """
    columns: list of (source_header, target_column, transform_fn_or_None)
    id_column / id_sequence: if the source has a real identity value to
    preserve (Pays.IDPays, CA.IDCA, Historique.Id_Histo), pass the target
    column name and the Postgres sequence name so we can reset it after load.
    pk_columns: target column name(s) that are this table's primary key.
    Rows with a blank/null value in any of these are skipped (with a
    warning, not silently) rather than crash the whole load — a NULL
    primary key is invalid by definition, this is a real data-quality gap
    in the source, not a bug to paper over.
    """
    path = Path(source_file)
    if not path.exists():
        print(f"  SKIPPED {table}: {path.name} not found")
        return 0

    df = load_sheet(path)
    missing = [src for src, _, _ in columns if src not in df.columns]
    if missing:
        raise SystemExit(f"{table}: expected column(s) not found in {path.name}: {missing}\nActual columns: {list(df.columns)}")

    target_cols = [tgt for _, tgt, _ in columns]
    pk_columns = pk_columns or []
    pk_indices = [target_cols.index(c) for c in pk_columns]
    overriding = " OVERRIDING SYSTEM VALUE" if id_column else ""
    insert_sql = (
        f"INSERT INTO {table} ({', '.join(target_cols)}){overriding} "
        f"VALUES %s"
    )

    rows = []
    skipped = 0
    for row_num, row in df.iterrows():
        values = []
        for src, _, transform in columns:
            raw = row[src]
            values.append(transform(raw) if transform else clean_value(raw))

        if pk_indices and any(values[i] is None for i in pk_indices):
            skipped += 1
            print(f"  WARNING: {table} row {row_num} (source row {row_num + 2}) has a blank primary key value — skipped: {tuple(values)}")
            continue

        rows.append(tuple(values))

    cur.execute(f"TRUNCATE TABLE {table} RESTART IDENTITY CASCADE")
    psycopg2.extras.execute_values(cur, insert_sql, rows, page_size=500)

    if id_sequence:
        cur.execute(f"SELECT setval(%s, COALESCE((SELECT MAX({id_column}) FROM {table}), 1))", (id_sequence,))

    conn.commit()
    skip_note = f", {skipped} skipped" if skipped else ""
    print(f"  loaded {table}: {len(rows)} rows (from {path.name}{skip_note})")
    return len(rows)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-dir", default=DEFAULT_SOURCE_DIR)
    parser.add_argument("--dsn", default=DEFAULT_DSN)
    args = parser.parse_args()

    src = Path(args.source_dir)
    conn = psycopg2.connect(args.dsn)
    cur = conn.cursor()

    print(f"Migrating from {src} into database via DSN: {args.dsn}\n")

    totals = {}

    # --- Reference tables first ---
    totals["type_famille"] = load_table(
        cur, conn,
        table="type_famille",
        source_file=src / "Type_Famille.xls",
        columns=[("Nom_Famille", "nom_famille", None)],
        pk_columns=["nom_famille"],
    )

    totals["type_franchise"] = load_table(
        cur, conn,
        table="type_franchise",
        source_file=src / "Type_Franchise.xls",
        columns=[("Nom_Franchise", "nom_franchise", None)],
        pk_columns=["nom_franchise"],
    )

    totals["pays"] = load_table(
        cur, conn,
        table="pays",
        source_file=src / "Pays.xls",
        columns=[
            ("IDPays", "id_pays", None),
            ("NomPays", "nom_pays", None),
            ("Indicatif", "indicatif", None),
            ("Masque", "masque", None),
        ],
        id_column="id_pays",
        id_sequence="pays_id_pays_seq",
        pk_columns=["id_pays"],
    )

    totals["assistantes"] = load_table(
        cur, conn,
        table="assistantes",
        source_file=src / "Assistantes.xls",
        columns=[
            ("Prénom_Nom", "prenom_nom", None),
            ("Service", "service", None),
            ("All_Filtres", "all_filtres", to_bool),
            ("Commentaire", "commentaire", None),
        ],
        pk_columns=["prenom_nom"],
    )

    # --- France_Optique (clients) ---
    totals["france_optique"] = load_table(
        cur, conn,
        table="france_optique",
        source_file=src / "France_Optique.xls",
        columns=[
            ("Cle_Opl", "cle_opl", None),
            ("Famille", "famille", None),
            ("Raison_Sociale", "raison_sociale", None),
            ("Complement", "complement", None),
            ("Franchise", "franchise", None),
            ("Franchise2", "franchise2", None),
            ("Franchise3", "franchise3", None),
            ("Franchise4", "franchise4", None),
            ("Rue", "rue", None),
            ("Localisation_1", "localisation_1", None),
            ("Localisation_2", "localisation_2", None),
            ("CP", "cp", None),
            ("Ville", "ville", None),
            ("Pays", "pays", None),
            ("Telephone", "telephone", None),
            ("TelBis", "tel_bis", None),
            ("Portable", "portable", None),
            ("Fax", "fax", None),
            ("Email", "email", None),
            ("Assistante_Commerciale", "assistante_commercial", None),
            ("Responsable_Achat", "responsable_achat", None),
            ("Représentant", "representant", None),
            ("Op_En_Cours", "op_en_cours", None),
            ("Status_Vente", "status_vente", None),
            ("Statuts_Clients", "statuts_clients", None),
            ("EtatClient", "etat_client", None),
            ("Production", "production", None),
            ("Magasin_Principal", "magasin_principal", None),
            ("Date_Saisie", "date_saisie", to_date),
            ("Heure_Saisie", "heure_saisie", to_time),
            ("Date_Rappel", "date_rappel", to_date),
            ("Heure_Rappel", "heure_rappel", to_time),
            ("Note", "note", None),
            ("NotePerm", "note_perm", None),
            ("bloque", "bloque", to_bool),
            ("RappelRDV", "rappel_rdv", to_bool),
        ],
        pk_columns=["cle_opl"],
    )

    # --- Historique (append-only interaction log) ---
    totals["historique"] = load_table(
        cur, conn,
        table="historique",
        source_file=src / "Historique.xlsx",
        columns=[
            ("Id_Histo", "id_histo", None),
            ("Num_Client", "num_client", None),
            ("Date_Saisie", "date_saisie", to_date),
            ("Heure_Saisie", "heure_saisie", to_time),
            ("Assistante_Commerciale", "assistante_commercial", None),
            ("Date_Rappel", "date_rappel", to_date),
            ("Heure_Rappel", "heure_rappel", to_time),
            ("Opération", "operation", None),
            ("Status_Vente", "status_vente", None),
            ("Note", "note", None),
            ("Franchise", "franchise", None),
            ("Raison_Sociale", "raison_sociale", None),
            ("CP", "cp", None),
            ("Ville", "ville", None),
            ("Statuts_Clients", "statuts_clients", None),
            ("Magasin_Principal", "magasin_principal", None),
            ("FicStk", "fic_stk", None),
        ],
        id_column="id_histo",
        id_sequence="historique_id_histo_seq",
        pk_columns=["id_histo"],
    )

    # --- CA (revenue) ---
    totals["ca"] = load_table(
        cur, conn,
        table="ca",
        source_file=src / "CA.xls",
        columns=[
            ("IDCA", "id_ca", None),
            ("Cle_Opl", "cle_opl", None),
            ("Année", "annee", None),
            ("CA", "ca", None),
        ],
        id_column="id_ca",
        id_sequence="ca_id_ca_seq",
        pk_columns=["cle_opl", "annee"],
    )

    # --- Filtre_Opératrice ---
    totals["filtre_operatrice"] = load_table(
        cur, conn,
        table="filtre_operatrice",
        source_file=src / "Filtre_Opératrice.xls",
        columns=[
            ("Nom_Operateur", "nom_operateur", None),
            ("Nom_Filtre", "nom_filtre", None),
            ("Filtre_Réel", "filtre_reel", None),
        ],
        pk_columns=["nom_operateur", "nom_filtre"],
    )

    # --- Filtre_Historique: password-protected, not available yet ---
    print("  SKIPPED filtre_historique: source file is password-protected")

    cur.close()
    conn.close()

    print("\nDone. Row counts:")
    for table, count in totals.items():
        print(f"  {table:20s} {count}")


if __name__ == "__main__":
    main()
