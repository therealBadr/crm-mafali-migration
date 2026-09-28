-- Real full france_optique data (CRM_Export, 2026-09-02) has tel_bis
-- values up to 27 characters — some rows use this field to hold a
-- franchise/group name rather than a phone number (e.g. "CENTRALE DES
-- OPTICIENS", "VOGUE DIFFUSION OPTIQUE VDO"), which is real legacy data
-- entry practice, not something this migration should silently reinterpret
-- or truncate. Widened to VARCHAR(60) — same size as raison_sociale,
-- comfortable headroom above the real observed max, consistent with the
-- note/note_perm widening (08_note_widen.sql) for the same underlying
-- reason: a column too narrow for real data, not a schema design error to
-- work around by dropping data.

ALTER TABLE france_optique
    ALTER COLUMN tel_bis TYPE VARCHAR(60);
