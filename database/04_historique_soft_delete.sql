-- Historique gets soft delete, not the legacy's permanent hard delete —
-- deliberate architecture change per Risk Register #13 ("Historique is
-- fully editable and hard-deletable... no soft delete, no audit-of-the-audit").
-- Confirmed with Badr (2026-08-11): same treatment as France_Optique's own
-- delete already got. NULL = active row (the default, normal state);
-- non-NULL = soft-deleted, hidden from every screen but recoverable.
ALTER TABLE historique ADD COLUMN deleted_at TIMESTAMP NULL;

-- Fichier_Historique will browse/paginate this table at real scale (1M+
-- rows once the full source data is migrated) — an index on the soft-delete
-- flag keeps "WHERE deleted_at IS NULL" (present on every read) cheap
-- instead of a full scan.
CREATE INDEX idx_historique_deleted_at ON historique (deleted_at);
