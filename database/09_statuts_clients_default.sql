-- ParcoursClientPage's Statut Client dropdown only ever knows about four
-- values: "-1" (not set — its own <option value="-1"> sentinel, documented
-- in ParcoursClientPage.razor as the real legacy "not set" convention,
-- confirmed by 486 of 489 real rows), "Payés", "Impayés", "Bloqués". Both
-- france_optique and historique were scaffolded with statuts_clients
-- defaulting to '0' instead — a stray value that matches none of those
-- options, so any client created without the column being explicitly set
-- (every "Nouveau Client" save) got a Statut Client dropdown that silently
-- rendered blank, with nothing selected. Not a rendering bug — '0' was
-- simply never a value the app's own sentinel convention recognizes.
--
-- historique is a snapshot/audit trail — its handful of existing '0' rows
-- are left as-is (that column isn't displayed anywhere in the history
-- grid, so they're harmless), only the default is fixed here so future
-- rows don't inherit the same stray value.

ALTER TABLE france_optique ALTER COLUMN statuts_clients SET DEFAULT '-1';
ALTER TABLE historique ALTER COLUMN statuts_clients SET DEFAULT '-1';

UPDATE france_optique SET statuts_clients = '-1' WHERE statuts_clients = '0';
