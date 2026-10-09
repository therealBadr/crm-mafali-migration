-- Supersedes 18_france_optique_en_attente.sql (Badr, after checking with his
-- boss, 2026-10-05). The earlier design had Commercial *propose* a client
-- into a separate pending table, with approval copying the row into
-- france_optique. The decision changed: a Commercial's add is a real add —
-- the row goes straight into france_optique and is immediately usable — and
-- Admin/Assistant validate it after the fact as a quality check.
--
-- This is a better fit for the schema than the two-table version: approval
-- no longer copies every column from one table to another, so there's no
-- second column list to keep in sync forever. One row, one place, a flag.
--
-- Three states, and only one of them changes behavior:
--   'pending'   — added by a Commercial, not yet checked. Behaves exactly
--                 like a normal client everywhere (confirmed decision:
--                 validation is pure metadata, it gates nothing).
--   'validated' — checked by an Admin/Assistant. Also behaves like a normal
--                 client. The difference from 'pending' is accountability
--                 only, which is why no read path needs to distinguish them.
--   'refused'   — rejected on review. Hidden from every screen, same role
--                 NULL/non-NULL plays for historique.deleted_at (migration
--                 04), so reads filter `validation_status <> 'refused'`
--                 rather than `= 'validated'`.
--
-- Why 'refused' is a state and not a DELETE: ClientService.DeleteAsync is a
-- genuine hard delete (db.FranceOptiques.Remove) and france_optique never
-- got a deleted_at column. Risk Register #4 flags precisely that as a High
-- liability ("no soft-delete flag, no audit trail… recommend explicitly
-- changing this behavior"), a recommendation never applied to clients until
-- now. Independently of the risk register, a delete here would also just
-- fail: historique.num_client and ca.cle_opl are ON DELETE RESTRICT, so a
-- client the Commercial has already logged a call or any CA against cannot
-- be deleted at all — which is the *normal* case when the whole point is
-- that the client is usable the moment it's added.
ALTER TABLE france_optique
    ADD COLUMN validation_status VARCHAR(20) NOT NULL DEFAULT 'pending'
        CHECK (validation_status IN ('pending', 'validated', 'refused')),
    ADD COLUMN added_by        VARCHAR(100) NULL REFERENCES users(login) ON UPDATE CASCADE,
    ADD COLUMN validated_by    VARCHAR(100) NULL REFERENCES users(login) ON UPDATE CASCADE,
    ADD COLUMN validated_at    TIMESTAMP NULL,
    ADD COLUMN refusal_reason  VARCHAR(250) NULL;

-- The 93,235 clients already in the table are migrated legacy data, not
-- anybody's unchecked submission — without this they'd all default to
-- 'pending' and the validation queue would open with 93k rows in it.
-- added_by stays NULL for them on purpose: nobody added them through this
-- app, and inventing an attribution would be worse than recording none.
-- validated_by/validated_at likewise stay NULL — nobody actually reviewed
-- these, so claiming a reviewer would be a fabricated audit trail; the
-- 'validated' status here means "not subject to this workflow", and the
-- NULL reviewer is what distinguishes that from a real human validation.
UPDATE france_optique SET validation_status = 'validated';

-- The validation queue reads WHERE validation_status = 'pending' on every
-- load, and every client-reading screen now carries
-- `validation_status <> 'refused'` — at 93k rows that filter should not be
-- a sequential scan.
CREATE INDEX idx_france_optique_validation_status ON france_optique (validation_status);

-- Backs both the "filter the queue by which Commercial added these" dropdown
-- and a Commercial's own "mes clients ajoutés" list.
CREATE INDEX idx_france_optique_added_by ON france_optique (added_by);

-- france_optique_en_attente is NOT dropped here, deliberately. The four
-- pages and the service built against it still reference it at this point,
-- and EF only fails on a missing table at query time, not at build time —
-- dropping it now would leave an app that compiles cleanly and then errors
-- the moment someone opens "Clients à valider". The drop is a separate
-- migration (20) to be run once no code references it. It holds no rows
-- either way, so nothing is at risk in the meantime.
