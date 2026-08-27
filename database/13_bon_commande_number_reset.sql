-- One-time reset, not a schema change: bon_commande_number_seq had
-- advanced to 7 from this session's own testing (see PROGRESS.md, Bon de
-- Commande phases). Badr wants real BC numbering to start at 50000 —
-- setval(..., false) makes the *next* nextval() return exactly 50000,
-- not 50001.

SELECT setval('bon_commande_number_seq', 50000, false);
