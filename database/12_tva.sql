-- New field, needed for the Bon de Commande "Adresse de Facturation"
-- generation (TVA line, appended after the address block) — confirmed
-- with Badr: Parcours Client only, same scope as SIRET/SIREN (not on
-- Nouveau Client, not part of the Historique audit snapshot).
-- VARCHAR(20): French format is 2-letter country code + up to 13 chars
-- (e.g. "FR92528685464"), sized with headroom rather than the exact
-- French-only minimum.

ALTER TABLE france_optique ADD COLUMN tva VARCHAR(20);
