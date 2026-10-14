-- V07: Add dispatch_sync_status to staff_accounts to track Dispatch synchronization state.
ALTER TABLE staff_accounts ADD COLUMN dispatch_sync_status VARCHAR(50) NULL;
