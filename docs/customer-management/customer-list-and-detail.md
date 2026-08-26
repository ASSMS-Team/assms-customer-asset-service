# Customer List & Detail

**Jira:** _not recorded — fill in_
**Repos touched:** `assms-customer-asset-service`, `assms-frontend`
**Follows:** [US-01A — Create Customer](US-01A-create-customer.md)

> As an Agent, I want to see the customers I have registered and open one of them,
> so that I can confirm what is on file.

---

## 1. What was built

Read side of the customer flow, end to end:

```
CustomerListPage    →  GET /api/customers       →  CustomerService.GetAllAsync   →  CustomerRepository.GetAllAsync
CustomerDetailPage  →  GET /api/customers/{id}  →  CustomerService.GetByIdAsync  →  CustomerRepository.GetByIdAsync
```

| Layer | Files |
|---|---|
| Data access | `Repositories/ICustomerRepository.cs`, `Repositories/CustomerRepository.cs` |
| Business logic | `Services/CustomerService.cs` |
| HTTP | `Controllers/CustomersController.cs` |
| Tests | `tests/.../Fakes/FakeCustomerRepository.cs`, `tests/.../UnitTests/CustomerServiceTests.cs` |
| Frontend — data | `src/services/customerService.ts`, `src/utils/formatDateTime.ts` |
| Frontend — pages | `src/pages/customers/CustomerListPage.tsx`, `src/pages/customers/CustomerDetailPage.tsx` |
| Frontend — shared | `src/components/layout/AppLayout.tsx`, `src/components/common/StatusBadge.tsx` |
| Frontend — shell | `src/routes/AppRoutes.tsx`, `src/styles/app.css`, `src/main.tsx` |

`GetByIdAsync` already existed from US-01A; only the list endpoint is new on the server.

---

## 2. API

| Endpoint | Success | Failures |
|---|---|---|
| `GET /api/customers` | **200** + array | — |
| `GET /api/customers/{id}` | **200** | **404** |

**An empty list is still 200, not 404.** "No customers registered" is a valid answer to a
valid question. A 404 on the collection would mean the *endpoint* does not exist, which is
a different claim, and it forces the frontend to treat a normal empty state as an error.

`[ProducesResponseType(typeof(IEnumerable<CustomerResponse>), 200)]` gives Swagger an array
of `CustomerResponse`. `[Produces("application/json")]` on the controller keeps the response
content-type dropdown to one entry — see [API conventions](../api/conventions.md).

The list returns every customer regardless of status. The "active" qualifier belongs to the
duplicate-phone rule only, not to reads — same as `GET /{id}` in US-01A.

---

## 3. Ordering — and why it needs a tiebreaker

```sql
ORDER BY created_at DESC, id;
```

**Why order at all.** Without an `ORDER BY`, MySQL guarantees nothing about row order. The
same query can come back shuffled between requests, and the list appears to reorder itself
for no reason. `created_at DESC` puts the newest first, which is what an Agent wants
immediately after registering someone.

**Why `id` as well.** `created_at` is a plain `TIMESTAMP` — **second precision, no fractional
seconds**. Two customers registered within the same second hold the *same* value, so
`created_at DESC` alone cannot separate them and they shuffle exactly as if there were no
`ORDER BY` at all. Adding `id` makes the ordering total, so the result is stable across
requests.

> ### Caveat worth knowing
>
> Within a same-second group the rows are ordered by `id` — a UUID, so effectively
> arbitrary. The order is **stable but not chronological** for those rows. This is the best
> available given the column's precision; genuine sub-second ordering would need a
> `TIMESTAMP(6)` column, which means a new `V02__` migration. Not worth it unless
> same-second registration becomes a real scenario.

This was found in testing: two customers created back to back both landed on
`2026-08-26T17:22:13` and the pair's order was not reproducible until `id` was added.

---

## 4. Implementation notes

**Repository.** `GetAllAsync` mirrors `GetByIdAsync` — same `GetOrdinal`-by-name lookups,
same `IsDBNull` handling for `phone_active_unique` and `email`, and the same
`GetValue(...)?.ToString()` workaround for the `CHAR(36)` id that MySqlConnector reads as a
`Guid` (see US-01A §5). Ordinals are resolved once above the loop rather than per row.

**Service.** `GetAllAsync` is a passthrough that maps each `Customer` through the existing
private `MapToResponse`, which now has three call sites.

**Controller.** `[HttpGet]` with no route parameter, so it sits at `/api/customers` alongside
the `[HttpPost]`. No conflict with `[HttpGet("{id}")]`.

---

## 5. Frontend

**Routes.** `/customers` → list, `/customers/:id` → detail, `/customers/new` → create,
`/` → redirect to the list. All nested under a shared `AppLayout` (top bar + content column).
`/customers/new` is never mistaken for an id: React Router ranks a static segment above a
dynamic one regardless of declaration order.

**Not-found is separate from error.** `CustomerDetailPage` holds `notFound` as its own piece
of state, set only when `axios.isAxiosError(caught) && caught.response?.status === 404`.
Anything else becomes the generic `error`. A 404 is an expected outcome — "that customer
does not exist" — and reads differently to the user than "the service is unreachable".
Collapsing the two would tell an Agent the system is broken when it is working correctly.

**Status badge.** `bg-success` for `ACTIVE`, `bg-secondary` for anything else. Lives in one
component (`components/common/StatusBadge.tsx`) used by both pages so they cannot drift.
The default is grey rather than green, so an unrecognised status never renders as healthy.

**Timestamps.** The API sends them without a timezone offset (`2026-08-26T17:22:13`), which
JavaScript parses as *local* time. `utils/formatDateTime.ts` formats without converting, so
what is displayed matches what the database holds. Worth revisiting if the service is ever
deployed outside one timezone.

**Styling.** The theme lives in `src/styles/app.css`, imported after Bootstrap and
`index.css`. Kept out of `index.css` so the Vite template's remaining rules stay where they
are and the overrides are visible in one place.

---

## 6. Testing

Two tests added to `CustomerServiceTests.cs` — **20 unit tests total**, all passing.

| Test | Asserts |
|---|---|
| Customers exist → mapped responses | count, all nine fields on the first, null email on the second |
| No customers → empty list | not null, empty — never null |

`FakeCustomerRepository` gained `CustomersToReturn`, defaulting to an **empty list rather
than null**, so an unconfigured fake stands in for "no customers yet" instead of blowing up
the caller.

Verified manually against real MySQL: two rows read back with the id converted correctly,
a null email preserved, phone returned as typed, and five consecutive reads returning
identical order after the `id` tiebreaker.

**Still for QA** — `CustomerRepository.GetAllAsync` itself is not unit tested, for the same
reason as the rest of the repository (see US-01A §8). The ordering guarantee in particular
is only meaningful against real MySQL and deserves an integration test with two same-second
rows.
