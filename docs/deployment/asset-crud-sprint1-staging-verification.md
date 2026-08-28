# Asset CRUD Sprint 1 Staging Verification

## Deployment Evidence

- Environment: Azure Staging
- Repository: `assms-customer-asset-service`
- Verification branch: `ASSMS-2-asset-crud-staging-deployment`
- Deployed application source SHA: `3577ba6042fbcc4364721ede517ff1c76dd1746d`
- CI: PASS (manually verified for the exact deployed SHA)
- VM: `vm-assms-customer-staging` (`aarch64` / ARM64)
- Container: `assms-customer-service`
- Image: `assms-customer-service:3577ba6042fbcc4364721ede517ff1c76dd1746d`
- Binding: `127.0.0.1:8080` only; smoke tests used an SSH tunnel.
- `GET /api/health`: HTTP 200
- `GET /api/health/db`: HTTP 200

## Initial Schema Finding

Before Asset migration, `customerdb` contained `customers` only. The `assets` and `schema_migrations` tables were absent. This was recorded before any application smoke test; no application defect was inferred before the authoritative migration check.

## Schema Migration

Authoritative repository migrations:

- `database/migrations/V01__create_customers.sql`
- `database/migrations/V02__create_assets.sql`
- `database/migrations/V03__add_asset_status.sql`

Safety review:

- V01 and V02 use `CREATE TABLE IF NOT EXISTS` and are safe to rerun.
- V03 uses `ALTER TABLE` and is not independently rerunnable. It was applied once through the repository's `schema_migrations` tracking approach, with a guard that refused to apply it if `status` already existed without a tracking record.
- No `DROP`, `TRUNCATE`, schema replacement, or customer/asset data deletion was used.

Result: PASS. Migration records for V01, V02, and V03 exist. The `assets` table has the expected columns, including lifecycle `status`; primary key, serial-number unique index, customer index, and status index are present.

## US-02 Smoke-Test Result

An ACTIVE synthetic staging customer was created only because no existing ACTIVE synthetic customer was available. One synthetic asset lifecycle was used for every Asset story test.

| Story | Operation | Result |
| --- | --- | --- |
| ASSMS-2 / US-02A | Register an asset | PASS — HTTP 201; the asset was linked to the synthetic customer and had `ACTIVE` lifecycle status. |
| ASSMS-2 / rejection | Repeat registration with the same serial | PASS — HTTP 409. |
| ASSMS-78 / US-02B | List customer assets and open detail | PASS — HTTP 200; list contained the asset and detail fields matched. |
| ASSMS-78 / not-found | List assets for an unknown customer | PASS — HTTP 404. |
| ASSMS-79 / US-02C | Update asset location and retrieve | PASS — HTTP 200; the changed location persisted. |
| ASSMS-79 / rejection | Update with invalid asset type | PASS — HTTP 400; the stored asset remained unchanged. |
| ASSMS-80 / US-02D | Deactivate asset and retrieve | PASS — HTTP 200; persisted lifecycle status is `INACTIVE`. |
| ASSMS-80 / repeat | Deactivate the same asset again | PASS — HTTP 200; idempotent result remains `INACTIVE`. |

Asset lifecycle status was verified as `ACTIVE` / `INACTIVE`; it was not treated as warranty status. No ownership-transfer behavior was tested or implemented.

## Operational Evidence

- Standard ASP.NET Core/container logs were reviewed after smoke testing.
- No unexpected fatal or unhandled application errors were detected.
- No database connectivity errors were detected.
- Serilog is not implemented in this service; no Serilog evidence is claimed.
- Prometheus/Grafana: unavailable — ASSMS-18 deferred.

## Security Verification

- MySQL remained private.
- Kafka remained private.
- Customer API remained loopback-only and was not publicly exposed.
- `/etc/assms/customer.env` contents were never read, printed, copied, or committed.
- Secrets remained outside Git and the image.
- Temporary SSH access was restricted to one client IPv4 `/32` and removed after verification.
- No custom Customer NSG inbound rule remains.

## Frontend

No merged React Asset implementation was found in `assms-frontend`; frontend staging deployment was not required for this API verification batch.
