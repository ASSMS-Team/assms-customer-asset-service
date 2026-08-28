# US-02D — Deactivate Asset

**Jira:** ASSMS-80 · branch `ASSMS-80-US-02D-Deactivate-Asset`
**Repos touched:** `assms-customer-asset-service`
**Service:** Customer & Asset Service · **Database:** `customerdb` · **Table:** `assets`
**Follows:** [US-02C — Update Asset](update-asset.md)
**Mirrors:** [Update & Deactivate Customer](../customer-management/update-and-deactivate-customer.md) *(US-01D)*

> As an Agent, I want to retire a unit that is no longer in service, so that it stops
> counting as live equipment without losing the record of what was installed.

---

## 1. What was built

Assets gained a lifecycle, and one endpoint to move them along it:

```
POST /api/assets/{id}/deactivate  →  AssetService.DeactivateAsync  →  AssetRepository.DeactivateAsync  →  customerdb
```

| Layer | Files |
|---|---|
| Database | `database/migrations/V03__add_asset_status.sql` |
| Tooling | `scripts/development/apply_migrations.ps1` — migration tracking, §2 |
| Model | `Models/Asset.cs` |
| DTOs | `DTOs/AssetResponse.cs` — **not** the two request DTOs, §5 |
| Data access | `Repositories/IAssetRepository.cs`, `Repositories/AssetRepository.cs` |
| Business logic | `Services/AssetService.cs` |
| HTTP | `Controllers/AssetsController.cs` |
| Tests | `tests/.../Fakes/FakeAssetRepository.cs`, `tests/.../UnitTests/AssetServiceTests.cs` |
| Docs | `README.md` — endpoint table, §8 |
| Frontend — data | `src/types/asset.ts`, `src/services/assetService.ts` |
| Frontend — pages | `src/pages/assets/AssetDetailPage.tsx`, `src/pages/customers/CustomerDetailPage.tsx` |

**The frontend followed separately.** The server side landed first, with `status` arriving
in the JSON and nothing consuming it. `assms-frontend` then caught up: `deactivateAsset` in
`assetService.ts`, `status` on `AssetResponse`, and on `AssetDetailPage` a `StatusBadge`, a
confirm-then-deactivate button, an `actionError` above the card, and the Edit button gated
on `ACTIVE` — the last of those is the rule from
[US-02C §6](update-asset.md#6-only-active-assets-are-editable), and its reasoning is in
[US-02C §7](update-asset.md#7-frontend). `CustomerDetailPage`'s assets table gained a
status column, because the list is not filtered by status and the badge is what says which
equipment is still in service.

**No new `ServiceError` value.** `NotFound` covers the only failure. See §4.

---

## 2. The migration — and the first one that cannot be re-run

```sql
ALTER TABLE assets
    ADD COLUMN status VARCHAR(20) NOT NULL DEFAULT 'ACTIVE' AFTER notes,
    ADD CONSTRAINT chk_assets_status CHECK (status IN ('ACTIVE', 'INACTIVE')),
    ADD KEY idx_assets_status (status);
```

Type, default and permitted values are copied from `customers.status` deliberately — two
tables with a `status` column that disagreed on width or vocabulary would be a trap.
Existing rows take the default and come out `ACTIVE`, which is what they already were in
every sense except the one the schema could express.

### Why this broke the migration runner

`V01` and `V02` are `CREATE TABLE IF NOT EXISTS` with their indexes declared **inline**,
which is what made them safe to apply on every run — MySQL 8 has no
`CREATE INDEX IF NOT EXISTS`, so a bare `CREATE INDEX` fails the second time. `V03` has no
such escape hatch: `ALTER TABLE ... ADD COLUMN IF NOT EXISTS` is **MariaDB syntax** and is
a plain syntax error on MySQL 8. A second run would have died with
`ERROR 1060 (42S21): Duplicate column name 'status'` and stopped every later migration
behind it.

`apply_migrations.ps1` re-applied every file on every run, so this was a decision that had
to be made before the first column addition, not after.

### schema_migrations

The runner now tracks what it has applied, which is the TODO its own header comment had
been carrying since `V01`:

```sql
CREATE TABLE IF NOT EXISTS schema_migrations (
    filename   VARCHAR(255) NOT NULL,
    applied_at TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (filename)
);
```

Create the table if absent → `SELECT` the applied filenames before the loop → skip any file
already listed → `INSERT` each filename **after** its file succeeded.

Three details that are not decoration:

- **Recorded after, never before.** A migration that failed is not in the table, so the next
  run retries it rather than skipping it as done.
- **A failed `INSERT` fails the run.** If the file applied but the record did not land, the
  next run would apply it a second time — which for a column addition is exactly the failure
  the tracking exists to prevent. Better to stop on the file that did it than to discover it
  later behind three more migrations.
- **The table is not itself a migration.** It has to exist before the first file is even
  looked at, and it belongs to the runner rather than to the schema the migrations build.

A database migrated before the tracking existed has no rows, so `V01` and `V02` are applied
once more on the first tracked run and then recorded. Both are `CREATE TABLE IF NOT EXISTS`;
that costs nothing. This is the one run where the old re-runnability property is still
load-bearing, and it is why the alternative — starting the table pre-seeded with the
filenames — was not worth the guesswork about which databases had seen what.

### Verified against real MySQL

| Check | Result |
|---|---|
| First run | `V01`, `V02`, `V03` applied; three rows in `schema_migrations` |
| `SHOW CREATE TABLE assets` | `status varchar(20) NOT NULL DEFAULT 'ACTIVE'`, `KEY idx_assets_status`, `CONSTRAINT chk_assets_status` |
| Second run | all three skipped, `Applied: none`, exit 0 |

`SHOW CREATE TABLE` renders the constraint with `_latin1` prefixes on the two literals.
That is the mysql client's connection charset at the moment the statement ran, not
something this migration chose — `chk_customers_status` from `V01` reads identically. The
values are ASCII, so the comparison against a `utf8mb4` column coerces cleanly.

---

## 3. What `status` on `assets` does **not** do

US-02A §2 said, of the `serial_normalized` unique index:

> Unlike the `customers` table there is no status here to scope it by — an asset row is
> never retired in place.

**Half of that is now out of date and half of it is still the point.** There is a status.
The index is still not scoped by it, and that is deliberate.

| | `customers` | `assets` |
|---|---|---|
| Unique value | phone number | serial number |
| Scoped by status? | **yes**, via the generated `phone_active_unique` | **no** |
| Deactivating frees the value? | **yes** — the generated column drops to `NULL` | **no** |

A phone number is genuinely reusable: the person who had it stopped being a customer, and
somebody else can now hold it. A serial number is not. It identifies one physical unit, and
retiring the row does not make the unit stop existing — if the same serial could be
registered again, the database would be claiming two machines share an identity. So
deactivating an asset **releases nothing**, and `AssetRepository.DeactivateAsync` has no
counterpart to the generated-column mechanics described in
[Update & Deactivate Customer §4](../customer-management/update-and-deactivate-customer.md#4-deactivate).

That asymmetry has a pleasant consequence: **the reactivation hazard that story left open
does not exist here.** A customer cannot always be reactivated, because somebody may have
taken their number while they were inactive. Nothing can take an asset's serial, so
reactivating one is a bare status flip that cannot fail. There is still no endpoint for it
(§9), but whoever writes one inherits no caveat.

### Two statuses that do not talk to each other

Deactivating a customer does **not** cascade to their assets, and nothing stops an
`INACTIVE` customer from owning `ACTIVE` assets or the reverse.
`CustomerRepository.DeactivateAsync` writes one row in `customers` and no rows in `assets`.

This is consistent with the read side, which already refuses to let the customer's status
narrow anything: [Asset List & Detail §2](asset-list-and-detail.md#2-api) keeps a
deactivated customer's equipment visible because it is history. If retiring a customer
should retire their equipment, that is a business rule nobody has stated, and it would need
to be a deliberate multi-row write rather than a side effect.

### The index is ahead of its query

`idx_assets_status` has no reader yet. `GET /api/customers/{customerId}/assets` returns
every asset whatever its status, and there is no `?status=` filter on any asset endpoint —
unlike `GET /api/customers`, whose filter is what `idx_customers_status` exists for. The
index was added with the column so the eventual filter does not need a second migration
against a table that by then has rows. It is a bet, and a cheap one on a table this size.

---

## 4. API

| Endpoint | Success | Failures |
|---|---|---|
| `POST /api/assets/{id}/deactivate` | **200** + the stored asset | **404** unknown id |

One failure, so one `ServiceError`: `NotFound`, reused as-is. Nothing new was added to the
enum, and the controller has a single branch.

Contrast the customer endpoint, which is also 200/404 — and the customer **update**
endpoint, which needed a second 409 shape for "not active". Asset deactivation has no
business rule that can be violated: an unknown asset is a 404, and every other input is
already a valid request (§5).

`POST` to a verb path, not `DELETE` and not `PUT`, for the reason given in
[Update & Deactivate Customer §2](../customer-management/update-and-deactivate-customer.md#deactivate-is-a-post-not-a-put-or-delete):
`DELETE` would say the row is gone, and it is not. This is the second endpoint in the
service to put a verb in a path, and the two now form a pattern rather than an exception —
worth writing into [API conventions](../api/conventions.md#routing) if a third appears.

Swagger picks the endpoint up from the `///` comments on the action; there is no second
place to update. `AssetResponse.Status` carries a `<summary>` so it renders as a field
description in the schema view.

---

## 5. Where `status` is written, and where it is not

The column is server-controlled end to end. Four paths touch the table and only one of them
writes it.

| Path | `status` |
|---|---|
| `CreateAsync` | **not in the `INSERT`** — the column's `DEFAULT 'ACTIVE'` writes it |
| `UpdateAsync` | **not in the `SET` list** |
| `DeactivateAsync` | the only column written |
| `GetByIdAsync` / `GetByCustomerIdAsync` | read, in both the `SELECT` and both reader mappings |

`CreateAssetRequest` and `UpdateAssetRequest` have no `status` property. Binding it from the
body would be the mass-assignment hole [API conventions](../api/conventions.md#dtos) names
explicitly, and it would also make "edit this asset" capable of retiring one.

### Why the default writes it rather than the INSERT

Naming `status` in the `INSERT` would work, and it is what `CustomerRepository.CreateAsync`
does. It was left out here because the column already states what a new asset starts as, and
sending the value would be a second place to keep that in step with the schema.

There is one consequence. `AssetService.CreateAsync` re-reads the row before mapping, so the
response normally carries whatever the database wrote — but it falls back to the in-memory
object if that read misses:

```csharp
var created = await _repository.GetByIdAsync(asset.Id) ?? asset;
```

On that fallback the object's `Status` is whatever C# put there, and an unset `string` is
`""`. So the service sets `Status = "ACTIVE"` on the `Asset` it builds — **for the fallback
only**; the value is never sent to MySQL. Without it a create whose read-back lost the race
would return an empty `status` in a 201 body.

### The UPDATE

```sql
UPDATE assets
SET asset_type = @assetType, model = @model, serial_number = @serialNumber,
    serial_normalized = @serialNormalized, installation_date = @installationDate,
    location = @location, notes = @notes
WHERE id = @id;
```

`status` joins `customer_id` and `created_at` on the list of columns the edit path may not
rewrite. Editing a form should never retire a unit, exactly as it should never move one to a
different customer. `updated_at` is maintained by `ON UPDATE CURRENT_TIMESTAMP`.

### The deactivate

```sql
UPDATE assets SET status = 'INACTIVE' WHERE id = @id;
```

Status is the only column written. `updated_at` fires on its own. Nothing else changes,
because — unlike the customer table — there is nothing else that depends on the status (§3).

---

## 6. The deactivate path

1. **Load** via `GetByIdAsync`. `null` → `NotFound` → 404.
2. **Status gate.** Anything other than `ACTIVE` → success, returning the asset as loaded,
   **without writing**.
3. **Write** `DeactivateAsync(id)`.
4. **Re-read** and map, so `status` and `updatedAt` are the values the database holds. Falls
   back to the loaded row if that read misses — the write did land, so this succeeds rather
   than throwing.

### Idempotent, and honest about it

| State | Result | Write? |
|---|---|---|
| Unknown id | `NotFound` → 404 | no |
| `ACTIVE` | success, now `INACTIVE` | yes |
| already `INACTIVE` | success, unchanged | **no** |

Returning success on the third row is the criterion — the caller asked for the asset to be
inactive and it is. **Skipping the write is what keeps `updated_at` honest:** a redundant
`UPDATE` would still fire `ON UPDATE CURRENT_TIMESTAMP`, and the audit trail would show the
unit being "changed" every time somebody pressed the button again. Identical reasoning, and
identical structure, to
[Update & Deactivate Customer §4](../customer-management/update-and-deactivate-customer.md#idempotent-and-honest-about-it).

The gate is written `!= "ACTIVE"` rather than `== "INACTIVE"`, so a third status added later
falls into the no-write branch by default rather than being silently deactivated by a rule
written before it existed.

---

## 7. Testing

Three tests added to `AssetServiceTests.cs`.

| Test | Asserts |
|---|---|
| Active asset | success, status `INACTIVE`, `DeactivateAsyncCallCount == 1`, the route's id reached the repository |
| Unknown id | `NotFound`, **call count 0** |
| **Already inactive** | success, status `INACTIVE`, **call count 0**, `UpdatedAt` unchanged |

The third is the one that matters. Success alone would pass with a redundant write in place;
the call count and the untouched `UpdatedAt` are what pin the no-write branch.

### `FakeAssetRepository`

New fields `DeactivatedId` and `DeactivateAsyncCallCount`, and — as with the customer fake —
`DeactivateAsync` **models the effect of the write** rather than only recording the call:

```csharp
if (AssetToReturn is not null && AssetToReturn.Id == id)
{
    AssetToReturn.Status = "INACTIVE";
}
```

Without it the service's read-back would receive the same `ACTIVE` object it was handed
before the write, and the first test would assert `INACTIVE` against a fake that never
changed anything — passing for the wrong reason.

### Suite size

Counted from the source, not from a run: **69 executed cases** across 49 test methods
(`AssetServiceTests` 20, `CustomerServiceTests` 21, `SerialNormalizerTests` 15,
`PhoneNormalizerTests` 13). Sixty-six before this story.

> **Not run in the session that wrote this.** The three tests were written, the solution
> builds clean (0 warnings, 0 errors), and `dotnet test` has not been executed against them.
> Run it before merging. No coverage figures are quoted here for the same reason — the last
> recorded ones are in [US-02A §7](US-02A-create-asset.md#coverage).

---

## 8. Documentation

`README.md` had an empty `## Documentation` heading — the shared skeleton every service repo
in the org carries — and no endpoint listing anywhere. It now holds how the API documents
itself, where Swagger is served, a link to [API conventions](../api/conventions.md), a table
of all twelve endpoints including this one, and links to the story notes in `docs/`.

Two Swagger summaries on `AssetsController` were corrected in the same pass, because
`status` made them wrong: `POST /api/assets` now says the asset is created **ACTIVE**, and
`PUT /api/assets/{id}` lists **status** among the fields it leaves alone.

---

## 9. Still for QA / still open

1. **Nothing here was exercised over HTTP.** The migration was applied and verified against
   real MySQL; the endpoint was not called. `AssetRepository.DeactivateAsync` and the
   controller are untested for the usual reason (US-01A §8), and this is the first write path
   on `assets` with no manual verification behind it at all.
2. ~~**An inactive asset is still editable.**~~ **Closed.** `AssetService.UpdateAsync` now
   refuses a non-`ACTIVE` asset with `AssetInactive` → 409, matching
   `CustomerService.UpdateAsync`. The reasoning — a retired row is the record of scrapped
   equipment, and an editable one would let a released serial be re-registered while the
   physical unit still carries it — is in
   [US-02C §6](update-asset.md#6-only-active-assets-are-editable). `AssetDetailPage` now
   hides its Edit button on an inactive asset too, so the rule is reflected in the UI and
   not only enforced at the API.
3. **No reactivate endpoint.** Unlike the customer case there is no hazard blocking one (§3);
   there is simply no endpoint. Until there is, reactivating is a manual `UPDATE`.
4. **No status filter on any asset endpoint**, so a retired unit still appears in the
   customer's asset list with nothing marking it. `idx_assets_status` is waiting for that
   query.
5. **The `CHECK` constraint is untested.** No `[RegularExpression]` mirrors it, because no
   request DTO carries `status` — the only writer is a hard-coded literal in the repository.
   A bad value cannot come from the API, which is the point, but it also means the constraint
   has never rejected anything.
6. **`schema_migrations` against a pre-existing staging database.** The first tracked run
   re-applies `V01` and `V02` there too. Locally that is a no-op; confirm it on staging
   before deploying, and confirm the service account can `CREATE TABLE`.
7. ~~**Frontend.** `status` is in the response and nothing consumes it.~~ **Closed.** Built
   to the customer detail page's template — badge, confirm dialog, both buttons hidden once
   inactive, state set from the response with no refetch, `actionError` above the card. It
   type-checks, builds and lints clean, but **none of it has been clicked through**: no dev
   server was run against a live API. The five acceptance checks are in the TODO list at the
   end of this note, all still open.

---

## 10. Decisions worth carrying forward

- **A tracked migration runner beats a re-runnable one.** Re-runnability held exactly as long
  as every migration was a `CREATE TABLE`. The first `ALTER` ended it, and no amount of care
  in the SQL would have saved it on MySQL 8.
- **Record a migration after it succeeds, and fail the run if the record does not land.** Both
  halves; either one alone is worse than no tracking.
- **The same word can mean different things on two tables.** `status` retires a customer and
  frees their phone number; it retires an asset and frees nothing. Copy the column
  definition, not the consequences.
- **Idempotent means succeed *and* skip the write.** Success is what the caller sees; skipping
  the write is what keeps the audit trail truthful.
- **Gate on `!= ACTIVE`, not `== INACTIVE`,** so a status added later defaults to the safe
  branch.
- **A column default is fine as the only writer** — but then anything that falls back to an
  in-memory object has to set the same value itself, or the fallback returns an empty string.

---

## TODO before merge

- [ ] `dotnet test` — the three new tests have not been run (§7)
- [x] Decide whether an inactive asset should stay editable — **it should not**; see
      [US-02C §6](update-asset.md#6-only-active-assets-are-editable)
- [x] Jira id and branch — **ASSMS-80**, `ASSMS-80-US-02D-Deactivate-Asset`
- [x] Frontend: surface `status`, and a deactivate action on `AssetDetailPage` — **done**,
      builds and lints clean (§9.7)
- [ ] Walk the frontend acceptance checks against a running API and dev server — none of
      these have been clicked through:
  - [ ] Active asset shows both buttons and a green badge
  - [ ] Deactivate → confirm → badge grey, both buttons gone
  - [ ] Dismissing the confirm fires no request
  - [ ] The asset appears with a grey badge in the customer's asset list
  - [ ] `/assets/{inactive-id}/edit` loads the form but 409s on save, as a form-level alert
- [ ] Coverage report regenerated — no figures quoted here
- [ ] US-02A's open `notes VARCHAR(1000)` item says to change it "via a `V03__` migration".
      **`V03` is taken** — that would now be `V04__`
- [x] US-02A §2's "an asset row is never retired in place" — **superseded, see §3**; the
      claim it was supporting, that `serial_normalized` is unique table-wide and unscoped,
      still holds
