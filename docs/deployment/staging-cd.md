# Customer and Asset Service Staging CD

## Scope and Trigger

`.github/workflows/staging-cd.yml` is reusable. `Customer Asset Service CI`
calls it only after `terraform-validation` and `dotnet-build-test` succeed for
a `dev` push. The caller passes its exact `github.sha`, which the reusable
workflow checks out, builds, tags, and deploys. This keeps CI and CD on the
same commit without `workflow_run` or a default-branch dependency. Manual
dispatch is accepted only when the workflow is run from `dev`.

## Required GitHub Configuration

Create these GitHub **Actions variables** in this repository:

| Variable | Value / purpose |
|---|---|
| `AZURE_CLIENT_ID` | Client ID of the dedicated ASSMS GitHub OIDC application. |
| `AZURE_TENANT_ID` | Azure tenant ID. |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID. |
| `AZURE_RESOURCE_GROUP` | `rg-assms-staging`. |
| `CUSTOMER_API_URL` | `https://assms-customer-staging-45ff260826.southeastasia.cloudapp.azure.com` |
| `CUSTOMER_NSG_NAME` | `nsg-assms-customer-staging`. |
| `CUSTOMER_TEMP_SSH_RULE_PRIORITY` | An unused NSG priority, such as `1000`. |
| `CUSTOMER_VM_HOST` | Customer staging VM public DNS hostname. |
| `CUSTOMER_VM_SSH_USERNAME` | `assmsadmin`. |
| `CUSTOMER_VM_SSH_KNOWN_HOSTS` | Verified `known_hosts` entry for the Customer VM. |

Create this GitHub **Actions secret** only:

| Secret | Purpose |
|---|---|
| `CUSTOMER_DEPLOY_SSH_PRIVATE_KEY` | Dedicated CD-only SSH private key. |

Do not store the MySQL password, `/etc/assms/customer.env`, TLS private key, or
developer SSH keys in GitHub Actions.

## Azure OIDC Setup

No ASSMS OIDC application registration currently exists. Before creating a
federated credential, an authorized GitHub administrator must determine this
repository's actual OIDC subject configuration. Do not assume the legacy branch
subject format: newer repositories may use immutable repository-claim subject
customization.

Perform this one-time GitHub check with a token authorized to read repository
Actions OIDC settings, then record the returned `include_claim_keys` / default
status:

```text
GET /repos/ASSMS-Team/assms-customer-asset-service/actions/oidc/customization/sub
```

Then issue a token from a tightly controlled `dev`-only diagnostic workflow or
inspect the GitHub OIDC configuration UI, and use the resulting `sub` claim
verbatim in Azure. The Azure federated credential issuer remains
`https://token.actions.githubusercontent.com` and its audience remains
`api://AzureADTokenExchange`.

Assign only `Network Contributor` at this NSG scope:

```text
/subscriptions/45ff51f1-702e-4ba3-98ff-435d3b08a04b/resourceGroups/rg-assms-staging/providers/Microsoft.Network/networkSecurityGroups/nsg-assms-customer-staging
```

The workflow uses OIDC; it does not use an Azure client secret.

## Dedicated Deployment Key

Generate a new CD-only Ed25519 key pair on a secure administrator machine. Add
only its public key to `assmsadmin` while preserving existing authorized keys.
Store the private key solely in `CUSTOMER_DEPLOY_SSH_PRIVATE_KEY`, and record a
verified host-key entry in `CUSTOMER_VM_SSH_KNOWN_HOSTS`. Do not reuse a
developer key.

## Deployment Flow

1. Build `linux/arm64` image `assms-customer-service:${GITHUB_SHA}` with Buildx.
2. Save a compressed image archive and transfer it over temporary SSH.
3. Load the exact image on the ARM64 VM and start the container with
   `/etc/assms/customer.env`, `--restart unless-stopped`, and
   `127.0.0.1:8080:8080`.
4. Confirm Nginx is active and verify public HTTPS `/api/health` and
   `/api/health/db`; either non-200 response fails the workflow.

The workflow calculates the GitHub runner public IPv4, adds only that `/32` to
a temporary TCP 22 NSG rule, and removes the rule with an `if: always()` step.
It never exposes port 8080.

## Rollback and Emergency Procedure

For rollback, use the prior known-good image tag already loaded on the VM,
temporarily allow a verified operator `/32`, replace the container with the
prior tag using the protected environment file, verify both HTTPS health
endpoints, then remove the temporary rule. A new workflow run from a known-good
`dev` SHA is preferred when available.

If a GitHub runner is forcibly cancelled or terminated, its `always()` cleanup
may not execute. An operator must immediately delete the corresponding
`gh-cd-customer-<run-id>` rule from `nsg-assms-customer-staging`, verify TCP 22
has no public rule, and review the failed workflow run.

## Known Limitations

This workflow does not deploy production, run Terraform, manage MySQL/Kafka,
change Nginx, or configure CD secrets automatically. The required Azure OIDC
identity, least-privilege role assignment, dedicated SSH key, GitHub variables,
and GitHub secret must be configured before it can run successfully.
