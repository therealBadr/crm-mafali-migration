-- Successful logins only — not a security-monitoring/failed-attempt log.
-- Legacy GPWHistoriqueConnexion had a manual "wipe entire history" button
-- (Risk Register #24); deliberately not replicating that here — if pruning
-- is ever needed, a retention job is the right tool, not a one-click delete.
CREATE TABLE login_history (
    id           BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    login        VARCHAR(100) NOT NULL REFERENCES users (login),
    logged_in_at TIMESTAMP NOT NULL DEFAULT now()
);

CREATE INDEX idx_login_history_login ON login_history (login);
CREATE INDEX idx_login_history_logged_in_at ON login_history (logged_in_at);
