# 001: Review log

## T-01: 2026-10-01: APPROVE

**Checks:** tests pass (backend 3/3: `SmokeTests` + 2 theory cases; web 1/1) · lint pass (`dotnet format --verify-no-changes` clean, oxlint clean) · typecheck pass (`tsc -b`)

**Scope reviewed:** `src/MicroCrm.Api/Program.cs`, `src/MicroCrm.Api/MicroCrm.Api.http`, `tests/MicroCrm.Api.Tests/Integration/Infrastructure/ApiFactory.cs`, `.../Infrastructure/ProblemAssert.cs`, `.../Integration/Contacts/GetContactByIdTests.cs`. Excluded as instructed: the `.editorconfig` chore and the spec/plan/ADR/harness.env planning changes.

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-019 | `GetContactByIdTests.GetContact_WithNonGuidId_Returns404Problem_AC019` (`not-a-guid`, `123`) | yes. Asserts 404 via real HTTP. RED was verified by the orchestrator (404, no content type). The test stays meaningful once T-03 adds `{id:guid}`, because a non-GUID still won't match the constraint. |
| AC-037 (404 from an unmatched route) | same test, via `ProblemAssert.IsProblemAsync` | yes. Asserts `application/problem+json` and the presence of `type`/`title`/`status`, with `status == 404`. It would fail if `AddProblemDetails()` or `UseStatusCodePages()` were removed. See finding 1 for a hardening suggestion. |

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Should-fix | test-writer | tests/MicroCrm.Api.Tests/Integration/Infrastructure/ProblemAssert.cs:25-26 | `type` and `title` are only checked with `TryGetProperty`, so a JSON `null` or `""` passes. This helper will be reused for every error test in the spec (400/404/409/500), so a weak check here weakens all of them. | Assert `ValueKind == JsonValueKind.String` and a non-empty value for `type` and `title`. Do this before T-03/T-04 start relying on the helper. |
| 2 | Nit | test-writer | tests/MicroCrm.Api.Tests/Integration/Infrastructure/ProblemAssert.cs:21 | If any assertion after `JsonDocument.Parse` fails, the `JsonDocument` is never disposed. This only matters on failure paths. | Optional: wrap the post-parse asserts in try/catch, dispose, and rethrow. |

**Notes:**
- Program.cs matches the T-01 entry in tasks.md and ADR-0004 for this step: the weatherforecast sample is removed, and `AddProblemDetails()` + `UseStatusCodePages()` are added. Nothing extra was added. `UseExceptionHandler()` and `ThrowOnBadRequest = false` are correctly left for T-04/T-06.
- Middleware order is fine for now: `UseStatusCodePages()` comes before OpenAPI and endpoints. T-04 must insert `UseExceptionHandler()` **before** `UseStatusCodePages()` (plan, line 185).
- `UseHttpsRedirection()` was kept. It doesn't affect the tests (no HTTPS port in the test host). The plan lets the implementer drop it later.
- `MicroCrm.Api.http` now contains only the host variable. That matches the T-01 instruction to drop the sample request. The plan adds contacts requests later.
- `ApiFactory` is minimal (Development only), as the task specifies. `SmokeTests` still uses the plain `WebApplicationFactory<Program>`; T-02 switches it over.
- Security: no input handling, logging, or secrets were added. ProblemDetails responses from status code pages contain no request data.

## T-02: 2026-10-01: CHANGES_REQUESTED

**Checks:** tests pass (backend 9/9: 6 `CreateContactTests`, `SmokeTests`, 2 AC-019 cases; web 1/1) · lint pass (`dotnet format --verify-no-changes` clean, oxlint exit 0) · typecheck pass (`tsc -b`) · build 0 warnings · `dotnet ef migrations has-pending-model-changes`: none · no `microcrm*.db*` file anywhere in the repo after the run (`*.db` is gitignored anyway).

**Scope reviewed:** `src/MicroCrm.Api/{MicroCrm.Api.csproj, appsettings.json, Program.cs}`, `Data/AppDbContext.cs`, `Data/Migrations/*_CreateContacts*` + snapshot, `Features/Contacts/{Contact,ContactDtos,ContactsEndpoints}.cs`, `.config/dotnet-tools.json`, the `.editorconfig` `generated_code` section, `tests/.../Integration/Contacts/CreateContactTests.cs`, `SmokeTests.cs`, and (committed in d07bfb6) `ApiFactory.cs`, `ProblemAssert.cs`, the test csproj. Excluded as instructed: `docs/.claude/`, spec/plan/ADR/harness.env changes.

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-001 | `CreateContact_WithAllFields_Returns201WithLocationAndBody_AC001` | Partly. 201, the `Location` path (relative or absolute, compared to the body id), and every body member are asserted. The "stored values" part isn't: see finding 2. |
| AC-002 | `CreateContact_WithOnlyFirstName_ReturnsNullOptionalFields_AC002` | yes. Raw JSON: each optional member is present and `JsonValueKind.Null`. |
| AC-003 | `CreateContact_IgnoresClientId_AssignsNewGuidV7_AC003` | yes. The client id is sent twice. The test asserts both ids differ from it and from each other, are non-empty, and are version 7. |
| AC-004 | `CreateContact_SetsTimestampsFromClock_IgnoresClientValues_AC004` | yes. It uses the fake clock with sub-ms ticks, sends client timestamps, and asserts `createdAt == now` and `updatedAt == createdAt`. AC-001 also checks both against `factory.Time`. |
| AC-012 | `CreateContact_WithUppercaseEmail_PreservesCasing_AC012` | yes for "returned". It sends `"  Ada.Lovelace@Example.com "` and expects the trimmed, case-preserved value, so it demands both the trim and the absence of lower-casing. "Stored" is not checked (finding 2). |
| NFR-002 | `CreateContact_Json_IsCamelCaseWithNullsAndUtcOffsets_NFR002` | yes. The exact ordinal member set is camelCase, nulls are present, and both timestamps end in `Z`/`+00:00`. |

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Blocking | implementer | src/MicroCrm.Api/Features/Contacts/ContactsEndpoints.cs:13 | `.WithName("CreateContact")` is OpenAPI metadata that no failing test demands. tasks.md ("Minimal implementation") explicitly forbids `WithName`/`WithSummary`/`Produces*` before T-17, so that T-17's `OpenApi_ContactsEndpoints_HaveNameSummaryAndStatusCodes_NFR001` stays genuinely red. It also breaks constitution §2 (no production code without a failing test that demands it). Nothing uses the name: the handler returns `TypedResults.Created` with a literal path, not `CreatedAtRoute`. | Remove `.WithName("CreateContact")`, leaving `group.MapPost(string.Empty, CreateContact);`. Re-run TEST_CMD and LINT_CMD. |
| 2 | Should-fix | test-writer | tests/MicroCrm.Api.Tests/Integration/Contacts/CreateContactTests.cs (whole class) | No T-02 test reads anything back from the database. Every assertion is on the response, which is built from the in-memory entity. If `await db.SaveChangesAsync(ct)` were deleted, all six tests would still pass, yet AC-001 ("reflecting the stored values") and AC-012 ("store and return") both cover storage. The plan routes the round trip to T-03 (AC-017 GET==POST, AC-039 via a new `SqliteConnection`), so this gap is temporary. | Either add a direct-DB assertion now (for example, in AC-012 open `new SqliteConnection(factory.ConnectionString)` and read `Email` for the returned id), or accept that T-03's AC-017/AC-039 tests close it. The reviewer will recheck at T-03. |
| 3 | Nit | test-writer | tests/MicroCrm.Api.Tests/Integration/Contacts/CreateContactTests.cs:95 | The comment says sub-ms ticks will detect "a lossy round trip", but the 201 body never round-trips through SQLite. Only T-03's GET can detect that. | Reword the comment, or move the note to the AC-017 test. |

**Notes:**
- **T-01 Should-fix 1 confirmed fixed.** `ProblemAssert.AssertNonEmptyString` (ProblemAssert.cs:49-54) now requires `type` and `title` to be JSON strings and non-empty. T-01 Nit 2 is also fixed: the document is disposed on assertion failure (ProblemAssert.cs:40-44).
- **FirstName null guard / email-only trim.** `Email = request.Email?.Trim()` is the minimum demanded: the AC-012 test pads the email with whitespace. Other fields aren't trimmed, so T-05's AC-005 stays red. `FirstName = request.FirstName ?? string.Empty` is the compile-minimum: `Contact.FirstName` is non-nullable, `CreateContactRequest.FirstName` is `string?`, and warnings are errors. The alternative `!` would turn a null firstName into a NOT NULL `DbUpdateException` and a 500. Interim behavior: a body without `firstName` currently yields 201 with `firstName: ""`. That's spec-incorrect but temporary, and it's safe: no crash, no data leak, and only the dev DB is affected. AC-007's null/missing cases (T-05/T-07) will fail against it and force the guard out, so it can't survive unnoticed. The implementer should make sure the `?? string.Empty` goes away when `ContactInput.Parse` is introduced, not stay as dead code.
- **Collation and unique index deferred.** The `CreateContacts` migration has plain TEXT columns, no `COLLATE NOCASE`, and no index on `Email`. That matches the plan (T-10 `AddContactEmailUniqueIndex`, T-12 `AddContactNameCollation`) and keeps those tasks red. The model has no max-length metadata either. The plan's schema comment calls max lengths "EF metadata; SQLite does not enforce", so they have no behavioral effect. No pending model changes.
- **Migrate at startup.** `MigrateAsync()` runs in a disposed async scope right after `Build()`, before any middleware or `Run()`, as plan line 19 requires. It applies in every environment, which the plan accepts for a single-user local app (revisit before deployment). The connection string is read lazily inside `AddDbContext((sp, options) => ...)` from `IConfiguration`, so `ApiFactory`'s `UseSetting` override takes effect (ADR-0005). The tests confirm this: no file DB was created.
- **ApiFactory vs ADR-0005:** unique named shared-cache in-memory DB; keep-alive opened in the constructor (before the host starts); `UseSetting("ConnectionStrings:MicroCrm")`; `FakeTimeProvider` replaces `TimeProvider` via `RemoveAll` + `AddSingleton`; Development environment; `ConnectionString` exposed; dispose closes the keep-alive and clears the pool. All conform. `SmokeTests` only changed its fixture type; the assertion is unchanged.
- **Id / timestamps:** a single `GetUtcNow()` feeds `Guid.CreateVersion7(now)`, `CreatedAt`, and `UpdatedAt`, as the task specifies. The request record has no `Id`/`CreatedAt`/`UpdatedAt` members, so client values are dropped at binding.
- **Dependencies:** EF Core Sqlite and Design 10.0.12 (Design is `PrivateAssets=all`), plus `dotnet-ef` 10.0.12 as a local tool. All are listed in the plan's dependency table. The `.editorconfig` `[src/MicroCrm.Api/Data/Migrations/**.cs] generated_code = true` section is the fallback the plan's risk section allows. It's narrowly scoped.
- **Security / NFR-004:** no logging was added, and no request values are logged. Input is bound to a typed record. EF parameterizes the insert. There are no secrets: the connection string is a local file path.

## T-02 re-review (fix cycle 1): 2026-10-01: APPROVE

**Checks:** backend tests pass (9/9, 0 failed, 0 skipped) · `dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exit 0. Web checks were not rerun because `web/` hasn't changed since the T-02 review.

**Resolution:**
- Finding 1 (Blocking, implementer) is **resolved**. `ContactsEndpoints.cs:13` is now `group.MapPost(string.Empty, CreateContact);`. `grep` finds no `WithName`/`WithSummary`/`Produces` anywhere under `src/`, so T-17's NFR-001 test stays red. `Location` is still the literal `/api/contacts/{id}`, so nothing depended on the route name.
- Finding 2 (Should-fix, test-writer) is **deferred to T-03** per the orchestrator. At T-03, I'll check that the AC-017 (GET == POST body) and AC-039 (new `SqliteConnection`) tests fail if `SaveChangesAsync` is removed.
- Finding 3 (Nit, test-writer) is still open: the comment is unchanged at CreateContactTests.cs:95. It isn't blocking.

**Scope check:** `git diff HEAD --stat` touches the same 7 tracked files as before. Among the T-02 source, test, and config files, only `ContactsEndpoints.cs` has been modified since the first T-02 review was written. The test files, `Program.cs`, `Contact.cs`, `ContactDtos.cs`, the migrations, and `.config/` are untouched. No new findings.

## T-03: 2026-10-01: APPROVE

**Checks:** backend tests pass (12/12, 0 failed, 0 skipped) · `dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exit 0. The web checks were not rerun because `web/` is unchanged.

**Scope reviewed:** `src/MicroCrm.Api/Features/Contacts/ContactsEndpoints.cs` (the GET handler, lines 15 and 20-30), `tests/.../Integration/Contacts/GetContactByIdTests.cs` (AC017, AC018, AC039), and the comment change in `CreateContactTests.cs` (AC-004 test, about lines 95-96).

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-017 | `GetContact_Existing_Returns200WithCreatedBody_AC017` | yes. It sets every field, uses a fake clock with sub-ms ticks, and compares the raw GET body with the raw POST body (`application/json`), so a lossy timestamp or field round trip would fail it. |
| AC-018 | `GetContact_UnknownGuid_Returns404Problem_AC018` | yes. See note 4: two mutants now fail it. |
| AC-039 (get-by-id) | `GetContact_FromNewClientAndConnection_ReturnsPersistedContact_AC039` | yes. It counts rows through a new `SqliteConnection` on `factory.ConnectionString`, then GETs from a new `HttpClient` and compares the raw bodies. `lower(Id)` makes the count independent of how EF stores the case of the GUID text. |
| AC-037 (404 for an unknown GUID) | same AC018 test, via the hardened `ProblemAssert.IsProblemAsync` | yes. |

**Carried-forward items:**
1. **The T-02 Should-fix 2 (no read-back of persisted data) is resolved.** I checked it with a mutation test in a scratch copy of the repo: I removed `await db.SaveChangesAsync(ct)` from `CreateContact`, and AC017 failed (GetContactByIdTests.cs:52, 404 instead of 200) and AC039 failed (line 93, row count 0). The other 10 tests still passed. The source tree was not touched.
2. **The T-02 Nit 3 is resolved.** The comment now says the 201 body is built from the in-memory entity and that the lossy-round-trip check lives in the AC-017 test. That is accurate: AC017 sets sub-ms ticks (GetContactByIdTests.cs:32-33) and compares GET to POST exactly.
3. **No early OpenAPI metadata.** `grep -rn "WithName|WithSummary|Produces|WithTags|WithOpenApi" src/` finds nothing. The plan's name `GetContactById` (plan.md:137) is correctly deferred to T-17, so NFR-001 stays red.
4. **The AC-018 guard is real.** Now that `{id:guid}` is mapped, an unknown v7 GUID reaches the handler instead of falling through as an unmatched route. I tried two mutants in the scratch copy, and each one failed only AC018:
   - Mutant A dropped the null check (`Ok(ContactResponse.From(contact!))`). The result is an NRE, then 500 and the developer exception page.
   - Mutant B returned `NotFound("missing")`. The body is not empty, so the status-code pages don't run and the response is not problem+json.

   Removing `UseStatusCodePages()` is also caught, by both this test and AC019. One limit: the guard can't tell a handler 404 from an unmatched-route 404. If the route itself broke, AC017 and AC039 would still catch it, so this is enough.

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Nit | test-writer | tests/MicroCrm.Api.Tests/Integration/Contacts/GetContactByIdTests.cs:48,82 | `JsonDocument.Parse(...)` is used inline to read `id` and is never disposed. It rents pooled buffers. This is harmless in tests, but it doesn't match the `using` pattern used elsewhere (`ProblemAssert`, `CreateContactTests.ReadJsonAsync`). | Optional: `using var doc = JsonDocument.Parse(createdBody);` then read `id`. |

**Notes:**
- The handler matches plan.md:137 and conventions.md:38 (minus the OpenAPI metadata deferred to T-17): `Results<Ok<ContactResponse>, NotFound>`, `TypedResults`, `AsNoTracking`, and the `CancellationToken` passed through. The 404 has an empty body, so problem+json comes from the T-01 status-code pages, as plan §3 intends. Nothing beyond the ACs was added. The list route, paging, and collation are untouched.
- Test isolation: each test in the shared class fixture uses its own email, and AC039 looks up by id only. No test in this class depends on exact counts, so a reset isn't needed yet. That changes at T-11, as the plan says. AC017 changes the shared `FakeTimeProvider`, but no other test in the class asserts on time.
- Security: the GUID is bound through a route constraint and the query is an EF parameterized lookup. The test SQL is parameterized. No logging or secrets were added.
- Still open from earlier reviews: the interim `FirstName ?? string.Empty` (ContactsEndpoints.cs:42) must go at T-05/T-07. At T-04, `UseExceptionHandler()` must come before `UseStatusCodePages()`.

## T-04: 2026-10-01: CHANGES_REQUESTED

**Checks:** backend tests pass (14/14, 0 failed, 0 skipped) · `dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exit 0. The web checks were not rerun because `web/` is unchanged.

**Scope reviewed:** `src/MicroCrm.Api/Program.cs` (lines 9-18 for `AddProblemDetails`/`CustomizeProblemDetails`, line 30 for `UseExceptionHandler()`), `tests/.../Integration/ErrorHandlingTests.cs` (new), `tests/.../Integration/Infrastructure/ApiFactory.cs` (`ExecuteSqlAsync`), and the T-03 nit fix in `GetContactByIdTests.cs:48,83`.

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-038 | `GetContact_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC038`, `CreateContact_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC038` | yes. A real DB fault (dropped table, on a separate connection, after the host has started) is checked in Development, the worst case. The body is checked against the message, the type names (`SqliteException`, and `Exception` generally), and stack frames. |
| AC-037 (500) | same tests, via `ProblemAssert.IsProblemAsync(response, 500)` | yes. |

**Mutation evidence** (in a scratch copy, plus a probe test that captures the 500 body; the source tree was not touched):
| Mutant | Result | 500 body |
|---|---|---|
| none (baseline) | 14/14 pass | `{"type":".../rfc9110#section-15.6.1","title":"An error occurred while processing your request.","status":500,"traceId":"00-…"}` |
| `CustomizeProblemDetails` block removed (plain `AddProblemDetails()`) | **14/14 pass** | **identical** to the baseline, `traceId` included. The framework's `ProblemDetailsDefaults` already adds `traceId`, and `ExceptionHandlerMiddleware` never sets `detail`. |
| `UseExceptionHandler()` removed | both AC038 tests fail | `title` = `"Microsoft.Data.Sqlite.SqliteException"`. The developer exception page handled it. The customization still removed `detail` and `exception`, but the type name leaked through `title`. |
| `UseExceptionHandler()` moved after `UseStatusCodePages()` | 14/14 pass | identical to the baseline. Order doesn't matter here, because the handler writes a body and the status-code pages only act on empty bodies. Program.cs follows the ADR-0004 order anyway. |

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Blocking | implementer | src/MicroCrm.Api/Program.cs:9-18 | The `CustomizeProblemDetails` block is production code that no failing test demands (constitution §2, and the "Minimal implementation" rule in tasks.md). Removing it changes no observable output: the baseline and mutant bodies are identical, and the framework already emits `traceId`. So no test can ever have been red for it. As defence in depth it is weak too. The only path it could affect is the developer exception page, which `UseExceptionHandler()` pre-empts, and even there it fails AC-038 because the type name leaks through `title` (mutant 3). It also adds a latent trap: `Extensions.Clear()` silently discards any extension a future 500 problem sets on purpose. | Revert to `builder.Services.AddProblemDetails();`. `traceId` stays in the 500 body (framework default), which still matches plan.md:69/184. If the team wants a policy for stripping 500s, the planner should record it in the plan/ADR with a test that fails without it. |

**Notes:**
- **Q2, effect on 400s:** the block is gated on `Status == 500`, so `ValidationProblem` (400) responses are untouched. Even for a 500, `errors` is a property of `HttpValidationProblemDetails` and not part of `Extensions`, so `Extensions.Clear()` would not remove it. This becomes moot once Finding 1 is fixed.
- **Q3, logging (NFR-004):** I captured every log at Information level or above during a failing POST with `firstName="SecretFirst"`, `email="secret.probe@example.com"`. `ExceptionHandlerMiddleware` logs "An unhandled exception has occurred while executing the request." at Error with the `DbUpdateException` attached, so the exception is not swallowed. EF logs the failed command at Error with parameters shown as `'?'` (no sensitive-data logging). "secret" appears 0 times in the log. No application logging was added, and `grep` finds no `ILogger`/`EnableSensitiveDataLogging`/`WithName`/`Produces*` under `src/` (OpenAPI drift is clean).
- **Q4, T-03 nit:** resolved. `GetContactByIdTests.cs:48` and `:83` now use `using var createdDoc = JsonDocument.Parse(...)`.
- Test design: `ErrorHandlingTests` gets its own `ApiFactory` (one class fixture per class, with a unique in-memory DB name), so dropping the table can't affect other classes. `DROP TABLE IF EXISTS` keeps the two tests independent of their order. `ExecuteSqlAsync` disposes its connection and command. Plan.md:249 also lists GET list, which correctly waits until the list endpoint exists (T-11+). Extending this class then is suggested; tasks.md doesn't require it for T-04.
- Still open: the interim `FirstName ?? string.Empty` (ContactsEndpoints.cs:42) must go at T-05/T-07.

## T-04 re-review (fix cycle 1): 2026-10-01: APPROVE

**Checks:** backend tests pass (14/14, 0 failed, 0 skipped) · `dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exit 0. Web checks not rerun because `web/` is unchanged.

**Finding 1 (Blocking, `CustomizeProblemDetails`): resolved.** Program.cs:9 is now plain `builder.Services.AddProblemDetails();`. `git diff HEAD -- src/MicroCrm.Api/Program.cs` shows one added line only: `app.UseExceptionHandler();` immediately before `app.UseStatusCodePages();`, which matches the ADR-0004 order.

**Scope:** only Program.cs changed in this cycle. `ErrorHandlingTests.cs` and `ApiFactory.cs` were last modified before the first T-04 review and match what that review covered. The T-03 changes (ContactsEndpoints.cs, GetContactByIdTests.cs) are still uncommitted and were approved earlier.

**Mutation evidence** (scratch copy, source tree not touched): with `app.UseExceptionHandler();` removed, both `*_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC038` tests fail (12/14 pass). Unmutated: 14/14 pass. AC-038 and the AC-037 500 part stay covered by tests that fail for the right reason.

**Findings:** none.

**Still open:** the interim `FirstName ?? string.Empty` (ContactsEndpoints.cs:42) must go at T-05/T-07.

## T-05: 2026-10-01: APPROVE

**Checks:** backend tests pass (27/27, 0 failed, 0 skipped) · `dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exit 0 · web lint/typecheck not rerun because `web/` is unchanged.

**Scope reviewed:** `src/MicroCrm.Api/Features/Contacts/ContactInput.cs` (new), `ContactsEndpoints.cs:38-48` (create handler), `tests/.../Unit/Contacts/ContactInputTests.cs` (new), and `tests/.../Integration/Contacts/CreateContactTests.cs:163-229` (two new tests plus `AssertOptionalFieldsNull`). The T-03/T-04 hunks in `git diff HEAD` were approved earlier and were not re-reviewed.

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-005 | `ContactInputTests.Parse_TrimsAllFields_AC005`, `CreateContactTests.CreateContact_WithSurroundingWhitespace_StoresTrimmed_AC005` | yes. All six fields are covered, with mixed space/`\t`/`\n` padding, so a space-only `Trim(' ')` is caught. The integration test checks via GET (stored, not only echoed). |
| AC-006 | `ContactInputTests.Parse_EmptyOrWhitespaceOptional_BecomesNull_AC006` (5 fields x `""`/`"   "`), `CreateContactTests.CreateContact_WithBlankOptionalFields_ReturnsNulls_AC006` | yes. The integration test asserts that the property is present with a JSON `null` kind, on both the POST body and the GET. It includes a `" \t "` case. |

**Mutation evidence** (scratch copy; the source tree was not touched):
| Mutant | Result |
|---|---|
| none (baseline) | 27/27 pass |
| `TrimToNull` returns the trimmed value without nulling blanks | 11 fail: all 10 AC006 theory rows plus integration AC006 |
| optional fields not trimmed (`trimmed = value`) | 9 fail: unit AC005, the 5 whitespace AC006 rows, integration AC005/AC006, and AC012 |
| `FirstName` not trimmed | 2 fail: unit and integration AC005 |
| `Trim(' ')` instead of `Trim()` | 3 fail: unit AC005, integration AC005/AC006 |
| handler bypasses `input` for `Notes` (`request.Notes`) | 2 fail: integration AC005/AC006, so the wiring is covered at the HTTP level |

**Points the orchestrator asked about:**
- **Minimality:** satisfied. `Parse` always returns `(input, null)`. There are no length constants, no `IsValidEmail`, and no error dictionary entries. Null or blank `firstName` still becomes `""` (`ContactInput.cs:14`), so T-06 stays red for AC-007. The interim `FirstName ?? string.Empty` and `Email?.Trim()` were removed from the handler, which closes the carried-forward item from T-02 to T-04. The `?? string.Empty` now lives in `Parse` and is required to compile because nullable warnings are errors and `FirstName` is non-nullable. T-06 replaces it with the error. Drift grep is clean: no `WithName`/`WithSummary`/`Produces*`/`WithTags`/`NOCASE`/`ILogger` under `src/`.
- **`var (input, _)` + `input!`:** acceptable as an interim step. It follows the plan.md contract that exactly one of (input, errors) is non-null, and with `errors` always null at this stage it can't fail. The TS-only `!` rule in conventions doesn't apply. T-06 has to replace the discard with an `if (errors is not null) return TypedResults.ValidationProblem(errors);` branch, which narrows the type. The handler's return type then becomes `Results<Created<...>, ValidationProblem>`, and the `!` should go then. This is tracked below, not a finding now.
- **Unicode whitespace:** `string.Trim()` removes every `char.IsWhiteSpace` character (Unicode White_Space: NBSP U+00A0, U+2000-U+200A, U+3000, U+0085, line/paragraph separators, and so on). It does not remove the zero-width space U+200B or the BOM U+FEFF. The spec says only "leading and trailing whitespace", so Unicode White_Space is a reasonable and conventional reading, and it does not conflict with any AC. The risk is consistency, not correctness. See Follow-ups.

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Nit | test-writer | tests/MicroCrm.Api.Tests/Unit/Contacts/ContactInputTests.cs:60-64 | `Assert.Null(field == "x" ? input.X : null)` x5: four of the five asserts are trivially true in each row. It works (mutation-proven), but a reader has to decode it. | Optional: have the switch also return a `Func<ContactInput, string?>` selector and use `Assert.Null(select(input))`. |

**Notes:**
- Security: pure string normalization, no new I/O or logging. The input still reaches EF only through parameterized inserts.
- Follow-ups for later tasks: (a) at T-06, remove the `_` discard and `input!` (see above). (b) At T-08, the email "contains no whitespace" check should use `char.IsWhiteSpace`, the same definition as `Trim()`. Otherwise an email with an interior NBSP or U+3000 would pass even though trimming treats that character as whitespace. (c) At T-15 (AC-032), trim `search` with the same `string.Trim()`.

## T-06: 2026-10-01: APPROVE

**Checks:** backend tests pass (34/34, 0 failed, 0 skipped) · `dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exit 0 · web lint/typecheck not rerun because `web/` is unchanged.

**Scope reviewed:** `src/MicroCrm.Api/Features/Contacts/ContactInput.cs` (the firstName guard in `Parse`), `ContactsEndpoints.cs:32-42` (the return type and the `input is null` branch), `tests/.../Unit/Contacts/ContactInputTests.cs` (the new AC007 theory and the refactored AC006 assertion), and `tests/.../Integration/Contacts/CreateContactValidationTests.cs` (new). The T-03 to T-05 hunks were approved earlier and were not re-reviewed.

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-007 | `ContactInputTests.Parse_MissingOrBlankFirstName_ReturnsFirstNameError_AC007` (null/""/"   "), `CreateContactValidationTests.CreateContact_WithoutFirstName_Returns400WithFirstNameError_AC007` (missing/null/""/"  ") | yes. The integration test checks the 400, the exact `errors` key set `{firstName}`, and that a direct-DB count of the per-case email marker is 0. The "missing" row sends `{"email":"ac007-missing@example.com"}`, so the member really is absent. |
| AC-037 (400) | same integration test, via `ProblemAssert.IsProblemAsync(response, 400, "firstName")` | yes. It asserts the `application/problem+json` media type, non-empty string `type`/`title`, and `status` = 400. |

**Mutation evidence** (scratch copy; the source tree was not touched):
| Mutant | Result |
|---|---|
| none (baseline) | 34/34 pass |
| AC006: `Company` trimmed but not nulled | 3 fail: both `company` rows of the refactored unit theory, plus integration AC006. The switch-selector form is no weaker than the T-05 form. |
| guard is `firstName is null` (blank strings pass) | 4 fail: unit ""/"   " and integration empty/whitespace |
| guard checks the untrimmed `request.FirstName` | does not compile (CS8604, nullable warnings are errors), so the guard is load-bearing for the type as well |
| error key `"FirstName"` (PascalCase) | 7 fail: all unit and integration AC007 rows. The serializer does not re-case dictionary keys, so the camelCase key comes from `Parse`, and the tests pin it. |
| extra `email` error alongside `firstName` | 4 fail: integration rows, because ProblemAssert checks the exact key set |
| handler inserts a row, then still returns 400 | 4 fail: integration rows, on the DB count |

**Points the orchestrator asked about:**
- **Minimality:** satisfied. `Parse` adds only the required-firstName rule: no length limits, no `IsValidEmail`, and it returns early instead of collecting errors (that's for T-08/AC-014). The `?? string.Empty` placeholder is gone. Drift grep is clean: no `WithName`/`WithSummary`/`Produces*`/`WithTags`/`NOCASE`/`ILogger`/`MaxLength`/`EnableSensitiveDataLogging` under `src/` outside the migrations. Program.cs is unchanged in this task.
- **`errors!` vs `errors is not null`:** acceptable, not a defect. The tuple from `Parse` carries no flow annotations, so either branch shape needs exactly one `!`. Branching on `errors is not null` would just move it to `input!` across the longer success path. Branching on `input is null` gives compiler-checked non-null use of `input` where it is actually dereferenced, and puts the single `!` on the line the plan.md:102 contract ("exactly one of (input, errors) is non-null") justifies. The only change that would remove `!` entirely is a `TryParse(..., [NotNullWhen(true)] out ContactInput? input, [NotNullWhen(false)] out ... errors)` shape, which departs from the planned signature. That's not worth it now. The T-05 follow-up (a) is closed: the `_` discard and `input!` are gone.
- **400 body:** camelCase `firstName` and `application/problem+json` are both asserted (see the mutants above).
- **"Member missing":** confirmed. Its InlineData fragment is `""`, so the body has only `email`.

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Nit | test-writer | tests/MicroCrm.Api.Tests/Unit/Contacts/ContactInputTests.cs:78 | `FirstName = firstName!`: `CreateContactRequest.FirstName` is `string?` (ContactDtos.cs:4), so the null-forgiving operator does nothing, and it suggests to a reader that null is unexpected here. | Optional: `Valid with { FirstName = firstName }`. |

**Notes:**
- Security/NFR-004: no logging was added, and the error message is static (it doesn't echo the input).
- Still open for later tasks: (b) T-08's email whitespace check should use `char.IsWhiteSpace`, and T-08 should turn the early return into collect-all (AC-014). (c) At T-15, trim `search` with `string.Trim()`.

## T-07: 2026-10-01: CHANGES_REQUESTED

**Checks:** backend tests pass (59/59, 0 failed, 0 skipped) · `dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exit 0 · web lint/typecheck not rerun because `web/` is unchanged.

**Scope reviewed:** `src/MicroCrm.Api/Features/Contacts/ContactInput.cs` (constants, `CheckMax`, the switch from early return to collecting errors), `tests/.../Unit/Contacts/ContactInputTests.cs:88-164` (helpers plus the AC008/AC009 theories, and the T-06 nit at :78), `tests/.../Integration/Contacts/CreateContactValidationTests.cs:32-63`, and `tests/.../Integration/Contacts/CreateContactTests.cs:222-249`. The T-03 to T-06 hunks were approved earlier and were not re-reviewed.

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-008 | `ContactInputTests.Parse_FieldOverMax_ReturnsFieldError_AC008` (6 fields at max+1), `CreateContactValidationTests.CreateContact_FieldOverMax_Returns400WithFieldError_AC008` (6 fields; exact key set; row-count delta) | **partly**. Each field's limit is pinned: removing a check, or raising `PhoneMax` to 51, fails both rows for that field. But AC-008 says `errors` contains "an entry for **each** offending field", and every case has exactly one offending field. With a first-error-wins mutant, all 59 tests still pass (see finding 1). |
| AC-009 | `ContactInputTests.Parse_FieldAtMax_IsValid_AC009` (6 fields x bare/whitespace-wrapped), `CreateContactTests.CreateContact_FieldsAtMax_Returns201_AC009` (all six at max in one request, three wrapped) | yes. The off-by-one and measured-before-trimming mutants both fail it (below). The unit test also asserts the stored value equals the trimmed max-length value. |

**Mutation evidence** (scratch copy; the source tree was not touched):
| Mutant | Result |
|---|---|
| none (baseline) | 59/59 pass |
| first-error-wins (`return` after the firstName error; `CheckMax` adds only if `errors.Count == 0`) | **0 fail**. No test distinguishes collecting errors from returning at the first one. |
| `value.Length >= max` (off-by-one) | 13 fail: all 12 unit AC009 rows plus integration AC009 |
| lengths measured on the raw `request.X` (before trimming) | 7 fail: the 6 wrapped unit AC009 rows plus integration AC009 |
| `&& !errors.ContainsKey(field)` removed | **0 fail** (see finding 2) |
| `PhoneMax = 51` | 2 fail: the unit and integration AC008 `phone` rows |
| notes `CheckMax` removed | 2 fail: the unit and integration AC008 `notes` rows |

**Points the orchestrator asked about:**
- **Collecting errors vs. returning at the first one.** This is not a minimality violation of the production code. In-scope AC-008 requires it: "an entry for each offending field". Two over-length fields in one request must give two keys, and returning at the first error would break AC-008 itself, not only T-08's AC-014. Reverting the production code would make it non-conformant with an in-scope AC, so I don't recommend it. The real defect is the missing test that demands this behaviour (finding 1). tasks.md T-08's "Expected RED: ... the multiple-error test misses `email`" also assumes `firstName` and `phone` are already reported together by then, which matches collecting errors after T-07. T-08 stays genuinely red: its integration AC-014 case (missing firstName + invalid email + phone 51) lacks `email` today, because there is no format rule yet. T-08's unit `Parse_MultipleInvalidFields_ReturnsAllErrors_AC014` must also include an invalid email so it goes red. Otherwise it is a guard and should be labelled as one.
- **Count-delta safety (AC-008 integration).** Safe. `ApiFactory` builds a unique `microcrm-test-{Guid}` shared-cache in-memory DB per instance, and `IClassFixture` gives one instance per class. With xUnit v3 defaults (no `CollectionBehavior` or runner-json override in `tests/`), each class is its own collection and its tests run serially. So nothing else can write to this DB between `before` and after. `before` is read after `CreateClient()`, so the schema exists. Combined with ProblemAssert's exact key set, a mutant that inserts and then returns 400 would be caught.
- **The AC-009 guards are real.** Off-by-one and measured-before-trimming are both caught (above).
- **No EF max-length configuration or migration.** Confirmed. No `HasMaxLength`/`MaxLength` under `src/`, and `Data/` and the migrations are unchanged. Drift grep is clean: no `WithName`/`WithSummary`/`Produces*`/`WithTags`/`NOCASE`. `Program.cs` has no T-07 changes. Lengths use `string.Length`, as Q8 decided.

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Blocking | test-writer | tests/MicroCrm.Api.Tests/Unit/Contacts/ContactInputTests.cs, tests/MicroCrm.Api.Tests/Integration/Contacts/CreateContactValidationTests.cs | AC-008's "an entry for each offending field" has no test. A first-error-wins `Parse` passes the whole suite. | Add `Parse_SeveralFieldsOverMax_ReturnsErrorForEach_AC008` (e.g. lastName 101 + phone 51 + notes 4001, asserting exactly those three keys) and an integration counterpart through `ProblemAssert.IsProblemAsync(response, 400, "lastName", "phone", "notes")` with no row created. The production code already satisfies it, so this test cannot go red against the real tree. Prove that it fails for the right reason against the first-error-wins mutant in a scratch copy (expected: only `lastName` returned), and record that as the RED evidence. Don't revert production code that is correct. |
| 2 | Should-fix | implementer | src/MicroCrm.Api/Features/Contacts/ContactInput.cs:47 | `&& !errors.ContainsKey(field)` is unreachable. The only earlier entry is `firstName` "required", which needs a length of 0, so it can't coincide with length > 100. Removing it changes no test outcome. This is speculative protection for a T-08 email-format collision that no test demands yet. | Remove the conjunct. If T-08 needs a precedence rule between the email length and format errors, add it then, driven by a test. |

**Notes:**
- The T-06 nit is closed: `ContactInputTests.cs:78` is now `Valid with { FirstName = firstName }`.
- Security/NFR-004: error messages are static and contain only the limit, not the input. No logging was added. The 4000-char notes cap bounds the input. There is no request-size limit beyond Kestrel's default, which is fine for v1.
- Test quality: the email helper builds well-formed addresses at max and max+1, so these cases stay length-only once T-08's format rule lands. The `Assert.Equal(max + 1, value.Length)` preconditions protect against helper arithmetic slips.
- Follow-up (planner, doc drift): plan.md:29 lists "max lengths" in `Data/ContactConfiguration.cs`, but no task schedules them, and SQLite doesn't enforce `TEXT` lengths. Either drop that wording from the plan or schedule it explicitly. Don't add it as an unrequested change.
- Still open for later tasks: (b) T-08's email whitespace check should use `char.IsWhiteSpace`. (c) At T-15, trim `search` with `string.Trim()`.

## T-07 re-review (fix cycle 1): 2026-10-01: APPROVE

**Checks:** backend tests pass (61/61, 0 failed, 0 skipped) in a scratch copy of the current tree. `dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exits 0 on the real tree. `web/` is unchanged, so the web lint and typecheck were not rerun.

**Finding 1 (Blocking, test-writer): resolved.** `ContactInputTests.Parse_SeveralFieldsOverMax_ReturnsErrorForEach_AC008` (ContactInputTests.cs:138-154) asserts the exact sorted key set {lastName, notes, phone} and that each message is non-empty. `CreateContactValidationTests.CreateContact_SeveralFieldsOverMax_Returns400WithErrorForEach_AC008` (CreateContactValidationTests.cs:56-73) checks the same keys exactly through `ProblemAssert` and confirms the row count is unchanged. I re-ran the mutation independently in scratch: `return` after the firstName error, plus `CheckMax` gated on `errors.Count == 0`. Exactly these 2 tests fail (59 pass), both with Expected `["lastName","notes","phone"]` / Actual `["lastName"]`, which is the right reason.

**Finding 2 (Should-fix, implementer): resolved.** `ContactInput.cs:48` is now `value is not null && value.Length > max`. No `ContainsKey` remains under `src/`.

**Nothing else changed:** only `ContactInput.cs`, `ContactInputTests.cs` and `CreateContactValidationTests.cs` were modified after the T-07 review. The existing AC-008/AC-009 tests are unchanged, with no assertions weakened and no skips. The drift grep is still clean (`WithName`/`WithSummary`/`Produces*`/`WithTags`/`NOCASE`/`HasMaxLength`). `Data/`, the migrations, `Program.cs` and `ContactsEndpoints.cs` are untouched.

**Notes:**
- A partial mutant (return after the firstName error only) is still not caught. No current test combines a missing firstName with an over-length field. That combination is T-08's AC-014 case, so it is not a T-07 gap.
- The plan.md:29 doc-drift follow-up is tracked by the orchestrator. It is out of scope here.

## T-08: 2026-10-01: APPROVE

**Checks:** backend tests pass (73/73, 0 failed, 0 skipped; re-run in a scratch copy of the current tree) · `dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exit 0 on the real tree · web lint/typecheck n.a. (`web/` unchanged).

**Scope reviewed:** `src/MicroCrm.Api/Features/Contacts/ContactInput.cs` (`IsValidEmail`, the email-format branch in `Parse`), `tests/.../Unit/Contacts/ContactInputTests.cs:184-215`, `tests/.../Integration/Contacts/CreateContactValidationTests.cs:75-103`. The T-04 to T-07 hunks were approved earlier and were not re-reviewed.

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-010 | `ContactInputTests.IsValidEmail_Rules_AC010` (7 invalid: `a`, `a@`, `@b`, `a@@b`, `a@b@c`, `a b@c`, `a b@c`; 2 valid: `a@b`, `Ada.Lovelace@Example.com`), `CreateContactValidationTests.CreateContact_WithInvalidEmail_Returns400WithEmailError_AC010` (exact key set `email`, row count unchanged) | yes. Each clause of the Definitions rule is pinned by its own mutant (below). The NBSP row goes beyond tasks.md and kills a space-only whitespace check. "Validated after trimming" is pinned by the existing AC005/AC009/AC012 tests. |
| AC-014 | `ContactInputTests.Parse_MultipleInvalidFields_ReturnsAllErrors_AC014` (null firstName + `not-an-email` + phone 51 → exactly {email, firstName, phone}), `CreateContactValidationTests.CreateContact_WithMultipleInvalidFields_ReturnsAllCamelCaseKeys_AC014` (firstName omitted + same → exact camelCase key set through `ProblemAssert`, no row) | yes. Kills both the firstName early-return mutant and the "format check only when no other errors" mutant. The PascalCase-key mutant fails it too. |

**Mutation evidence** (scratch copy; the source tree was not touched):
| Mutant | Result |
|---|---|
| none (baseline) | 73/73 pass |
| `!errors.ContainsKey("email")` removed | **0 fail** (see finding 1) |
| `return (null, errors)` right after the firstName error (the partial mutant left open by the T-07 re-review) | 2 fail: unit and integration AC014. **The T-07 gap is closed.** |
| format check gated on `errors.Count == 0` | 2 fail: unit and integration AC014 |
| format check on the untrimmed `request.Email` | 4 fail: unit/integration AC005, integration AC012, the wrapped unit AC009 email row |
| `at > 0` weakened (empty local part allowed) | 5 fail: theory (`@b`), integration AC010, both AC014 |
| `at < value.Length - 1` removed (empty domain allowed) | 1 fail: theory row `a@` |
| single-`@` check removed | 2 fail: theory rows `a@@b`, `a@b@c` |
| whitespace check removed | 2 fail: theory rows `a b@c`, `a b@c` |
| whitespace check is `Contains(' ')` | 1 fail: theory row `a b@c` |
| format error key `"Email"` | 3 fail: integration AC010, both AC014 |

**Points the orchestrator asked about:**
- **`!errors.ContainsKey("email")`.** It's reachable, unlike the T-07 conjunct: an email that is both over 254 chars and malformed hits it. It decides which message wins (length kept, format dropped). But neither AC-008, AC-010 nor AC-014 says which message takes precedence. They only require an `email` entry, and every test asserts keys, not message text. Removing it fails 0 tests. That's production behaviour no test demands (§2), the same pattern as T-07 finding 2. I'm rating it the same way: Should-fix, implementer.
- **Checked against the documents the implementer skipped:**
  - *Signature:* matches plan.md:104 (`public static bool IsValidEmail(string)`, on `ContactInput`) except for the parameter name. The plan calls it `trimmedEmail`, the code uses `value` (finding 2, Nit).
  - *Rule:* matches the spec Definitions/Q6 exactly: one `@`, non-empty both sides, no whitespace. No dot-in-domain or RFC 5322 strictness.
  - *Whitespace:* `char.IsWhiteSpace` is consistent with `string.Trim()`, which closes carried item (b).
  - *Message style:* "Must be a valid email address." follows the existing static, sentence-case, full-stop messages ("First name is required.", "Must be N characters or fewer."). It doesn't echo the input. Neither the plan nor ADR-0004 prescribes message text.
  - *Errors pipeline:* follows ADR-0004 line 27. It's a pure function, the key is camelCase, and it runs after trimming. No DataAnnotations or `AddValidation()` were added.
  - *Drift grep:* clean. No `WithName`/`WithSummary`/`Produces*`/`WithTags`/`NOCASE`/`HasMaxLength`/`ILogger` under `src/` outside migrations. Only `ContactInput.cs` changed in `src/`.
- **RED:** consistent with tasks.md "Expected RED". The orchestrator's scratch evidence shows 201 instead of 400, and `email` missing from AC014. The stub `IsValidEmail => true` fails the 7 invalid rows, so the theory isn't vacuous.

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Should-fix | implementer | src/MicroCrm.Api/Features/Contacts/ContactInput.cs:37 | `&& !errors.ContainsKey("email")` sets a length-over-format precedence that no AC specifies and no test demands. Removing it fails 0 of 73 tests (§2: no production code without a failing test). | Remove the conjunct. Without it, an over-length malformed email gets the format message under the same single `email` key, which still satisfies AC-008/010/014. If a precedence is actually wanted, it needs a spec/plan decision first, then a test-writer test pinning the message, before the guard comes back. |
| 2 | Nit | implementer | src/MicroCrm.Api/Features/Contacts/ContactInput.cs:50 | The parameter is named `value`, but plan.md:104 names it `trimmedEmail`, which documents the precondition that callers pass a trimmed value. | Optional: rename it to `trimmedEmail`. |

**Notes:**
- Test quality: deterministic, no skips, and no existing assertions were changed. The unit AC014 test uses `Keys.Order()` with the default comparer while `ProblemAssert` uses `Ordinal`. That's harmless for these three ASCII keys.
- Security/NFR-004: no logging, static messages, and the email is bounded by the 254 cap before the O(n) scan.
- Carried item (b) is closed. (c) is still open for T-15: trim `search` with `string.Trim()`.

## T-09: 2026-10-01: APPROVE

**Checks:** backend tests pass (78/78, 0 failed, 0 skipped; re-run in a scratch copy of the current tree, 79/79 with a temporary probe test that exists only in scratch) · `dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exits 0 on the real tree · web lint and typecheck n.a. (`web/` unchanged).

**Scope reviewed:** `src/MicroCrm.Api/Program.cs:10` (`Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = false)`) and `tests/.../Integration/Contacts/CreateContactValidationTests.cs:105-120`. `app.UseExceptionHandler()` in Program.cs belongs to T-04 (already approved) and was not re-reviewed.

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-015 | `CreateContactValidationTests.CreateContact_WithMalformedBody_Returns400Problem_AC015` (raw `application/json` bodies `{`, empty, `null`, `[]`, `{"firstName":123}`; asserts 400 through `ProblemAssert` and an unchanged row count) | yes. All three AC clauses are covered: invalid JSON (`{`), empty body (empty, plus `null`, which the framework treats as missing), and a wrong JSON type (`{"firstName":123}`; `[]` is a wrong root type). "SHALL NOT create" is pinned by the count. |
| AC-037 (binding 400) | same test, through `ProblemAssert` (problem+json content type, non-empty string `type`/`title`, `status` == 400) | yes |

**Mutation evidence** (scratch copy; the source tree was not touched):
| Mutant | Result |
|---|---|
| none (baseline) | 78/78 pass. Probe: all 7 bodies (the 5 test rows, plus `{"firstName":"Ada","email":5}` and the truncated `{"firstName":"Ada"`) return 400 `application/problem+json` with exactly `{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"Bad Request","status":400,"traceId":"..."}` |
| `ThrowOnBadRequest = false` line removed | **5 fail**, all 5 AC015 rows, each Expected 400 / Actual 500. The cause is `BadHttpRequestException` wrapping `JsonException`/`JsonReaderException` (in Development the default is `true`), which T-04's handler catches. The line is required. The 500 bodies were the generic `{type,title:"An error occurred while processing your request.",status,traceId}`, so even the mutant leaks nothing. |

**Points the orchestrator asked about:**
1. **Is the line required?** Yes, as shown above. It's also the exact mechanism ADR-0004 and plan design point #4 prescribe. In Production the framework default is already `false`, so setting it explicitly is what makes the behaviour the same in every environment, as ADR-0004 requires. It's not defensive no-op config.
2. **Leakage.** None. The probe bodies contain no `detail`, no `errors`, no `exception`, and none of "JsonReaderException", "Failed to read parameter", "$.firstName", "LineNumber" or "BytePositionInLine". That's structural: with `ThrowOnBadRequest = false` the framework sets an empty-body 400, and `UseStatusCodePages` writes a ProblemDetails from the status code alone. No exception or parser text reaches the writer. The test doesn't assert absence of these strings. I'm **not** raising that as a Should-fix. AC-038's no-leak requirement is scoped to 500s, and those are already pinned by the two `ErrorHandlingTests` AC038 tests. AC-015/AC-037 require only the problem shape. A no-leak assertion here would guard against a hypothetical future change (for example someone adding `CustomizeProblemDetails` that copies the exception message) rather than any AC clause. That's optional hardening (finding 1, Nit). If the conventions rule "Never leak exception details" is meant to be tested for every error class, the planner should say so in an AC at the next spec, not through a T-09 fix cycle.
3. **conventions.md vs plan.md/ADR-0004 on `errors`.** This is ambiguous wording, not a real conflict. The conventions row says "400 validation (with `errors` dictionary)". A body that can't be deserialized is a malformed request, not a field-validation failure: there are no fields to key errors by, and AC-015 asks for none. Field validation 400s (AC-007..AC-014, AC-028) do carry `errors`, so they follow the convention. The decision is already recorded in ADR-0004 ("AC-015 doesn't require an `errors` dictionary, so none is added"), so no planner action or new ADR is needed. The wording should still be fixed so the next reader doesn't hit the same question. **Owner: documenter**, at `/document 001`, in the same edit ADR-0004's Consequences already asks for (replacing the DataAnnotations/AddValidation line). Suggested wording: "400 validation (with `errors` dictionary keyed by camelCase field); 400 for an unreadable body (malformed/empty/mistyped JSON) is plain ProblemDetails without `errors` (ADR-0004)". This is a follow-up, not a finding against T-09.

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Nit | test-writer | tests/MicroCrm.Api.Tests/Integration/Contacts/CreateContactValidationTests.cs:119 | The AC015 theory doesn't pin that the binding-400 body carries no parser or exception detail. Today that is guaranteed structurally (verified by probe), but nothing would catch a regression. | Optional: assert that the body has no `detail`/`errors` members and doesn't contain `"Json"` or `"$."`. Don't do it in a fix cycle for this task. |

**Notes:**
- Test quality: deterministic, no skips, existing tests unchanged. `ProblemAssert` is unchanged (still the hardened T-02 version). Calling it with no keys doesn't assert that `errors` is absent, which is consistent with AC-015 not specifying it either way.
- Drift grep (`WithName`/`WithSummary`/`Produces`/`WithTags`/`ContainsKey`/`ILogger`/`NOCASE` under `src/`, excluding migrations) returns no matches. The T-08 `ContainsKey` Should-fix has evidently been addressed.
- RED is consistent with tasks.md "Expected RED" (500 from `BadHttpRequestException` via T-04's handler). The orchestrator recorded it, and the removal mutant above reproduces it independently.
- NFR-004: no app logging added. With `ThrowOnBadRequest = false` the framework logs the binding failure at Debug. `JsonException` messages carry only path and position, not values.
- Uncovered and out of scope: a non-JSON content type (415) is not in AC-015. ADR-0004 lists 415 as covered by status-code pages, but no test pins it.

## T-10: 2026-10-01: APPROVE

**Checks:** backend tests pass (83/83, 0 failed, 0 skipped) · `dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exits 0 · `dotnet ef migrations has-pending-model-changes` reports no changes · `dotnet build` 0 errors · web lint/typecheck n.a. (`web/` unchanged).

**Scope reviewed:** `src/MicroCrm.Api/Data/ContactConfiguration.cs`, `Data/AppDbContext.cs`, `Data/SqliteErrors.cs`, `Data/Migrations/20261001212318_AddContactEmailUniqueIndex{,.Designer}.cs`, the `AppDbContextModelSnapshot.cs` diff, `Features/Contacts/ContactsEndpoints.cs:32,60-69`, and `tests/.../Integration/Contacts/CreateContactConflictTests.cs`.

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-011 | `CreateContact_WithExistingEmailDifferentCaseAndWhitespace_Returns409Problem_AC011` (409 via `ProblemAssert`; email absent from body; row count stays 1) | yes. Case and whitespace are both varied, and "SHALL NOT create" is pinned by the count. |
| AC-013 | `CreateContact_ManyWithoutEmail_AllSucceed_AC013` (absent ×2, `null`, `""`; all 201; 4 rows with `Email IS NULL`) | yes. The `IS NULL` count kills even the narrow mutant that stores only a literal `""` as `""`. |
| AC-016 | `Database_RejectsDuplicateEmailDifferingOnlyInCase_AC016` (direct SQL, `SqliteException` extended code 2067); `CreateContact_ConcurrentSameEmail_ExactlyOneCreatedRestConflict_AC016` (10 POSTs behind a ready-count + gate; zero 5xx, one 201, nine 409 problem+json, one row) | yes. Follows plan design point #1 parts 1 and 3 exactly. The assertions hold for every interleaving. |
| AC-037 (409) | AC011 and the concurrent test, through `ProblemAssert` | yes |
| NFR-004 (guard) | `CreateContact_DuplicateEmail_EmailNotLoggedAtInformationOrAbove_NFR004` (capturing provider; a canary proves the EF command category is live at Information; message, state and exception text checked) | yes |

**Stability (point 1):** the conflict class passed 15/15 sequential runs on the real tree (`--filter-class "*CreateContactConflictTests*"`), plus 20/20 runs under contention (4 parallel processes × 5 runs). Add the orchestrator's 3 full-suite runs and the implementer's 6. No flake was seen.

**Mutation evidence (point 2; scratch copy, source untouched):**
| Mutant | Result |
|---|---|
| baseline | 83/83 pass |
| NOCASE removed from the migration's `collation:` argument only | **0 fail.** This is not a real mutant. SQLite's `AlterColumn` rebuild takes the column definition from the migration's TargetModel (Designer), so the generated SQL still has `COLLATE NOCASE`. Worth knowing: hand-editing the `Up` body doesn't change the collation. |
| NOCASE removed everywhere (config, migration, Designer, snapshot; unique index kept) | **3 fail:** AC011, Database_…_AC016, Concurrent_…_AC016 |
| NOCASE removed from config only (snapshot still has it) | 40 fail. EF's pending-model-changes guard throws at `MigrateAsync`, which is useful as an incidental drift guard |
| `catch (DbUpdateException)` with no filter (everything → 409) | **1 fail:** `ErrorHandlingTests.CreateContact_WhenDatabaseFails_…_AC038` (DROP TABLE → 409 instead of 500) |
| conflict mapped to 500 | **2 fail:** AC011, Concurrent_AC016 |
| conflict mapped to 400 | **2 fail:** AC011, Concurrent_AC016 |
| `Email = input.Email ?? string.Empty` | **6 fail**, including AC013 |
| only a literal `""` request stored as `""` | **1 fail:** AC013 |
| `.EnableSensitiveDataLogging()` on `UseSqlite` | **1 fail:** NFR004 |
| filter widened to `SqliteErrorCode: 19` (any SQLITE_CONSTRAINT → 409) | **0 fail**, see finding 1 |

**Migration (point 3):** `dotnet ef database update` on a fresh file DB produces `"Email" TEXT COLLATE NOCASE NULL` and `CREATE UNIQUE INDEX "IX_Contacts_Email"`, and both migrations are recorded in history. On a DB seeded at `CreateContacts` (a full row and two rows with NULL email), the rebuild's `INSERT … SELECT` copies every column by name, so values are preserved. Nullability is unchanged (`PRAGMA table_info`: Id/FirstName/CreatedAt/UpdatedAt NOT NULL, the rest nullable), and the column order becomes alphabetical, which is harmless. After the migration, a case-variant duplicate fails with `UNIQUE constraint failed: Contacts.Email`, a further NULL-email row succeeds, and `Down` restores a plain `TEXT NULL` Email with all rows kept.

**409 body (point 4):** a probe returned `409 application/problem+json` with `{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"A contact with this email already exists.","status":409,"traceId":"…"}`. There's no `detail`, the email isn't echoed (AC011 asserts this too), and the title matches plan.md design point #1 verbatim. Conventions don't prescribe title text. The wording is static and sentence-case with a full stop, consistent with the existing validation messages, and it doesn't vary per occurrence, which suits RFC 9457 `title`.

**Minimality (point 5):** clean. `ContactConfiguration` has only the Email collation and the unique index: no name collations (T-12), no `HasMaxLength`. The drift grep (`WithName|WithSummary|Produces|WithTags|MaxLength|EnableSensitive|ILogger|catch`) under `src/` (excluding migrations) matches only the single filtered catch at `ContactsEndpoints.cs:64`. There's no pre-check query, other `DbUpdateException`s propagate, and every `using` is used. The handler return type adds `ProblemHttpResult` (ADR-0004: `TypedResults.Problem`, not `Conflict`).

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Should-fix | test-writer | tests/MicroCrm.Api.Tests/Integration/ErrorHandlingTests.cs (new test) | Nothing pins that a constraint failure *other than* UNIQUE (2067) stays a 500. Widening `SqliteErrors` to base code 19 survives (0 fail), and the result would report "A contact with this email already exists." for an unrelated failure. Plan design point #1 states "every other `DbUpdateException` is re-thrown". AC-038's DROP TABLE test only kills the catch-all mutant. This isn't reachable through the API today (the NOT NULL columns are validated or set by the server), so it's not Blocking. | Add `CreateContact_WhenNonUniqueConstraintFails_Returns500Problem_AC038` in a class with its own fixture. Install `CREATE TRIGGER t BEFORE INSERT ON Contacts BEGIN SELECT RAISE(ABORT,'x'); END` via `factory.ExecuteSqlAsync` (SQLITE_CONSTRAINT_TRIGGER, base 19, extended 1811), POST a valid contact, and assert the safe 500 from `AssertSafe500Async`. It should pass against the current code and kill the code-19 mutant. |

**Notes:**
- Test quality: deterministic, no skips, existing tests unchanged, `ProblemAssert` unchanged. Every test uses a distinct email, so the shared class fixture needs no reset. That holds as long as the AC013 count stays filtered by its unique `FirstName`.
- NFR-004 inspection: no app logging was added. On a lost race EF logs the `DbUpdateException` at Error, and the message (`UNIQUE constraint failed: Contacts.Email`) names the index, not the value. The guard includes exception text, so it would catch a value-bearing message.
- Correctness: the failed `Contact` stays tracked on the request-scoped context, which is then disposed, so no state leaks. Cancellation surfaces as `OperationCanceledException`, not `DbUpdateException`, so it isn't mapped to 409.
- RED matches tasks.md "Expected RED" (the orchestrator recorded 201s, no throw, and 10×201). The NOCASE-everywhere mutant above independently reproduces the AC011/AC016 failures.

## T-11: 2026-10-01: APPROVE

**Checks:** backend tests pass (88/88, 0 failed, 0 skipped) · `dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exits 0 · web lint/typecheck n.a. (`web/` unchanged).

**Scope reviewed:** `src/MicroCrm.Api/Common/Paging.cs`, `src/MicroCrm.Api/Features/Contacts/ContactsEndpoints.cs:17,23-41`, `tests/.../Integration/Infrastructure/ApiFactory.cs` (`ResetAsync`), `tests/.../Integration/Contacts/ListContactsTests.cs`, `tests/.../Integration/ErrorHandlingTests.cs:45-54`.

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-020 | `ListContactsTests.ListContacts_NoQuery_ReturnsFirst20WithEnvelope_AC020` (25 seeded; exact envelope member set; `page`=1, `pageSize`=20, `totalCount`=25; 20 distinct seeded items; each item's member set equals the create response's) | yes. It also deliberately leaves out which 20 and in what order, which is correct because that's T-12/T-13. |
| AC-021 | `ListContacts_NoContacts_ReturnsEmptyItemsAndZeroTotal_AC021` (`items` is a JSON array of length 0; `totalCount` 0) | yes |
| AC-039 (list) | `ListContacts_FromNewClient_IncludesPersistedContact_AC039` (`COUNT(*)` through an independent `SqliteConnection`; a new `HttpClient`; raw JSON of the listed item equals the create response) | yes. The raw-text equality also pins AC-020's "same shape as AC-017" for values, not just names. |
| AC-038 (list) | `ErrorHandlingTests.ListContacts_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC038` | yes (point 4) |
| AC-037 (500 on list) | same, through `ProblemAssert` | yes |

**Mutation evidence** (scratch copy; source untouched):
| Mutant | Result |
|---|---|
| baseline | 88/88 pass (89/89 with the scratch probe) |
| `pageSize = 10` | **1 fail:** AC020 |
| `pageSize = 25` | **1 fail:** AC020 |
| `page = 0` (echo 0, `Skip(-20)`) | **1 fail:** AC020 |
| echo `page` 0, slice unchanged | **1 fail:** AC020 |
| `Take(25)` with `pageSize` still echoed as 20 | **1 fail:** AC020 |
| `Skip(20)` (second page served as the default) | **2 fail:** AC020, AC039 |
| `totalCount = 0` | **1 fail:** AC020 |
| handler skips the DB (empty list, total 0) | **3 fail:** AC020, AC039, ListContacts AC038 |
| `OrderBy(c => c.Id)` removed | **0 fail** (point 1) |

**Points the orchestrator asked about:**
1. **`OrderBy(c => c.Id)` placeholder: accepted, no finding.** It is untested, since removing it fails 0 tests. Unlike the T-04 `CustomizeProblemDetails` block, though, it isn't a no-op. A scratch probe with a capturing logger, running alone so EF's shared compiled-query cache doesn't hide the event, showed that without it EF logs `Microsoft.EntityFrameworkCore.Query.RowLimitingOperationWithoutOrderByWarning` at **Warning** ("…without an 'OrderBy' operator. This may lead to unpredictable results"), and the SQL becomes `LIMIT/OFFSET` with no `ORDER BY`, which SQLite leaves unspecified. With it, the SQL is `ORDER BY "c"."Id" LIMIT @p1 OFFSET @p`. Plan design point "Pipeline order" (count → order → Skip/Take) assumes an order before paging. It's the smallest order that avoids the warning, and it's commented as a T-12 placeholder. **It doesn't make T-12 go green early.** Two sketch T-12-style tests in scratch had the same results with and without the placeholder. Seeding in expected order with `Time.Advance` between creates passed both ways (insertion order and v7 id order coincide). Seeding in reverse order failed both ways (3/3 runs with the placeholder, 1/1 without). So the early-pass risk depends on how T-12 seeds, not on this line (see follow-up).
2. **`ResetAsync` races: none.** Each `IClassFixture<ApiFactory>` gets its own factory and a uniquely named shared-cache DB (`microcrm-test-{Guid}`). There's no `[Collection]`/`CollectionBehavior` override, so each class is its own collection, and xUnit v3 runs a class's tests (and their `InitializeAsync`) serially. `_ = Server` (not thread-safe in WAF) is therefore only touched by one test at a time per factory. `ErrorHandlingTests` and `NonUniqueConstraintFailureTests` sabotage their own fixtures only. `DELETE FROM Contacts` runs on a fresh pooled connection and completes before the test body starts.
3. **AC-020 pins the defaults.** pageSize 10 and 25, page 0, a wrong echo, an over-take, a wrong offset and a wrong total all fail AC020 (table above). Seeding 25 (> 20) is what lets the 25 and `Take(25)` mutants die.
4. **AC038 list is a real RED→GREEN.** RED was 405 (no GET mapped on the path). The GREEN depends on the handler actually querying the DB and letting the exception reach T-04's handler. The "handler skips the DB" mutant returns 200 and fails it, so the test isn't satisfied by the route merely existing. The no-leak assertions (`AssertSafe500Async`) apply unchanged.

**Findings:** none.

**Notes:**
- Minimality: clean. `Paging.cs` holds only `PagedResponse<T>` (no `ListQuery`; that's T-13/T-14). The handler returns `Ok<...>` only (`ValidationProblem` arrives at T-14). The drift grep (`WithName|WithSummary|Produces|WithTags|ContainsKey|ILogger|NOCASE|EF.Functions|ListQuery`) under `src/` excluding migrations matches only the T-10 Email collation.
- NFR-004: no app logging added. `AsNoTracking` + `CancellationToken` are passed through, per conventions.
- Test quality: deterministic (AC020 asserts membership and uniqueness, not order; AC039 uses `Assert.Single` with a predicate). No skips, and existing tests are unchanged. `ProblemAssert` is unchanged.
- Follow-up for the T-12 test-writer: seed AC022/AC023 data in an order that is neither the expected order nor the expected order's id order. For example, insert the no-last-name contact first and the names in reverse, and call `factory.Time.Advance` between creates so that v7 id order equals insertion order. Otherwise the tests can pass against the T-11 placeholder. tasks.md's T-12 "Expected RED: insertion/ordinal order" is accurate only if the clock advances. With the fixed `FakeTimeProvider` clock, v7 ids within one millisecond are random, so the pre-T-12 order is random too, and a small seed could pass by chance.

## T-12: 2026-10-01: CHANGES_REQUESTED

**Checks:** backend tests pass (90/90, 0 failed, 0 skipped) · `dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exits 0 · `dotnet ef migrations has-pending-model-changes` reports no changes · web lint/typecheck n.a. (`web/` unchanged).

**Scope reviewed:** `src/MicroCrm.Api/Data/ContactConfiguration.cs:12-13`, `src/MicroCrm.Api/Features/Contacts/ContactsEndpoints.cs:31-35`, `src/MicroCrm.Api/Data/Migrations/20261001213907_AddContactNameCollation{,.Designer}.cs`, `AppDbContextModelSnapshot.cs`, `tests/.../Integration/Contacts/ListContactsTests.cs` (`SeedAsync`, `ListEmailsAsync`, `OrderTiedByDbIdOrder`, AC022, AC023).

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-022 | `ListContactsTests.ListContacts_SortsByLastFirstIdIgnoringCase_AC022` | **partly.** Case-insensitive last name and first name are pinned. The "then id" clause is not (finding 1). |
| AC-023 | `ListContacts_ContactsWithoutLastName_SortLastByFirstNameThenId_AC023` | **partly.** Nulls last and case-insensitive first name among the null-last-name group are pinned. "Then id" is not (finding 1). |

**Mutation evidence** (scratch copy; source untouched; baseline 90/90):
| Mutant | Result |
|---|---|
| name NOCASE removed from config, migration (deleted), Designer and snapshot together | **2 fail:** AC022, AC023 |
| nulls first (`LastName != null`) | **1 fail:** AC023 |
| no null flag (`OrderBy(LastName)`, so SQLite puts NULLs first) | **1 fail:** AC023 |
| `ThenBy(FirstName)` dropped | **2 fail:** AC022, AC023 |
| `ThenBy(Id)` dropped | **0 fail** (finding 1) |
| `ThenByDescending(Id)` | **2 fail:** AC022, AC023 |
| `ToLower()` on both names, NOCASE kept | 0 fail (expected; equivalent) |
| `ToLower()` on both names, NOCASE removed everywhere | 0 fail. The tests can't tell the collation from `lower()`. That's acceptable, because both fold ASCII only (Q1) and AC-022 states behaviour, not mechanism. The schema collation is pinned only by the migration and the "Done when". |

**Points the orchestrator asked about:**
1. **Id comparison: correct.** A scratch probe read the stored rows. EF stores `Guid` as **uppercase TEXT** (`typeof(Id)` = `text`, e.g. `019B7CA9-8C88-7BEF-…`), and the column has default BINARY collation. Hex digits sort the same in either case, and the hyphens sit at fixed positions, so `ORDER BY Id` equals `OrderBy(id.ToString(), StringComparer.OrdinalIgnoreCase)` on the API's lowercase ids. The probe compared 12 ids: `True`.
2. **Migration: OK.** On a fresh file DB, `dotnet ef database update` gives `FirstName TEXT COLLATE NOCASE NOT NULL`, `LastName TEXT COLLATE NOCASE NULL`, `Email TEXT COLLATE NOCASE NULL`, and `CREATE UNIQUE INDEX "IX_Contacts_Email"`, with all 3 migrations in history. I also took a DB to `AddContactEmailUniqueIndex`, seeded 3 rows (mixed case, NULL last name, NULL emails) and then updated. The `SELECT *` rows were byte-identical before and after, nullability was unchanged (`PRAGMA table_info`), and the index was recreated. `a@X.COM` against the existing `A@x.com` fails with `UNIQUE constraint failed: Contacts.Email`, and a further NULL email inserts fine. The rule `LastName='ADAMS'` now matches `Adams`, and the ORDER BY yields `Carl Adams, alice smith, Bob -, z -`. `Down` restores names without a collation while keeping Email NOCASE, the index and all 4 rows.
3. **Minimality: clean.** The production diff is exactly the two `UseCollation` lines, the four-key order and the generated migration. The drift grep (`WithName|WithSummary|Produces|WithTags|ContainsKey|ILogger|EF.Functions|ListQuery|ToLower|MaxLength|NOCASE`) under `src/`, excluding migrations, matches only the three collation lines. There's no paging or search work early. The T-11 placeholder `OrderBy(c => c.Id)` is gone, and the new order still precedes Skip/Take, so EF's no-OrderBy warning can't fire.
4. **Seeding (T-11 follow-up): done** for the name keys. Both tests seed in reverse of the expected order and call `Time.Advance(5ms)` between creates. RED failed at index 0, stable over 3 runs (orchestrator).

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Blocking | test-writer | `tests/MicroCrm.Api.Tests/Integration/Contacts/ListContactsTests.cs` (AC022 `dupont1`/`dupont2`; AC023 `dana1`/`dana2`) | The id tie-break that AC-022 and AC-023 both state ("then id") has no test that would fail if it broke. `SeedAsync` advances the clock, so v7 id order equals insertion order, and the tied pair is inserted in ascending id order. The plan is `SCAN Contacts` + `USE TEMP B-TREE FOR ORDER BY`, so without the `Id` key ties come back in rowid (insertion) order, which is the same as the expected order. Dropping `.ThenBy(c => c.Id)` passes 90/90. The behaviour is reachable in production: the probe shows ids created in the same millisecond are **not** in insertion order (`insertion order == id order: False`), so without the key, equal names would list in insertion order rather than id order, and that violates the AC. The test comment "so also not in id order" is untrue for the tied pair. | Make the tied pair's rowid order differ from its id order. The API can't do this: `FakeTimeProvider` can't move backward, and same-ms ids are random. So insert the pair directly with `factory.ExecuteSqlAsync`, **higher id first**. For example, `INSERT INTO Contacts (Id, FirstName, LastName, Email, Phone, Company, Notes, CreatedAt, UpdatedAt) SELECT 'FFFFFFFF-0000-7000-8000-000000000000', … FROM Contacts WHERE Email = '<api-created row>'`, then `'00000000-…'`, using uppercase like EF. Expect the `00000000` row first. I verified this shape in scratch: baseline lists `lo, seed, hi`, and the drop-Id mutant lists `seed, hi, lo`. Do this in both AC022 and AC023 (or at least once per AC). Then confirm that the drop-Id mutant fails and that `ThenByDescending(Id)` still fails. |
| 2 | Nit | test-writer | `ListContactsTests.cs`, comment above `OrderTiedByDbIdOrder` | "Guid.CompareTo orders differently" is inaccurate. .NET compares `_a`/`_b`/`_c` unsigned, then the bytes in string order, which matches ordinal hex order (the probe sample agreed). The OrdinalIgnoreCase helper is correct regardless. | Reword it, for example: "Mirror the DB: ordinal on the stored TEXT id." |

**Notes:**
- Test quality: deterministic (the clock advances, and expected tie order is computed rather than hard-coded). No skips, and the existing tests are unchanged. AC020 still asserts membership only, which is correct.
- Production code needs no change; this is a test-only fix cycle. The implementer's `ThenBy(c => c.Id)` is required behaviour and should stay.

## T-12 re-review (fix cycle 1): 2026-10-01: APPROVE

**Checks:** backend tests pass (90/90, 0 failed, 0 skipped) · `dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exits 0 · web n.a. (unchanged). No production code changed in this cycle.

**Prior findings:**
| # | Status | Evidence |
|---|---|---|
| 1 (Blocking, id tie-break untested) | **Resolved** | `InsertTiedPairAsync` inserts the tied pair through raw SQL, high id (`FFFFFFFF-…`) first, so rowid order is the reverse of id order. AC022 (Pat Dupont) and AC023 (Dana, NULL last name) both expect lo before hi. In my scratch run, the drop-`ThenBy(Id)` mutant fails 2 tests: AC022 (`Expected dupont-lo / Actual dupont-hi`) and AC023 (`Expected dana-lo / Actual dana-hi`). |
| 2 (Nit, Guid.CompareTo comment) | **Resolved** | `OrderTiedByDbIdOrder` and its comment are gone. The new comment ("uppercase TEXT compared ordinally") is accurate. |

**Mutation evidence** (scratch copy; source untouched; baseline 90/90):
| Mutant | Result |
|---|---|
| `ThenBy(Id)` dropped | **2 fail:** AC022, AC023 |
| `ThenByDescending(Id)` | **2 fail:** AC022, AC023 |
| name NOCASE removed (config, migration deleted, snapshot) | **2 fail:** AC022, AC023 (no other failures, so no migration noise) |
| nulls first (`LastName != null`) | **1 fail:** AC023 |
| `ThenBy(FirstName)` dropped | **2 fail:** AC022, AC023 |

**Raw-SQL round-trip:** a scratch probe inserted rows with the test's exact literals. `GET /api/contacts` and `GET /api/contacts/{id}`, for both lowercase ids, return 200 with `id` parsed (`00000000-0000-7000-8000-000000000000`, `ffffffff-…`) and `createdAt`/`updatedAt` = `2026-01-01T00:00:00+00:00`. Stored as `typeof` = `text`, in the same `yyyy-MM-dd HH:mm:ss+00:00` / uppercase-GUID format as an API-created row (`019B7CA9-… | 2026-01-02 03:04:05+00:00`). So the hand-written rows are indistinguishable from EF's own.

**Earlier checks not weakened:** both tests still seed the name rows in reverse of the expected order, with `Time.Advance` between creates. The mixed-case pairs (`adams/alice` vs `Adams/Bob`; `alice`/`Bob`/`carl`) and the NULL-last-name group after named contacts are still asserted with full-sequence `Assert.Equal`. The tied pair goes in first (lowest rowids), so the nulls-first and drop-FirstName mutants still fail on it as well as on the name rows.

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Nit | test-writer | `ListContactsTests.cs`, `SeedAsync` (returns `Dictionary<string, Guid>`, doc comment "Returns email -> id.") | Both callers discard the return value, so the dictionary and its comment are dead code. | Return `Task` and drop the dictionary and the "Returns" line, or leave it if T-13/T-15 will use it. |

**Notes:** the interpolated SQL in `InsertTiedPairAsync` uses only test-local constants, so there's no injection concern.

## T-13: 2026-10-01: APPROVE

**Checks:** backend tests pass (94/94, 0 failed, 0 skipped) · `dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exits 0 · web n.a. (unchanged) · typecheck n.a. (backend only).

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-024 | `ListContactsPagingTests.ListContacts_PageAndPageSize_ReturnsSliceAndEchoesParams_AC024` | yes. 7 rows are seeded out of order, and the test asserts the exact slice `Delta, Echo, Foxtrot`, the `page`/`pageSize` echo, and `totalCount` = 7. It kills Skip+1, Skip−1, `Take(default)` and `totalCount = items.Count`. |
| AC-025 | `ListContacts_PagingThroughAll_ReturnsEachContactOnce_AC025` | yes. It checks that no id is duplicated, that the set of ids equals the seeded set, that each page has at most 3 items and ends short, and that `totalCount` holds on every page. Both the gap mutant (Skip+1) and the overlap mutant (Skip−1) fail it. See finding 2 on its tie-break comment. |
| AC-026 | `ListContacts_PageBeyondLast_ReturnsEmptyItemsWithTotal_AC026` | yes. It covers page 3 of 2 at pageSize 3, and `page=int.MaxValue` with the default pageSize, pageSize 3 and pageSize 100. Every case asserts an empty array and `totalCount` = 5. It kills the int-arithmetic mutant and the ignore-`TryGetSkip` mutant. |
| AC-027 | `ListContacts_PageSize100_ReturnsUpTo100_AC027` | yes. 101 rows are inserted through raw SQL, and it asserts the `pageSize` echo 100, `totalCount` 101 and 100 items. It kills `Take(default)`. The AC says "up to 100", so asserting the count rather than which rows is correct. |

**Mutation evidence** (scratch copy; source untouched; baseline 94/94):
| # | Mutant | Result |
|---|---|---|
| M1 | range checks removed (`&& parsed >= min && parsed <= max`) | 0 fail |
| M1b | min check only removed | 0 fail |
| M1c | max check only removed | 0 fail |
| M2 | plain `int.TryParse(value, out parsed)` (no `NumberStyles.None`/invariant culture) | 0 fail |
| M3 | `TryGetSkip` in int: `(long)((Page - 1) * PageSize)` | **1 fail:** AC026 |
| M4 | `.Skip(skip + 1)` | **5 fail:** AC024, AC025, AC022, AC023, AC039 |
| M5 | `.Skip(skip > 0 ? skip - 1 : 0)` (overlapping pages) | **2 fail:** AC024, AC025 |
| M6 | `ThenBy(Id)` dropped | 2 fail: AC022, AC023 (**AC025 survives**, see finding 2) |
| M7 | `totalCount = items.Count` | **5 fail:** AC020, AC024, AC025, AC026, AC027 |
| M8 | `TryGetSkip` result ignored (always query, skip 0 on overflow) | **1 fail:** AC026 |
| M9 | `.Take(ListQuery.DefaultPageSize)` | **3 fail:** AC024, AC025, AC027 |

**Points the orchestrator asked about:**
1. **Range checks and `NumberStyles.None` aren't demanded by T-13 (M1, M1b, M1c, M2 all survive). This is not Blocking; it's a Nit to carry forward.** Unlike T-04, this isn't a byte-identical no-op. A scratch probe shows observable differences. Under M1, `?page=0` echoes `page: 0`, and `?pageSize=101` echoes 101 and is uncapped. Under M1+M2, `?pageSize=-1` becomes SQLite `LIMIT -1`, which is unlimited, and `?page=%202`/`%2B2` parse as page 2. By the T-11 rule (accept a placeholder if removing it changes anything observable) it's acceptable. Two further reasons it isn't treated as drift:
   (a) tasks.md T-14 "Expected RED" says "invalid values fall back to defaults or are clamped → 200", so the plan anticipates exactly this interim fallback.
   (b) It can't be "fixed" by adding tests. Any T-13 test that pinned the fallback would assert a 200 that AC-028 forbids, and T-14 would have to delete or weaken it, which §2 prohibits. Removing the checks would only make the intermediate state worse, with negative page echoes and an unlimited `pageSize=-1`.
   The plan prescribes the code comment at `Paging.cs:11-12`, and the comment states the interim intent honestly. The T-07 and T-08 precedents were permanent guards that no future task would test; here T-14 is planned to replace and test the code.
2. **T-14 will go red. Confirmed.** Under the current code, every T-14 input returns **200** with defaults: `page` = `0`, `-1`, `abc`, `1.5`, the empty string, or `2147483648`, and `pageSize` = `0`, `101`, or `-1`. Under M1 and under M1+M2 they also all return 200. None produce a 500, because SQLite treats a negative OFFSET as 0. So `ListContacts_InvalidPaging_Returns400WithParamError_AC028` and `ListContacts_BothInvalid_ReportsBoth_AC028` will fail on the status code. The `ListQueryTests.Parse_*` unit tests will fail to compile, because `Parse` must change to the plan's `(Query, Errors)` tuple. `TryGetSkip_HugePage_ReturnsFalse_AC026` will pass at RED, since T-13 already implements it; T-14 should list it as a guard (M3 and M8 prove it guards something real).
3. **`TryGetSkip` in int: covered.** M3 fails AC026 on `page=2147483647`, because the wrapped product yields a valid skip and returns rows.
4. **CountAsync when the skip overflows: correct.** The handler always runs `CountAsync`, and only the row query is skipped. The probe returns `{"items":[],"page":2147483647,"pageSize":1,"totalCount":3}`. With `pageSize=1` the skip is `int.MaxValue - 1`, which fits, so EF sends a huge OFFSET and gets 200 with an empty list. `page=1073741825&pageSize=2` takes the overflow path and also returns 200 with an empty list. The page is always ≥ 1 after `Parse`, so `rows` is never negative.
5. **Recursive-CTE seed: EF reads it.** AC027 materializes 100 entities through `ToListAsync`, which parses the Guid and DateTimeOffset of every row, and `totalCount` is 101. The ids come from `printf('00000000-0000-7000-8000-%012X', i)`: uppercase TEXT, v7 nibble, variant 8. That matches EF's stored format, and the timestamp literal is the same one T-12 verified for round-tripping through list and get-by-id. The SQL is a constant string, so there's no injection surface.
6. **AC025 catches both gaps and duplicates.** M4 (Skip+1) fails AC025 because the first row is never seen. M5 (Skip−1) fails it because of duplicates.

**Minimality grep:** under `src/` (excluding migrations) there's no `WithName|WithSummary|Produces|WithTags`, no `EF.Functions`/`Search`, and no `ILogger`. `ListQuery` has no `Search` member and no error tuple yet (T-14/T-15). The handler binds `string?` as ADR-0004 specifies.

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Nit | test-writer (T-14) | `src/MicroCrm.Api/Common/Paging.cs:25-29` | The range checks and `NumberStyles.None` aren't pinned by any test yet (M1, M1b, M1c and M2 survive). This is accepted for T-13 (see point 1), and no action is needed now. | At T-14, the AC028 cases must kill M1b (`page=0`), M1c (`pageSize=101`) and M2 (`page=-1`, which only parses without `NumberStyles.None`; also consider `+2` or ` 2`). The T-14 reviewer should re-run these mutants. |
| 2 | Nit | test-writer | `ListContactsPagingTests.cs:72` | The comment says the duplicate names mean "the id tie-break must hold across pages". But `SeedAsync` advances the clock, so rowid order equals id order, and dropping `ThenBy(Id)` passes AC025 (M6). The tie-break is actually pinned by the T-12 tests (AC022/AC023), not this one. The AC025 behaviour itself (each contact exactly once) is fully tested. | Reword the comment, for example: "Duplicate names: traversal must still be complete and duplicate-free (the id tie-break itself is pinned by AC022/AC023)". Alternatively, insert some of the tied rows high-id-first via `ExecuteSqlAsync`, as T-12 does. |

**Notes:**
- T-12 Nit resolved: `ListContactsTests.SeedAsync` now returns `Task`, and the dead dictionary and its "Returns" comment are gone. No assertions changed.
- `Parse` currently returns `ListQuery` rather than the plan's `(ListQuery?, Dictionary?)` tuple. That's expected for T-13; T-14 owns the plan signature.
- Test quality: deterministic (clock advanced; raw-SQL ids fixed), with no skips. Each test resets the database through `ResetAsync`.

## T-14: 2026-10-01: APPROVE

**Checks:** backend tests pass (131/131, 0 failed, 0 skipped; scratch baseline matches the orchestrator) · `dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exits 0 · web n.a. (unchanged) · typecheck n.a. (backend only).

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-028 | `ListQueryTests.Parse_InvalidPageOrPageSize_ReturnsNamedError_AC028` (14 rows, asserting exactly one key), `Parse_BothInvalid_ReportsBothKeys_AC028`, `Parse_Absent_UsesDefaults_AC028`, `Parse_ValidBoundaries_AreAccepted_AC028`; `ListContactsPagingTests.ListContacts_InvalidPaging_Returns400WithParamError_AC028` (14 rows), `ListContacts_BothInvalid_ReportsBoth_AC028` | yes. Every AC-028 clause (page < 1, pageSize < 1, pageSize > 100, non-integer) has unit and integration rows. The integration rows go through `ProblemAssert` with an exact key set, so they check status, content type, `type`/`title`/`status` and `errors`. Values are sent through `Uri.EscapeDataString`, so `+1` really arrives as `+1` and isn't turned into a space. |
| AC-037 (list 400) | the same integration tests (through `ProblemAssert`) | yes |
| AC-026 (guard) | `ListQueryTests.TryGetSkip_HugePage_ReturnsFalse_AC026` plus the existing integration test with `page=int.MaxValue` | yes. M10 and M11 each fail both. |

**Mutation evidence** (scratch copy of `Paging.cs`; source untouched, and `diff` confirms it's identical; baseline 131/131):
| # | Mutant | Result |
|---|---|---|
| M1b | min check removed (T-13 Nit 1) | **6 fail:** unit `0` page/pageSize, unit BothInvalid, integration `0` page/pageSize, integration BothInvalid (`-1` is rejected by `NumberStyles.None` regardless) |
| M1c | max check removed (T-13 Nit 1) | **4 fail:** unit and integration `pageSize=101`, plus both BothInvalid tests |
| M2 | plain `int.TryParse(value, out parsed)` (T-13 Nit 1) | **8 fail:** `" 1"` and `"+1"` for page and pageSize, in both unit and integration |
| M4 | first error only (pageSize not parsed once page has failed) | **2 fail:** unit and integration BothInvalid |
| M5 | PascalCase key (`Page`/`PageSize`) | **30 fail:** every invalid row. The framework writes `errors` keys exactly as given, and `ProblemAssert` compares ordinally. |
| M6 | `""` treated as absent (`string.IsNullOrEmpty`) | **2 fail:** unit and integration `page=""` |
| M7 | `NumberStyles.Integer` | **8 fail:** the same rows as M2 |
| M8 | whitespace treated as absent (`IsNullOrWhiteSpace`) | **2 fail:** via the `""` rows |
| M9 | `CultureInfo.CurrentCulture` | 0 fail. **This is an equivalent mutant:** with `NumberStyles.None`, only ASCII digits are accepted, so culture can't change the result. |
| M10 | `TryGetSkip` computed in int | **2 fail:** unit HugePage, integration AC026 |
| M11 | `TryGetSkip` always returns true | **2 fail:** the same pair |
| M12 | `rows < int.MaxValue` (off by one) | 0 fail. **This is an equivalent mutant:** 2147483647 is prime, so `(page-1)*pageSize == int.MaxValue` is unreachable with pageSize ≤ 100. |

**Points the orchestrator asked about:**
1. **The T-13 Nit 1 mutants are now killed.** M1b, M1c and M2 each fail both unit and integration tests (see the table). The T-13 carry-forward is closed.
2. **The additional mutants are killed:** first error only (M4), wrong key casing (M5), `""` treated as absent (M6) and `NumberStyles.Integer` (M7). The only survivors (M9, M12) are equivalent.
3. **`""` binding is correct.** I ran a scratch probe (`ZzProbeTests`, raw bodies). With no query string the response is 200 `{"items":[],"page":1,"pageSize":20,…}`. `?page=` and `?pageSize=` each return 400 with only that key, and so does `?page` (no `=`), because minimal APIs bind it as `""`, not null. `?page=%20%20` returns 400. So "absent" really means "not in the query string", and present-but-empty is invalid, as AC-028 and the plan require.
4. **The shape matches create's exactly.** List 400 body: `{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"page":[…],"pageSize":[…]},"traceId":…}` with `application/problem+json`. Create 400 has the same members in the same order, with the same type, title and content type. Both handlers use the same `var (x, errors) = Parse(...); if (x is null) return TypedResults.ValidationProblem(errors!);` pattern, which matches the T-06 precedent (the tuple has no flow annotations, so the one `!` is accepted).
5. **Minimality is clean.** `Parse` has the plan's tuple shape without `search`, which is correct because search belongs to T-15/T-16. The drift grep (`WithName|WithSummary|Produces|WithTags|EF.Functions|Search|ILogger|Like`) under `src/`, excluding migrations, finds nothing. There's no `ContainsKey`/precedence guard: each key has only one rule, so there's a single message. The handler only adds the `Results<…, ValidationProblem>` return type and the early return. The `Paging.cs:11-12` comment now describes final behaviour rather than an interim placeholder.

**Other observations (probe):** `?Page=0` binds case-insensitively and reports key `page`. `?page=1&page=2` returns 400 (the string binder joins repeated values as `"1,2"`), which is a reasonable rejection. `?page=0001` returns 200 with page 1. A full-width digit (`%EF%BC%91`) returns 400. Error messages contain only the parameter name and the range, never the submitted value, so nothing is reflected back.

**T-13 carry-forward:**
| # | Status | Evidence |
|---|---|---|
| T-13 Nit 1 (range checks / NumberStyles unpinned) | **Resolved** | M1b, M1c and M2 are killed (see above). |
| T-13 Nit 2 (AC025 tie-break comment) | **Resolved** | `ListContactsPagingTests.cs:72-73` now says the tie-break is pinned by AC022/AC023 and can't fail here. |

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Nit | test-writer | `ListQueryTests.cs:10-23`, `ListContactsPagingTests.cs:149-162` | The empty-string row only exists for `page`. `pageSize=""` and a whitespace-only value (`"  "`) have no rows. Today `ParseValue` is shared, so no realistic mutant survives (M6 and M8 are killed via `page=""`). The gap would only matter if the two parameters' parsing were ever split. | Optionally add `(null, "", "pageSize")` and `("  ", null, "page")` rows. Not required. |

**Notes:**
- Test quality: deterministic, with no skips, and no existing assertion was changed or weakened. The unit invalid theory asserts the **exact** key set, so a mutant that also reports the other parameter would fail. The `ValidBoundaries` theory pins 1, 100 and `int.MaxValue` as accepted, so an over-tight range (for example `< max`) would fail it.
- RED was valid (the orchestrator's evidence): the unit file failed to compile only because of the planned signature change, and the integration rows failed with `Expected 400 / Actual 200`, which is what tasks.md's "Expected RED" predicts.

## T-15: 2026-10-01: CHANGES_REQUESTED

**Checks:** backend tests pass (147/147, 0 failed, 0 skipped; scratch baseline matches the orchestrator) · `dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exits 0 · web n.a. (unchanged) · typecheck n.a. (backend only).

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-029 | `SearchContactsTests.ListContacts_Search_MatchesAnyFieldIgnoringCase_AC029` (4 rows: first, last, email-only, prefix; all different case from the data), `ListContacts_SearchHitsInDifferentFields_ReturnsAllWithMatchingTotal_AC029` | **partly.** Each field's `Like` is pinned and `totalCount` is asserted, but every seeded contact has a last name AND an email. Contacts with an absent optional field are never searched (finding 1). |
| AC-030 | `ListContacts_SearchAcrossFirstAndLast_DoesNotMatch_AC030` | yes. The preconditions (`Ada` and `Love` each hit) make the empty result non-vacuous. |
| AC-031 | `ListContacts_BlankSearch_SameAsNoSearch_AC031` (`""`, `%20%20`, `%09%20`; guard) | yes. It compares the whole body with the no-search body, plus `totalCount` 4. |
| AC-032 | `ListContacts_SearchWithSurroundingWhitespace_UsesTrimmedTerm_AC032` | yes |
| AC-035 | `ListContacts_SearchNoMatches_ReturnsEmptyAndZero_AC035` | yes. It asserts an array kind, length 0 and `totalCount` 0. |
| AC-036 | `ListContacts_SearchWithPaging_FiltersThenSortsThenPages_AC036` | yes. The seed is unsorted with non-matches interleaved, so page 2 differs under every wrong order. The Amy/Zed Smith tie checks the first-name key inside the filtered set. |

**Mutation evidence** (scratch copy; source untouched, and `diff` confirms it's identical; baseline 147/147):
| # | Mutant | Result |
|---|---|---|
| M1 | FirstName `Like` removed | **1 fail:** AC029 row `gRaCe` |
| M2 | LastName `Like` removed | **6 fail:** AC029 (3 rows), AC029 different-fields, AC030 precondition, AC032, AC036 |
| M3 | Email `Like` removed | **2 fail:** AC029 row `COMPUTING`, AC029 different-fields |
| M4 | `totalCount` from unfiltered `db.Contacts` (count before filter) | **9 fail**, including AC035 and AC036 |
| M13 | filter applied in memory after paging (count unfiltered too) | **9 fail** |
| M13b | count correct, but paging over unfiltered rows, then filtering the page | **1 fail:** AC036 (the page holds Goldsmith only) |
| M6 | no trim | **3 fail:** AC032, AC031 rows `%20%20` and `%09%20` |
| M11 | `Trim(' ')` instead of `Trim()` | **1 fail:** AC031 row `%09%20` (char.IsWhiteSpace consistency, follow-up (c) from T-05, is enforced) |
| M7 | blank not turned into null (`""` passed through as a term) | 0 fail. **Equivalent at HTTP level:** the pattern is `LIKE '%%'`, and `FirstName` is NOT NULL, so every row matches and the body is byte-identical (finding 3). |
| M7b | whitespace checked before trimming (`"  "` → `""` term) | 0 fail. Equivalent for the same reason. |
| M8 | `string.Contains` (translates to case-sensitive `instr`) | **4 fail:** the 3 mixed/upper-case AC029 rows and AC029 different-fields (`HOP`) |
| M9 | `Like(FirstName + " " + LastName)` replaces the FirstName branch | **1 fail:** AC030 |
| M12 | prefix pattern `term%` | **4 fail** |
| M10 | `c.LastName != null` / `c.Email != null` guards removed (with `!`) | 0 fail. **Equivalent** (see point 1). |
| M10b | guards removed, no `!` | compiles, 0 fail. `EF.Functions.Like` takes `string?`, so the guards aren't needed for nullability either. |
| M15 | `c.LastName != null && c.Email != null && (any Like)`: contacts missing an optional field are never returned | **0 fail.** This is a real behaviour break that nobody catches (finding 1). |

**Points the orchestrator asked about:**
1. **Null guards: they're equivalent, so no test can need them.** In SQL, `NULL LIKE p` is NULL. `NULL OR TRUE` is TRUE, and a NULL `WHERE` row is excluded, so `x IS NOT NULL AND x LIKE p` selects exactly the same rows as `x LIKE p` (M10 and M10b, 0 fail). They also aren't needed for compilation (M10b). And they diverge from the plan's prescribed form (`plan.md` design point 2: `EF.Functions.Like(c.LastName!, pattern, "\\")`). This is the same category as T-07 (unreachable `ContainsKey`) and T-08 (reachable but unassertable precedence guard): code that no test can ever turn red. It isn't a T-04-style unrequested block, but it's still dead logic that adds `IS NOT NULL AND` to the SQL. **Rated Should-fix (implementer), consistent with T-07 and T-08.** The easiest time to fix it is T-16, which rewrites these exact lines to add the `ESCAPE` argument. Note that removing the guards doesn't close finding 1, which is a test gap whatever form the filter takes.
2. **`search = null` defaults: acceptable, as a Nit.** The plan signatures are `ListQuery(int Page, int PageSize, string? Search)` and `Parse(string? page, string? pageSize, string? search)`, with no defaults. The implementer can't edit `ListQueryTests`, so it used defaults to keep the existing calls compiling, which is a legitimate constraint. The risk is a call site silently omitting `search`. But the only production caller passes it, and if it were dropped every search integration test would fail (the RED run shows exactly that). So this isn't Blocking. At T-16, the test-writer extends `ListQueryTests` with `search` arguments anyway. That's the natural point to pass `search` explicitly everywhere and drop the defaults (finding 4).
3. **Minimality: clean.** There's no `LikePattern`, `ESCAPE`, or search length constant (the only 254 is `ContactInput.EmailMax`). The drift grep (`WithName|WithSummary|Produces|WithTags|ILogger|ESCAPE|LikePattern`) under `src/`, excluding migrations, is empty. The filter sits before `CountAsync`, then order, then `Skip`/`Take`, as plan design point 2 says. The `""`/absent split for page and pageSize still holds: the `page=""`, `pageSize=""` and `page="  "` rows still return 400, so normalizing `search` didn't leak into `ParseValue`.
4. **T-16 can still go red. Confirmed with sqlite3 against the current pattern (no ESCAPE):** a `%` term gives `LIKE '%%%'`, which matches all 6 rows of the plan's AC-033 data. A `_` term gives `'%_%'`, which also matches all 6. A 255-character search returns 200. `LikePatternTests` won't compile, since there's no `LikePattern` yet. `Parse_SearchOver254AfterTrim_ReturnsSearchError_AC034` fails because `Parse` returns a query. One caveat for the T-16 test-writer: **a `\` term passes at RED.** Without `ESCAPE`, SQLite treats `\` literally, so `'%\%'` matches only `c\d`. So the backslash integration row is a guard until `ESCAPE '\'` is added. After that it's real, because `'%\%' ESCAPE '\'` matches nothing. Likewise, `Parse_Search254WithWhitespace_IsValid_AC034` passes at RED. Both should be labelled guards in T-16's RED report.

**T-14 carry-forward:**
| # | Status | Evidence |
|---|---|---|
| T-14 Nit 1 (no `pageSize=""` or whitespace rows) | **Resolved** | `ListQueryTests.cs:24-25` adds `(null, "", "pageSize")` and `("  ", null, "page")`; `ListContactsPagingTests.cs:163-164` adds the matching integration rows. All pass. The comment in `Paging.cs` ("unlike a blank page/pageSize") documents the deliberate asymmetry with AC-031. |

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | **Blocking** | test-writer | `SearchContactsTests.cs:47-53` (`SeedFamousAsync`), `:55-67` | Every searched contact has both `lastName` and `email`. Both are optional (AC-006), so contacts without them are normal data, yet AC-029's "first name, last name, **or** email" is never checked against them. Mutant M15 (`c.LastName != null && c.Email != null && (...)`) silently drops every contact missing an optional field from search results, and all 147 tests pass. The same blind spot would hide swapped guards (`c.Email != null && Like(c.LastName)`). | Seed contacts with absent fields and add AC029 rows that hit them. For example, `("Margaret", "Hamilton", null)` hit by `HAMIL` (last name, no email), `("Katherine", null, "kj@nasa.test")` hit by `NASA` (email, no last name), and `("Mary", null, null)` hit by `mary` (first name only). Each must return exactly that contact with `totalCount` 1. The seed helper needs nullable `Last`/`Email`, and the name projection must cope with a null last name. M15 and the swapped-guard mutant must each fail at least one row. Check that the existing rows stay single-hit (for example, `gRaCe` and `Torv` mustn't also hit the new seeds). |
| 2 | Should-fix | implementer | `ContactsEndpoints.cs:41-42` | The `c.LastName != null &&` and `c.Email != null &&` guards are equivalent mutants (M10 and M10b: 0 fail, and the code compiles without `!`). They're dead logic that diverges from plan design point 2. Same category as T-07 and T-08 (point 1). | Use the plan form `EF.Functions.Like(c.LastName, pattern)` / `EF.Functions.Like(c.Email, pattern)` (no `!` is needed). Do it at T-16 when adding `ESCAPE`, after finding 1's tests exist so the change is protected. |
| 3 | Nit | test-writer | `Paging.cs:26-30` (`NormalizeSearch`) | The `blank → null` branch is prescribed by the plan and tasks.md, so it isn't drift. But it's only observable through a coincidence: `LIKE '%%'` matches every row because `FirstName` is NOT NULL (M7 and M7b survive). Nothing pins the `ListQuery.Search` contract directly. | At T-16, while extending `ListQueryTests`, add `Parse(null, null, "")`, `"  "` and `"\t "` → `Search` is null, and `" ada "` → `Search == "ada"`. That kills M7 and M7b at the unit level. |
| 4 | Nit | implementer (after test-writer) | `Paging.cs:7, 14` | `string? Search = null` and `string? search = null` defaults diverge from the plan signatures (point 2). | At T-16, the test-writer passes `search` explicitly in `ListQueryTests`. Then the implementer drops both defaults. |

**Notes:**
- RED was valid (the orchestrator's evidence): 9 search tests failed because all 4 or 10 contacts came back, which is tasks.md's "Expected RED". The AC031 guard rows passed, as planned. The guard comment in the test (`SearchContactsTests.cs:100-102`) states honestly what it protects.
- Test quality: deterministic (the clock advances between creates; the AC036 order doesn't depend on ids, because there are no full name ties). There are no skips, and no existing assertion was changed or weakened. Terms reach the server through literal `%20`/`%09` escapes, so the whitespace tests really send whitespace.
- Trimming uses `string.Trim()`, and M11 confirms it's enforced. Follow-up (c) from T-05 is **closed**.

## T-15 re-review (fix cycle 1): 2026-10-01: APPROVE

**Checks:** backend tests pass (150/150, 0 failed, 0 skipped; the scratch baseline matches the orchestrator) · `dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exits 0 · web n.a. · typecheck n.a. No production code changed: `ContactsEndpoints.cs:36-43` is the same as at the first T-15 review.

**AC coverage:** the same as the T-15 table, except that AC-029 is now **yes**. Seven `MatchesAnyFieldIgnoringCase_AC029` rows include `HAMIL` (last name, email null), `NASA` (email, last name null) and `mary` (first name, both optional fields null). Each must return exactly one contact with `totalCount` 1.

**Mutation evidence** (scratch copy; the restored source is `diff`-identical to the repo):
| # | Mutant | Result |
|---|---|---|
| M15 | `c.LastName != null && c.Email != null && (any Like)` | **3 fail:** AC029 rows `HAMIL`, `NASA`, `mary` |
| M16 | both guards swapped (`c.Email != null && Like(LastName)`, `c.LastName != null && Like(Email)`) | **2 fail:** AC029 rows `HAMIL`, `NASA`. The test-writer's 1 fail was for swapping only the LastName guard, which is consistent. |
| M10b | both `!= null` guards removed | 0 fail (still equivalent, as expected). This confirms the T-16 guard removal is now protected by tests that would catch a broken replacement. |

**Points the orchestrator asked about:**
1. **Finding 1 is resolved.** Both M15 and the swapped-guard mutant are killed, and the seed helper takes nullable `Last` and `Email`. The projection maps a null `lastName` to `"(none)"`, which can't collide with real data.
2. **The AC031 change from 4 to 7 is a legitimate seed-count update, not a weakening.** `SeedFamousAsync` now creates 7 contacts, and the test still asserts that the whole body is byte-equal to the no-search baseline, so it's actually stronger: the baseline now includes null-field rows. The 7 is an exact equality, not a range.
3. **The existing rows are still single-hit.** I checked each term against all 7 seeds by hand, and the test run agrees. `LOVE`, `gRaCe`, `COMPUTING`, `Torv` and `lace` each hit only their original contact. `HOP` still hits only Hopper and linus@hop.test, giving 2. `Ada` and `Love` (the AC030 preconditions) still hit 1 each. `Ada Love` and `zzzz` still hit 0. The AC036 test uses its own seed, so it's unaffected.

**Findings:** none blocking. The T-15 findings 2 (remove the `!= null` guards, Should-fix, implementer), 3 (NormalizeSearch unit rows, Nit, test-writer) and 4 (drop the `= null` defaults, Nit, implementer after test-writer) are **deferred to T-16** as agreed, and they stay open there.

**Notes:** the T-16 RED caveats from the T-15 entry still apply: the `\` integration row and `Parse_Search254WithWhitespace_IsValid_AC034` are guards at RED.

## T-16: 2026-10-01: APPROVE

**Checks:** backend tests pass (169/169, 0 failed, 0 skipped; the scratch baseline matches the orchestrator) · `dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exits 0 · web n.a. (unchanged) · typecheck n.a. (backend only). Every scratch file was restored and is `diff`-identical to the repo.

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-033 | `LikePatternTests.Contains_EscapesBackslashPercentUnderscore_AC033` (8 rows), `SearchContactsTests.ListContacts_SearchWithPercentUnderscoreBackslash_MatchesLiterally_AC033` (`%25`, `_`, `%5C`; a literal holder in each of the three fields, plus 2 decoys) | yes. Every escape step, the escape order, the ESCAPE argument on **each** field, and the escape character itself are killed (see below). Each row asserts exactly one name and `totalCount` 1. |
| AC-034 | `ListQueryTests.Parse_SearchOver254AfterTrim_ReturnsSearchError_AC034`, `Parse_Search254WithWhitespace_IsValid_AC034` (guard), `SearchContactsTests.ListContacts_SearchTooLong_Returns400WithSearchError_AC034` (254 gives 200, 255 gives 400) | yes. Both off-by-one directions and the before-trim mutant are killed. `ProblemAssert` checks that the error keys are exactly `{search}`. The error combined with a page error isn't pinned (finding 1). |

**Mutation evidence** (scratch copy; baseline 169/169):
| # | Mutant | Result |
|---|---|---|
| E1 | `%` escaped before `\` | **5 fail:** integration `%25`; unit `50%`, `%_`, `\%`, `50%_a\b` |
| E2 | `_` escaped before `\` | **4 fail** (unit `a_b`, `%_`, `50%_a\b`; integration `_`) |
| E3 | `\` not escaped | **5 fail** |
| E4 | `%` not escaped | **5 fail** |
| E5 | `_` not escaped | **4 fail:** integration `_`; unit `a_b`, `%_`, `50%_a\b` |
| E6 | ESCAPE argument dropped on all three fields | **3 fail:** `%25`, `_`, **and the `%5C` guard**. The guard catches this mutant: without ESCAPE, `'%\\%'` only matches a literal double backslash, which I confirmed with sqlite3. |
| E6a/b/c | ESCAPE dropped on FirstName / LastName / Email only | **1 / 3 / 1 fail** (`%25` / all three rows / `_`). Each field's argument is pinned by the field holding its literal. |
| E6d | ESCAPE kept on FirstName only | **3 fail** |
| E7 | `EscapeChar = "!"` | **3 fail** |
| E8 | no `%` wrap at the start (prefix match) | **16 fail** |
| L1 | `>= 254` | **2 fail:** unit 254 guard, integration 254 gives 200 |
| L2 | max 255 | **2 fail:** unit over-254, integration 255 gives 400 |
| L3 | max 253 | **2 fail** |
| L4 | length measured on the raw `search` (before trimming) | **1 fail:** `Parse_Search254WithWhitespace_IsValid_AC034` (it's the only test that pins this) |
| L5 | search error returned on its own (page errors dropped) | **0 fail** (finding 1) |
| L6 | early return on page errors before the search check | **0 fail** (finding 1) |
| M7 | blank not turned into null (T-15) | **3 fail** (unit `""`, `"  "`, `"\t "`) |
| M7b | blank checked before trimming (T-15) | **2 fail** (unit `"  "`, `"\t "`) |
| M6 | no trim | **7 fail** |
| B1 | `!` removed from `c.LastName!`, `c.Email!` | compiles with **0 warnings** under `TreatWarningsAsErrors`; 0 fail |

**Points the orchestrator asked about:**
1. **The `!` operators aren't needed.** The 2- and 3-argument `EF.Functions.Like` overloads both take a nullable `matchExpression`, and B1 builds warning-free with nullable enabled and warnings as errors. They have no effect at runtime either (they're erased in the expression tree, so the SQL is identical). But `plan.md` design point 2 prescribes exactly `EF.Functions.Like(c.LastName!, pattern, "\\")`, so the code follows the plan. It's a Nit with no action required (finding 2). My T-15 expected fix ("no `!` is needed") was a statement of fact, not a requirement.
2. **T-15 carry-forward:**
   | # | Status | Evidence |
   |---|---|---|
   | 2 (remove `!= null` guards) | **Closed** | `ContactsEndpoints.cs:39-42` uses the plan form. A grep for `!= null` under `src/` (excluding migrations) is empty. The T-15 M15/M16 tests still protect the null-field paths (the suite is green with the guards gone). |
   | 3 (NormalizeSearch unit rows) | **Closed** | `ListQueryTests.Parse_BlankOrAbsentSearch_YieldsNullSearch_AC031` (`""`, `"  "`, `"\t "`, null) and `Parse_SearchWithSurroundingSpaces_IsTrimmed_AC032`. M7 and M7b, which survived at T-15, are now killed at unit level. `NormalizeSearch` was inlined into `Parse` (a refactor; the behaviour is unchanged). |
   | 4 (drop the `= null` defaults) | **Closed** | `Paging.cs:7, 13-14` have no defaults, and every `ListQueryTests` call passes `search` explicitly (including `new ListQuery(int.MaxValue, 100, null)`). The handler's own `string? search = null` binding parameter predates this task and is the minimal-API way to mark it optional. It's out of scope. |
3. **Search errors are correct, as shown by a probe in scratch.** A 255-character search on its own gives `errors: {search}` only. With `page=0` it gives `{page, search}`, and with `page=0&pageSize=101` it gives `{page, pageSize, search}`. The message (`'search' must be at most 254 characters.`) doesn't echo the term. A whitespace-padded 254-character term gives 200. Adding `search` didn't break the `""`/absent split for page and pageSize: the `""` and `"  "` rows still give 400. The combination is correct, but **no test pins it** (L5 and L6 survive), so finding 1.
4. **Minimality: clean.** `LikePattern` (escape plus wrap), `MaxSearchLength`, and the ESCAPE argument are exactly what the failing tests demand. Nothing beyond the plan. A grep for `WithName|WithSummary|Produces|WithTags|ILogger` under `src/` is empty, so nothing from T-17 crept in. The escape order and form match ADR-0003 and plan design point 2.

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Should-fix | test-writer | `ListQueryTests.cs` (search tests) | Today a request with both an over-long search and an invalid page correctly names both. But nothing pins that, and two natural regressions survive: L5 (search error returned on its own) and L6 (early return on page errors). Under L6, `?page=0&search=<255>` answers without `search`, which breaks AC-034's "naming `search`". It isn't Blocking, because AC-034 doesn't spell out the combined case the way AC-008 does, and the code is correct. This is the same pattern as `Parse_BothInvalid_ReportsBothKeys_AC028`. | Add `Parse_SearchTooLongAndPageInvalid_ReportsBothKeys_AC034`: `ListQuery.Parse("0", null, new string('a', 255))`, which gives a null query and keys `{page, search}` (sorted ordinally). Both L5 and L6 must fail it. |
| 2 | Nit | implementer | `ContactsEndpoints.cs:41-42` | The `c.LastName!` and `c.Email!` operators are unnecessary (B1), but they match `plan.md` design point 2 word for word. | None required. Drop them only if the plan is touched anyway. |

**Notes:**
- RED was valid (the orchestrator's evidence): the unit file failed with CS0103 (`LikePattern`). In a scratch copy without it, exactly 4 tests failed for the right reasons: `%25` and `_` matched the decoys, and the 255 unit and integration tests got a query/200. This matches tasks.md's "Expected RED". Both guards (the `%5C` row and `Parse_Search254WithWhitespace_IsValid_AC034`) are labelled honestly in comments, and each one now kills a real mutant (E6 and L1/L4 respectively), as the T-15 caveat expected.
- Test quality: the seeds for each AC033 row are disjoint (the literal holders are spread across first name, last name and email; the decoys contain no `%`, `_` or `\`), and each row asserts the exact single result. That makes the per-field ESCAPE mutants observable. The tests are deterministic (the clock advances between creates), there are no skips, and no existing assertion was weakened. The unit `\\` row (`%\\\\%`) and the `\%` row pin the backslash-first order on their own.
- `MaxSearchLength` counts UTF-16 code units (`string.Length`), the same as `ContactInput`'s field limits. That's consistent, and the spec doesn't distinguish, so it's not raised as a finding.

## T-17: 2026-10-01: APPROVE

**Checks:** backend tests pass (175/175, 0 failed, 0 skipped; the scratch baseline matches the orchestrator) · `dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exits 0 · web n.a. (unchanged) · typecheck n.a. (backend only). The scratch copy was restored `diff`-identical, then deleted.

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| NFR-001 | `OpenApiTests`: `OpenApi_ContactsEndpoints_HaveNameSummaryAndStatusCodes_NFR001` (operationIds), `OpenApi_ContactsEndpoints_HaveNonEmptySummaries_NFR001`, `OpenApi_CreateContact_DocumentsStatusCodes201_400_409_NFR001`, `OpenApi_GetContactById_DocumentsStatusCodes200_404_NFR001` (guard), `OpenApi_ListContacts_DocumentsStatusCodes200_400_NFR001` (guard) | yes. Each test fetches the real `/openapi/v1.json` from the Development host. Every metadata call and every response-type source is killed by at least one test (see below). The test names are covered under finding 1. |

**Generated document** (dumped by a scratch probe): POST `/api/contacts`: `CreateContact`, "Create a contact", responses 201/400/409 (400 and 409 as `application/problem+json`). GET `/api/contacts`: `ListContacts`, responses 200/400. GET `/api/contacts/{id}`: `GetContactById`, responses 200/404. All three are tagged `ContactsEndpoints`, the framework default.

**Mutation evidence** (scratch copy, `ContactsEndpoints.cs` only; each mutant is killed by exactly the intended test unless noted):
| # | Mutant | Result |
|---|---|---|
| N1/N2/N3 | `WithName` removed from Create / List / GetById | **1 fail each** (operationId test) |
| N4 | `WithName("GetContact")` (wrong id) | **1 fail** |
| N5 | `WithName("createContact")` (wrong case) | **1 fail** (`Assert.Equal` is ordinal) |
| S1/S2/S3 | `WithSummary` removed from Create / List / GetById | **1 fail each** (summaries test) |
| S4 | `WithSummary(" ")` | **1 fail** (`IsNullOrWhiteSpace`) |
| P1 | `.ProducesProblem(409)` removed | **1 fail** (Create codes) |
| P2 | `.ProducesProblem(422)` instead of 409 | **1 fail** |
| G1 | GetById returns `Task<IResult>` (all response metadata lost) | **1 fail** (GetById guard: 404 and 200 missing) |
| G2 | GetById returns `Results<Ok<…>, ProblemHttpResult>` (`TypedResults.Problem(404)`; runtime 404 unchanged) | **1 fail** (GetById guard). This is the realistic "404 drops out of the document" regression. |
| G3 | List returns `Task<IResult>` | **1 fail** (List guard) |
| G4 | Create returns `Task<IResult>` | **1 fail** (Create codes) |
| G5 | Create drops `ValidationProblem` from `Results<>` (400 via `Problem`) | **14 fail**, including the Create codes test. The POST 400 in the document comes only from `ValidationProblem` in the return type. The framework adds no 400 for body binding, so `.ProducesValidationProblem()` isn't needed. |

**Points the orchestrator asked about:**
1. **Mutation:** every `WithName`, every `WithSummary`, and the `ProducesProblem` is killed when removed, and so are a wrong id, a wrong-case id, a blank summary and a wrong status code (table above).
2. **The first test's name is misleading** (finding 1). It says "NameSummaryAndStatusCodes" but checks only operationIds. Splitting the checks into five tests is better than tasks.md's single test, because a failure points at one concern. But the name is what shows in failure output and in the traceability table, so a comment inside the method doesn't fix it. I recommend a **rename** to `OpenApi_ContactsEndpoints_HaveOperationIds_NFR001` (keeping the `_NFR001` suffix) over a comment. tasks.md's test list is a plan, not a contract, and renaming a test changes no assertion (constitution §2 is not engaged). Nit, because NFR-001 coverage is complete either way.
3. **The guards are real.** They passed at RED because `TypedResults` union return types (`Results<Ok<T>, NotFound>`, `Results<Ok<…>, ValidationProblem>`) already emit response metadata. G1/G2 (GetById) and G3 (List) show that a return-type regression which drops 404 or 400 from the document fails the matching guard, even when the runtime behaviour is unchanged (G2).
4. **Minimality is clean.** A grep for `WithTags|WithDescription|Produces|WithOpenApi|ILogger|EnableSensitiveDataLogging` under `src/` (excluding migrations) finds only the `ProducesProblem(409)` line. tasks.md's "Likely source files" listed `.ProducesValidationProblem()` and `.WithTags("Contacts")`, but no test demands either: G5 shows 400 is already documented, and NFR-001 says nothing about tags. Leaving them out is correct under the minimal-implementation rule. The `ProducesProblem(409)` matches ADR-0004 ("OpenAPI metadata is declared with `.ProducesProblem(409)`") and `docs/conventions.md` (`.WithName()` + `.WithSummary()`).

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Nit | test-writer | `tests/MicroCrm.Api.Tests/Integration/OpenApiTests.cs:51` | `OpenApi_ContactsEndpoints_HaveNameSummaryAndStatusCodes_NFR001` asserts only the operationIds; summaries and codes live in four sibling tests. The name overstates what fails when it fails. | Rename to `OpenApi_ContactsEndpoints_HaveOperationIds_NFR001`. When tasks.md is next touched, update the NFR-001 traceability row (`tasks.md:349`) to list all five OpenApiTests (it lists only this one today, which is incomplete regardless of the rename). |
| 2 | Nit | test-writer | `OpenApiTests.cs:16` | `JsonDocument.Parse(json)` is never disposed (the root is `Clone()`d, so this only returns a pooled buffer late). | `using var doc = JsonDocument.Parse(json); return doc.RootElement.Clone();`. Optional. |

**Notes:**
- RED was valid (the orchestrator's evidence): 3 failed for the expected reasons (operationId null, no POST summary, no POST 409), which matches tasks.md "Expected RED". The two guards were honestly reported, and each now kills a real mutant.
- NFR-004: no logging was added in this task.
- The test is deterministic (no data, no clock) and shares an `ApiFactory` per class. There are no skips, and no existing test was modified.
- **Doc vs runtime (follow-up, not a finding):** the documented 404 for GetById has no content type (`TypedResults.NotFound()` carries no body metadata), while at runtime status code pages writes an `application/problem+json` body (AC-018). NFR-001 asks only for status codes, so it's conforming. If clients will be generated from the document, consider `.ProducesProblem(404)` in a later spec. The default tag `ContactsEndpoints` (from the class name) is cosmetic. A `Contacts` tag would need a test first.

**For the FINAL review:**
- The traceability table in `tasks.md` must name tests that exist, under their current names: the NFR-001 row (finding 1), and every AC row after the T-04..T-16 renames and splits. Grep each name against `tests/`.
- T-04..T-17 are approved but **uncommitted** (`COMMIT_PER_TASK=0`). Make sure the final commits are split per task with Conventional Commits `feat(001): … [T-XX]`, or that a single commit is explicitly accepted. Also check that unrelated working-tree changes (`.claude/harness.env`, `docs/adr/0002-…`) are intended and reviewed.
- T-18 (NFR-003 manual performance check) is still open, and its timings must be recorded in this file before the spec is `Done`.
- The `/document` step: `docs/architecture.md` (API host is still "scaffolded"; add the Contacts feature, Data, Common, and migrations), CHANGELOG, and the ADR-0003/0004/0005 status (Proposed → Accepted). Check the `Microsoft.AspNetCore.OpenApi` 10.0.0 vs test 10.0.12 version-drift follow-up from plan.md.
- The T-16 Should-fix (combined search+page error) appears closed: `ListQueryTests.Parse_SearchTooLongAndPageInvalid_ReportsBothKeys_AC034` exists. FINAL should confirm that the T-16 entry is marked resolved.
- `docs/.claude/` (agent memory) is untracked inside `docs/`. Decide whether it belongs in version control at that path.

## T-18: NFR-003 manual performance check (2026-10-01): PASS

Recorded by the orchestrator, following plan.md → Rollout. This is a manual check, not a CI gate.

- **Machine:** Apple M1 Pro, 32 GB RAM, macOS 26.6.2, .NET SDK 10.0.100.
- **Code:** HEAD `1d1eba0` plus the uncommitted T-04..T-17 working tree, which was green at 175/175.
- **Setup:**
  - The API ran through `dotnet run --project src/MicroCrm.Api` (Development, Information-level EF logging on) on port 5099.
  - `ConnectionStrings__MicroCrm` pointed at a scratch SQLite file DB, so the repo's `microcrm.db` was not touched. Migrations were applied at startup.
  - 10,000 contacts were seeded with one `sqlite3` recursive-CTE insert:
    - varied first and last names;
    - 1,000 contacts with a NULL last name;
    - unique emails.
- **Method:** two warm-up requests, then 5 timed runs per query with `curl -w %{time_total}`. The table reports the median.

| Request | Matches (`totalCount`) | Median | Runs (s) |
|---|---|---|---|
| `?pageSize=100` | 10,000 | 3.8 ms | 0.0036–0.0040 |
| `?pageSize=100&search=a` (every row matches) | 10,000 | 5.1 ms | 0.0050–0.0052 |
| `?pageSize=100&search=hopper` (selective) | 1,286 | 5.2 ms | 0.0048–0.0053 |
| `?pageSize=100&search=zzzz` (full scan, no match) | 0 | 3.5 ms | 0.0034–0.0036 |
| `?page=50&pageSize=100` (deep page) | 10,000 | 10.6 ms | 0.0101–0.0108 |
| `?page=50&pageSize=100&search=a` | 10,000 | 11.2 ms | 0.0111–0.0120 |

**Result:** every median is under 500 ms by more than 40×. NFR-003 is met and no follow-up task is needed. The search scans the whole table (leading-wildcard LIKE), as plan.md expects; revisit with FTS only if the data grows well beyond 10k.

## FINAL: 2026-10-01: APPROVE

**Checks (re-run independently by the reviewer):** `dotnet build MicroCrm.slnx` 0 warnings, 0 errors · backend 175/175 (0 failed, 0 skipped) · web 1/1 · `dotnet format --verify-no-changes` exit 0 · oxlint exit 0 · `tsc -b` exit 0 · `dotnet ef migrations has-pending-model-changes` reports "No changes have been made to the model since the last migration" · there are no `Skip`/`Explicit`/`#if false` markers anywhere under `tests/`.

**Scope:** all hand-written files under `src/MicroCrm.Api/`, the three migrations plus the snapshot, all of `tests/MicroCrm.Api.Tests/`, and spec/plan/tasks/ADR-0003..0005. Excluded as instructed: `docs/adr/0002-…` (the user's own change) and the planner's `MIGRATIONS_ADD_CMD` line in `.claude/harness.env` (seen by the user at plan time).

### 1. Traceability (AC → test, under current names)
- **Forward check.** I extracted all 62 distinct test names from the `tasks.md` Traceability table and grepped `tests/` for each method declaration. **All 62 exist under their current names. None are stale or missing.** The T-17 rename (`OpenApi_ContactsEndpoints_HaveOperationIds_NFR001`) is reflected, and the NFR-001 row lists all five OpenApiTests.
- **Per-ID coverage.** Every AC-001..AC-036, AC-038 and AC-039 has at least one test whose name ends in its ID. NFR-001 has 5, NFR-002 1 and NFR-004 1 (the guard). **AC-037** has no `_AC037` test by design. It is traced through `ProblemAssert` (its header comment says AC-037) and explicit `AC-037` comments in GetContactByIdTests and ErrorHandlingTests. `ProblemAssert.IsProblemAsync` is called for every error class the ACs name: 400 validation (create and list), 400 binding, 404 (unknown GUID and non-GUID), 409 and 500. That satisfies constitution §2 ("test names or comments carry the AC ID").
- **NFR-003** is manual. Its record is present under "T-18" above (machine, code state, method, six medians from 3.5 to 11.2 ms, all under 500 ms). The record says "HEAD `1d1eba0` plus the uncommitted T-04..T-17 working tree"; once commits exist, `/document` may want to add the resulting SHA.
- **Reverse check (completeness, not staleness).** 10 tests carry an AC suffix but aren't listed in the table: `CreateContact_SeveralFieldsOverMax_Returns400WithErrorForEach_AC008`, `Parse_SeveralFieldsOverMax_ReturnsErrorForEach_AC008`, `CreateContact_WhenNonUniqueConstraintFails_Returns500Problem_AC038` (class `NonUniqueConstraintFailureTests`), `ListContacts_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC038`, `ListContacts_SearchHitsInDifferentFields_ReturnsAllWithMatchingTotal_AC029`, `Parse_BlankOrAbsentSearch_YieldsNullSearch_AC031`, `Parse_SearchWithSurroundingSpaces_IsTrimmed_AC032`, `Parse_BothInvalid_ReportsBothKeys_AC028`, `Parse_ValidBoundaries_AreAccepted_AC028` and `Parse_SearchTooLongAndPageInvalid_ReportsBothKeys_AC034`. Several of them are the tests that closed Blocking or Should-fix findings, so they belong in the table (`/document`; finding 3). The AC-037 row also omits SearchContactsTests (AC034) and the list 500 test.

### 2. Spec conformance (Definitions, Q1–Q8)
I checked the code and a whole-spec probe of raw responses (scratch copy, `ZzProbeTests`, source untouched):

| Definition / decision | Code | Verdict |
|---|---|---|
| Fields + system `id`/`createdAt`/`updatedAt` | `Contact`, `ContactResponse`; there's no `Id` on `CreateContactRequest`, so a client id is ignored | conforms |
| Trimming; blank optional → `null` (Q3) | `ContactInput.Parse`: `Trim()` / `TrimToNull` on all six fields | conforms |
| Max lengths after trimming; `string.Length` (Q8) | `CheckMax` on trimmed values, constants 100/100/254/50/200/4000 | conforms |
| Valid email (Q6) | `IsValidEmail`: one `@`, non-empty both sides, `char.IsWhiteSpace`. The check runs on the trimmed value and casing is preserved | conforms |
| Uniqueness ignoring case and whitespace; null never conflicts | trimmed before store; `Email COLLATE NOCASE` + `IX_Contacts_Email` UNIQUE; 2067 → 409; NULLs allowed | conforms |
| Timestamps | one `TimeProvider.GetUtcNow()` for both; serialized `+00:00` | conforms |
| Sort: last, first, id; ignoring case (Q5); no-last-name last (Q4) | `OrderBy(LastName == null).ThenBy(LastName).ThenBy(FirstName).ThenBy(Id)`, NOCASE on both name columns | conforms |
| Paging defaults, 1..100, reject not clamp (Q2) | `ListQuery.Parse`, `NumberStyles.None` + invariant culture; `TryGetSkip` in `long`. Probe: `page=0&pageSize=999999999999` → 400 naming both | conforms |
| Search: trimmed, blank = no filter, single-field substring, literal wildcards, max 254 (Q7) | `Parse` trims and blank → null; `LikePattern.Contains` + `ESCAPE '\'` on all three fields; filter → count → order → page | conforms |
| ASCII-only case folding (Q1) | NOCASE and SQLite `LIKE` both fold ASCII A–Z only | conforms (documented by ADR-0003) |
| Every 4xx/5xx is problem+json (AC-037) | Probe: 400 (validation, binding, missing content type), 404 (unknown and non-GUID), **405** (`PUT /api/contacts`, `DELETE /api/contacts/{id}`, with `Allow`), 409, **415** (`text/plain`) and 500 all return `application/problem+json` with `type`/`title`/`status`. 405/415 aren't AC-named but are covered by the same status-code-pages path | conforms |

Nothing beyond the ACs was found in production code. The only metadata is `WithName`/`WithSummary` ×3 plus `ProducesProblem(409)`, and every piece of it is demanded by NFR-001.

### 3. Per-task Blocking / Should-fix findings
| Task | Finding | Status | Evidence |
|---|---|---|---|
| T-01 #1 | Should-fix: weak `ProblemAssert` type/title | Resolved (T-02) | `ProblemAssert.cs:49-54` |
| T-02 #1 | Blocking: early `WithName` | Resolved (T-02 fix 1) | re-review |
| T-02 #2 | Should-fix: no DB read-back | Resolved (T-03) | SaveChanges mutant fails AC017/AC039 |
| T-04 #1 | Blocking: `CustomizeProblemDetails` | Resolved (T-04 fix 1) | `Program.cs:9` plain `AddProblemDetails()` |
| T-07 #1 | Blocking: multi-field AC-008 untested | Resolved (T-07 fix 1) | both `SeveralFieldsOverMax` tests exist |
| T-07 #2 | Should-fix: unreachable `ContainsKey` | Resolved | no `ContainsKey` under `src/` |
| T-08 #1 | Should-fix: email precedence guard | Resolved | no `ContainsKey` under `src/`; `ContactInput.cs:37` |
| T-10 #1 | Should-fix: non-UNIQUE constraint must stay 500 | **Resolved, re-verified now** | Scratch mutant `SqliteErrorCode: 19` → 1 fail: `NonUniqueConstraintFailureTests.CreateContact_WhenNonUniqueConstraintFails_Returns500Problem_AC038` |
| T-12 #1 | Blocking: id tie-break vacuous | Resolved (T-12 fix 1) | `InsertTiedPairAsync` |
| T-15 #1 | Blocking: null-field search seeds | Resolved (T-15 fix 1) | M15 killed |
| T-15 #2 | Should-fix: `!= null` guards | Resolved (T-16) | no `!= null` under `src/` |
| T-16 #1 | Should-fix: combined search+page error | **Resolved, re-verified now** | `ListQueryTests.Parse_SearchTooLongAndPageInvalid_ReportsBothKeys_AC034`; scratch mutants L5 (search error alone) and L6 (early return on page errors) each → 1 fail, that test |

**Every Blocking and Should-fix finding is resolved. None remain open.** Of the Nits, T-17 #1 (rename) and #2 (`using var doc`) are both applied, and T-16 #2 (`!` operators) is intentionally left as the plan prescribes.

### 4. Whole-spec checks
- **Leftovers in `src/`:** a grep for `T-NN`, `TODO`, `FIXME`, `HACK`, `placeholder`, `weather`, `ILogger`/`Log*`, `Console.`, `DateTime.Now/UtcNow` and `EnableSensitiveDataLogging` (including Migrations) finds nothing. The single `catch` is the filtered 2067 catch. There's no dead code: every public constant and type is used by production code or tests.
- **Migrations:** the model is in sync (above). From an empty file DB, `dotnet ef database update` applies `CreateContacts` → `AddContactEmailUniqueIndex` → `AddContactNameCollation`, producing exactly plan.md's schema: `FirstName`/`LastName`/`Email` `COLLATE NOCASE`, the NOT NULLs on Id/FirstName/CreatedAt/UpdatedAt, and `CREATE UNIQUE INDEX "IX_Contacts_Email"`. `database update 0` rolls back cleanly to just the history/lock tables. Column order is alphabetical after the T-12 rebuild, which is harmless (noted at T-10).
- **Dependencies:** the plan's table is followed exactly. EF Core Sqlite, EF Design, `dotnet-ef` and test `Microsoft.Data.Sqlite` are all **10.0.12** (one patch, as the plan requires), and `Microsoft.Extensions.TimeProvider.Testing` is 10.0.0. `Microsoft.OpenApi` 2.7.5 predates this spec (bootstrap `5b4b75b`). No unplanned packages.
- **NFR-004 (inspection):** there's no application logging anywhere in `src/`. Sensitive-data logging is never enabled. `Microsoft.AspNetCore` is at Warning, so request URLs aren't logged at Information. The 409 path's EF Error log names the index, not the value, and the T-10 guard test pins this. **Conforms.**
- **Error message style:** inconsistent across the two validators (finding 1).
- **`MicroCrm.Api.http`:** a one-line stub (finding 2).

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Nit | implementer | `src/MicroCrm.Api/Features/Contacts/ContactInput.cs:21,39,63` vs `src/MicroCrm.Api/Common/Paging.cs:25,55` | The two validators use different styles. Create says `"First name is required."`, `"Must be 50 characters or fewer."` and `"Must be a valid email address."` (sentence case; only firstName names the field). List says `"'page' must be an integer between 1 and 2147483647."` and `"'search' must be at most 254 characters."` (quoted parameter name, lowercase start). The page message also shows `int.MaxValue` to clients. No AC fixes message text, and all tests assert keys only, so this isn't a conformance issue. It will matter when spec 004 shows these messages in the UI. | Pick one style before spec 002 adds update validation. For example, field-agnostic sentence case (`"Must be a whole number of 1 or more."`, `"Must be 254 characters or fewer."`), since the `errors` key already names the field. Record the rule in conventions.md (finding 4). Optional for this spec. |
| 2 | Nit | implementer | `src/MicroCrm.Api/MicroCrm.Api.http:1` | plan.md "Components touched" says to replace the weatherforecast sample "with contacts requests". The file holds only `@MicroCrm.Api_HostAddress = http://localhost:5185`, and that port differs from `DEV_API_CMD` (5080). No AC or test covers it (dev convenience). | Optional: add POST/GET-by-id/list sample requests and align the port with `DEV_API_CMD`, or drop the plan line during `/document`. |

**For `/document 001` (not code findings):**
3. `tasks.md` Traceability: add the 10 unlisted AC-suffixed tests from section 1, and add SearchContactsTests (AC034) and `ListContacts_WhenDatabaseFails_…_AC038` to the AC-037 and AC-038 rows.
4. `docs/conventions.md`: (a) the Validation line still says "DataAnnotations + `AddValidation()`". ADR-0004 (Accepted) replaced that with pure `Parse` functions plus `TypedResults.ValidationProblem`, `ThrowOnBadRequest = false`, and `TypedResults.Problem(409)` (not `Conflict`). (b) The integration-test line still says `DataSource=:memory:` with one held connection. ADR-0005 replaced that with a named shared-cache in-memory DB and a connection per `DbContext`. (c) Optionally, a rule for validation message style (finding 1). Changes to conventions.md go through an ADR, and ADR-0004/0005 are that ADR.
5. `docs/architecture.md`: "API host: scaffolded" and "Contacts feature: planned" are stale. Describe `Features/Contacts`, `Common/` (Paging, LikePattern), `Data/` (AppDbContext, ContactConfiguration, SqliteErrors, Migrations), migrate-at-startup, and the error pipeline order.
6. `plan.md` vs reality: (a) the ADRs section still says ADR-0003/0004/0005 are "Proposed", but all three files say **Accepted**. (b) The "Components touched" row says `ContactConfiguration` holds "max lengths", and the schema comments say "max 100 (EF metadata)". No `HasMaxLength` exists, and that's correct under the minimal-implementation rule, because SQLite doesn't enforce it and no test demands it. (c) The `.http` row (finding 2). (d) T-17's "Likely source files" listed `.ProducesValidationProblem()`/`.WithTags("Contacts")`, which were correctly left out. The OpenAPI tag is the default `ContactsEndpoints`.
7. `CHANGELOG.md`: add the spec 001 entry. The spec "Implementation notes" section still holds its placeholder and needs filling in. Set the spec status to `Done` after `/document`.
8. Package version drift (plan.md follow-up): API `Microsoft.AspNetCore.OpenApi` **10.0.0** vs `Microsoft.AspNetCore.Mvc.Testing`/EF **10.0.12**, and test `Microsoft.Extensions.TimeProvider.Testing` **10.0.0** (plan: "latest 10.x"). Record it or bump it in a `chore:` with its own test run.
9. Doc vs runtime (from T-17): the OpenAPI 404 for GetById has no content type, while runtime returns problem+json. This is a candidate for a later spec.

**Process items for the user (not defects):**
- T-04..T-17 are approved but **uncommitted** (`COMMIT_PER_TASK=0`). Choose between per-task `feat(001): … [T-XX]` commits (the hunks for T-04..T-17 interleave in `ContactsEndpoints.cs`, `Program.cs` and `ApiFactory.cs`, so a clean split needs `git add -p`) and one squashed commit that you accept explicitly. The working tree is green at the current state.
- `docs/.claude/` (reviewer agent memory) is untracked inside `docs/`. Decide whether it is version-controlled at that path.
- Leave `docs/adr/0002-…` out of the spec-001 commits unless you intend to include it.

**Verdict:** APPROVE. Every AC and NFR is traced to existing, correctly named tests (NFR-003 manual and recorded, NFR-004 inspected plus guarded). The behaviour conforms to the Definitions and Q1–Q8. Every Blocking and Should-fix finding is closed, and all checks are green. The remaining items are two Nits and documentation work.
