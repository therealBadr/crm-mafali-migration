-- Retires the two-table proposal design that 18_france_optique_en_attente.sql
-- introduced and 19_france_optique_validation.sql replaced. Split out of 19
-- on purpose: EF only fails on a missing table at query time, not at build
-- time, so dropping this while the old pages still referenced it would have
-- produced an app that compiled cleanly and then errored the moment someone
-- opened the validation screen. Safe to run now that ClientProposalService
-- and the four pages built against it are gone.
--
-- Contents at drop time (checked, not assumed): one row, id_attente=5,
-- raison_sociale="proposition", every other field blank, submitted by the
-- commercial account "David David" on 2026-10-07 one minute after logging in
-- — i.e. someone trying out the new screen, not a real prospect. Confirmed
-- discardable with Badr before running this. Besides that, the table only
-- held rows created while building and
-- testing the proposal flow, all of which were cleaned up at the time.

DROP TABLE IF EXISTS france_optique_en_attente;
