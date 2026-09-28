"""
MAFALI CRM — full migration from CRM_Export into Postgres. Runs every
load_*.py script in the correct order, dropping the foreign keys first
and re-adding them (as NOT VALID, exactly matching
database/02_foreign_keys.sql) afterward.

Why the FKs get dropped and re-added rather than left alone: they
already exist on the live tables from the original test-data migration.
NOT VALID only skipped checking pre-existing rows at the moment each
constraint was first added — every new INSERT is still checked against
it in real time. Real legacy data is known (per 02_foreign_keys.sql's
own header comment) to have typos/blanks in famille/franchise/pays, and
this export's own france_optique data confirms it (mis-keyed rows exist,
e.g. a franchise name typed into the city field). Loading with the
constraints still active would reject a meaningful share of real rows
outright. Dropping them for the load and re-adding as NOT VALID
afterward restores exactly the same protection the live schema has today
— new data going forward is still checked — without blocking the bulk
load on old data's real imperfections.

Order matters:
  1. Drop FKs.
  2. Reference tables (type_famille, type_franchise, pays, assistantes,
     filtre_operatrice) — france_optique's FKs point at the first three.
  3. france_optique — ca and historique's FKs point at this.
  4. ca, historique.
  5. Re-add FKs.

Usage:
    python3 run_full_migration.py
"""

import psycopg2

import load_reference_data
import load_france_optique
import load_ca
import load_historique

DSN = "dbname=mafali_crm_db user=godspeed host=/var/run/postgresql"

DROP_FKS = [
    "ALTER TABLE france_optique DROP CONSTRAINT IF EXISTS fk_france_optique_famille",
    "ALTER TABLE france_optique DROP CONSTRAINT IF EXISTS fk_france_optique_franchise",
    "ALTER TABLE france_optique DROP CONSTRAINT IF EXISTS fk_france_optique_franchise2",
    "ALTER TABLE france_optique DROP CONSTRAINT IF EXISTS fk_france_optique_franchise3",
    "ALTER TABLE france_optique DROP CONSTRAINT IF EXISTS fk_france_optique_franchise4",
    "ALTER TABLE france_optique DROP CONSTRAINT IF EXISTS fk_france_optique_pays",
    "ALTER TABLE historique DROP CONSTRAINT IF EXISTS fk_historique_num_client",
    "ALTER TABLE ca DROP CONSTRAINT IF EXISTS fk_ca_cle_opl",
]

# Exactly database/02_foreign_keys.sql's definitions.
ADD_FKS = [
    """ALTER TABLE france_optique ADD CONSTRAINT fk_france_optique_famille
       FOREIGN KEY (famille) REFERENCES type_famille(nom_famille)
       ON DELETE SET NULL ON UPDATE CASCADE NOT VALID""",
    """ALTER TABLE france_optique ADD CONSTRAINT fk_france_optique_franchise
       FOREIGN KEY (franchise) REFERENCES type_franchise(nom_franchise)
       ON DELETE SET NULL ON UPDATE CASCADE NOT VALID""",
    """ALTER TABLE france_optique ADD CONSTRAINT fk_france_optique_franchise2
       FOREIGN KEY (franchise2) REFERENCES type_franchise(nom_franchise)
       ON DELETE SET NULL ON UPDATE CASCADE NOT VALID""",
    """ALTER TABLE france_optique ADD CONSTRAINT fk_france_optique_franchise3
       FOREIGN KEY (franchise3) REFERENCES type_franchise(nom_franchise)
       ON DELETE SET NULL ON UPDATE CASCADE NOT VALID""",
    """ALTER TABLE france_optique ADD CONSTRAINT fk_france_optique_franchise4
       FOREIGN KEY (franchise4) REFERENCES type_franchise(nom_franchise)
       ON DELETE SET NULL ON UPDATE CASCADE NOT VALID""",
    """ALTER TABLE france_optique ADD CONSTRAINT fk_france_optique_pays
       FOREIGN KEY (pays) REFERENCES pays(nom_pays)
       ON DELETE SET NULL ON UPDATE CASCADE NOT VALID""",
    """ALTER TABLE historique ADD CONSTRAINT fk_historique_num_client
       FOREIGN KEY (num_client) REFERENCES france_optique(cle_opl)
       ON DELETE RESTRICT ON UPDATE CASCADE NOT VALID""",
    """ALTER TABLE ca ADD CONSTRAINT fk_ca_cle_opl
       FOREIGN KEY (cle_opl) REFERENCES france_optique(cle_opl)
       ON DELETE RESTRICT ON UPDATE CASCADE NOT VALID""",
]


def run_sql_statements(statements, label):
    conn = psycopg2.connect(DSN)
    cur = conn.cursor()
    for stmt in statements:
        cur.execute(stmt)
    conn.commit()
    cur.close()
    conn.close()
    print(f"{label}: done ({len(statements)} statements)")


def print_row_counts():
    conn = psycopg2.connect(DSN)
    cur = conn.cursor()
    tables = ["type_famille", "type_franchise", "pays", "assistantes",
              "filtre_operatrice", "france_optique", "ca", "historique"]
    print("\nFinal row counts:")
    for t in tables:
        cur.execute(f"SELECT count(*) FROM {t}")
        print(f"  {t:20s} {cur.fetchone()[0]:,}")
    cur.close()
    conn.close()


def main():
    print("=== Step 1: dropping foreign keys ===")
    run_sql_statements(DROP_FKS, "Drop FKs")

    print("\n=== Step 2: reference tables (type_famille, type_franchise, pays, assistantes, filtre_operatrice) ===")
    load_reference_data.main()

    print("\n=== Step 3: france_optique ===")
    load_france_optique.main()

    print("\n=== Step 4: ca ===")
    load_ca.main()

    print("\n=== Step 5: historique ===")
    load_historique.main()

    print("\n=== Step 6: re-adding foreign keys (NOT VALID, same as database/02_foreign_keys.sql) ===")
    run_sql_statements(ADD_FKS, "Add FKs")

    print_row_counts()
    print("\nMigration complete.")


if __name__ == "__main__":
    main()
