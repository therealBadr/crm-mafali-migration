-- SIRET/SIREN are French business identifiers, one per client, not picked
-- from a shared list — plain text with a length constraint, not a lookup
-- table. 14/9 digits per the official format; stored as text (not numeric)
-- since they're identifiers, not quantities, and legitimately start with 0.
--
-- facturation_electronique: exact semantics still open (Badr — "no idea,
-- I'll have that info later", 2026-08-17). Added as a plain boolean for
-- now, matching the existing bloque/rappel_rdv columns on this same table —
-- the cheapest possible placeholder to change later if it turns out to need
-- more than two states.
ALTER TABLE france_optique
    ADD COLUMN siret VARCHAR(14),
    ADD COLUMN siren VARCHAR(9),
    ADD COLUMN facturation_electronique BOOLEAN NOT NULL DEFAULT false;
