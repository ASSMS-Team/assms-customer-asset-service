# assms-customer-asset-service

## Overview

## Responsibilities

## Technology

## Project Structure

## Local Development

## Environment Variables

## Testing

## Deployment

## Documentation

The API documents itself. `GenerateDocumentationFile` is set and Swashbuckle reads
the resulting XML, so the `///` comments on the controllers and DTOs *are* the API
reference — there is no second copy to keep in step. Swagger UI is served at
`/swagger` in Development only; the raw document is at `/swagger/v1/swagger.json`.

The rules a new endpoint is expected to follow — routing, status codes, error
shapes, what has to be documented — are in
[docs/api/conventions.md](docs/api/conventions.md).

### Endpoints

| Method | Path | Purpose |
|---|---|---|
| `GET` | `/api/Health` | Liveness. |
| `GET` | `/api/Health/db` | Liveness plus database reachability; **503** when the database is unavailable. |
| `POST` | `/api/customers` | Register a customer. Created ACTIVE with a server-generated id. |
| `GET` | `/api/customers` | List customers, newest first. Optional `?status=ACTIVE\|INACTIVE`. |
| `GET` | `/api/customers/{id}` | One customer, whatever its status. |
| `PUT` | `/api/customers/{id}` | Update a customer. Only ACTIVE customers are editable. |
| `POST` | `/api/customers/{id}/deactivate` | Set status INACTIVE, which releases the phone number for reuse. Idempotent. |
| `GET` | `/api/customers/{customerId}/assets` | List that customer's assets, newest first. |
| `POST` | `/api/assets` | Register an asset against an existing **active** customer. Created ACTIVE with a server-generated id. |
| `GET` | `/api/assets/{id}` | One asset, whatever its status. |
| `PUT` | `/api/assets/{id}` | Update an asset. Ownership, status and creation time are not editable. |
| `POST` | `/api/assets/{id}/deactivate` | Set status INACTIVE. Idempotent. |

Deactivation is a `POST` to a verb path rather than a `DELETE`: the row is kept and
only its status changes. Both deactivate endpoints return **200** with the record as
it now stands, or **404** for an unknown id. Calling either one on a record that is
already INACTIVE succeeds and returns it unchanged **without writing**, so `updatedAt`
still reflects the deactivation itself rather than the last time somebody pressed the
button. Deactivating an asset releases nothing — a serial number stays taken, because
the physical unit behind a retired row still exists.

### Story notes

| Area | Documents |
|---|---|
| Customers | [US-01A — Create Customer](docs/customer-management/US-01A-create-customer.md) · [List & Detail](docs/customer-management/customer-list-and-detail.md) · [Update & Deactivate](docs/customer-management/update-and-deactivate-customer.md) |
| Assets | [US-02A — Create Asset](docs/asset-management/US-02A-create-asset.md) · [List & Detail](docs/asset-management/asset-list-and-detail.md) · [US-02C — Update Asset](docs/asset-management/update-asset.md) · [US-02D — Deactivate Asset](docs/asset-management/deactivate-asset.md) |
| Authentication | [US-AUTH-01 — Staff Login and RBAC](docs/authentication/US-AUTH-01-staff-login-rbac.md) · [Sprint 2 staging accounts](docs/authentication/staging-accounts.md) |
| Deployment | [Staging infrastructure](docs/deployment/staging-infrastructure.md) · [Customer CRUD sprint 1 staging verification](docs/deployment/customer-crud-sprint1-staging-verification.md) |

## Database Ownership

## Kafka Responsibilities

## Terraform Infrastructure

This repository owns only the Customer and Asset Service VM, NIC, service-specific NSG, and optional public IP. Shared resource-group and services-subnet values are consumed from the platform Terraform remote state or supplied through explicit overrides; this repository does not recreate the shared VNet or subnets.

## Continuous Integration

GitHub Actions runs on pull requests targeting `dev` or `main` and pushes to `dev` or `main`. CI validates Terraform formatting and both environment roots, then restores and builds the .NET 8 solution in Release mode and runs its tests with the configured XPlat coverage collector. Mandatory failures fail CI; test results and coverage are retained as workflow artifacts. No deployment occurs from this workflow.
