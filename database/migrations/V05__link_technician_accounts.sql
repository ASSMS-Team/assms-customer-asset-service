-- Logical Dispatch ID only: no cross-service database foreign key or reads.
-- The migration runner checks column/index existence to allow safe retry.
ALTER TABLE staff_accounts ADD COLUMN technician_id CHAR(36) NULL;
ALTER TABLE staff_accounts ADD UNIQUE KEY uq_staff_accounts_technician (technician_id);
