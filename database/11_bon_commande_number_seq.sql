-- Backs the auto-incrementing "N° bon de commande" (BonCommandeTemplateService's
-- GetNextBonCommandeNumberAsync). Same mechanism already used for
-- france_optique_cle_opl_seq (see 03_cle_opl_sequence.sql) — a real Postgres
-- sequence instead of a hand-rolled "read the last value, add 1, save it back"
-- counter, which is exactly the kind of concurrent-write race the legacy
-- Cle_Opl max+1 scan was replaced for in the first place. Two commerçants
-- generating a bon de commande at the same moment can't collide on the same
-- number this way.

CREATE SEQUENCE IF NOT EXISTS bon_commande_number_seq;
