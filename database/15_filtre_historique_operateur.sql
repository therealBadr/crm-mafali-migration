-- filtre_historique was scaffolded as a flat named list (nom_filtre PK
-- only, no owner) — a literal reading of the legacy Fichier_Filtre_Historique
-- table structure. Badr wants Historique filters to have the same
-- per-assistant ownership Client filters (filtre_operatrice) already have,
-- so this brings the two tables to the same shape: composite key
-- (nom_operateur, nom_filtre), same column order, same types.
--
-- Table is empty (0 rows migrated — confirmed live, 2026-09-02), so this is
-- a pure structural change, no backfill needed.

ALTER TABLE filtre_historique
    ADD COLUMN nom_operateur VARCHAR(50) NOT NULL;

ALTER TABLE filtre_historique
    DROP CONSTRAINT filtre_historique_pkey;

ALTER TABLE filtre_historique
    ADD PRIMARY KEY (nom_operateur, nom_filtre);
