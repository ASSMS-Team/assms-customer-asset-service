# Asset List & Detail

**Jira:** _not recorded — fill in_
**Repos touched:** `assms-customer-asset-service`, `assms-frontend`
**Service:** Customer & Asset Service · **Database:** `customerdb` · **Table:** `assets`
**Follows:** [US-02A — Create Asset](US-02A-create-asset.md)
**Mirrors:** [Customer List & Detail](../customer-management/customer-list-and-detail.md)

> As an Agent, I want to see the equipment registered against a customer and open one of
> those units, so that I can confirm what is on site before raising a service request.

---

## 1. What was built

Read side of the asset flow, end to end:

```
CustomerDetailPage  →  GET /api/customers/{customerId}/assets  →  AssetService.GetByCustomerIdAsync  →  AssetRepository.GetByCustomerIdAsync
                                                                            ↓
                                                                   ICustomerRepository  (owner must exist)

AssetDetailPage     →  GET /api/assets/{id}                    →  AssetService.GetByIdAsync         →  AssetRepository.GetByIdAsync
```

| Layer | Files |
|---|---|
| Data access | `Repositories/IAssetRepository.cs`, `Repositories/AssetRepository.cs` |
| Business logic | `Services/AssetService.cs` |
| HTTP | `Controllers/CustomersController.cs` |
| Tests | `tests/.../Fakes/FakeAssetRepository.cs`, `tests/.../UnitTests/AssetServiceTests.cs` |
| Frontend — data | `src/services/assetService.ts` |
| Frontend — pages | `src/pages/customers/CustomerDetailPage.tsx`, `src/pages/assets/AssetDetailPage.tsx` |
| Frontend — shell | `src/routes/AppRoutes.tsx` |

`GET /api/assets/{id}` and `AssetService.GetByIdAsync` already existed from US-02A — the
asset detail page is the first thing to consume them. Only the per-customer list is new on
the server.

---

## 2. API

| Endpoint | Success | Failures |
|---|---|---|
| `GET /api/customers/{customerId}/assets` | **200** + array | **404** |
| `GET /api/assets/{id}` | **200** | **404** |

**An empty list is still 200, not 404.** A customer with no equipment on file is a valid
answer to a valid question — the same reasoning as `GET /api/customers`. The 404 on this
endpoint means something different: *no such customer*.

`[ProducesResponseType(typeof(IEnumerable<AssetResponse>), 200)]` gives Swagger an array of
`AssetResponse`; the 404 carries no body, so its attribute has no type — see
[API conventions](../api/conventions.md).

### Why the route hangs off `customers`, not `assets`

The list belongs to a customer, so it is addressed through one:
`[HttpGet("{customerId}/assets")]` on `CustomersController`. A separate action on
`AssetsController` with an explicit `[Route]` would have produced the same URL, but route
ownership would then be split across two controllers and `/api/customers/...` would no
longer be defined entirely in the file that owns that prefix.

The cost is that `CustomersController` now takes `AssetService` alongside `CustomerService`.
That is deliberate: the route lives here, but *listing assets* stays the asset service's
job rather than being reimplemented on the customer side.

### Why an unknown customer is a 404 here, and a 409 on create

US-02A §4 explains why `POST /api/assets` returns **409** keyed on `customerId` when the
owner does not exist: the addressed resource is the assets collection, which exists, and it
is a *field in the body* that is wrong.

Here the customer is in the **route**. `/api/customers/{customerId}/assets` addresses a
sub-resource of that customer, so a customer that does not exist means the addressed
resource does not exist — which is exactly what 404 is for. Same missing customer, different
position in the request, different status code. Both follow the rule in
[API conventions](../api/conventions.md#status-codes) rather than contradicting it.

### The customer's own status does not filter the list

`AssetService.CreateAsync` refuses a non-`ACTIVE` owner (`CustomerInactive`). The read path
deliberately does **not**: a deactivated customer can take no new equipment, but the
equipment already on file is history, and hiding it would lose the record of what was
installed. So `GetByCustomerIdAsync` checks only that the customer *exists*.

This matches the read side of the customer flow, where "active" qualifies the duplicate-phone
rule and nothing else.

---

## 3. Ordering

```sql
ORDER BY created_at DESC, id;
```

Identical to the customer list, and for identical reasons — without an `ORDER BY` MySQL
guarantees nothing about row order, and `created_at` is a second-precision `TIMESTAMP`, so
two assets registered within the same second tie and shuffle unless `id` breaks it. The full
argument, including the caveat that a same-second group ends up ordered by an effectively
arbitrary UUID, is in
[Customer List & Detail §3](../customer-management/customer-list-and-detail.md#3-ordering--and-why-it-needs-a-tiebreaker).

**No `LIMIT`.** This is one customer's whole equipment history, not a page of it. A domestic
customer has a handful of units; if that ever stops being true, paging is a deliberate API
change with a page parameter and a total count, not a silent cap that quietly truncates the
list.

---

## 4. Implementation notes

**Repository.** `GetByCustomerIdAsync` mirrors `GetByIdAsync` — same `SELECT` list, same
`GetOrdinal`-by-name lookups resolved once above the loop rather than per row, the same
`GetValue(...)?.ToString()` workaround for the `CHAR(36)` `id` and `customer_id` columns that
MySqlConnector reads as `Guid`, and the same `GetFieldValue<DateOnly>` for
`installation_date` (US-02A §5 — `GetDateTime` would hand back a midnight `DateTime`
instead). The differences are the `WHERE customer_id = @customerId`, the `ORDER BY`, and the
`while` loop in place of the single `ReadAsync`.

**Service.** `GetByCustomerIdAsync` returns `Result<List<AssetResponse>>` rather than a bare
list, because it has a failure the caller must tell apart from an empty result:

```
_customerRepository.GetByIdAsync(customerId)  →  null  →  ServiceError.CustomerNotFound
                                             →  found →  _repository.GetByCustomerIdAsync → map
```

`CustomerNotFound` is reused as-is; no new `ServiceError` value was needed. The mapping goes
through the existing private `MapToResponse`, which now has three call sites.

**Controller.** `[HttpGet("{customerId}/assets")]` does not collide with `[HttpGet("{id}")]`
— the trailing static `assets` segment makes the two templates different lengths, so
`/api/customers/{id}` is never matched by a request for the list.

---

## 5. Frontend

**Route.** `/assets/:id` → `AssetDetailPage`, declared after `/assets/new` for readability.
As with `/customers/new`, React Router ranks the static segment above the dynamic one
regardless of declaration order, so `new` is never read as an id.

**The assets section owns its own loading and error state.** `CustomerDetailPage` already
held `loading` / `notFound` / `error` for the customer. The assets list adds `assetsLoading`
and `assetsError` rather than reusing them. Folding a failed assets fetch into `error` would
blank out a customer that had already loaded and is still correct — one section failing is
not the page failing. The assets error renders inside the assets card only; everything above
it stays on screen.

**Fetched after the customer, not alongside it.** The assets effect is keyed on
`customer?.id` and returns early until there is one. Firing both requests on mount would be
marginally faster, but an id that turns out to be nobody would then also fire a request bound
to 404 for no benefit. Deactivating the customer does not re-run it: the id is unchanged.

**Empty state.** A customer with no equipment gets "No assets registered" and a link to
register one, not a table with nothing but headers — the same treatment the customer list
gives an empty database.

**Rows link to the asset.** The table carries a real `<Link>` in the first cell as well as
the row-level `onClick`, so the detail page stays reachable by keyboard. Same pattern as
`CustomerListPage`.

**Asset detail reuses the four-state pattern.** `loading`, `notFound`, `error`, and the data,
with the 404 identified by `axios.isAxiosError(caught) && caught.response?.status === 404`
and everything else falling through to the generic `error`. A 404 is an expected outcome —
"that asset does not exist" — and must not be reported as the service being unreachable. See
[Customer List & Detail §5](../customer-management/customer-list-and-detail.md#5-frontend).

**Link back to the owner.** `AssetResponse` already carries `customerId`, so the link to the
owning customer is built from the response with no second request. The page's back-link
points at the owner once the asset has loaded, and falls back to the customer list until then.

### `installationDate` is rendered as sent, not through `formatDateTime`

`installationDate` is a `DateOnly` on the server and arrives as a plain `'YYYY-MM-DD'`
string. It is displayed exactly as received, in both the table and the detail page.

`formatDateTime` is deliberately not used on it. `new Date('2026-08-26')` is parsed by
JavaScript as **UTC midnight**, so formatting it in any timezone behind UTC renders the
*previous day* — a unit installed on the 26th would show as the 25th. A day with no time of
day has nothing to convert. The timestamps `createdAt` / `updatedAt` still go through
`formatDateTime`, which is what it is for.

---

## 6. Testing

Three tests added to `AssetServiceTests.cs` — **61 unit tests total**, all passing.

| Test | Asserts |
|---|---|
| Customer with several assets → mapped responses | success, count, every field on the first, the id the repository was asked for |
| Unknown customer → `CustomerNotFound` | failure, the error value, **and that the asset list was never queried** |
| Customer with no assets → empty list | success, not null, empty — never null |

The second test asserts `GetByCustomerIdCustomerId` is still null: the customer check has to
happen *first*, or an unknown customer would cost a pointless second query.

`FakeAssetRepository` gained `AssetsToReturn`, defaulting to an **empty list rather than
null** so an unconfigured fake stands in for "this customer owns nothing yet", and
`GetByCustomerIdCustomerId`, recording the id the list was asked for.

### Verified manually

Against real MySQL, the running API and the dev server:

| Check | Result |
|---|---|
| Nimal Perera's detail page lists his assets | Table shows the row, columns render, link points at `/assets/{id}` |
| Opening an asset from that row | All fields correct; "View owning customer" points back at Nimal |
| Kamal Silva (no assets) | "No assets registered" empty state with the register link |
| `/assets/nonexistent-id` | "Asset not found", not the error state |
| `GET /api/customers/{unknown}/assets` | 404 |
| `GET /api/customers/{kamal}/assets` | 200 `[]` |

Kamal is `INACTIVE`, so that row also confirms the read path does not filter on the
customer's status — it returns his list rather than refusing it.

**Still for QA** — `AssetRepository.GetByCustomerIdAsync` is not unit tested, for the same
reason as the rest of the repository (US-01A §8). The `ORDER BY` in particular is only
meaningful against real MySQL and deserves an integration test with two same-second rows.
The frontend has no test script yet (see the CI note in the frontend README), so the four
checks above are manual.

---

## 7. Decisions worth carrying forward

- **The position of an id in the request decides the status code.** In the route it is the
  addressed resource (404); in the body it is a field (409 keyed on that field). Both cases
  now exist for the same missing customer, one in each direction.
- **A nested collection's route belongs to the parent's controller; the work belongs to the
  child's service.** The URL says who owns the list; the injected service says who knows how
  to build it.
- **Write rules do not automatically become read rules.** `CustomerInactive` blocks creating
  an asset and deliberately does not block listing one.
- **A section that loads separately fails separately.** Its own loading and error state, so
  a partial failure degrades one card instead of the page.
- **Date-only values are not timestamps.** Never route `'YYYY-MM-DD'` through `Date` for
  display.
