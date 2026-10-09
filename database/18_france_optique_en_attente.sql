-- New workflow (Badr, 2026-10-05): Commercial lost direct access to
-- /clients/new in the 2026-09-04 permissions pass and has had no way to
-- add a client since. This restores that ability indirectly: Commercial
-- submits a proposed client here, an Assistant (or Admin) reviews it, and
-- only on approval does a real france_optique row get created.
--
-- Deliberately a separate table, not a status column bolted onto
-- france_optique itself: a pending submission must never be visible from
-- Recherche Client, exports, or any filter until approved, and a shared
-- table means every present AND future query against france_optique would
-- need to remember to exclude pending rows. A separate table makes that
-- failure mode structurally impossible instead of relying on discipline.
--
-- Same business columns as france_optique (see 01_tables.sql) minus
-- cle_opl, which doesn't exist until approval mints one. submitted_by /
-- reviewed_by key off users.login (a "PRENOM NOM" string), matching the
-- natural-key convention already used everywhere in this schema rather
-- than introducing a surrogate-ID identity just for this table.
--
-- Rejection keeps a trace (reviewed_by/reviewed_at/rejection_reason)
-- instead of deleting the row — consistent with how Risk Register #4 and
-- #13 already pushed this project away from hard-delete-with-no-audit-trail
-- for france_optique and historique; a rejected submission is the same
-- shape of decision.
CREATE TABLE france_optique_en_attente (
    id_attente              BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    famille                 VARCHAR(30),
    raison_sociale          VARCHAR(60),
    complement              VARCHAR(60),
    franchise               VARCHAR(50),
    franchise2              VARCHAR(50),
    franchise3              VARCHAR(50),
    franchise4              VARCHAR(50),
    rue                     VARCHAR(60),
    localisation_1          VARCHAR(60),
    localisation_2          VARCHAR(60),
    cp                      VARCHAR(5),
    ville                   VARCHAR(40),
    pays                    VARCHAR(30),
    telephone               VARCHAR(20),
    tel_bis                 VARCHAR(20),
    portable                VARCHAR(20),
    fax                     VARCHAR(20),
    email                   VARCHAR(50),
    assistante_commercial   VARCHAR(50),
    responsable_achat       VARCHAR(50),
    representant            VARCHAR(100),
    op_en_cours             VARCHAR(50),
    status_vente            VARCHAR(50),
    statuts_clients         VARCHAR(50) DEFAULT '0',
    etat_client             VARCHAR(50) DEFAULT '0',
    production              VARCHAR(50),
    magasin_principal       VARCHAR(50),
    note                    VARCHAR(250),
    note_perm               TEXT,

    submitted_by            VARCHAR(100) NOT NULL REFERENCES users(login) ON UPDATE CASCADE,
    submitted_at            TIMESTAMP NOT NULL DEFAULT now(),
    status                  VARCHAR(20) NOT NULL DEFAULT 'pending'
                                CHECK (status IN ('pending', 'approved', 'rejected')),
    reviewed_by             VARCHAR(100) NULL REFERENCES users(login) ON UPDATE CASCADE,
    reviewed_at             TIMESTAMP NULL,
    rejection_reason        VARCHAR(250) NULL,
    approved_cle_opl        BIGINT NULL REFERENCES france_optique(cle_opl)
);

-- "Clients à valider" review queue filters to pending only, on every load.
CREATE INDEX idx_france_optique_en_attente_status ON france_optique_en_attente (status);

-- Commercial's own "my submissions" view filters to their own login.
CREATE INDEX idx_france_optique_en_attente_submitted_by ON france_optique_en_attente (submitted_by);
