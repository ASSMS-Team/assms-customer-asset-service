# Sprint 2 staging staff accounts

The following approved `StaffAccount` records are available in the ASSMS staging
`customerdb` for Sprint 2 authentication and role-based access verification.
They were verified on 8 September 2026 after restarting the deployed Customer &
Asset Service.

## Sign-in

- Frontend: <https://app-assms-frontend-staging-45ff260826.azurewebsites.net/login>
- Customer API: <https://assms-customer-staging-45ff260826.southeastasia.cloudapp.azure.com>
- Login endpoint: `POST /api/auth/login`
- The login identifier may be either the username or email address.

## Accounts

| Role | Username | Email | Password source |
| --- | --- | --- | --- |
| Manager | `bootstrap.manager` | `manager@assms.local` | Approved team credential store |
| Agent | `staging.agent` | `agent@assms.local` | Approved team credential store |
| Dispatcher | `staging.dispatcher` | `dispatcher@assms.local` | Approved team credential store |
| Technician | `staging.technician` | `technician@assms.local` | Approved team credential store |

Passwords are intentionally excluded from this repository. Obtain them from the
project owner through the team's private credential-sharing channel. Do not add
plaintext passwords, password hashes, JWT signing keys or authentication tokens
to GitHub issues, source files, documentation, screenshots or normal logs.

These accounts are for staging verification only. They do not create or link
Dispatch Service Technician records. Production does not load the staging seed
list.

## Verified behaviour

The 8 September 2026 staging smoke verification confirmed:

- all four accounts can authenticate and receive time-limited JWTs;
- the JWT identifies the staff account and assigned role;
- `/api/auth/me` accepts a valid Manager token;
- missing tokens and invalid credentials return `401`;
- a Technician can view customers but receives `403` when attempting to create one;
- logout clears the frontend session and protected routes return to `/login`; and
- the accounts remain available after a backend restart with seed credentials
  removed from `/etc/assms/customer.env`.

Rotate the staging passwords after the Sprint 2 evaluation or immediately if a
credential is shared outside the approved team channel.
