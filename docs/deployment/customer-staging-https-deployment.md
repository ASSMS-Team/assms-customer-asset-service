# Customer Staging HTTPS Deployment

## Endpoint

- API FQDN: `https://assms-customer-staging-45ff260826.southeastasia.cloudapp.azure.com`
- Deployed image: `assms-customer-service:97ae25b01d046fab46856162918543612d6b4c9c`
- Architecture: `linux/arm64`

## Architecture

```text
Internet -> HTTPS 443 -> Nginx -> 127.0.0.1:8080 -> Customer Docker container
```

The Docker port is bound only to `127.0.0.1:8080`. Nginx terminates TLS and
proxies to that loopback target with `Host`, `X-Real-IP`, `X-Forwarded-For`,
and `X-Forwarded-Proto` headers.

The application trusts forwarded headers only from the observed Docker bridge
gateway `172.17.0.1` (IPv4-mapped IPv6 in the container socket), as documented
in `customer-staging-forwarded-headers.md`.

## Terraform and Network Exposure

Terraform updated the existing Static Standard Customer Public IP with the DNS
label `assms-customer-staging-45ff260826` and added inbound TCP rules only for
ports 80 and 443. No VM, NIC, MySQL, Kafka, or port 8080 resource was replaced
or exposed.

Port 80 redirects to HTTPS, except for the Let's Encrypt HTTP-01 challenge
path. Port 443 is handled by Nginx. SSH remains default-deny apart from
temporary `/32` operational rules that are removed after use.

## TLS

Nginx uses a trusted Let's Encrypt certificate for the API FQDN. Certbot's
system timer is enabled and active. A renewal dry run completed successfully.

## Application and CORS Verification

- `GET /api/health`: HTTP 200 over public HTTPS.
- `GET /api/health/db`: HTTP 200 over public HTTPS.
- Allowed CORS origin: `https://app-assms-frontend-staging-45ff260826.azurewebsites.net`.
- An unrelated origin receives no `Access-Control-Allow-Origin` permission.

The Customer database remains private. Prometheus and Grafana are unavailable;
ASSMS-18 remains deferred.

## Security Verification

- No plaintext public API proxy is used.
- No public port 8080, MySQL, or Kafka access was added.
- No permanent public SSH rule exists.
- Secrets remain in the protected VM environment file and were not recorded in
  this document or Git.

## Integrated Staging UI Verification

Manual browser verification is complete. The currently deployed HTTPS
container image remains `assms-customer-service:97ae25b01d046fab46856162918543612d6b4c9c`;
the Customer and Asset functional verification source SHA remains
`3577ba6042fbcc4364721ede517ff1c76dd1746d` as recorded in the CRUD evidence.

- US-01A through US-01D: PASS.
- US-02A through US-02D: PASS.
- Frontend SPA fallback: working.
- Frontend HTTPS, Customer API HTTPS, and the approved staging-origin CORS
  policy: working.
- No unexpected browser-console, CORS, mixed-content, routing, or runtime
  errors were observed during manual verification.
