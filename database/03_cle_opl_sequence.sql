-- New clients created through the new app need a safe way to get a new
-- cle_opl value — the legacy app assigned these via a manual max+1 scan
-- (searching downward from 999,999,999), a known concurrency bug (two users
-- creating a client at once could compute the same value). This sequence
-- replaces that with Postgres's own atomic nextval(), same column, same
-- meaning, just safe.
--
-- Starting point matters: real data has two clusters — 476 clients in a
-- normal low range (max 185,644), and 13 recent clients up near 999,999,999
-- (the legacy's own downward-scan behavior, once the low range filled up —
-- not a data quality issue, confirmed against real dates/company names).
-- Starting at 200,000 stays clear of both ranges rather than picking up
-- right after the near-billion cluster, which would give every new client
-- an ugly, confusing ID.
--
-- Run once, after 01_tables.sql and the data migration. Not tied to
-- 02_foreign_keys.sql's ordering — can run before or after it.

CREATE SEQUENCE IF NOT EXISTS france_optique_cle_opl_seq;
SELECT setval('france_optique_cle_opl_seq', 200000, false);
