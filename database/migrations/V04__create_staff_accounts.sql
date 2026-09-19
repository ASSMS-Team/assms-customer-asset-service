-- StaffAccount belongs only to Customer & Asset Service. It has no Dispatch Technician foreign key.
USE customerdb;

CREATE TABLE IF NOT EXISTS staff_accounts (
    id                  CHAR(36)      NOT NULL,
    username            VARCHAR(100)  NOT NULL,
    username_normalized VARCHAR(100)  NOT NULL,
    email               VARCHAR(255)  NOT NULL,
    email_normalized    VARCHAR(255)  NOT NULL,
    password_hash       VARCHAR(512)  NOT NULL,
    role                VARCHAR(20)   NOT NULL,
    status              VARCHAR(20)   NOT NULL DEFAULT 'ACTIVE',
    created_at          TIMESTAMP     NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at          TIMESTAMP     NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,

    PRIMARY KEY (id),
    UNIQUE KEY uq_staff_accounts_username (username_normalized),
    UNIQUE KEY uq_staff_accounts_email (email_normalized),
    KEY idx_staff_accounts_role_status (role, status),
    CONSTRAINT chk_staff_accounts_role CHECK (role IN ('Agent', 'Dispatcher', 'Technician', 'Manager')),
    CONSTRAINT chk_staff_accounts_status CHECK (status IN ('ACTIVE', 'INACTIVE'))
);
