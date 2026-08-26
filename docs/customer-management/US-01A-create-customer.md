# US-01A — Create Customer

**Jira:** ASSMS-1
**Branch:** `ASSMS-1-US-01A-Create-Customer`
**Repos touched:** `assms-customer-asset-service`, `assms-frontend`
**Service:** Customer & Asset Service · **Database:** `customerdb` · **Table:** `customers`

> As an Agent, I want to register a new customer so that assets and service requests
> can be linked to accurate customer information.

---

## 1. What was built

A customer registration flow, end to end:

```
React form  →  POST /api/customers  →  CustomerService  →  CustomerRepository  →  customerdb
```

| Layer | Files |
|---|---|
| Database | `database/migrations/V01__create_customers.sql` |
| Model | `Models/Customer.cs` |
| DTOs | `DTOs/CreateCustomerRequest.cs`, `DTOs/CustomerResponse.cs` |
| Helper | `Extensions/PhoneNormalizer.cs` |
| Data access | `Repositories/ICustomerRepository.cs`, `Repositories/CustomerRepository.cs` |
| Business logic | `Services/CustomerService.cs`, `Services/Result.cs` |
| HTTP | `Controllers/CustomersController.cs` |
| Frontend | `src/types/customer.ts`, `src/constants/customer.ts`, `src/services/customerService.ts`, `src/components/forms/CreateCustomerForm.tsx`, `src/pages/customers/CreateCustomerPage.tsx`, `src/routes/AppRoutes.tsx` |

---

## 2. The `customers` table

| Column | Type | Notes |
|---|---|---|
| `id` | CHAR(36) | PK. UUID generated in C#, not by MySQL |
| `name` | VARCHAR(100) | required |
| `phone` | VARCHAR(20) | required — stored **exactly as typed** |
| `phone_normalized` | VARCHAR(20) | required — digits only, canonical form |
| `phone_active_unique` | VARCHAR(20) | **generated, STORED** — `phone_normalized` when ACTIVE, else NULL |
| `address` | VARCHAR(255) | required |
| `customer_type` | VARCHAR(20) | CHECK: `INDIVIDUAL` \| `BUSINESS` |
| `email` | VARCHAR(255) | nullable |
| `status` | VARCHAR(20) | CHECK: `ACTIVE` \| `INACTIVE`, defaults `ACTIVE` |
| `created_at` | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP |
| `updated_at` | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP |

Indexes: PK on `id`, UNIQUE on `phone_active_unique`, plain index on `status`.

### Why the generated column

Acceptance criterion 3 says an **active** customer's phone number blocks another
active one. A plain `UNIQUE(phone)` would be wrong — it would permanently burn a
deactivated customer's number.

`phone_active_unique` holds the normalized phone only while the row is ACTIVE, and
`NULL` otherwise. MySQL treats multiple NULLs as distinct in a unique index, so
inactive rows never collide. Deactivating a customer frees their number
automatically; no application code is involved.

**Consequence for Sprint 4:** reactivating a customer can now fail if someone else
took their number in the meantime. That's arguably correct, but whoever implements
the deactivate/reactivate story needs to handle it with a sensible message rather
than a raw MySQL error.

---

## 3. Phone normalization

Stored twice on purpose:

- `phone` — what the Agent typed, for display
- `phone_normalized` — canonical form, for the duplicate check

Without this, `0771234567` and `+94 77 123 4567` are different strings and criterion 3
silently fails. Target shape is local, digits-only, leading zero:

| Input | Normalized |
|---|---|
| `0771112222` | `0771112222` |
| `+94 77 111 2222` | `0771112222` |
| `94771112222` | `0771112222` |
| `771112222` | `0771112222` |
| `077-111-2222` | `0771112222` |

Unrecognised shapes fall back to digits-only rather than being forced into a wrong
country's format.

---

## 4. API

Base: `/api/customers` · `[Produces("application/json")]` on the controller.

| Endpoint | Success | Failures |
|---|---|---|
| `POST /api/customers` | **201** + `Location` header | **400** validation, **409** duplicate phone |
| `GET /api/customers` | **200** + array | — |
| `GET /api/customers/{id}` | **200** | **404** |

All errors are RFC 7807 `ValidationProblemDetails` with an `errors` object keyed by
field name. Keys are **camelCase** across all sources — `DictionaryKeyPolicy` is set to
camelCase in `Program.cs`, because `Errors` is a dictionary and wasn't covered by the
default property-name policy.

The 409 is keyed on `phone`, deliberately, so the frontend renders it against the
phone input like any other field error — one code path handles both 400 and 409.

`GET` returns a customer regardless of status; the "active" qualifier applies only to
the duplicate-phone rule, not to reads.

The list endpoint was added by a later story — its ordering and empty-list behaviour are
covered in [Customer List & Detail](customer-list-and-detail.md).

Swagger UI: `/swagger` (Development only). XML summaries are enabled via
`GenerateDocumentationFile` + `IncludeXmlComments`.

---

## 5. Request flow

1. **Validation** — DataAnnotations on `CreateCustomerRequest`. With `[ApiController]`,
   ASP.NET returns 400 before the action runs, so there is no validation code in the
   controller or service.
2. **Normalize** the phone.
3. **Pre-check** — `ActivePhoneExistsAsync`. Cheap, and produces a clean error.
4. **Build** the `Customer` — `Guid.NewGuid().ToString()`, status `ACTIVE`, both phone fields.
5. **Insert** — 8 columns. Not `phone_active_unique` (generated), not the timestamps (defaults).
6. **Catch `MySqlException` 1062** — the pre-check can be beaten by a concurrent request;
   the unique index is what actually guarantees the rule. Both paths return the same
   `DuplicatePhone` error.
7. **Re-read** via `GetByIdAsync` before mapping, to pick up the real database timestamps.
   Mapping the in-memory object would return `0001-01-01`.

`CustomerService` returns `Result<T>` with a `ServiceError` enum rather than throwing —
duplicate phone is expected control flow, not an exceptional condition. **This is the
pattern to carry into the other three services.**

### Bug found during testing

The first valid POST returned 500: `Unable to cast object of type 'System.Guid' to
type 'System.String'`. MySqlConnector reads `CHAR(36)` as a `Guid` by default
(`GuidFormat=Char36`), so `reader.GetString(idOrdinal)` throws. The insert had
succeeded — only the read-back failed. Fixed by reading that one column via
`GetValue(...)?.ToString()`. The connection string was left unchanged.

---

## 6. Frontend

`VITE_CUSTOMER_API_URL` in `.env` (gitignored); `.env.example` is committed.
`src/vite-env.d.ts` declares it as a required `string` so it isn't `string | undefined`
at every call site.

`CUSTOMER_TYPES` lives in `constants/customer.ts` as an `as const` array, and
`types/customer.ts` derives the `CustomerType` union from it via `typeof`. Adding a
third type in one place cannot leave the other behind. (A TS `enum` wouldn't compile —
`erasableSyntaxOnly` is on.)

**Blank email sends `null`, not `''`.** The backend's `[EmailAddress]` passes on null but
rejects an empty string, so an untouched optional field would otherwise produce a
spurious 400.

CORS: named policy `"Frontend"`, origins read from `Cors:AllowedOrigins` in
configuration. No `AllowAnyOrigin`. Azure overrides the same key — no code change.

Bootstrap is imported in `main.tsx` before `./index.css` so project styles still win.
Errors use `is-invalid` + `invalid-feedback`, which Bootstrap only reveals when a
preceding sibling carries `is-invalid`.

---

## 7. Testing

**20 unit tests, all passing.** `dotnet test` from the repo root.

```
tests/CustomerAssetService.Tests/
├── UnitTests/
│   ├── PhoneNormalizerTests.cs      13 tests
│   └── CustomerServiceTests.cs       7 tests
├── Fakes/
│   └── FakeCustomerRepository.cs
├── IntegrationTests/                 (empty — QA)
└── TestData/                         (empty)
```

> Test files must live **inside** the test project. A `.cs` file under the outer
> `tests/` folder is not part of `CustomerAssetService.Tests.csproj` and will never run.

### The service tests

| Test | Asserts |
|---|---|
| Valid request creates active customer | success; non-empty Id, `ACTIVE`, both phone fields |
| Normalizes phone, keeps what was typed | `PhoneNormalized == "0771112222"`, `Phone == "077-111-2222"` |
| Active phone exists → fails without inserting | `DuplicatePhone`, **`CreateAsyncCallCount == 0`** |
| GetById when exists → mapped response | all nine fields match |
| GetById when missing → null | returns null |

The other two tests in the same file cover the list endpoint and belong to a later
story — see [Customer List & Detail](customer-list-and-detail.md).

`FakeCustomerRepository` implements `ICustomerRepository` with fields controlling
returns and recording arguments — no database, no mocking library. The third test's
call-count assertion is the valuable one: it proves the insert is never attempted.

### Coverage

```
31.8% line (90 / 283)  ·  26.6% branch (16 / 60)
```

Report: `docs/testing/coverage/index.html`

Covered: `PhoneNormalizer`, `CustomerService`. Near-zero: `CustomerRepository`,
`CustomersController`, `Program.cs`.

**That split is expected, not a defect.** The uncovered code is data access and startup
wiring — neither is meaningfully unit-testable. Raising this number needs integration
tests against the real container, not more unit tests.

Regenerate:

```powershell
dotnet test --collect:"XPlat Code Coverage"
reportgenerator -reports:"tests\CustomerAssetService.Tests\TestResults\**\coverage.cobertura.xml" -targetdir:"docs\testing\coverage" -reporttypes:Html
```

The `**` wildcard matters — the GUID folder name changes every run.

---

## 8. Handover to QA

Not covered by unit tests, and genuinely better as integration tests:

1. **Criterion 3 end to end against real MySQL.** Verified manually in the mysql client
   and at service level with a fake, but never in code against the actual generated
   column and unique index. Highest-value test in this story.
2. **The 1062 path.** `MySqlException` has no public constructor, so it can't be
   constructed in a unit test without reflection. Integration testing is the natural
   way to cover it.
3. **`CustomerRepository`** — all three methods, including the `Guid`/`string` read
   fixed above.
4. **Controller status codes** over HTTP.

Manual verification already done through Swagger UI: 201 with Location header, 400
field-level, 409 on `+94 77 111 2222` against a stored `077-111-2222`, GET returning
matching values, 404 on unknown id, and 400 on `customerType: "GOVERNMENT"`.

---

## 9. Running it locally

```powershell
# 1. Database (platform repo)
cd assms-platform-infrastructure\docker\local
docker compose up -d

# 2. Migration (service repo)
$env:MYSQL_PASSWORD = '<customer_svc password>'
.\scripts\development\apply_migrations.ps1 -Database customerdb -User customer_svc

# 3. API
dotnet run --project src\CustomerAssetService     # http://localhost:5037

# 4. Frontend
npm run dev                                        # http://localhost:5173
```

Requires `appsettings.Development.json` (see `appsettings.Example.json`) and frontend
`.env` (see `.env.example`). Neither is committed.

---

## 10. Decisions worth carrying forward

- **`Result<T>` + `ServiceError`** for expected failures, exceptions for genuine faults.
- **camelCase everywhere**, including dictionary keys, so the frontend has one convention.
- **Field-keyed errors even for 409**, so one rendering path handles all validation.
- **Versioned migrations**, append-only. Never edit a migration a teammate has run —
  add `V02__`. *Currently applied in this repo only; the other three still have the old
  flat `schema.sql` layout.*
- **DTOs separate from the model.** The API shape can change without touching persistence.
- **Re-read after insert** when the database populates columns.

---

## TODO before merge

- [ ] Per-class coverage percentages — fill in from `docs/testing/coverage/index.html`
- [ ] Confirm exact `ICustomerRepository` method signatures match section 5
- [x] Route path for the create-customer page (`AppRoutes.tsx`) — **`/customers/new`**
- [x] Whether `index.css` template styling stays or gets replaced — **stays.** The app theme
      lives in `src/styles/app.css`, imported after Bootstrap and `index.css` in `main.tsx`,
      and overrides the template leftovers it needs to (root font size, the `#root` rules)
