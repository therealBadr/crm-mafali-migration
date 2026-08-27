-- historique.fic_stk (BYTEA) already existed, scaffolded but unused — see
-- ParcoursClientPage.razor's file-scope comment history. It has no
-- companion column for the original filename/extension, but the legacy
-- viewer (Fen_Parcours_Client's "Voir le Fichier Stocké") reconstructs a
-- real filename on extract, so the new upload feature needs somewhere to
-- keep it.

ALTER TABLE historique ADD COLUMN fic_stk_nom VARCHAR(255);
