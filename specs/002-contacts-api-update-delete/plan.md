# 002: Contacts API: update and delete: Technical plan

**Status:** Planned
**Spec:** ./spec.md
**ADRs:** ADR-0006 (validation message style, Accepted). Builds on ADR-0003 (case-insensitive text), ADR-0004 (validation and error pipeline), ADR-0005 (integration test database).

## Approach
Spec 002 adds two endpoints to the existing `Contacts` feature group: `PUT /api/contacts/{id:guid}` (full replace) and `DELETE /api/contacts/{id:guid}`. Everything they need already exists from spec 001: the pure `ContactInput.Parse` rules, the unique `NOCASE` index on `Email`, the `SqliteErrors` unique-violation check, and the environment-independent ProblemDetails pipeline. No new dependencies, no schema change, no new ADR for the endpoints themselves.

Four design choices shape the work:

1. **One source of field rules (NFR-004).** Update reuses `ContactInput.Parse` through a second overload that shares a single private core with the create overload. Create and update therefore can't drift, and update inherits trimming, null-mapping, max lengths, and the email rule unchanged.
2. **The database stays the arbiter of email uniqueness, also for updates.** No pre-check query. The update writes, and a SQLite 2067 (`SQLITE_CONSTRAINT_UNIQUE`) maps to 409 exactly as on create. A contact never conflicts with itself, because SQLite checks a unique index against *other* rows when a row is updated, so changing only the case of a contact's own email (AC-013) succeeds.
3. **Races resolve to documented status codes, never to 500.** Update loads the tracked entity and saves; if the row vanished in between (concurrent delete), EF Core reports zero affected rows as `DbUpdateConcurrencyException`, which the handler maps to 404 (AC-035). Delete is a single `DELETE ... WHERE Id = @id` statement (`ExecuteDeleteAsync`) whose affected-row count decides 204 vs 404, so two concurrent deletes give exactly one 204 (AC-036) with no read-then-delete window.
4. **New 404s are explicit ProblemDetails results.** PUT and DELETE return `TypedResults.Problem(statusCode: 404)` (a `ProblemHttpResult`) instead of `TypedResults.NotFound()`, and declare `.ProducesProblem(404)`. The runtime body is the same problem+json that status code pages produce today, but the OpenAPI document gets one 404 entry with `application/problem+json` content instead of a content-less 404 (NFR-001, as requested at planning).

The spec also aligns validation message wording on create and list to one style (AC-039, ADR-0006). That is scheduled first (T-01) so the update endpoint reuses compliant messages from the moment it exists.

Queries stay in the handlers (conventions).

## Components touched
| Component / module | Change | Notes |
|---|---|---|
| `src/MicroCrm.Api/Features/Contacts/ContactDtos.cs` | modified | + `UpdateContactRequest` (same six nullable string members as create) |
| `src/MicroCrm.Api/Features/Contacts/ContactInput.cs` | modified | + `Parse(UpdateContactRequest)` overload; both overloads delegate to one private core. `firstName` message becomes `"Required."` (T-01) |
| `src/MicroCrm.Api/Features/Contacts/ContactsEndpoints.cs` | modified | + `UpdateContact` (PUT `{id:guid}`), + `DeleteContact` (DELETE `{id:guid}`), OpenAPI metadata (T-09) |
| `src/MicroCrm.Api/Common/Paging.cs` | modified | `ListQuery.Parse` messages aligned to ADR-0006 (T-01) |
| `src/MicroCrm.Api/MicroCrm.Api.http` | modified | Sample PUT and DELETE requests (dev convenience, no tests) |
| `src/MicroCrm.Api/Data/*` | **unchanged** | No migration. See "Schema" below |
| `tests/.../Integration/Infrastructure/ProblemAssert.cs` | modified | + validation-message style assertion (AC-039) |
| `tests/.../Integration/Contacts/Update*.cs`, `DeleteContactTests.cs`, `UpdateDeleteRaceTests.cs` | new | See Test strategy |
| `tests/.../Integration/ErrorHandlingTests.cs`, `OpenApiTests.cs` | modified | AC-038 for PUT/DELETE; NFR-001 for PUT/DELETE |
| `tests/.../Integration/Contacts/CreateContactValidationTests.cs`, `ListContactsPagingTests.cs` | modified | AC-039 for create and list |
| `tests/.../Unit/Contacts/ContactInputTests.cs`, `Unit/Common/ListQueryTests.cs` | modified | AC-039 exact messages; NFR-004 overload equivalence |
| `docs/conventions.md` | modified (documenter) | Errors row: message style per ADR-0006. Implementer can't edit this file |
| `docs/adr/0006-validation-message-style.md` | new (planner) | Proposed |

## Interfaces & data

### HTTP contract
```text
PUT /api/contacts/{id:guid}
  Request  (application/json): { "firstName": string?, "lastName": string?, "email": string?,
                                 "phone": string?, "company": string?, "notes": string? }
           Unknown members (id, createdAt, updatedAt, ...) are ignored. Missing optional member == null (full replace).
  Check order (spec Q2):
    1. unreadable body (malformed JSON, empty, JSON null, array, wrong JSON type)  -> 400 problem+json (no errors dictionary required)
    2. field validation errors (ContactInput.Parse)                               -> 400 validation problem+json, errors keyed by camelCase field
    3. no contact with that id                                                    -> 404 problem+json (never creates)
    4. email held by another contact (SQLite 2067)                                -> 409 problem+json (detail doesn't echo the email)
    5. contact deleted between load and save (DbUpdateConcurrencyException)       -> 404 problem+json
  200 Body: Contact (same shape as spec 001), id/createdAt unchanged, updatedAt = TimeProvider.GetUtcNow()
  Non-GUID id: no route matches -> 404 problem+json (status code pages).

DELETE /api/contacts/{id:guid}
  Request body, if any: ignored (no body parameter).
  204 No Content, empty body: exactly one row deleted
  404 problem+json: zero rows deleted (unknown, already deleted, or lost a concurrent delete)
  Non-GUID id: no route matches -> 404 problem+json.

Any unexpected exception -> 500 problem+json { type, title, status[, traceId] } with no exception details (existing pipeline).

Validation messages (ADR-0006, every contacts endpoint):
  "Required." | "Must be {max} characters or fewer." | "Must be a valid email address." | "Must be an integer between {min} and {max}."
  Spec 001 changes: firstName "First name is required." -> "Required."
                    page      "'page' must be an integer between 1 and 2147483647." -> "Must be an integer between 1 and 2147483647."
                    pageSize  "'pageSize' must be an integer between 1 and 100."     -> "Must be an integer between 1 and 100."
                    search    "'search' must be at most 254 characters."             -> "Must be 254 characters or fewer."
```

### C# signatures (targets for tests; names may be refined by the implementer, behavior may not)
```csharp
// Features/Contacts/ContactDtos.cs
public sealed record UpdateContactRequest(string? FirstName, string? LastName, string? Email,
                                          string? Phone, string? Company, string? Notes);

// Features/Contacts/ContactInput.cs  (pure; unit-tested)
public static (ContactInput? Input, Dictionary<string, string[]>? Errors) Parse(CreateContactRequest request); // existing
public static (ContactInput? Input, Dictionary<string, string[]>? Errors) Parse(UpdateContactRequest request); // new
// Both delegate to one private core taking the six raw strings. Same input => identical result (NFR-004).

// Features/Contacts/ContactsEndpoints.cs
//   PUT    "/{id:guid}" -> Task<Results<Ok<ContactResponse>, ValidationProblem, ProblemHttpResult>>  name "UpdateContact"
//          (Guid id, UpdateContactRequest request, AppDbContext db, TimeProvider time, CancellationToken ct)
//          .WithSummary(...).ProducesProblem(404).ProducesProblem(409)
//   DELETE "/{id:guid}" -> Task<Results<NoContent, ProblemHttpResult>>                               name "DeleteContact"
//          (Guid id, AppDbContext db, CancellationToken ct)
//          .WithSummary(...).ProducesProblem(404)
```

Why a separate `UpdateContactRequest` instead of reusing `CreateContactRequest`: the name stays honest in the OpenAPI schema and the two can diverge later, while the shared private core keeps one set of rules. Renaming `CreateContactRequest` would force edits to existing tests, which the implementer can't make.

### Update handler outline (behavior the tests pin; not code to copy)
```text
(input, errors) = ContactInput.Parse(request)            -> errors: ValidationProblem(errors)          [T-03]
contact = db.Contacts.FirstOrDefaultAsync(c => c.Id == id)  (tracked)
                                                         -> null:   Problem(404)                       [T-04]
assign all six fields from input (full replace); UpdatedAt = time.GetUtcNow(); CreatedAt, Id untouched   [T-02]
SaveChangesAsync
  catch DbUpdateConcurrencyException                     -> Problem(404)                               [T-08]
  catch DbUpdateException when IsUniqueConstraintViolation -> Problem(409, title "A contact with this email already exists.") [T-05]
  any other exception: not caught -> 500 via UseExceptionHandler
Ok(ContactResponse.From(contact))
```
`DbUpdateConcurrencyException` derives from `DbUpdateException`; its catch must come first (and the 2067 filter wouldn't match it anyway, because it has no `SqliteException` inner exception).

If the request values equal the stored ones and the clock hasn't moved, EF issues no UPDATE and the handler still returns 200 (AC-006 is tested with an advanced clock, so `UpdatedAt` always changes in that test).

### Delete handler outline
```text
deleted = db.Contacts.Where(c => c.Id == id).ExecuteDeleteAsync(ct)
deleted == 0 -> Problem(404)   [T-07]
else         -> NoContent()    [T-06]
```

### Schema
**No schema change and no migration.** Verified against the current model snapshot and `ContactConfiguration`:
- `IX_Contacts_Email` (unique, `NOCASE` inherited from the column) guards updates exactly as it guards inserts, including concurrent create/update mixes (AC-034). SQLite excludes the row being updated from its own uniqueness check, so self-updates and case-only changes of a contact's own email succeed (AC-012, AC-013).
- Multiple `NULL` emails remain allowed (AC-015).
- Delete is a hard `DELETE`; no foreign keys reference `Contacts` yet.

### Config
None. `EnableSensitiveDataLogging` stays off (NFR-003).

## Design points (resolved)

### 1. Check order and full replace (AC-003, AC-021..AC-025)
- Body binding happens before the handler runs, so an unreadable body is always 400 first (existing `ThrowOnBadRequest = false` + status code pages, ADR-0004). This makes AC-021 a guard as soon as PUT exists.
- `Parse` runs before the existence lookup, so an invalid body sent to an unknown id is 400 (AC-024). The lookup runs before `SaveChanges`, so a conflicting email sent to an unknown id is 404 (AC-025): the write that would trigger 2067 never happens.
- Full replace: all six fields are assigned from `ContactInput` on every update, so an omitted, `null`, empty, or whitespace optional field becomes `null` (AC-003). `UpdateContactRequest` has no `Id`/`CreatedAt`/`UpdatedAt` members, and System.Text.Json skips unknown members, so client values for them are ignored (AC-005).

### 2. Email uniqueness on update (AC-012..AC-016, AC-034)
- Same mechanism as create (plan 001, design point 1): no pre-check, constraint violation → 409, other `DbUpdateException`s re-thrown → 500.
- A failed update leaves the row unchanged: `SaveChanges` runs in a transaction that rolls back on the constraint error, and the tracked entity is discarded with the request scope.
- **Concurrency (AC-034), interleaving-invariant assertions** (same approach as spec 001 AC-016):
  - Seed K contacts with distinct emails. Release K `PUT`s (each moving one of them to email X, in mixed casings) plus M `POST`s with email X together behind a shared gate.
  - Assert: zero 5xx; every response is 200, 201, or 409 problem+json; **exactly one** response is a success (200 or 201); exactly one row holds X (counted via a separate connection, `Email = X COLLATE NOCASE`); every contact whose PUT got 409 still has its original email.
  - These hold for every interleaving, because nothing in the race releases X once it's taken.
- The sequential AC-014 test exercises the identical constraint → 409 path that a lost race takes.

### 3. Update/delete and delete/delete races (AC-035, AC-036)
- **Delete:** `ExecuteDeleteAsync` is one SQL statement; SQLite serializes writers, so of N concurrent deletes of one id exactly one sees `changes() = 1`. A read-then-`Remove`-then-`SaveChanges` implementation would throw `DbUpdateConcurrencyException` (→ 500) for the losers, so the plan rules it out.
- **Update:** a delete that lands between the update's load and save makes EF's `UPDATE ... WHERE Id = @id` affect zero rows → `DbUpdateConcurrencyException` → 404. The update can never re-insert the row, so if the delete returned 204 a later GET is 404.
- **Testing AC-035 deterministically:** the test forces the bad interleaving instead of hoping for it. It builds a derived host with `factory.WithWebHostBuilder(... ConfigureTestServices(s => s.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(interceptor))))`, where a test-local `SaveChangesInterceptor` deletes the target row through a separate `SqliteConnection` in `SavingChangesAsync` (once). The PUT must return 404 problem+json and a later GET 404. Without the catch, it returns 500 (that's the RED). `ConfigureDbContext` is EF Core 9+ API; EF Core 10.0.12 is already a transitive dependency of the test project through the API reference, so nothing new is added. If the test-writer finds that API unsuitable, an equivalent is registering the interceptor by replacing the `DbContextOptions<AppDbContext>` registration in `ConfigureTestServices`. The derived factory shares the fixture's database (the same pattern spec 001's NFR-004 guard uses).
- Plus an interleaving-invariant smoke: for N contacts, release one PUT and one DELETE per contact together; assert PUT ∈ {200, 404}, DELETE ∈ {204, 404}, zero 5xx, and for every 204 a later GET is 404.
- **AC-036:** 10 concurrent DELETEs of one id: exactly one 204, nine 404 problem+json, zero 5xx.

### 4. Errors and safe 500s (AC-037, AC-038)
- 400/404/409 bodies come from `ValidationProblem`, `Problem(...)`, or status code pages; all are `application/problem+json` with `type`/`title`/`status`. Every error test goes through `ProblemAssert.IsProblemAsync`.
- 500s come from the existing `UseExceptionHandler()`. AC-038 tests for PUT and DELETE are expected to pass at RED (guards): `DROP TABLE` (as in spec 001) and, for "stored data unchanged", a fault-injecting trigger in its own fixture: `BEFORE UPDATE ON Contacts ... RAISE(ABORT, 'x')` (extended code 1811, not 2067, so it must stay 500, never 409) and `BEFORE DELETE ON Contacts ... RAISE(ABORT, 'x')`. After the 500, the row is read through a separate connection and is unchanged / still present.

### 5. OpenAPI (NFR-001)
- `UpdateContact`: `.WithName("UpdateContact").WithSummary(...)`, `.ProducesProblem(404)`, `.ProducesProblem(409)`. 200 comes from `Ok<ContactResponse>`, 400 (problem+json) from `ValidationProblem`'s endpoint metadata.
- `DeleteContact`: `.WithName("DeleteContact").WithSummary(...)`, `.ProducesProblem(404)`. 204 comes from `NoContent`.
- The test asserts PUT ⊇ {200, 400, 404, 409}, DELETE ⊇ {204, 404}, and that the PUT 400/404/409 and DELETE 404 responses list `application/problem+json` under `content`.
- `ProblemHttpResult` contributes no endpoint metadata, so each 404 appears once, with problem+json content. (GET by id still documents its 404 without content; see Follow-ups.)

### 6. Validation message style (AC-039, ADR-0006)
- Messages are aligned in `ContactInput` (`firstName` → `"Required."`) and `ListQuery` (page/pageSize/search). The max-length and email messages already comply.
- The AC says a message must not "contain the field or parameter name", yet the spec lists `"Must be a valid email address."` (under key `email`) as compliant. Tests therefore check: starts with an uppercase letter; ends with `.`; doesn't contain the key quoted (`'key'`, `"key"`); doesn't start with the key or its humanized form (`firstName` / `First name`), case-insensitively; and equals the exact canonical message for the rule (ADR-0006 table). A literal "no substring `email`" check is **not** used. See Open questions in the report.
- `docs/conventions.md` (Errors row) gets the rule during `/document 002` (documenter; implementer-protected file).

## Test strategy
Paths abbreviated: `T/` = `tests/MicroCrm.Api.Tests/`. All integration tests use `ApiFactory` (ADR-0005) and seed through the HTTP API. Classes that assert exact counts or order call `ResetAsync()` in `InitializeAsync`; others use unique emails/names per test.

| AC | Level | Test file | Notes (fixtures, fakes) |
|---|---|---|---|
| AC-001 | integration | `T/Integration/Contacts/UpdateContactTests.cs` | All six fields changed; assert every body member |
| AC-002 | integration | `UpdateContactTests.cs` | GET with a **new** `HttpClient`, list with `search`, and a row read through a new `SqliteConnection` |
| AC-003 | integration | `UpdateContactTests.cs` | Theory per optional field: omitted / `null` / `""` / `"  "` on a contact that had a value → `null` |
| AC-004 | integration | `UpdateContactTests.cs` | Surrounding whitespace on all fields; verify via GET |
| AC-005 | integration | `UpdateContactTests.cs` | Body with different `id`, bogus `createdAt`/`updatedAt`; response and GET keep original id/createdAt |
| AC-006 | integration | `UpdateContactTests.cs` | `factory.Time.Advance(...)`; PUT identical values; `updatedAt` == fake now, `createdAt` unchanged |
| AC-007 | integration | `UpdateContactTests.cs` | Each field at exactly max (and max wrapped in whitespace) → 200 |
| AC-008 | integration | `T/Integration/Contacts/UpdateContactListEffectsTests.cs` | `ResetAsync`; rename last/first names and assert new list positions |
| AC-009 | integration | `UpdateContactListEffectsTests.cs` | Search new email finds; old email doesn't |
| AC-010 | integration | `UpdateContactTests.cs` | `"  Ada@Example.COM "` → `"Ada@Example.COM"` |
| AC-011 | integration | `UpdateContactListEffectsTests.cs` | Snapshot other contacts' raw JSON before/after |
| AC-012 | integration | `T/Integration/Contacts/UpdateContactConflictTests.cs` | Same email with different case/whitespace |
| AC-013 | integration | `UpdateContactConflictTests.cs` | `ada@…` → `Ada@Example.com`; stored casing changes |
| AC-014 | integration | `UpdateContactConflictTests.cs` | 409 problem+json, detail doesn't echo email; target row unchanged (direct read) |
| AC-015 | integration | `UpdateContactConflictTests.cs` | Several contacts updated to no email |
| AC-016 | integration | `UpdateContactConflictTests.cs` | Old email reusable by create and by another update (both after change and after removal) |
| AC-017 | integration | `T/Integration/Contacts/UpdateContactValidationTests.cs` | Theory: missing/null/""/whitespace; contact unchanged (direct read) |
| AC-018 | integration | `UpdateContactValidationTests.cs` | Theory per field at max+1; contact unchanged |
| AC-019 | integration | `UpdateContactValidationTests.cs` | Invalid email; contact unchanged |
| AC-020 | integration | `UpdateContactValidationTests.cs` | Exact key set, camelCase |
| AC-021 | integration | `UpdateContactValidationTests.cs` | Raw bodies `{`, empty, `null`, `[]`, `{"firstName":123}`; contact unchanged |
| AC-022 | integration | `UpdateContactTests.cs` | Unknown v7 GUID → 404 problem+json; GET on that id still 404; total count unchanged |
| AC-023 | integration | `UpdateContactTests.cs` | `not-a-guid`, `123` → 404 problem+json; count unchanged |
| AC-024 | integration | `UpdateContactValidationTests.cs` | Invalid body + unknown GUID → 400 |
| AC-025 | integration | `UpdateContactTests.cs` | Another contact's email + unknown GUID → 404 |
| AC-026 | integration | `T/Integration/Contacts/DeleteContactTests.cs` | 204, empty body (`Content-Length` 0 / empty string) |
| AC-027 | integration | `DeleteContactTests.cs` | GET with new client → 404 problem+json; row count 0 via new connection |
| AC-028 | integration | `DeleteContactTests.cs` | `ResetAsync`; list and search exclude; `totalCount` decremented |
| AC-029 | integration | `DeleteContactTests.cs` | After delete, create with case-variant email → 201; another contact updated to it → 200 |
| AC-030 | integration | `DeleteContactTests.cs` | Unknown GUID, and a second delete of the same id → 404 problem+json |
| AC-031 | integration | `DeleteContactTests.cs` | `not-a-guid`, `123` → 404; count unchanged |
| AC-032 | integration | `DeleteContactTests.cs` | Other contacts' raw JSON unchanged |
| AC-033 | integration | `DeleteContactTests.cs` | PUT valid body after delete → 404; GET still 404; count 0 |
| AC-034 | integration | `UpdateContactConflictTests.cs` | Mixed concurrent PUT/POST race; design point 2 |
| AC-035 | integration | `T/Integration/Contacts/UpdateDeleteRaceTests.cs` | Deterministic interceptor test + invariant smoke; design point 3 |
| AC-036 | integration | `DeleteContactTests.cs` | 10 concurrent DELETEs |
| AC-037 | integration | every error test via `ProblemAssert.IsProblemAsync` | 400, 404, 409, 500 on PUT and DELETE |
| AC-038 | integration | `T/Integration/ErrorHandlingTests.cs` | `DROP TABLE` for PUT/DELETE (existing class) + new trigger fixtures (own classes) asserting data unchanged |
| AC-039 | unit + integration | `T/Unit/Contacts/ContactInputTests.cs`, `T/Unit/Common/ListQueryTests.cs`, `CreateContactValidationTests.cs`, `ListContactsPagingTests.cs`, `UpdateContactValidationTests.cs` | Exact canonical messages + style helper in `ProblemAssert` |
| NFR-001 | integration | `T/Integration/OpenApiTests.cs` | operationIds, summaries, codes, problem+json content |
| NFR-002 | integration | `UpdateContactTests.cs` | Raw JSON of the PUT response: camelCase, nulls present, timestamps `Z`/`+00:00` |
| NFR-003 | reviewer inspection (+ guard) | `UpdateContactConflictTests.cs` (guard) | Capturing `ILoggerProvider` via `WithWebHostBuilder` (as spec 001 NFR-004): a unique email used in an update and its 409 never appears at Information+ |
| NFR-004 | unit + integration | `ContactInputTests.cs`, `UpdateContactValidationTests.cs` | Same invalid payload through both `Parse` overloads / POST and PUT → identical `errors` dictionaries |

## Dependencies
None. `ConfigureDbContext`/`SaveChangesInterceptor` (test-only, AC-035) come from EF Core 10.0.12, already a transitive dependency of the test project via the API project reference.

## Risks & mitigations
- **Update/delete race returning 500.** Mitigation: catch `DbUpdateConcurrencyException` → 404 (T-08), forced deterministically by an interceptor test; delete via `ExecuteDeleteAsync` from its first task (T-06).
- **Shared-cache locking flakiness in the concurrency tests (AC-034..AC-036).** Same mitigation as spec 001 AC-016: interleaving-invariant assertions, Microsoft.Data.Sqlite's busy/locked retry, and ADR-0005's file-DB fallback without production changes.
- **`SqliteErrors.IsUniqueConstraintViolation` doesn't check which unique index was violated** (it checks only extended code 2067). Correct today because `IX_Contacts_Email` is the only unique index (primary-key violations are 1555, not 2067). If a later spec adds another unique index on `Contacts`, an update or create violating it would be misreported as "email already exists". Not changed here (out of scope); recorded as a follow-up and already noted in ADR-0003.
- **Duplicate 404 metadata in OpenAPI** if `TypedResults.NotFound()` were combined with `.ProducesProblem(404)`. Mitigation: PUT/DELETE return `ProblemHttpResult` for 404 (design point 5); the NFR-001 test checks the content type.
- **AC-039 wording vs its own example** (`"Must be a valid email address."` contains `email`). Mitigation: tests use the interpretation in design point 6; flagged for human confirmation.
- **Tests that pass at RED (guards).** tasks.md flags each one; the test-writer reports them separately and the reviewer confirms each would fail if its behavior regressed.
- **Changing messages could break a client that matches message text.** No client exists yet (web starts in spec 004), and no existing test asserts message text. Upgrade note in CHANGELOG.

## Rollout
- No feature flag, no migration, no data change. Additive endpoints; the only change to existing behavior is validation message wording on create and list (status codes and `errors` keys unchanged). The documenter adds a CHANGELOG "Changed" entry with the old → new messages.
- **Rollback:** revert the commits. No schema to roll back.
- **Spec 003 hand-off (Unlink decision):** to-dos must be unlinked atomically with the contact delete. Because delete uses `ExecuteDeleteAsync` (a set-based statement that bypasses EF's client-side cascade), spec 003 should configure the `Todo.ContactId` foreign key with `OnDelete(DeleteBehavior.SetNull)` so the **database** applies `ON DELETE SET NULL` in the same statement. EF Core's SQLite provider enables `PRAGMA foreign_keys` on open. Spec 003's plan must verify this with a test, and must not switch delete back to load-then-remove.

## ADRs
- ADR-0006: Validation message style (`docs/adr/0006-validation-message-style.md`): Proposed. Needed because `docs/conventions.md` changes go through an ADR. Accept together with this plan.
- No other new ADR: the endpoint, uniqueness, and error-pipeline choices follow ADR-0003/0004/0005; the race handling is an implementation detail recorded in design point 3.
