# US-02A — Create Asset

**Jira:** _not recorded — fill in_
**Repos touched:** `assms-customer-asset-service`, `assms-frontend`
**Service:** Customer & Asset Service · **Database:** `customerdb` · **Table:** `assets`
**Follows:** [US-01A — Create Customer](../customer-management/US-01A-create-customer.md)

> As an Agent, I want to register a customer's equipment so that service requests can be
> raised against a specific unit rather than against the customer in general.

---

## 1. What was built

An asset registration flow, end to end:

```
React form  →  POST /api/assets  →  AssetService  →  AssetRepository  →  customerdb
                                          ↓
                                 ICustomerRepository  (owner must exist and be ACTIVE)
```

| Layer | Files |
|---|---|
| Database | `database/migrations/V02__create_assets.sql` |
| Model | `Models/Asset.cs` |
| DTOs | `DTOs/CreateAssetRequest.cs`, `DTOs/AssetResponse.cs` |
| Helper | `Extensions/SerialNormalizer.cs` |
| Data access | `Repositories/IAssetRepository.cs`, `Repositories/AssetRepository.cs` |
| Business logic | `Services/AssetService.cs`, `Services/Result.cs` (two new `ServiceError` values) |
| HTTP | `Controllers/AssetsController.cs` |
| Frontend | `src/constants/asset.ts`, `src/types/asset.ts`, `src/services/assetService.ts`, `src/components/forms/AssetForm.tsx`, `src/pages/assets/CreateAssetPage.tsx`, `src/routes/AppRoutes.tsx`, `src/components/layout/AppLayout.tsx` |

The customer list endpoint also gained an optional status filter, because the asset form
needs *active customers only* — see section 6.

---

## 2. The `assets` table

| Column | Type | Notes |
|---|---|---|
| `id` | CHAR(36) | PK. UUID generated in C#, not by MySQL |
| `customer_id` | CHAR(36) | FK → `customers(id)`, `ON DELETE RESTRICT` |
| `asset_type` | VARCHAR(20) | CHECK: `AIR_CONDITIONER` \| `REFRIGERATOR` \| `WASHING_MACHINE` \| `WATER_HEATER` \| `OTHER` |
| `model` | VARCHAR(100) | required |
| `serial_number` | VARCHAR(100) | required — stored **exactly as typed** |
| `serial_normalized` | VARCHAR(100) | required — uppercase alphanumerics, canonical form |
| `installation_date` | DATE | required — a date, not a timestamp |
| `location` | VARCHAR(255) | required — where the unit sits on the premises |
| `notes` | VARCHAR(1000) | nullable |
| `created_at` | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP |
| `updated_at` | TIMESTAMP | DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP |

Indexes: PK on `id`, UNIQUE on `serial_normalized`, plain index on `customer_id`.

Declared inline in the `CREATE TABLE` body, like `V01`, so the whole migration stays
safe to re-run — MySQL 8 has no `CREATE INDEX IF NOT EXISTS`.

### Why the serial is unique table-wide, not per customer

A serial number identifies one physical unit. The same unit cannot be owned by two
customers at once, so scoping the uniqueness to `(customer_id, serial_normalized)`
would let the same machine be registered twice under different owners — which is the
data error the constraint exists to prevent.

Note the contrast with `customers`: there, uniqueness is scoped to ACTIVE rows via a
generated column, because a *phone number* is genuinely reusable once a customer
retires. A serial is not — there is no status on `assets` to scope by, and an asset row
is never retired in place.

### Why `ON DELETE RESTRICT`

Deleting a customer who still owns equipment is a mistake to surface, not a reason to
destroy their service history. `CASCADE` would silently take the assets with them.

In practice this never fires from the application: customers are deactivated, never
deleted. It guards against a manual `DELETE` in a mysql client.

---

## 3. Serial normalization

Stored twice, for the same reason phones are:

- `serial_number` — what the Agent typed, for display
- `serial_normalized` — canonical form, for the duplicate check

Manufacturers print serials with hyphens, spaces and slashes that carry no meaning, and
an Agent copying one off a unit will not reproduce them consistently. Target shape is
uppercase, alphanumerics only:

| Input | Normalized |
|---|---|
| `ABC-123` | `ABC123` |
| `abc 123` | `ABC123` |
| `Abc123` | `ABC123` |
| `ABC/123` | `ABC123` |
| `  abc-123  ` | `ABC123` |

Null, empty and whitespace normalize to `""`, as does a string with nothing
alphanumeric in it (`---`). Those cases never reach the database — `[Required]` rejects
them as a 400 first — but the helper is total rather than throwing.

---

## 4. API

Base: `/api/assets` · `[Produces("application/json")]` on the controller.

| Endpoint | Success | Failures |
|---|---|---|
| `POST /api/assets` | **201** + `Location` header | **400** validation, **409** unknown/inactive customer, **409** duplicate serial |
| `GET /api/assets/{id}` | **200** | **404** |

All errors are RFC 7807 `ValidationProblemDetails` keyed by field name, camelCase, the
same as US-01A. The three 409s are told apart by **which key** carries the message:

| Condition | Key | Message |
|---|---|---|
| No customer with that id | `customerId` | No customer exists with this id. |
| Customer is INACTIVE | `customerId` | This customer has been deactivated and cannot have new assets registered. |
| Serial already registered | `serialNumber` | An asset already exists with this serial number. |

### Why an unknown customer is a 409, not a 404

`404` means *the addressed resource* does not exist. `POST /api/assets` addresses the
assets collection, which exists — it is a **field in the body** that is wrong. So it is
a business-rule conflict keyed on that field, and the frontend renders it against the
customer dropdown like any other validation error.

This rule is now written down in [API Conventions](../api/conventions.md#status-codes)
so future endpoints follow it rather than rediscovering it.

Swagger UI: `/swagger` (Development only).

---

## 5. Request flow

1. **Validation** — DataAnnotations on `CreateAssetRequest`. `[ApiController]` returns the
   400 before the action runs, so there is no validation code in controller or service.
2. **Load the owner** — `ICustomerRepository.GetByIdAsync`. Missing → `CustomerNotFound`.
3. **Check the owner is ACTIVE** — otherwise `CustomerInactive`.
4. **Normalize** the serial.
5. **Pre-check** — `SerialExistsAsync`. Cheap, and produces a clean error.
6. **Build** the `Asset` — `Guid.NewGuid().ToString()`, both serial fields.
7. **Insert** — 9 columns. Not the timestamps (defaults).
8. **Catch `MySqlException` 1062** — the pre-check can be beaten by a concurrent request;
   the unique index is what actually guarantees the rule. Both paths return `DuplicateSerial`.
9. **Re-read** via `GetByIdAsync` before mapping, to pick up the real database timestamps.

Steps 2–3 run **before** the serial pre-check: there is no point asking the database
about a serial for an asset that cannot be created anyway. A unit test pins that
ordering (`SerialExistsAsyncCallCount == 0` on both customer failures).

`AssetService` takes **two** repositories. That is the first service here to depend on
another aggregate's data access, and it is deliberate — validating the FK target is a
business rule, and pushing it into a SQL join would hide it.

### The `ServiceError` values

`CustomerNotFound` and `DuplicateSerial` are new. **`CustomerInactive` was reused**, not
duplicated: in the customer update story it means "the customer this operation acts on
is not ACTIVE, so refuse", and here it means the same thing about the customer being
referenced. One meaning, one value — only the controller's wording differs, and wording
is presentation.

`CustomerNotFound` is *not* redundant with `NotFound` for the reason in section 4:
`NotFound` becomes a 404, this becomes a field-keyed 409.

### `DateOnly` and MySqlConnector

`installation_date` is a `DATE`, so the model uses `DateOnly` — the day a unit was
fitted has no time of day, and a `DateTime` would invent one.

Writing and reading are **not symmetric**, which is worth knowing before copying this:

- **Write** — `AddWithValue` takes a `DateOnly` as-is. No conversion needed.
- **Read** — the column reports `GetFieldType` as `System.DateTime` and `GetValue` boxes
  one, so `GetDateTime` hands back a midnight `DateTime`. `GetFieldValue<DateOnly>(ordinal)`
  is what makes the driver do the conversion.

### Bug found during testing

Submitting the empty form returned a 400 that **rendered nothing**:

```json
{"errors":{ "request": ["The request field is required."],
            "$.installationDate": ["The JSON value could not be converted to System.DateOnly…"] }}
```

An `<input type="date">` yields `""` when blank. That fails **JSON deserialization**, not
validation, which aborts model binding entirely — so the key came back as
`$.installationDate` (matching no input name) and the four genuine field errors on
`customerId`, `model`, `serialNumber` and `location` were never even checked.

Worse, the same root cause left a silent hole in the other direction: omitting
`installationDate` altogether returned **201 Created with `0001-01-01`**. `[Required]` on
a non-nullable `DateOnly` can never fail, because `Required` only rejects null — so the
installation date was effectively unenforceable by any API client.

Fixed by making the DTO property nullable, which is what lets "not supplied" be
expressed at all:

```csharp
[Required]
public DateOnly? InstallationDate { get; set; }
```

The service then reads `request.InstallationDate!.Value` — `[Required]` has already
rejected null before the action ran. The frontend sends `null` rather than `''` for a
blank date. Empty form now returns all five field errors, correctly keyed.

---

## 6. Frontend

`ASSET_TYPES` lives in `constants/asset.ts` as an `as const` array with a matching
`ASSET_TYPE_LABELS` record; `types/asset.ts` derives the `AssetType` union from it via
`typeof`. Same pattern as `CUSTOMER_TYPES`, same reason — the select options and the
union cannot drift apart.

`assetService.ts` reuses the **same axios instance** exported from `customerService.ts`.
The assets endpoints live on the same service and the same base URL; a second instance
would be a second place to configure.

`installationDate` is a `string` on both request and response types, not a `Date` —
`YYYY-MM-DD` is exactly what the date input produces and what `DateOnly` parses. On the
request it is `string | null`, for the reason in section 5.

### The customer dropdown

Populated from `getAllCustomers('ACTIVE')` on mount. Offering an inactive customer would
be offering a guaranteed 409.

That required a new capability on the list endpoint:

```
GET /api/customers?status=ACTIVE
```

`status` is an optional `[FromQuery] string?` on the action, carrying a
`[RegularExpression]`. `[ApiController]` validates action parameters just as it does DTO
properties, so `?status=BANANA` is a **400 keyed on `status`** with no validation code in
the action body. That matters: silently returning an empty list would read as "no
customers" and hide the typo.

Two consequences worth knowing:

- `?status=active` is also a 400. Case-sensitive, matching how `customerType` already
  treats `individual`.
- `?status=` (empty value) returns **everything**, not a 400 — the model binder converts
  an empty query value to null before validation, so it reads as "no filter". A frontend
  that always appends `?status=${selected}` will get the full list when nothing is
  selected. `getAllCustomers` therefore appends the parameter only when it has a value.

The dropdown shows **name and phone**, because two customers can share a name and the
phone is what tells them apart at a glance. The option value is the id.

Loading the customers is its own async concern with its own loading and error state — it
is a second request that can fail independently. If there are **no active customers**, the
form says so and disables submission rather than showing an empty dropdown the Agent
would keep clicking at.

All error rendering reuses the existing `errorsFor()` path from `CustomerForm` unchanged,
which is the payoff of keying every failure — 400 and both 409s — by field name.

Route: `/assets/new`, plus a "New asset" link in the top nav.

---

## 7. Testing

**24 of the suite's 58 unit tests belong to this story.** `dotnet test` from the repo root.

```
tests/CustomerAssetService.Tests/
├── UnitTests/
│   ├── AssetServiceTests.cs          9 tests   ← this story
│   ├── SerialNormalizerTests.cs     15 tests   ← this story
│   ├── CustomerServiceTests.cs      21 tests
│   └── PhoneNormalizerTests.cs      13 tests
└── Fakes/
    ├── FakeAssetRepository.cs                  ← this story
    ├── FakeCustomerRepository.cs
    └── MySqlExceptions.cs                      ← this story
```

### The service tests

| Test | Asserts |
|---|---|
| Valid request creates asset | success; Id is a real GUID, every field written through |
| Normalizes serial, keeps what was typed | `SerialNormalized == "ABC123"`, `SerialNumber == "abc 123"`, and the **normalized** form is what the duplicate check queries |
| Customer missing → fails without inserting | `CustomerNotFound`, `CreateAsyncCallCount == 0`, **`SerialExistsAsyncCallCount == 0`** |
| Customer inactive → fails without inserting | `CustomerInactive`, same two call counts at 0 |
| Serial exists → fails without inserting | `DuplicateSerial`, **`CreateAsyncCallCount == 0`** |
| Unique index rejects the insert | `DuplicateSerial`, **`CreateAsyncCallCount == 1`** — the race, not the pre-check |
| Reads the row back | response carries the database timestamps, and the id re-read is the one just written |
| GetById when exists / missing | full mapping / null |

The paired call-count assertions are the valuable ones: `== 0` proves the pre-check
short-circuits, `== 1` proves the race path actually attempted the write.

### Constructing a `MySqlException`

The 1062 test needs a real one. `MySqlException` is sealed with only internal
constructors, so `Fakes/MySqlExceptions.cs` reaches the internal
`(MySqlErrorCode, string)` one by reflection.

A substitute exception type would **not** do: the service filters on
`catch (MySqlException ex) when (ex.Number == 1062)`, so a stand-in would take the wrong
branch and the test would pass for the wrong reason. The reflection lives in one place
because it is the part most likely to break on a MySqlConnector upgrade.

The same helper closed the equivalent gap in `CustomerService`, which had sat untested
since US-01A listed it as un-unit-testable.

### Coverage

| Class | Line | Branch |
|---|---|---|
| `AssetService` | **100%** (57 / 57) | **100%** (10 / 10) |
| `SerialNormalizer` | **100%** (7 / 7) | **100%** (4 / 4) |
| `AssetRepository` | 0% (0 / 84) | 0% (0 / 30) |
| `AssetsController` | 0% (0 / 42) | 0% (0 / 8) |

Repository and controller are data access and HTTP wiring — same reasoning as US-01A,
and the same answer: integration tests, not more unit tests. Full figures in
[US-01A §7](../customer-management/US-01A-create-customer.md#coverage).

---

## 8. Handover to QA

Not covered by unit tests, and genuinely better as integration tests:

1. **The FK against real MySQL.** `ON DELETE RESTRICT` is asserted by the schema, never
   exercised in code — nothing in the service deletes a customer.
2. **`AssetRepository`** — all three methods, and specifically the
   `GetFieldValue<DateOnly>` read-back against a real `DATE` column.
3. **Controller status codes** over HTTP, including the three-way 409 split.
4. **The serial unique index under concurrency** — two simultaneous POSTs of the same
   serial. The 1062 handler is unit-tested with a fabricated exception; nobody has
   watched the real index raise it.
5. **The asset type CHECK constraint.** The `[RegularExpression]` mirrors it, so a bad
   value is a 400 long before MySQL sees it — the constraint itself is untested.

---

## 9. Running it locally

```powershell
# 1. Database (platform repo)
cd assms-platform-infrastructure\docker\local
docker compose up -d

# 2. Migrations (service repo) — applies every V*.sql not already recorded
$env:MYSQL_PASSWORD = '<customer_svc password>'
.\scripts\development\apply_migrations.ps1 -Database customerdb -User customer_svc

# 3. API
dotnet run --project src\CustomerAssetService     # http://localhost:5037

# 4. Frontend
npm run dev                                        # http://localhost:5173
```

Then `/assets/new`, or "New asset" in the nav.

Step 2 applied only `V01` and `V02` when this story was written. There are three
migrations now, and the runner tracks applied filenames in a `schema_migrations` table and
skips the ones already there — so re-running it is safe and does nothing on a database that
is up to date. On a database migrated before the tracking existed, `V01` and `V02` are
applied once more and then recorded, which costs nothing since both are
`CREATE TABLE IF NOT EXISTS`. See
[US-02D §2](deactivate-asset.md#2-the-migration--and-the-first-one-that-cannot-be-re-run).

Requires `appsettings.Development.json` (see `appsettings.Example.json`) and frontend
`.env` (see `.env.example`). Neither is committed.

**An asset needs an active customer.** On a fresh database, register one first — the
form will tell you so rather than presenting an empty dropdown.

---

## 10. Decisions worth carrying forward

- **Referenced ≠ addressed.** A bad foreign key in a request body is a field-keyed 409,
  not a 404. Written up in [API Conventions](../api/conventions.md#status-codes).
- **Reuse a `ServiceError` when the meaning matches.** `CustomerInactive` serves two
  stories; wording differences belong in the controller.
- **Normalize-and-store-both** for any value with more than one written form. Second
  use of the pattern after phones.
- **Nullable value types in request DTOs** when a field is required. `[Required]` on a
  non-nullable `DateOnly`/`int`/`Guid` is dead code — it can never fail.
- **Validate query parameters with attributes on the action parameter.** No wrapper DTO,
  no `if` in the body, and the 400 still comes out field-keyed.
- **One axios instance per backing service**, not per resource.

---

## TODO before merge

- [ ] `notes VARCHAR(1000)` — the width was chosen here, not specified. Confirm it, or
      change it via a `V03__` migration; `V02` is already applied
- [ ] Jira id and branch name for this story
- [ ] Asset list and detail pages — `GET /api/assets/{id}` exists and is unused by the
      UI, which currently only creates
- [x] Asset type values — **`AIR_CONDITIONER`, `REFRIGERATOR`, `WASHING_MACHINE`,
      `WATER_HEATER`, `OTHER`**, mirrored between the CHECK constraint and the
      `[RegularExpression]`
- [x] Route path for the create-asset page — **`/assets/new`**
