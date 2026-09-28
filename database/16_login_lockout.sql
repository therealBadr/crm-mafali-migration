-- Brute-force protection on login. login_history.sql was a deliberate
-- decision to stay "successful logins only, not a security-monitoring log"
-- (see its own header) — this doesn't reverse that. It's a separate
-- concern: a transient counter to actually block repeated password
-- guessing, not a permanent audit trail. Resets to 0/NULL on a successful
-- login, so there's nothing here to browse historically.
ALTER TABLE users
    ADD COLUMN failed_login_count INT NOT NULL DEFAULT 0,
    ADD COLUMN lockout_until TIMESTAMP NULL;
