# US-02C — Update Asset

**Jira:** ASSMS-79 · branch `ASSMS-79-US-02C-Update-Asset` · PR #16 (service), #12 (frontend)
**Repos touched:** `assms-customer-asset-service`, `assms-frontend`
**Service:** Customer & Asset Service · **Database:** `customerdb` · **Table:** `assets`
**Follows:** [Asset List & Detail](asset-list-and-detail.md) *(US-02B)*
**Mirrors:** [Update & Deactivate Customer](../customer-management/update-and-deactivate-customer.md) *(US-01C)*

> As an Agent, I want to correct the details recorded against a unit, so that what is on
> file matches the equipment actually installed.

> **Written after the fact.** This note was reconstructed from the merged commits
> (`37576de` in the service, `1c0804a` in the frontend) rather than alongside the work, so
> it records what the code does and why, not a session's worth of manual verification.
> The "verified manually" tables the other asset notes carry are absent for that reason —
> see §9.

---

## 1. What was built

The write side of the asset flow:

```
EditAssetPage  →  PUT /api/assets/{id}  →  AssetService.UpdateAsync  →  AssetRepository.UpdateAsync
```

| Layer | Files |
|---|---|
| DTOs | `DTOs/UpdateAssetRequest.cs` |
| Data access | `Repositories/IAssetRepository.cs`, `Repositories/AssetRepository.cs` |
| Business logic | `Services/AssetService.cs` |
| HTTP | `Controllers/AssetsController.cs` |
| Tests | `tests/.../Fakes/FakeAssetRepository.cs`, `tests/.../UnitTests/AssetServiceTests.cs` |
| Frontend — data | `src/types/asset.ts`, `src/services/assetService.ts` |
| Frontend — form | `src/components/forms/AssetForm.tsx` |
| Frontend — pages | `src/pages/assets/EditAssetPage.tsx`, `src/pages/assets/AssetDetailPage.tsx`, `src/pages/assets/CreateAssetPage.tsx` |
| Frontend — shell | `src/routes/AppRoutes.tsx` |

**No migration.** Every column the update writes already existed.

**No new `ServiceError` value.** `NotFound` and `DuplicateSerial` both already existed —
the first from the customer stories, the second from US-02A. (`AssetInactive` was added
later, when the status gate was: §6.)

---

## 2. API

| Endpoint | Success | Failures |
|---|---|---|
| `PUT /api/assets/{id}` | **200** + the stored asset | **400** validation, **404** unknown id, **409** inactive asset *or* duplicate serial |

`PUT` returns 200 with a body rather than 204, for the same reason the customer update
does: the response carries the database-maintained `updatedAt`, so the client does not
have to re-read to display it.

### The two 409s are different shapes

As on the customer update, both conflicts are 409 — the request is well-formed but breaks a
business rule — but the bodies differ so the frontend can tell them apart without parsing
prose. **Inactive asset** is a plain `ProblemDetails`, shown in §6; **duplicate serial** is a
`ValidationProblemDetails` keyed on the offending field:

```json
{
  "title": "One or more validation errors occurred.",
  "status": 409,
  "errors": { "serialNumber": ["Another asset already exists with this serial number."] }
}
```

Note the wording differs from the create path's *"An asset already exists with this serial
number."* — **another** is accurate on update, where the asset being edited is itself an
asset holding a serial. The distinction is presentation only; both come from
`ServiceError.DuplicateSerial`.

The frontend branches on **whether `errors` is present**, not on the status code. That is
the whole reason for the two shapes, and it is why the inactive case must not be keyed.

An unknown id is a **404**, not the field-keyed 409 the create path returns for a missing
customer. The id is in the route, so it is the addressed resource — the rule from
[API conventions](../api/conventions.md#status-codes), and the same split
[Asset List & Detail §2](asset-list-and-detail.md#2-api) drew for the customer id.

---

## 3. What the update may not touch

```sql
UPDATE assets
SET asset_type = @assetType, model = @model, serial_number = @serialNumber,
    serial_normalized = @serialNormalized, installation_date = @installationDate,
    location = @location, notes = @notes
WHERE id = @id;
```

| Column | Why it is absent |
|---|---|
| `id` | Identity. It travels in the route. |
| `customer_id` | **An asset does not change hands.** Rewriting it would move a unit's history to a different owner. |
| `created_at` | A fact about the past. |
| `updated_at` | Maintained by `ON UPDATE CURRENT_TIMESTAMP`. |
| `status` | Did not exist yet. Added by [US-02D](deactivate-asset.md) and deliberately kept out of this `SET` list then. |

`UpdateAssetRequest` is a separate type from `CreateAssetRequest` rather than a reuse, even
though the two differ by exactly one field. That one field is `customerId`, and its absence
is the whole point: a request DTO that cannot express an owner cannot be used to change one,
which is stronger than validating that it was not changed.

`serialNormalized` is written but is not in the request — the service derives it from
`SerialNumber` with the same `SerialNormalizer` the create path uses (US-02A §3). Both forms
are stored on update exactly as on create: what the Agent typed, and the canonical form the
unique index compares.

The service carries the untouchable values forward from the row it loaded rather than
trusting anything in the request:

```csharp
Id = existing.Id,
CustomerId = existing.CustomerId,
CreatedAt = existing.CreatedAt,
```

---

## 4. Excluding the asset's own row

This is the part of the story that could actually have shipped broken.

`SerialExistsAsync` gained an optional second parameter:

```csharp
Task<bool> SerialExistsAsync(string serialNormalized, string? excludeAssetId = null);
```

Without it, saving the edit form **without touching the serial number** would fail. The
asset's own row holds that serial, the duplicate pre-check would find it, and the Agent
would be told their unit is a duplicate of itself. The parameter is optional so every
existing create call site keeps working unchanged — the same shape
`ActivePhoneExistsAsync` took in US-01C, and for the same reason.

The SQL is built conditionally and the parameter added only when it is used, so the create
path runs exactly the query it ran before:

```sql
SELECT 1 FROM assets
WHERE serial_normalized = @serialNormalized
  [ AND id != @excludeId ]
LIMIT 1;
```

### Where this differs from the customer version

`ActivePhoneExistsAsync` also carries `AND status = 'ACTIVE'`, because a phone number is
only taken while its holder is active. `SerialExistsAsync` has no status clause and did not
gain one in US-02D: a serial is unique across the whole table whatever the row's status,
because retiring a row does not make the physical unit stop existing. The full argument is
in [Deactivate Asset §3](deactivate-asset.md#3-what-status-on-assets-does-not-do).

So the two exclusions look alike and mean slightly different things: the customer query
excludes a row **and** filters by status; the asset query only excludes a row.

---

## 5. Order of operations

1. **Validation** — DataAnnotations on `UpdateAssetRequest`; `[ApiController]` returns the
   400 before the action runs. `InstallationDate` is `DateOnly?`, nullable for the reason
   US-02A §5 found the hard way: `[Required]` on a non-nullable `DateOnly` can never fail.
2. **Load** via `GetByIdAsync`. `null` → `NotFound` → 404.
3. **Status gate** — anything other than `ACTIVE` → `AssetInactive`, before any other work
   (§6).
4. **Normalize** the serial.
5. **Pre-check** `SerialExistsAsync(normalized, excludeAssetId: id)`.
6. **Build** the `Asset`, carrying over `Id`, `CustomerId` and `CreatedAt` (§3).
7. **Write**, with `MySqlException` 1062 caught and mapped to `DuplicateSerial`.
8. **Re-read** and map, so `updatedAt` is the value the database wrote.

Step 3 sits ahead of step 4 for the reason US-02A §5 put the customer checks ahead of the
serial pre-check: there is no point asking the database about a serial for an edit that
cannot proceed. A unit test pins that ordering (`SerialExistsAsyncCallCount == 0`).

With the gate in place these eight steps are now the same shape as
`CustomerService.UpdateAsync`, step for step.

### The 1062 race, again

The pre-check produces a clean error; the unique index is what actually enforces the rule.
Two concurrent requests can both clear step 4 before either one writes, and the loser gets
`DuplicateSerial` rather than a 500 — the same handling US-02A §5 gave the insert, extended
to the update. Both paths in the service are `catch (MySqlException ex) when (ex.Number == 1062)`.

---

## 6. Only active assets are editable

```csharp
if (existing.Status != "ACTIVE")
{
    return Result<AssetResponse>.Failure(ServiceError.AssetInactive);
}
```

**Added after this story shipped.** US-02C went out with no status gate, because `assets`
had no `status` column to gate on — [US-02D](deactivate-asset.md) added one, and this note
carried the gap as an open question until it was closed. The rule now matches
`CustomerService.UpdateAsync`, which has refused non-`ACTIVE` customers since US-01C.

### Why

A deactivated asset is the record of **scrapped equipment**. The unit came out, and the row
survives to say what was on site, what model it was and when it was fitted. Editing it does
not correct a record — it changes what the equipment *was*. Every service job ever raised
against that asset points at it, and those jobs described a specific machine; rewriting the
row retrospectively rewrites what they were about.

Correcting a genuine typo on a retired unit is the case this refuses along with the rest,
and that is the accepted cost. The alternative — an editable history — is worse, and the
correction can still be made deliberately in the database by someone who has decided the
record is wrong rather than by an Agent tabbing through a form.

### The serial is the concrete failure

§4 explains that `serial_normalized` is unique across the whole table and **is not scoped by
status**: a retired row keeps its serial, because the physical unit still exists and still
carries it.

An editable inactive asset punches a hole straight through that. Change a scrapped unit's
serial number and the old value is released — free for a new asset to claim — while a real
machine somewhere still bears it. The database would then be able to hold two rows for one
physical serial, which is precisely the data error US-02A §2 built the unique index to
prevent. The gate is what keeps the index's guarantee true over time rather than only at the
moment of insert.

Note this is the mirror image of the customer rule and not the same reasoning. Editing an
inactive customer is refused partly to stop the generated `phone_active_unique` column being
driven into an un-reactivatable state — a value that is *released* on deactivation. Here the
protected value is one that is *deliberately never released*. Same gate, opposite mechanics.

### Response shape

`AssetInactive` becomes a **409 with a plain `ProblemDetails`**, not the keyed
`ValidationProblemDetails` the duplicate serial uses:

```json
{
  "title": "Asset is not active.",
  "status": 409,
  "detail": "This asset has been deactivated and can no longer be edited."
}
```

The rule belongs to the request as a whole, so there is no field to key it on. This mirrors
`CustomersController` exactly, and it matters: the frontend branches on **whether `errors` is
present**, never on the status code, so a keyed body here would be rendered under whichever
input the key named. `AssetForm` already has the `else` branch that builds a form-level alert
from `title` + `detail` (§7), so no frontend change was needed for the message to appear —
though §7 covers why the Edit button is no longer offered in the first place.

### The owning customer's status is still not checked

Unchanged by this. The create path refuses an inactive owner (`CustomerInactive`); the update
path never asks, because the owner is not part of the request and cannot be changed by it.
`AssetService` still takes `ICustomerRepository`, and the update path still does not use it.
An **active** asset belonging to a deactivated customer therefore remains editable — the
equipment is in service whatever the account's state.

---

## 7. Frontend

### One form, two modes

`AssetForm` was taught to edit rather than a second form being written, exactly as
`CustomerForm` was in US-01C. The props are a discriminated union, so `mode="edit"` cannot
be written without the things edit needs:

```tsx
type AssetFormProps =
  | { mode: 'create' }
  | {
      mode: 'edit'
      assetId: string
      initialValues: AssetFormValues
      customerName: string | null
      onSaved: (asset: AssetResponse) => void
      onNotFound: () => void
    }
```

`CreateAssetPage` changed by exactly one line — `<AssetForm />` became
`<AssetForm mode="create" />` — which is the union doing its job: the compiler demanded the
discriminant rather than letting an un-tagged call fall into an ambiguous default.

### The form state keeps a field the request does not

```tsx
type AssetFormValues = CreateAssetRequest
```

Seven fields, including `customerId`, even in edit mode where `UpdateAssetRequest` has six.
The owner is held so the form can **show** which customer the asset belongs to, and is
dropped at submit time — listed field by field rather than spread, which is what keeps it
off the wire:

```tsx
await updateAsset(props.assetId, {
  assetType: request.assetType,
  model: request.model,
  serialNumber: request.serialNumber,
  installationDate: request.installationDate,
  location: request.location,
  notes: request.notes,
})
```

A spread with a `delete` would have compiled and would have been one refactor away from
sending the owner again.

### The owner is displayed, not offered

In edit mode the customer `<select>` is replaced by read-only text — a `<div>` rather than
a `<label>`, since there is no control to label — with the standing explanation that an
asset does not change hands, and a link to the owning customer.

Read-only rather than hidden: **the owner is what confirms this is the right asset.** A
serial number and a model are easy to mistake between two similar units on the same street.

`EditAssetPage` fetches that customer, and deliberately gives it **no loading or error
state of its own**. The field is informational, the id is already on hand as a fallback,
and a customer record that will not load is no reason to block an edit that cannot touch
the owner anyway. The form renders `customerName ?? <code>{customerId}</code>`.

This is the opposite call from `CustomerDetailPage`'s assets section
([Asset List & Detail §5](asset-list-and-detail.md#5-frontend)), which *does* get its own
error state — because there the list is the point of the section, and here the name is a
label on a field.

### Edit mode loads no customers at all

The `getAllCustomers('ACTIVE')` effect returns early when `isEdit`, and `customersLoading`
is initialised to `!isEdit` so edit mode starts **out** of the loading state rather than
stuck in it forever waiting for a fetch that never runs. `noActiveCustomers` is likewise
`!isEdit && …`: an asset being edited already has an owner, whatever that customer's
status, so "there are no active customers" is not a reason to block the save.

That last point matters — an asset whose owner has since been deactivated is still
editable, and the form must not disable itself because the create-mode dropdown would have
been empty.

### Four error cases, one branch

| Response | Rendered as |
|---|---|
| **400** | field errors under each input |
| **409** on `serialNumber` | the same field path |
| **404** | `onNotFound()` → the page's not-found state |
| anything else | form-level alert from `title` + `detail` |

The 404 is checked **before** `problem?.errors`, because it is the only failure that is not
a field problem and the page, not the form, owns the response to it — the asset was deleted
between loading the form and saving it.

### Routes and entry point

`/assets/:id/edit` → `EditAssetPage`, declared after `/assets/:id`. `AssetDetailPage` gained
an **Edit** button in its page head, shown once the asset has loaded.

That button originally had **no status condition**, which was consistent while there was no
rule to reflect. §6 gave it one, and the button now carries the matching guard:

```tsx
{asset.status === 'ACTIVE' && (
  <Link className="btn btn-primary" to={`/assets/${asset.id}/edit`}>Edit</Link>
)}
```

Absent rather than disabled, the same treatment `CustomerDetailPage` gives both its actions
once a customer is inactive — there is nothing left to do, so there is nothing to offer. The
route `/assets/:id/edit` is still reachable by URL, and the 409 remains the backstop for an
asset deactivated in another tab after the form was loaded. **Hiding the link is a
convenience, not the enforcement.**

---

## 8. Testing

Five tests added to `AssetServiceTests.cs`, taking the suite to **66 executed cases**.

| Test | Asserts |
|---|---|
| Valid request changes editable fields only | new values written; `Id`, `CustomerId`, `CreatedAt` carried over; serial stored as typed *and* normalized |
| Asset missing | `NotFound`, **`UpdateAsyncCallCount == 0`** |
| Another asset has the serial | `DuplicateSerial`, **`UpdateAsyncCallCount == 0`** |
| **Serial unchanged → own row excluded** | the asset's own id reached the repository as the row to exclude, and the save succeeded |
| Unique index rejects the update | `DuplicateSerial`, **`UpdateAsyncCallCount == 1`** |

A sixth arrived with the status gate, taking the suite to **70**:

| Test | Asserts |
|---|---|
| **Asset not active** | `AssetInactive`, **`UpdateAsyncCallCount == 0`**, and **`SerialExistsAsyncCallCount == 0`** — the gate runs before the serial is looked at (§5) |

`ExistingAsset()` gained `Status = "ACTIVE"` in the same change. It had been leaving the
field at its `string.Empty` default, which the new gate reads as not-active — without it the
four tests above would have started failing on a rule they are not about.

The fourth is the story's real risk (§4). The fake is configured so the serial check
*would* report a clash, and the update succeeds anyway — proving the exclusion is passed
through rather than the check happening to return false. It also retypes the serial as
`"abc 123"` against a stored `"ABC-123"`, so it exercises normalization and exclusion
together: the exclusion has to match on the **normalized** form or it would not fire.

The paired call counts are the same instrument US-02A used: `== 0` proves the pre-check
short-circuits, `== 1` proves the race path actually attempted the write.

### `FakeAssetRepository`

Gained `UpdatedAsset`, `UpdateAsyncCallCount` and `SerialExistsExcludeAssetId`, and — like
the customer fake before it — `SerialExistsAsync` now **models the effect of the exclusion**
rather than only recording the argument:

```csharp
if (excludeAssetId is not null
    && excludeAssetId == AssetToReturn?.Id
    && serialNormalized == AssetToReturn.SerialNormalized)
{
    return Task.FromResult(false);
}
```

That stands in for `WHERE id != @excludeId`. Any *other* serial still clashes, so the
duplicate test and the exclude-self test stay meaningful against the same fake. A fake that
merely recorded the id would let a service that ignored the exclusion pass both.

`ExceptionToThrow` was extended to fire from `UpdateAsync` as well as `CreateAsync`, using
the reflection-built `MySqlException` from `Fakes/MySqlExceptions.cs` (US-02A §7).

---

## 9. Still for QA

1. **No manual verification is recorded for this story.** US-02A and US-02B both close with
   a table of checks run against real MySQL and the dev server; this note has none, because
   it was written after the fact (see the header). The endpoint and the edit page have not
   been walked through in any session that produced a written record.
2. **`AssetRepository.UpdateAsync`** — untested, like the rest of the repository (US-01A §8).
   The conditional SQL in `SerialExistsAsync` is the piece most worth an integration test:
   it is the only query in the asset service whose *text* changes at runtime.
3. **The 1062 path on update.** Covered only by a fabricated exception. Nobody has watched
   the real unique index raise it on an `UPDATE`.
4. **Concurrent update and deactivate.** Now that US-02D exists, an update can be in flight
   while a deactivate lands on the same row. The row survives either way — the two write
   disjoint column sets — but which `updated_at` wins is untested.
5. **Editing an asset whose owner is inactive.** The form deliberately allows it (§7). Worth
   confirming end to end, since it is the one path where create and update disagree about
   the owner's status.
6. **The frontend has no test script**, so everything in §7 is manual. See the CI note in
   the frontend README.
7. ~~**`AssetDetailPage` still offers Edit on an inactive asset.**~~ **Closed.** The button
   is now gated on `status === 'ACTIVE'`, matching `CustomerDetailPage` (§7). The 409
   remains the backstop for an asset deactivated after the page was loaded.

---

## 10. Decisions worth carrying forward

- **A request DTO that cannot express a field cannot change it.** `UpdateAssetRequest` omits
  `customerId` rather than validating that it did not change.
- **Optional exclusion parameters keep existing call sites still.** Second use of the shape
  after `ActivePhoneExistsAsync`; the create path runs the identical query it always did.
- **Exclude on the normalized form.** The exclusion and the uniqueness check have to agree
  about what "the same value" means, or retyping a serial with different punctuation
  defeats it.
- **A fake that models the effect of a write catches what a fake that records the call
  cannot.** Both the exclusion and the status flip in US-02D followed this.
- **Two similar rules are not one rule.** The customer serial-equivalent filters by status;
  the asset one does not. Copying the exclusion without re-deriving the status clause would
  have been wrong in both directions.
- **Discriminated-union props over optional props** for a component with two modes. The
  one-line change to `CreateAssetPage` is the compiler proving nothing was missed.

---

## TODO before merge

- [ ] Record a manual verification pass, or accept §9.1 as a known gap
- [x] Hide `AssetDetailPage`'s Edit button on an inactive asset — **done**, gated on
      `status === 'ACTIVE'` (§7)
- [x] §6 decided — **an inactive asset is not editable**; `AssetInactive` → 409
- [x] Jira id and branch — **ASSMS-79**, `ASSMS-79-US-02C-Update-Asset`
- [x] Route path for the edit page — **`/assets/:id/edit`**, matching `/customers/:id/edit`
- [x] Whether `UpdateAssetRequest` should reuse `CreateAssetRequest` — **no**, §3
