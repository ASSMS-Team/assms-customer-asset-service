# Customer & Asset Service Staging Infrastructure

## Current Infrastructure

| Item | Value |
|---|---|
| VM | `vm-assms-customer-staging` |
| Region | Southeast Asia |
| SKU / architecture | `Standard_B2pls_v2`, Arm64, 2 vCPU / 4 GiB |
| OS | Ubuntu 22.04 Arm64 with Standard_LRS OS disk |
| Private network | Primary services subnet `10.20.1.0/24`; assigned private IP `10.20.1.4` |
| Public IP | Static Standard resource exists for future controlled access |
| State key | `customer-asset-service/staging.tfstate` |
| Platform dependency | Reads `platform/staging.tfstate` |

Password authentication is disabled. The service NSG has no custom inbound rules, so Azure default deny-inbound remains effective. SSH, public application ports, MySQL, and Kafka are not exposed publicly.

## Application Status

The VM infrastructure is complete, but the Customer & Asset API, authentication/JWT/RBAC features, Docker runtime, and database configuration are **not deployed**. The VM is currently deallocated for staging cost control.

## ARM64 Note

The service targets .NET 8 and its inspected managed dependencies did not show existing x64-only runtime requirements. This is likely safe with conditions: future Dockerfiles must use multi-architecture .NET images and the completed container build/test must verify Arm64 compatibility.

## Future Deployment Work

1. Start the VM and configure restricted deployment access.
2. Implement/test the real Dockerfile locally.
3. Install Docker, configure non-secret runtime settings, and deploy the API.
4. Add reviewed private MySQL/Kafka connectivity and health checks.

No application deployment, public port, or secret configuration is included in the current staging Terraform.
