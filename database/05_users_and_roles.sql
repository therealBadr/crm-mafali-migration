-- Replaces the legacy GPW/WDGPU auth component (GPWUtilisateur/GPWConfiguration)
-- rather than porting it — see docs/Mafali_CRM_Functional_Spec_v1.md Module 9
-- and Risk Register #21/#23/#25. Two decisions, confirmed with Badr:
--   1. Plaintext passwords (legacy) are non-negotiable to fix — password_hash
--      only, real hashing happens at the application layer (bcrypt/argon2).
--   2. Three fixed business roles, not the legacy's per-UI-element permission
--      engine: admin / assistant / commercial. A plain CHECK-constrained column
--      is enough — there's no case for a generic roles/permissions table when
--      the three roles and their capabilities are fixed, not admin-configurable.
--
-- Keyed on login (a "PRENOM NOM"-style string), matching the prenom_nom /
-- nom_operateur / assistante_commercial naming convention already used
-- everywhere else in this schema, so a user row joins naturally with existing
-- name-string references instead of introducing a parallel surrogate-ID identity.
CREATE TABLE users (
    login                 VARCHAR(100) PRIMARY KEY,
    password_hash         VARCHAR(255) NOT NULL,
    role                  VARCHAR(20) NOT NULL CHECK (role IN ('admin', 'assistant', 'commercial')),
    must_change_password  BOOLEAN NOT NULL DEFAULT true,
    is_active             BOOLEAN NOT NULL DEFAULT true,
    last_login_at         TIMESTAMP NULL,
    created_at            TIMESTAMP NOT NULL DEFAULT now()
);

-- Seed one account per existing assistant profile, since all 20 are real
-- people who'll need to log in. Role derived from the legacy all_filtres
-- flag (2026-08-14 decision): the 4 people already flagged all_filtres=true
-- (BADR BADR, OA OA, OLIVIER OLIVIER, OMAR) become admin per Badr's call —
-- Assistant vs Admin among them is still an open question for his boss, so
-- all 4 land on admin for now rather than guess. Everyone else becomes
-- commercial. No password set yet (login screen isn't built) — password_hash
-- is a placeholder that must_change_password will force everyone to replace
-- before this table is actually usable for authentication.
INSERT INTO users (login, password_hash, role, must_change_password)
SELECT
    prenom_nom,
    '',
    CASE WHEN all_filtres THEN 'admin' ELSE 'commercial' END,
    true
FROM assistantes;

-- assistantes.all_filtres becomes redundant once role exists (view-all is
-- now fully determined by role, per Badr's spec) — not dropped yet, kept as
-- a documented deprecation until the app actually reads from users.role
-- instead, so nothing breaks mid-migration.
COMMENT ON COLUMN assistantes.all_filtres IS
    'Deprecated 2026-08-14: superseded by users.role (admin/assistant see all, commercial sees own only). Kept until the app is fully switched over to reading users.role.';
