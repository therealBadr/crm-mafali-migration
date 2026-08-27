-- Badr: the "Notes :" field on Parcours Client needs to hold more text
-- than the legacy's original 250-character cap allowed. Widened to TEXT
-- (unbounded) rather than picking a new arbitrary fixed limit, so this
-- doesn't just move the same complaint further down the road.
ALTER TABLE france_optique
    ALTER COLUMN note TYPE TEXT;

-- historique.note has to widen too, not just france_optique.note: every
-- "Appliquer" on Parcours Client unconditionally mirrors the client's
-- current Note into a fresh historique row as an audit-trail snapshot
-- (ParcoursClientPage.razor, the HistoriqueInput built in HandleApply).
-- Confirmed live: widening only france_optique.note left that INSERT
-- still capped at 250 chars, which made the *entire* Appliquer
-- transaction fail (and silently roll back the france_optique update
-- too) the moment a note actually used the new room. Not a separate
-- feature — the save path doesn't work end-to-end without this.
ALTER TABLE historique
    ALTER COLUMN note TYPE TEXT;
