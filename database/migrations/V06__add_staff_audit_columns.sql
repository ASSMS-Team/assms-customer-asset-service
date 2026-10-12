-- Sprint 4: audit metadata for Manager-created staff accounts.
-- created_at already exists with a server default; add created_by to track
-- which Manager provisioned the account. Legacy/bootstrap accounts will have
-- NULL, displayed as "N/A" in the UI.
-- Also adds full_name for display purposes (username is the login identifier).
USE customerdb;

ALTER TABLE staff_accounts ADD COLUMN full_name VARCHAR(200) NOT NULL DEFAULT '';
ALTER TABLE staff_accounts ADD COLUMN created_by CHAR(36) NULL;
