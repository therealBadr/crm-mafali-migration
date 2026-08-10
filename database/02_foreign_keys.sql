-- Mafali CRM — Phase C: add FKs AFTER the migration script has loaded real
-- data into the tables from 01_tables.sql. Do not run this against an empty
-- database — there'd be nothing wrong to catch yet, and worse, it silently
-- removes the whole point of NOT VALID (skip checking rows that already
-- exist) since at that point every row would count as "already existing."
--
-- Confirmed: real HFSQL data is expected to have typos/blanks in
-- famille/franchise/pays, so these must not be checked against pre-existing
-- rows. Run VALIDATE CONSTRAINT later, per table, only once that table's
-- mismatches have been resolved.

ALTER TABLE france_optique ADD CONSTRAINT fk_france_optique_famille
    FOREIGN KEY (famille) REFERENCES type_famille(nom_famille)
    ON DELETE SET NULL ON UPDATE CASCADE NOT VALID;

ALTER TABLE france_optique ADD CONSTRAINT fk_france_optique_franchise
    FOREIGN KEY (franchise) REFERENCES type_franchise(nom_franchise)
    ON DELETE SET NULL ON UPDATE CASCADE NOT VALID;

ALTER TABLE france_optique ADD CONSTRAINT fk_france_optique_franchise2
    FOREIGN KEY (franchise2) REFERENCES type_franchise(nom_franchise)
    ON DELETE SET NULL ON UPDATE CASCADE NOT VALID;

ALTER TABLE france_optique ADD CONSTRAINT fk_france_optique_franchise3
    FOREIGN KEY (franchise3) REFERENCES type_franchise(nom_franchise)
    ON DELETE SET NULL ON UPDATE CASCADE NOT VALID;

ALTER TABLE france_optique ADD CONSTRAINT fk_france_optique_franchise4
    FOREIGN KEY (franchise4) REFERENCES type_franchise(nom_franchise)
    ON DELETE SET NULL ON UPDATE CASCADE NOT VALID;

ALTER TABLE france_optique ADD CONSTRAINT fk_france_optique_pays
    FOREIGN KEY (pays) REFERENCES pays(nom_pays)
    ON DELETE SET NULL ON UPDATE CASCADE NOT VALID;

ALTER TABLE historique ADD CONSTRAINT fk_historique_num_client
    FOREIGN KEY (num_client) REFERENCES france_optique(cle_opl)
    ON DELETE RESTRICT ON UPDATE CASCADE NOT VALID;

ALTER TABLE ca ADD CONSTRAINT fk_ca_cle_opl
    FOREIGN KEY (cle_opl) REFERENCES france_optique(cle_opl)
    ON DELETE RESTRICT ON UPDATE CASCADE NOT VALID;
