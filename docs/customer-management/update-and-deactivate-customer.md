# Update & Deactivate Customer

**Jira:** _not recorded — fill in_
**Repos touched:** `assms-customer-asset-service`, `assms-frontend`
**Follows:** [US-01A — Create Customer](US-01A-create-customer.md), [Customer List & Detail](customer-list-and-detail.md)

> As an Agent, I want to correct a customer's details and retire a customer who is no
> longer served, so that what is on file stays accurate without losing history.

---

## 1. What was built

The write side of the customer flow, end to end:

```
EditCustomerPage    →  PUT  /api/customers/{id}             →  CustomerService.UpdateAsync      →  CustomerRepository.UpdateAsync
CustomerDetailPage  →  POST /api/customers/{id}/deactivate  →  CustomerService.DeactivateAsync  →  CustomerRepository.DeactivateAsync
```

| Layer | Files |
|---|---|
| DTOs | `DTOs/UpdateCustomerRequest.cs` |
| Data access | `Repositories/ICustomerRepository.cs`, `Repositories/CustomerRepository.cs` |
| Business logic | `Services/CustomerService.cs`, `Services/Result.cs` |
| HTTP | `Controllers/CustomersController.cs` |
| Tests | `tests/.../Fakes/FakeCustomerRepository.cs`, `tests/.../UnitTests/CustomerServiceTests.cs` |
| Frontend — data | `src/types/customer.ts`, `src/services/customerService.ts` |
| Frontend — form | `src/components/forms/CustomerForm.tsx` *(renamed from `CreateCustomerForm.tsx`)* |
| Frontend — pages | `src/pages/customers/EditCustomerPage.tsx`, `src/pages/customers/CustomerDetailPage.tsx`, `src/pages/customers/CreateCustomerPage.tsx` |
| Frontend — shell | `src/routes/AppRoutes.tsx` |

No migration. Both operations write columns the table already has.

---

## 2. API

| Endpoint | Success | Failures |
|---|---|---|
| `PUT /api/customers/{id}` | **200** + the stored customer | **400** validation, **404** unknown id, **409** duplicate phone *or* inactive customer |
| `POST /api/customers/{id}/deactivate` | **200** + the stored customer | **404** unknown id |

`PUT` returns 200 with a body rather than 204: the response carries the
database-maintained `updatedAt`, so the client does not have to re-read to display it.

### The two 409s are different shapes

Both conflicts are 409 — the request is well-formed but breaks a business rule — but the
bodies differ so the frontend can tell them apart without parsing prose.

**Duplicate phone** is a `ValidationProblemDetails` keyed on the offending field, exactly
as on create (see [API conventions](../api/conventions.md)):

```json
{
  "title": "One or more validation errors occurred.",
  "status": 409,
  "errors": { "phone": ["Another active customer already exists with this phone number."] }
}
```

**Inactive customer** is a plain `ProblemDetails` — it belongs to the request as a whole,
not to any one field, so there is nothing to key it on:

```json
{
  "title": "Customer is not active.",
  "status": 409,
  "detail": "This customer has been deactivated and can no longer be edited."
}
```

The frontend branches on **whether `errors` is present**, not on the status code. That is
the whole reason for the two shapes.

### Deactivate is a POST, not a PUT or DELETE

`DELETE` would say the record is gone; it is not — deactivation is a status change that
keeps the row. `POST /{id}/deactivate` is the one place this service puts a verb in a path,
because the action is not "replace this resource with what I sent". It is idempotent
regardless (§4), which is what matters in practice.

---

## 3. Update

### Order of operations

1. **Validation** — DataAnnotations on `UpdateCustomerRequest`; `[ApiController]` returns
   the 400 before the action runs.
2. **Load** via `GetByIdAsync`. `null` → `NotFound`.
3. **Status gate** — anything other than `ACTIVE` → `CustomerInactive`, before any write.
4. **Normalize** the phone, same helper as create.
5. **Pre-check** `ActivePhoneExistsAsync(normalized, excludeCustomerId: id)`.
6. **Build** the `Customer`, carrying over `Id`, `Status` and `CreatedAt` from the loaded
   row and taking the other five fields from the request.
7. **Write**, with `MySqlException` 1062 caught and mapped to `DuplicatePhone` — the same
   race as on create (US-01A §5), and the unique index is still what actually enforces the
   rule.
8. **Re-read** and map, so `updatedAt` is the value the database wrote.

### Excluding the customer's own row

`ActivePhoneExistsAsync` gained an optional second parameter:

```csharp
Task<bool> ActivePhoneExistsAsync(string phoneNormalized, string? excludeCustomerId = null);
```

Without it, saving the edit form **without touching the phone** would fail: the customer's
own row holds that number, the check would find it, and the Agent would be told their own
customer is a duplicate of themselves. The parameter is optional so every existing create
call site keeps working unchanged.

The SQL is built conditionally and the parameter added only when it is used, so the create
path runs exactly the query it ran before:

```sql
SELECT 1 FROM customers
WHERE phone_normalized = @phoneNormalized AND status = 'ACTIVE'
  [ AND id != @excludeId ]
LIMIT 1;
```

### What the UPDATE does not touch

```sql
UPDATE customers
SET name = @name, phone = @phone, phone_normalized = @phoneNormalized,
    address = @address, customer_type = @customerType, email = @email
WHERE id = @id;
```

- `status` — changing it is a different operation, with a different endpoint and a
  different confirmation. Editing a form should never silently retire a customer.
- `created_at` — a fact about the past.
- `updated_at` — maintained by `ON UPDATE CURRENT_TIMESTAMP`.
- `phone_active_unique` — generated; it tracks `phone_normalized` and `status` on its own.

### Why an inactive customer cannot be edited

A deactivated customer is history. Rewriting it would quietly change what was already
agreed, and it interacts badly with the generated column: an inactive row's
`phone_active_unique` is `NULL`, so an edit could put a number on it that an active
customer already holds, and the row would then be un-reactivatable. Refusing the edit
keeps that from being created in the first place.

---

## 4. Deactivate

```sql
UPDATE customers SET status = 'INACTIVE' WHERE id = @id;
```

Status is the only column written. `updated_at` fires automatically, and
`phone_active_unique` drops to `NULL` on its own — **which is what frees the number for a
new active customer.** No application code releases it; that is the point of the generated
column described in US-01A §2.

### Idempotent, and honest about it

Deactivating a customer that is **already inactive succeeds** and returns the customer
unchanged — but the service **skips the write entirely**:

| State | Result | Write? |
|---|---|---|
| Unknown id | `NotFound` → 404 | no |
| `ACTIVE` | success, now `INACTIVE` | yes |
| already `INACTIVE` | success, unchanged | **no** |

Returning success is the criterion. Skipping the write is what keeps `updated_at` honest:
a redundant `UPDATE` would still fire `ON UPDATE CURRENT_TIMESTAMP` and make a no-op look
like a modification, so the audit trail would show a customer being "changed" every time
someone pressed the button again.

Verified against real MySQL: a second deactivate call two seconds after the first returned
the identical body, `updatedAt` included.

> ### No reactivate endpoint yet
>
> US-01A §2 flagged that reactivating a customer can fail if someone else took their number
> while they were inactive. That is still open — there is no reactivate endpoint. Whoever
> adds one inherits the caveat and needs to turn the resulting 1062 into a readable message
> rather than a 500. Until then, reactivating is a manual `UPDATE` in the mysql client.

---

## 5. `ServiceError` gained two members

```csharp
public enum ServiceError
{
    None = 0,
    DuplicatePhone = 1,
    NotFound = 2,
    CustomerInactive = 3
}
```

Both are expected outcomes, returned through `Result<T>` rather than thrown — the pattern
from US-01A §5, unchanged. `NotFound` is a service-level answer, distinct from
`GetByIdAsync` returning `null`, because the update path has to report it through the same
`Result<T>` the other failures use.

---

## 6. Frontend

### One form, two modes

`CreateCustomerForm.tsx` was **renamed to `CustomerForm.tsx`** and taught to edit. The
five fields, their constraints and the whole error-rendering path are identical between
create and update, and duplicating them would mean fixing every field bug twice.

The props are a discriminated union rather than optional props, so `mode="edit"` cannot be
written without the things edit needs:

```tsx
type CustomerFormProps =
  | { mode: 'create' }
  | {
      mode: 'edit'
      customerId: string
      initialValues: CustomerFormValues
      onSaved: (customer: CustomerResponse) => void
      onNotFound: () => void
    }
```

The form does no routing. `EditCustomerPage` navigates to the detail page on `onSaved` —
the detail page showing the new values *is* the confirmation, so there is no success
message to leave behind — and swaps to its not-found state on `onNotFound`.

`CreateCustomerRequest` and `UpdateCustomerRequest` are separate types that happen to be
identical today, mirroring the backend DTOs. The form holds one piece of state for both;
the parameter type on each service function is what would break loudly if the two diverge.

### Four error cases, one branch

| Response | Rendered as |
|---|---|
| **400** | field errors under each input |
| **409** with `errors` | the same field path, under `phone` |
| **409** without `errors` | form-level alert built from `title` + `detail` |
| **404** | the page's not-found state — the customer was deleted between load and save |

The 400 and the duplicate 409 share a code path because the API keys both by field name.
The inactive 409 is separated by the presence of `errors`, never by reading the message.

### Edit page

Loads the customer on mount with the same `loading` / `notFound` / `error` states as the
detail page, then mounts the form with `initialValues` taken from the response — only the
five editable fields; the id travels in the URL and the rest stays server-owned. Route:
`/customers/:id/edit`.

### Deactivate on the detail page

- **Confirmed before the call**, not after. `window.confirm` naming the customer;
  dismissing it fires no request at all.
- **The badge flips from the response.** The endpoint returns the customer as it now
  stands, so `setCustomer(await deactivateCustomer(id))` is enough — no refetch, no
  reload. Verified by request log: one `POST`, nothing else.
- **404 → the not-found state**, for a customer deleted between loading the page and
  pressing the button. Anything else becomes a general alert that sits *above* the card,
  kept separate from the page's load `error`, which replaces the card — a failed
  deactivation leaves a perfectly good customer on screen and should not blank it out.

### Both action buttons are hidden once inactive

`Deactivate` is absent rather than disabled once the customer is inactive — there is
nothing left to do. `Edit` is hidden on the same condition: the server refuses the save, so
offering the form only produces a filled-in page that cannot be submitted.

The route itself is still reachable by URL, and the 409 remains the backstop for a customer
deactivated in another tab after this page was loaded. **Hiding the link is a convenience,
not the enforcement.**

---

## 7. Testing

Eight tests added to `CustomerServiceTests.cs` — **28 unit tests total**, all passing via
`dotnet test`.

### Update

| Test | Asserts |
|---|---|
| Valid request changes editable fields only | new values written; `Id`, `Status`, `CreatedAt` carried over |
| Customer missing | `NotFound`, **`UpdateAsyncCallCount == 0`** |
| Customer not active | `CustomerInactive`, **`UpdateAsyncCallCount == 0`** |
| Another active customer has the phone | `DuplicatePhone`, **`UpdateAsyncCallCount == 0`** |
| **Phone unchanged → own row excluded** | the customer's id reached the repository as the row to exclude, and the save succeeded |

The last one is the story's real risk. The fake is configured so the phone check *would*
report a clash, and the update succeeds anyway — proving the exclusion is passed through
rather than the check happening to return false.

### Deactivate

| Test | Asserts |
|---|---|
| Active customer | success, status `INACTIVE`, `DeactivateAsyncCallCount == 1` |
| Unknown id | `NotFound`, call count 0 |
| **Already inactive** | success, **call count 0**, `UpdatedAt` unchanged |

### `FakeCustomerRepository`

New fields: `UpdatedCustomer`, `UpdateAsyncCallCount`, `DeactivatedId`,
`DeactivateAsyncCallCount`, `ActivePhoneExistsExcludeCustomerId`.

Two of its methods now **model the effect of the write**, not just record the call:

- `ActivePhoneExistsAsync` returns `false` when the excluded id is the configured
  customer's own row and the queried number is that row's number — standing in for
  `WHERE id != @excludeId`. Any other number still clashes, so both the duplicate test and
  the exclude-self test remain meaningful.
- `DeactivateAsync` flips the configured customer's `Status` to `INACTIVE`, so the
  service's read-back sees what the real `UPDATE` would have done instead of the same
  `ACTIVE` object handed straight back.

Without those two behaviours the fake would quietly pass tests that the real repository
would fail.

### Manual verification

Through Swagger and the running frontend against real MySQL:

- update returning 200 with `updatedAt` moved, `id` and `status` untouched
- the same number retyped as `+94 71 555 0009` saving cleanly — the exclude-self path
- another active customer's number → 409 under the phone input
- an inactive customer → 409 form-level alert, row unchanged in the database
- unknown id → 404 on both endpoints; invalid body → 400 keyed by field
- deactivate → badge grey, button gone, `phone_active_unique` `NULL`, and the number
  immediately reusable by a new active customer
- a second deactivate leaving `updatedAt` exactly where it was

---

## 8. Still for QA

Everything in US-01A §8 still applies. Specific to this story:

1. **`CustomerRepository.UpdateAsync` and `DeactivateAsync`** — untested for the same
   reason as the rest of the repository. The conditional SQL in
   `ActivePhoneExistsAsync` is the piece most worth an integration test: it is the only
   query in the service whose *text* changes at runtime.
2. **The 1062 path on update.** Still unreachable in a unit test — `MySqlException` has no
   public constructor (US-01A §8) — so the update race is covered by the pre-check tests
   only.
3. **Concurrent deactivate and update.** An update that passes the status gate can still be
   writing while a deactivate lands. The row survives either way, but which `updated_at`
   wins is untested.
4. **Reactivation**, once an endpoint exists — in particular a customer whose number was
   taken while they were inactive.

---

## TODO before merge

- [ ] Jira id and branch name for this story
- [x] US-01A §1 file table — **updated to `CustomerForm.tsx`**
- [x] Coverage report regenerated — **31.1% line (144 / 463) · 26.3% branch (30 / 114)**
      across 28 tests, in US-01A §7 and `docs/testing/coverage/index.html`
