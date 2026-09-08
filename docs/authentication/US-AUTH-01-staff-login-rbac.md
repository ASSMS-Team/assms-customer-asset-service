# US-AUTH-01 Staff login and role-based access

Customer & Asset Service owns `StaffAccount` in `customerdb`. It does not query or create Dispatch Technician data. A Technician role is an authorization value and does not imply a linked Dispatch record.

## Runtime configuration

Set these values outside Git:

- `Authentication__Jwt__SigningKey`: random secret of at least 32 UTF-8 bytes.
- `Authentication__BootstrapManager__Username`
- `Authentication__BootstrapManager__Email`
- `Authentication__BootstrapManager__Password`

The service creates one Manager only when no Manager exists and all bootstrap values are supplied. It stores a PBKDF2-SHA256 hash and never logs the password or hash. Remove bootstrap values from the runtime environment after the first successful creation.

For role verification, Development and Staging may supply `Authentication__SeedAccounts__{index}__Username`, `Email`, `Password` and `Role`. Supported roles are Agent, Dispatcher, Technician and Manager. These approved accounts are created only when the email is absent; plaintext credentials remain external to Git. Production ignores this seed list.

JWTs use the configured issuer and audience and expire after 60 minutes. Sprint 2 has no refresh-token or server-side revocation flow. Logout clears the browser session token; expiry requires login again.

## API

`POST /api/auth/login`

```json
{ "identifier": "manager@example.com", "password": "<password>" }
```

Success returns `accessToken`, `tokenType`, `expiresAt` and the public staff identity. Invalid credentials, unknown accounts and inactive accounts all return the same `401` response. The response never contains a password hash.

`GET /api/auth/me` verifies a bearer token. Missing, invalid and expired tokens receive `401`; an authenticated role outside an endpoint's allowed roles receives `403`.

## Customer & Asset permissions

| Operation | Agent | Dispatcher | Technician | Manager |
| --- | --- | --- | --- | --- |
| View customers and assets | Allow | Allow | Allow | Allow |
| Create, update or deactivate customers | Allow | Deny | Deny | Allow |
| Create, update or deactivate assets | Allow | Deny | Deny | Allow |

Other services remain responsible for enforcing their own job, dispatch and reporting permissions using the shared token contract.
