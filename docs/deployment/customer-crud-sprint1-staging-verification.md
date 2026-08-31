# Customer CRUD Sprint 1 Staging Verification

## Deployment Evidence

- Environment: Azure Staging
- Repository: `assms-customer-asset-service`
- Verification branch: `ASSMS-1-customer-crud-staging-deployment`
- Deployed application source SHA: `659592d77d284f59715c7735b065e8addfc3b578`
- CI: PASS (manually verified for the deployed source SHA)
- VM: `vm-assms-customer-staging` (`aarch64`)
- Container: `assms-customer-service`
- Image: `assms-customer-service:659592d77d284f59715c7735b065e8addfc3b578`
- Binding: `127.0.0.1:8080` only, verified through an SSH tunnel
- `GET /api/health`: HTTP 200
- `GET /api/health/db`: HTTP 200

## Smoke-Test Result

The four Customer CRUD stories are **blocked** at the first create operation.

| Story | Operation | Result | Evidence |
| --- | --- | --- | --- |
| ASSMS-1 / US-01A | `POST /api/customers` | BLOCKED — HTTP 500 | The database connection succeeds, but the `customerdb.customers` table is absent. |
| ASSMS-75 / US-01B | `GET /api/customers/{id}` | NOT RUN | No customer ID was created because US-01A failed. |
| ASSMS-76 / US-01C | `PUT /api/customers/{id}` | NOT RUN | No customer lifecycle could be established. |
| ASSMS-77 / US-01D | `POST /api/customers/{id}/deactivate` | NOT RUN | No customer lifecycle could be established. |

## Validation-Failure Result

The duplicate active-phone validation test was not validly reached because the initial create request failed before a customer could be persisted.

## Application Logs

The service uses standard ASP.NET Core/container logging; Serilog is not implemented.

- Startup and health endpoints operate successfully.
- CRUD requests produce an unhandled `MySqlConnector.MySqlException` because `customerdb.customers` does not exist.
- No credentials or connection-string values were recorded in this document.

## Required Follow-Up

Provision the Customer Service-owned database schema/migration that creates the `customers` table, then redeploy or rerun the smoke lifecycle. This is a database/application delivery prerequisite; no business logic was changed during this verification.

## Monitoring

Prometheus/Grafana: unavailable — ASSMS-18 deferred.

## Security Checks

- Secrets remain external to Git and the image.
- Customer API remains bound to VM loopback and is not publicly exposed.
- Temporary SSH access was limited to a single `/32` source and removed after testing.
- MySQL remains private.
- Kafka remains private.

## Schema Remediation and Successful Rerun

### Authoritative Schema Source

- Source: `database/migrations/V01__create_customers.sql`
- Safety review: uses `CREATE TABLE IF NOT EXISTS`; no `DROP`, `TRUNCATE`, or other destructive statements.
- Applied privately from the Customer VM using the existing root-only runtime connection configuration.
- No customer data was read, altered, or deleted during schema application.

### Schema Verification

- `customers` table: present.
- Columns: `id`, `name`, `phone`, `phone_normalized`, `phone_active_unique`, `address`, `customer_type`, `email`, `status`, `created_at`, `updated_at`.
- Constraints/indexes: primary key on `id`, unique key `uq_customers_phone_active`, and `idx_customers_status`.
- Lifecycle support: `status` defaults to `ACTIVE`; `phone_active_unique` is generated only for active customers; timestamps are database-managed.

### Rerun Evidence

Rerun timestamp: `2026-08-27T12:17:44Z`.

| Story | Operation | Result |
| --- | --- | --- |
| ASSMS-1 / US-01A | Create a unique synthetic staging customer | PASS — HTTP 201. |
| ASSMS-1 / validation | Create a second active customer with the same phone | PASS — HTTP 409. |
| ASSMS-75 / US-01B | Retrieve the created customer | PASS — HTTP 200; ID and persisted fields matched. |
| ASSMS-76 / US-01C | Update address, then retrieve | PASS — HTTP 200; updated address persisted. |
| ASSMS-77 / US-01D | Deactivate, then retrieve | PASS — HTTP 200; persisted status is `INACTIVE`. |

Both `GET /api/health` and `GET /api/health/db` returned HTTP 200 during the rerun. Post-migration standard ASP.NET Core/container log review found no fatal, unhandled, or missing-table errors.

## Final Verification Outcome

- Docker deployment: PASS — the ARM64 image was deployed successfully.
- Schema deployment: PASS — `database/migrations/V01__create_customers.sql` created the required Customer schema.
- US-01A Create Customer: PASS — HTTP 201.
- Duplicate active-phone validation: PASS — HTTP 409.
- US-01B View Customer: PASS — HTTP 200 and persisted values matched.
- US-01C Update Customer: PASS — HTTP 200 and the changed value persisted.
- US-01D Deactivate Customer: PASS — HTTP 200 and subsequent retrieval showed `INACTIVE`.
- Application logging: standard ASP.NET Core/container logs reviewed; no unexpected fatal or unhandled errors after the schema fix.
- Prometheus/Grafana: unavailable — ASSMS-18 deferred.
- MySQL public exposure: none.
- Customer API public exposure: none; the API remained loopback-only during verification.
- Business code modified: no.
