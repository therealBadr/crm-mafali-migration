-- Powers the Heure Rappel "view as" timezone toggle in ParcoursClientPage
-- (Badr, 2026-10-05): the record's own Heure Rappel never changes, but
-- clicking the clock icon can show what that same moment reads as in
-- Maroc's timezone, using each Pays' IANA zone name for the conversion
-- math. Etats Unis spans 6 real timezones and MONDE isn't an actual
-- country, so both are deliberately left NULL — confirmed with Badr to
-- just skip the toggle for clients in either.

ALTER TABLE pays ADD COLUMN fuseau_horaire VARCHAR(50);

UPDATE pays SET fuseau_horaire = 'Europe/Berlin'     WHERE nom_pays = 'Allemagne';
UPDATE pays SET fuseau_horaire = 'Europe/Andorra'    WHERE nom_pays = 'Andorre';
UPDATE pays SET fuseau_horaire = 'Europe/Brussels'   WHERE nom_pays = 'Belgique';
UPDATE pays SET fuseau_horaire = 'Europe/Madrid'     WHERE nom_pays = 'Espagne';
UPDATE pays SET fuseau_horaire = 'Europe/Paris'      WHERE nom_pays = 'France';
UPDATE pays SET fuseau_horaire = 'Europe/Luxembourg' WHERE nom_pays = 'Luxembourg';
UPDATE pays SET fuseau_horaire = 'Africa/Casablanca' WHERE nom_pays = 'Maroc';
UPDATE pays SET fuseau_horaire = 'Europe/Monaco'     WHERE nom_pays = 'Monaco';
UPDATE pays SET fuseau_horaire = 'Europe/Amsterdam'  WHERE nom_pays = 'Pays-Bas';
UPDATE pays SET fuseau_horaire = 'Europe/London'     WHERE nom_pays = 'Royaume-Uni';
UPDATE pays SET fuseau_horaire = 'Europe/Zurich'     WHERE nom_pays = 'Suisse';
