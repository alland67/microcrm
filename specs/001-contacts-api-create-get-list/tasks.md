# 001: Contacts API: create, get by id, list (paging + search): Tasks

Legend: `[ ]` todo · `[~]` in progress · `[x]` done
Each task = one red → green → refactor cycle and leaves the suite green. All tasks are **API-side only** (`BACKEND_TEST_CMD`).

Paths are abbreviated as follows:
- `T/` = `tests/MicroCrm.Api.Tests/`
- `S/` = `src/MicroCrm.Api/`

Test names follow `Method_Scenario_Expected_ACnnn`.

**Guard tests.** A guard is a test expected to **pass at RED**, because an earlier task (or the task's own wiring) already produces the behavior. Each guard is listed under its task. The test-writer reports guards in the RED evidence, separately from the failing tests. The reviewer confirms each guard would fail if its behavior regressed. Every task has at least one test that fails for the right reason.

**Minimal implementation.** Implementers add only what the task's failing tests demand. In particular:
- no OpenAPI metadata (`WithName`/`WithSummary`/`Produces*`) before T-17;
- no LIKE escaping before T-16;
- no search-length rule before T-16;
- no case-insensitive collation on names before T-12.

This keeps later tasks genuinely red.

---

- [x] T-01: Error pipeline: unmatched routes and non-GUID ids return 404 ProblemDetails — done: AddProblemDetails + UseStatusCodePages; weatherforecast removed (review APPROVE)
  - **ACs:** AC-019, AC-037 (404 from an unmatched route)
  - **Depends on:** none
  - **Tests:**
    - `T/Integration/Infrastructure/ApiFactory.cs` (new): minimal `WebApplicationFactory<Program>`, environment `Development`
    - `T/Integration/Infrastructure/ProblemAssert.cs` (new): asserts status, `application/problem+json`, and `type`/`title`/`status` members; optional expected `errors` keys
    - `T/Integration/Contacts/GetContactByIdTests.cs` (new): `GetContact_WithNonGuidId_Returns404Problem_AC019` (Theory: `not-a-guid`, `123`)
  - **Likely source files:**
    - `S/Program.cs`: remove the weatherforecast sample; `AddProblemDetails()`, `UseStatusCodePages()`
    - `S/MicroCrm.Api.http`: drop the sample request
  - **Expected RED:** 404 with an empty body (not problem+json).
  - **Done when:** `GET /api/contacts/not-a-guid` returns 404 `application/problem+json` with `type`, `title`, `status`=404. The weatherforecast endpoint is gone. The full suite (including `SmokeTests`) is green.

- [x] T-02: Create a contact (happy path) and establish persistence + test database — done: POST /api/contacts, AppDbContext, CreateContacts migration, in-memory test DB (review APPROVE after 1 fix cycle)
  - **ACs:** AC-001, AC-002, AC-003, AC-004, AC-012, NFR-002
  - **Depends on:** T-01
  - **Tests:**
    - `T/MicroCrm.Api.Tests.csproj` (+ `Microsoft.Data.Sqlite`, + `Microsoft.Extensions.TimeProvider.Testing`)
    - `T/Integration/Infrastructure/ApiFactory.cs` (extend per ADR-0005):
      - named shared-cache in-memory DB with a keep-alive connection opened before host start
      - `UseSetting("ConnectionStrings:MicroCrm", ...)`
      - `FakeTimeProvider Time` replacing `TimeProvider`
      - `ConnectionString` property
      - dispose clears the pool
    - `T/Integration/Contacts/CreateContactTests.cs` (new):
      - `CreateContact_WithAllFields_Returns201WithLocationAndBody_AC001`
      - `CreateContact_WithOnlyFirstName_ReturnsNullOptionalFields_AC002` (raw JSON: members present and `null`)
      - `CreateContact_IgnoresClientId_AssignsNewGuidV7_AC003`
      - `CreateContact_SetsTimestampsFromClock_IgnoresClientValues_AC004`
      - `CreateContact_WithUppercaseEmail_PreservesCasing_AC012`
      - `CreateContact_Json_IsCamelCaseWithNullsAndUtcOffsets_NFR002`
    - `T/Integration/SmokeTests.cs`: switch the fixture to `ApiFactory`; the assertion is unchanged
  - **Likely source files:**
    - `S/MicroCrm.Api.csproj` (+ EF Core SQLite, + EF Core Design `PrivateAssets=all`)
    - `.config/dotnet-tools.json` (`dotnet-ef`)
    - `S/appsettings.json` (connection string)
    - `S/Program.cs`:
      - `AddSingleton(TimeProvider.System)`
      - `AddDbContext` with a lazy connection string
      - migrate at startup
      - `MapContactsEndpoints()`
    - `S/Data/AppDbContext.cs`
    - `S/Data/Migrations/*_CreateContacts` (generated via `MIGRATIONS_ADD_CMD`)
    - `S/Features/Contacts/Contact.cs`, `ContactDtos.cs`, `ContactsEndpoints.cs` (POST only; `Guid.CreateVersion7(now)`; one `GetUtcNow()` for both timestamps)
  - **Size note:** this is the foundation task and is larger than usual: about 7 small hand-written files plus the generated migration. It can't be split without leaving a test-less step.
  - **Expected RED:** compile succeeds; every test gets 404/405 because `POST /api/contacts` doesn't exist.
  - **Done when:**
    - `POST /api/contacts` with valid fields returns 201, `Location: /api/contacts/{id}`, and the full camelCase body with nulls present.
    - The id is a server-assigned GUID v7.
    - `createdAt == updatedAt ==` the fake clock.
    - Email casing is preserved.
    - Migration `CreateContacts` exists and is applied at startup.
    - `SmokeTests` runs on `ApiFactory` (no `microcrm.db` file created by tests).
    - Suite green.

- [x] T-03: Get a contact by id, persisted across connections — done: GET /api/contacts/{id:guid}; persistence proven via direct-DB read (review APPROVE)
  - **ACs:** AC-017, AC-018, AC-039 (get-by-id part), AC-037 (404 for an unknown GUID)
  - **Depends on:** T-02
  - **Tests:** `T/Integration/Contacts/GetContactByIdTests.cs` (extend):
    - `GetContact_Existing_Returns200WithCreatedBody_AC017` (raw JSON equality with the POST body)
    - `GetContact_UnknownGuid_Returns404Problem_AC018`
    - `GetContact_FromNewClientAndConnection_ReturnsPersistedContact_AC039`: new `HttpClient`, plus a row count through a new `SqliteConnection` on `factory.ConnectionString`
  - **Likely source files:** `S/Features/Contacts/ContactsEndpoints.cs` (GET `{id:guid}` → `Results<Ok<ContactResponse>, NotFound>`)
  - **Guards:** AC-018. It already returns 404 problem+json through T-01's status code pages, because no route matches yet.
  - **Expected RED:** AC-017 and AC-039 return 404.
  - **Done when:** an existing contact is returned with values identical to creation (timestamps round-trip exactly); an unknown GUID returns 404 ProblemDetails; data is visible from a new client and a new DB connection. Suite green.

- [x] T-04: Unexpected errors return a safe 500 ProblemDetails — done: UseExceptionHandler first in pipeline (review APPROVE after 1 fix cycle)
  - **ACs:** AC-038, AC-037 (500)
  - **Depends on:** T-03
  - **Tests:**
    - `T/Integration/Infrastructure/ApiFactory.cs` (+ `ExecuteSqlAsync(string sql)` on a fresh connection)
    - `T/Integration/ErrorHandlingTests.cs` (new, own fixture instance). After host start, `DROP TABLE Contacts`, then:
      - `GetContact_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC038`
      - `CreateContact_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC038`

      Each asserts 500, problem+json `type`/`title`/`status`, and that the body contains none of `no such table`, `SqliteException`, `Exception`, `   at `.
  - **Likely source files:** `S/Program.cs` (`UseExceptionHandler()` first in the pipeline, in all environments)
  - **Expected RED:** the Development exception page returns details (message, stack trace).
  - **Done when:** in the Development environment, DB failures on GET and POST return a 500 ProblemDetails with no exception message, type name, or stack trace. Suite green.

- [x] T-05: Trim text fields; empty optional fields become null — done: ContactInput.Parse trims fields, blank optionals → null (review APPROVE)
  - **ACs:** AC-005, AC-006
  - **Depends on:** T-02
  - **Tests:**
    - `T/Unit/Contacts/ContactInputTests.cs` (new):
      - `Parse_TrimsAllFields_AC005`
      - `Parse_EmptyOrWhitespaceOptional_BecomesNull_AC006` (Theory over each optional field × `""`, `"   "`)
    - `T/Integration/Contacts/CreateContactTests.cs` (extend):
      - `CreateContact_WithSurroundingWhitespace_StoresTrimmed_AC005` (verified via GET)
      - `CreateContact_WithBlankOptionalFields_ReturnsNulls_AC006`
  - **Likely source files:**
    - `S/Features/Contacts/ContactInput.cs` (new; `Parse` normalization only)
    - `S/Features/Contacts/ContactsEndpoints.cs`
  - **Expected RED:** compile error / missing type for the unit tests; integration tests receive untrimmed values.
  - **Done when:** created contacts are stored and returned trimmed, and blank optional fields are `null`. Suite green.

- [x] T-06: First name is required — done: blank firstName → 400 ValidationProblem, nothing stored (review APPROVE)
  - **ACs:** AC-007, AC-037 (400 validation)
  - **Depends on:** T-04, T-05
  - **Tests:**
    - `T/Unit/Contacts/ContactInputTests.cs` (extend): `Parse_MissingOrBlankFirstName_ReturnsFirstNameError_AC007` (Theory: null, `""`, `"   "`)
    - `T/Integration/Contacts/CreateContactValidationTests.cs` (new): `CreateContact_WithoutFirstName_Returns400WithFirstNameError_AC007` (Theory: member missing, `null`, `""`, `"  "`). Asserts `errors.firstName` via `ProblemAssert` and that no row was created (DB count).
  - **Likely source files:** `S/Features/Contacts/ContactInput.cs` (errors dictionary), `S/Features/Contacts/ContactsEndpoints.cs` (`TypedResults.ValidationProblem`)
  - **Expected RED:** 500 (NOT NULL constraint) or 201 with an empty name.
  - **Done when:** a missing, null, empty, or whitespace first name returns 400 validation ProblemDetails with a `firstName` key, and nothing is stored. Suite green.

- [x] T-07: Maximum field lengths (measured after trimming) — done: max lengths after trim, all offending fields reported (review APPROVE after 1 fix cycle)
  - **ACs:** AC-008, AC-009
  - **Depends on:** T-06
  - **Tests:**
    - `T/Unit/Contacts/ContactInputTests.cs` (extend):
      - `Parse_FieldOverMax_ReturnsFieldError_AC008` (Theory over the six fields at max+1)
      - `Parse_FieldAtMax_IsValid_AC009` (exact max, and max wrapped in whitespace)
    - `T/Integration/Contacts/CreateContactValidationTests.cs`: `CreateContact_FieldOverMax_Returns400WithFieldError_AC008` (Theory; no row created)
    - `T/Integration/Contacts/CreateContactTests.cs`: `CreateContact_FieldsAtMax_Returns201_AC009`
  - **Likely source files:** `S/Features/Contacts/ContactInput.cs`
  - **Guards:** AC-009 (both integration and unit). There are no limits yet, so values at max are accepted.
  - **Expected RED:** AC-008 returns 201 / no errors.
  - **Done when:**
    - Each field over its limit after trimming → 400 with that field's key.
    - Exactly-at-limit values, with or without surrounding whitespace, → 201.
    - Lengths use `string.Length`.
    - Suite green.

- [x] T-08: Email format and reporting all errors at once — done: IsValidEmail (one @, non-empty parts, no char.IsWhiteSpace); all errors in one 400 (review APPROVE; should-fix + nit applied)
  - **ACs:** AC-010, AC-014
  - **Depends on:** T-07
  - **Tests:**
    - `T/Unit/Contacts/ContactInputTests.cs` (extend):
      - `IsValidEmail_Rules_AC010` (Theory: invalid `a`, `a@`, `@b`, `a@@b`, `a@b@c`, `a b@c`; valid `a@b`, `Ada.Lovelace@Example.com`)
      - `Parse_MultipleInvalidFields_ReturnsAllErrors_AC014`
    - `T/Integration/Contacts/CreateContactValidationTests.cs`:
      - `CreateContact_WithInvalidEmail_Returns400WithEmailError_AC010` (no row created)
      - `CreateContact_WithMultipleInvalidFields_ReturnsAllCamelCaseKeys_AC014` (missing firstName + invalid email + phone 51 chars → exactly `firstName`, `email`, `phone`)
  - **Likely source files:** `S/Features/Contacts/ContactInput.cs`
  - **Expected RED:** an invalid email is accepted (201); the multiple-error test misses `email`.
  - **Done when:** invalid emails return 400 with an `email` key; all errors in one request come back in a single 400 keyed by camelCase name. Suite green.

- [x] T-09: Malformed or mistyped request bodies return 400 ProblemDetails — done: ThrowOnBadRequest = false; binding failures → 400 ProblemDetails (review APPROVE)
  - **ACs:** AC-015, AC-037 (400 from a binding failure)
  - **Depends on:** T-04
  - **Tests:** `T/Integration/Contacts/CreateContactValidationTests.cs` (extend): `CreateContact_WithMalformedBody_Returns400Problem_AC015`
    - Theory over raw `application/json` bodies: `{`, empty, `null`, `[]`, `{"firstName":123}`.
    - Asserts 400 problem+json and an unchanged DB count.
  - **Likely source files:** `S/Program.cs` (`Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = false)`)
  - **Expected RED:** 500 ProblemDetails (Development throws `BadHttpRequestException` into T-04's handler). If any case already returns 400 problem+json, record it as a guard.
  - **Done when:** every malformed, empty, or mistyped body returns 400 `application/problem+json` and creates nothing, in all environments. Suite green.

- [x] T-10: Email uniqueness, enforced by the database (including concurrent requests) — done: NOCASE + unique index on Email (migration AddContactEmailUniqueIndex), 2067 → 409 ProblemDetails, concurrency stable; non-unique constraint stays 500 (review APPROVE; should-fix applied)
  - **ACs:** AC-011, AC-013, AC-016, AC-037 (409), NFR-004 (guard)
  - **Depends on:** T-08
  - **Tests:** `T/Integration/Contacts/CreateContactConflictTests.cs` (new):
    - `CreateContact_WithExistingEmailDifferentCaseAndWhitespace_Returns409Problem_AC011` (DB count stays 1)
    - `CreateContact_ManyWithoutEmail_AllSucceed_AC013` (absent, `null`, `""`)
    - `Database_RejectsDuplicateEmailDifferingOnlyInCase_AC016`: direct inserts through a new `SqliteConnection` expect `SqliteException` with extended code 2067
    - `CreateContact_ConcurrentSameEmail_ExactlyOneCreatedRestConflict_AC016`: 10 POSTs released together; exactly one 201, nine 409 problem+json, zero 5xx, DB count 1
    - `CreateContact_DuplicateEmail_EmailNotLoggedAtInformationOrAbove_NFR004`: capturing `ILoggerProvider` added via `WithWebHostBuilder` in the test
  - **Likely source files:**
    - `S/Data/ContactConfiguration.cs` (new; `Email` NOCASE + unique index; may move the existing mapping here)
    - `S/Data/AppDbContext.cs`
    - `S/Data/Migrations/*_AddContactEmailUniqueIndex` (generated)
    - `S/Data/SqliteErrors.cs` (new)
    - `S/Features/Contacts/ContactsEndpoints.cs` (catch → `TypedResults.Problem(statusCode: 409)`, other `DbUpdateException`s re-thrown; no pre-check; problem detail doesn't echo the email)
  - **Guards:** AC-013 and NFR-004 (there's no unique index yet; nothing logs bodies).
  - **Expected RED:** duplicates return 201; the direct insert succeeds.
  - **Done when:**
    - A duplicate email (case and whitespace ignored) returns 409 `application/problem+json` and stores nothing.
    - Email-less contacts never conflict.
    - Concurrent duplicates yield exactly one 201 and no 5xx.
    - The database rejects case-variant duplicates on its own.
    - Suite green.

- [x] T-11: List contacts: envelope, defaults, empty list — done: GET /api/contacts with PagedResponse, defaults page=1/pageSize=20; list 500 safe (review APPROVE)
  - **ACs:** AC-020, AC-021, AC-039 (list part)
  - **Depends on:** T-03
  - **Tests:**
    - `T/Integration/Infrastructure/ApiFactory.cs` (+ `ResetAsync()`)
    - `T/Integration/Contacts/ListContactsTests.cs` (new; `IAsyncLifetime` calls `ResetAsync`):
      - `ListContacts_NoQuery_ReturnsFirst20WithEnvelope_AC020` (25 seeded; `page`=1, `pageSize`=20, 20 items, `totalCount`=25, item shape as AC-017)
      - `ListContacts_NoContacts_ReturnsEmptyItemsAndZeroTotal_AC021`
      - `ListContacts_FromNewClient_IncludesPersistedContact_AC039`
  - **Likely source files:**
    - `S/Common/Paging.cs` (new; `PagedResponse<T>`; defaults only)
    - `S/Features/Contacts/ContactsEndpoints.cs` (GET `""`)
  - **Expected RED:** 405 problem+json (only POST is mapped on the path).
  - **Done when:** `GET /api/contacts` returns `{ items, page: 1, pageSize: 20, totalCount }` with at most 20 items and the true total; an empty DB returns `items: []`, `totalCount: 0`. Suite green.

- [x] T-12: Sort order: last name, first name, id; case-insensitive; no last name last — done: NOCASE names (migration AddContactNameCollation), sort LastName-nulls-last/FirstName/Id; tie-break pinned (review APPROVE after 1 fix cycle)
  - **ACs:** AC-022, AC-023
  - **Depends on:** T-11, T-10
  - **Tests:** `T/Integration/Contacts/ListContactsTests.cs` (extend):
    - `ListContacts_SortsByLastFirstIdIgnoringCase_AC022`: `adams`/`Baker`/`carter`, same last name with different-case first names, identical names tie-broken by id ordinal
    - `ListContacts_ContactsWithoutLastName_SortLastByFirstNameThenId_AC023`
  - **Likely source files:**
    - `S/Data/ContactConfiguration.cs` (NOCASE on `FirstName`, `LastName`)
    - `S/Data/Migrations/*_AddContactNameCollation` (generated; SQLite table rebuild)
    - `S/Features/Contacts/ContactsEndpoints.cs` (`OrderBy(LastName == null).ThenBy(LastName).ThenBy(FirstName).ThenBy(Id)`)
  - **Expected RED:** insertion/ordinal order; nulls first. (Note: the id tie-break only fails in RED if the clock advances between inserts, since v7 ids then follow insertion order.)
  - **Done when:** list order matches AC-022/AC-023 for mixed-case data, and the name collation is in the schema via the new migration. Suite green.

- [x] T-13: Paging slices and limits — done: ListQuery parses page/pageSize, skip in long (int.MaxValue page safe) (review APPROVE)
  - **ACs:** AC-024, AC-025, AC-026, AC-027
  - **Depends on:** T-12
  - **Tests:** `T/Integration/Contacts/ListContactsPagingTests.cs` (new; `ResetAsync`):
    - `ListContacts_PageAndPageSize_ReturnsSliceAndEchoesParams_AC024`
    - `ListContacts_PagingThroughAll_ReturnsEachContactOnce_AC025` (duplicate names; pageSize 3)
    - `ListContacts_PageBeyondLast_ReturnsEmptyItemsWithTotal_AC026` (also `page=2147483647`)
    - `ListContacts_PageSize100_ReturnsUpTo100_AC027` (101 seeded)
  - **Likely source files:**
    - `S/Common/Paging.cs` (`ListQuery` with valid-integer parsing + defaults; `TryGetSkip` in `long`)
    - `S/Features/Contacts/ContactsEndpoints.cs` (bind `page`/`pageSize` as `string?`)
  - **Expected RED:** query parameters are ignored (always page 1 / 20).
  - **Done when:** valid `page`/`pageSize` return the right slice of the sorted list, echo the parameters, and report the full `totalCount`; full traversal has no gaps or duplicates; pages beyond the end are empty; pageSize 100 is accepted. Suite green.

- [x] T-14: Reject invalid paging parameters — done: Parse returns (query, errors); invalid page/pageSize → 400 keyed by param (review APPROVE)
  - **ACs:** AC-028, AC-037 (400 validation on list)
  - **Depends on:** T-13
  - **Tests:**
    - `T/Unit/Common/ListQueryTests.cs` (new):
      - `Parse_InvalidPageOrPageSize_ReturnsNamedError_AC028` (Theory: `0`, `-1`, `abc`, `1.5`, `""`, `2147483648`; pageSize `0`, `101`)
      - `Parse_Absent_UsesDefaults_AC028`
      - `TryGetSkip_HugePage_ReturnsFalse_AC026`
    - `T/Integration/Contacts/ListContactsPagingTests.cs`:
      - `ListContacts_InvalidPaging_Returns400WithParamError_AC028` (Theory, asserting the `errors` key `page`/`pageSize`)
      - `ListContacts_BothInvalid_ReportsBoth_AC028`
  - **Likely source files:** `S/Common/Paging.cs`, `S/Features/Contacts/ContactsEndpoints.cs` (`ValidationProblem`)
  - **Expected RED:** invalid values fall back to defaults or are clamped → 200.
  - **Done when:** each out-of-range or non-integer `page`/`pageSize` returns 400 validation ProblemDetails naming the parameter. Suite green.

- [x] T-15: Search by first name, last name, or email — done: trimmed search, EF.Functions.Like on first/last/email before count/sort/page (review APPROVE after 1 fix cycle)
  - **ACs:** AC-029, AC-030, AC-031, AC-032, AC-035, AC-036
  - **Depends on:** T-14
  - **Tests:** `T/Integration/Contacts/SearchContactsTests.cs` (new; `ResetAsync`):
    - `ListContacts_Search_MatchesAnyFieldIgnoringCase_AC029` (separate hits on first name, last name, and email; `totalCount` = matches)
    - `ListContacts_SearchAcrossFirstAndLast_DoesNotMatch_AC030`
    - `ListContacts_BlankSearch_SameAsNoSearch_AC031` (`search=`, `search=%20%20`)
    - `ListContacts_SearchWithSurroundingWhitespace_UsesTrimmedTerm_AC032`
    - `ListContacts_SearchNoMatches_ReturnsEmptyAndZero_AC035`
    - `ListContacts_SearchWithPaging_FiltersThenSortsThenPages_AC036`
  - **Likely source files:**
    - `S/Common/Paging.cs` (trim search; blank → null)
    - `S/Features/Contacts/ContactsEndpoints.cs` (`EF.Functions.Like` on the three fields; filter before count/order/page)
  - **Guards:** AC-031 (no filter exists yet, so a blank search equals no search).
  - **Expected RED:** search is ignored; all contacts are returned.
  - **Done when:** search filters on any single field, case-insensitively for ASCII, using the trimmed term; `totalCount` counts only matches; paging applies after filtering and sorting. Suite green.

- [x] T-16: Search treats wildcard characters literally; search length limit — done: LikePattern escapes \ % _ with ESCAPE; search > 254 after trim → 400 (review APPROVE; should-fix applied)
  - **ACs:** AC-033, AC-034
  - **Depends on:** T-15
  - **Tests:**
    - `T/Unit/Common/LikePatternTests.cs` (new): `Contains_EscapesBackslashPercentUnderscore_AC033` (Theory incl. `\%`, `%_`, `\\`)
    - `T/Unit/Common/ListQueryTests.cs` (extend): `Parse_SearchOver254AfterTrim_ReturnsSearchError_AC034`, `Parse_Search254WithWhitespace_IsValid_AC034`
    - `T/Integration/Contacts/SearchContactsTests.cs` (extend):
      - `ListContacts_SearchWithPercentUnderscoreBackslash_MatchesLiterally_AC033`
      - `ListContacts_SearchTooLong_Returns400WithSearchError_AC034`
  - **Likely source files:**
    - `S/Common/LikePattern.cs` (new)
    - `S/Common/Paging.cs` (search max 254)
    - `S/Features/Contacts/ContactsEndpoints.cs` (`ESCAPE '\'` argument)
  - **Expected RED:** `%` matches everything, and `_` matches any character; a 255-character search returns 200.
  - **Done when:** `%`, `_`, and `\` in search terms match only literal occurrences; a search over 254 characters after trimming returns 400 with a `search` key. Suite green.

- [x] T-17: OpenAPI document describes the three endpoints — done: WithName/WithSummary on all three, ProducesProblem(409) on POST (review APPROVE)
  - **ACs:** NFR-001
  - **Depends on:** T-16
  - **Tests:** `T/Integration/OpenApiTests.cs` (new): `OpenApi_ContactsEndpoints_HaveNameSummaryAndStatusCodes_NFR001`. Parses `/openapi/v1.json` and checks:
    - operationIds `CreateContact`, `GetContactById`, `ListContacts`
    - non-empty summaries
    - responses: POST ⊇ {201, 400, 409}; GET by id ⊇ {200, 404}; list ⊇ {200, 400}
  - **Likely source files:** `S/Features/Contacts/ContactsEndpoints.cs` (`.WithName`, `.WithSummary`, `.ProducesValidationProblem()`, `.ProducesProblem(409)`, `.WithTags("Contacts")`)
  - **Expected RED:** missing operationIds/summaries/409.
  - **Done when:** the OpenAPI document in Development lists all three operations with names, summaries, and the required status codes. Suite green.

- [x] T-18: Manual performance check (no code) — done: medians 3.5–11.2 ms over 10k contacts, recorded in review.md
  - **ACs:** NFR-003
  - **Depends on:** T-17
  - **Tests:** none (manual; not a CI gate). Follow the procedure in `plan.md` → Rollout.
  - **Likely source files:** none. If the threshold fails, open a follow-up task (for example an index) and go through the plan again; don't optimize ad hoc.
  - **Done when:** `review.md` records the machine, commit, and median timings for page-of-100 list, search, and deep-page requests over 10,000 contacts, each under 500 ms.

NFR-004 has no task of its own. The reviewer inspects every task's logging (no request bodies or emails at Information or above; `EnableSensitiveDataLogging` never enabled), and T-10 adds an automated guard.

## Traceability
| AC | Task(s) | Test(s) (filled in during build) |
|---|---|---|
| AC-001 | T-02 | CreateContactTests.CreateContact_WithAllFields_Returns201WithLocationAndBody_AC001 |
| AC-002 | T-02 | CreateContactTests.CreateContact_WithOnlyFirstName_ReturnsNullOptionalFields_AC002 |
| AC-003 | T-02 | CreateContactTests.CreateContact_IgnoresClientId_AssignsNewGuidV7_AC003 |
| AC-004 | T-02 | CreateContactTests.CreateContact_SetsTimestampsFromClock_IgnoresClientValues_AC004 |
| AC-005 | T-05 | ContactInputTests.Parse_TrimsAllFields_AC005; CreateContactTests.CreateContact_WithSurroundingWhitespace_StoresTrimmed_AC005 |
| AC-006 | T-05 | ContactInputTests.Parse_EmptyOrWhitespaceOptional_BecomesNull_AC006; CreateContactTests.CreateContact_WithBlankOptionalFields_ReturnsNulls_AC006 |
| AC-007 | T-06 | ContactInputTests.Parse_MissingOrBlankFirstName_ReturnsFirstNameError_AC007; CreateContactValidationTests.CreateContact_WithoutFirstName_Returns400WithFirstNameError_AC007 |
| AC-008 | T-07 | ContactInputTests.Parse_FieldOverMax_ReturnsFieldError_AC008; ContactInputTests.Parse_SeveralFieldsOverMax_ReturnsErrorForEach_AC008; CreateContactValidationTests.CreateContact_FieldOverMax_Returns400WithFieldError_AC008; CreateContactValidationTests.CreateContact_SeveralFieldsOverMax_Returns400WithErrorForEach_AC008 |
| AC-009 | T-07 (guard) | ContactInputTests.Parse_FieldAtMax_IsValid_AC009; CreateContactTests.CreateContact_FieldsAtMax_Returns201_AC009 |
| AC-010 | T-08 | ContactInputTests.IsValidEmail_Rules_AC010; CreateContactValidationTests.CreateContact_WithInvalidEmail_Returns400WithEmailError_AC010 |
| AC-011 | T-10 | CreateContactConflictTests.CreateContact_WithExistingEmailDifferentCaseAndWhitespace_Returns409Problem_AC011 |
| AC-012 | T-02 | CreateContactTests.CreateContact_WithUppercaseEmail_PreservesCasing_AC012 |
| AC-013 | T-10 (guard) | CreateContactConflictTests.CreateContact_ManyWithoutEmail_AllSucceed_AC013 |
| AC-014 | T-08 | ContactInputTests.Parse_MultipleInvalidFields_ReturnsAllErrors_AC014; CreateContactValidationTests.CreateContact_WithMultipleInvalidFields_ReturnsAllCamelCaseKeys_AC014 |
| AC-015 | T-09 | CreateContactValidationTests.CreateContact_WithMalformedBody_Returns400Problem_AC015 |
| AC-016 | T-10 | CreateContactConflictTests.Database_RejectsDuplicateEmailDifferingOnlyInCase_AC016; CreateContactConflictTests.CreateContact_ConcurrentSameEmail_ExactlyOneCreatedRestConflict_AC016 |
| AC-017 | T-03 | GetContactByIdTests.GetContact_Existing_Returns200WithCreatedBody_AC017 |
| AC-018 | T-03 (guard) | GetContactByIdTests.GetContact_UnknownGuid_Returns404Problem_AC018 |
| AC-019 | T-01 | GetContactByIdTests.GetContact_WithNonGuidId_Returns404Problem_AC019 |
| AC-020 | T-11 | ListContactsTests.ListContacts_NoQuery_ReturnsFirst20WithEnvelope_AC020 |
| AC-021 | T-11 | ListContactsTests.ListContacts_NoContacts_ReturnsEmptyItemsAndZeroTotal_AC021 |
| AC-022 | T-12 | ListContactsTests.ListContacts_SortsByLastFirstIdIgnoringCase_AC022 |
| AC-023 | T-12 | ListContactsTests.ListContacts_ContactsWithoutLastName_SortLastByFirstNameThenId_AC023 |
| AC-024 | T-13 | ListContactsPagingTests.ListContacts_PageAndPageSize_ReturnsSliceAndEchoesParams_AC024 |
| AC-025 | T-13 | ListContactsPagingTests.ListContacts_PagingThroughAll_ReturnsEachContactOnce_AC025 |
| AC-026 | T-13, T-14 | ListContactsPagingTests.ListContacts_PageBeyondLast_ReturnsEmptyItemsWithTotal_AC026; ListQueryTests.TryGetSkip_HugePage_ReturnsFalse_AC026 |
| AC-027 | T-13 | ListContactsPagingTests.ListContacts_PageSize100_ReturnsUpTo100_AC027 |
| AC-028 | T-14 | ListQueryTests.Parse_InvalidPageOrPageSize_ReturnsNamedError_AC028; ListQueryTests.Parse_Absent_UsesDefaults_AC028; ListQueryTests.Parse_BothInvalid_ReportsBothKeys_AC028; ListQueryTests.Parse_ValidBoundaries_AreAccepted_AC028; ListContactsPagingTests.ListContacts_InvalidPaging_Returns400WithParamError_AC028; ListContactsPagingTests.ListContacts_BothInvalid_ReportsBoth_AC028 |
| AC-029 | T-15 | SearchContactsTests.ListContacts_Search_MatchesAnyFieldIgnoringCase_AC029; SearchContactsTests.ListContacts_SearchHitsInDifferentFields_ReturnsAllWithMatchingTotal_AC029 |
| AC-030 | T-15 | SearchContactsTests.ListContacts_SearchAcrossFirstAndLast_DoesNotMatch_AC030 |
| AC-031 | T-15 (guard) | SearchContactsTests.ListContacts_BlankSearch_SameAsNoSearch_AC031; ListQueryTests.Parse_BlankOrAbsentSearch_YieldsNullSearch_AC031 |
| AC-032 | T-15 | SearchContactsTests.ListContacts_SearchWithSurroundingWhitespace_UsesTrimmedTerm_AC032; ListQueryTests.Parse_SearchWithSurroundingSpaces_IsTrimmed_AC032 |
| AC-033 | T-16 | LikePatternTests.Contains_EscapesBackslashPercentUnderscore_AC033; SearchContactsTests.ListContacts_SearchWithPercentUnderscoreBackslash_MatchesLiterally_AC033 |
| AC-034 | T-16 | ListQueryTests.Parse_SearchOver254AfterTrim_ReturnsSearchError_AC034; ListQueryTests.Parse_Search254WithWhitespace_IsValid_AC034; ListQueryTests.Parse_SearchTooLongAndPageInvalid_ReportsBothKeys_AC034; SearchContactsTests.ListContacts_SearchTooLong_Returns400WithSearchError_AC034 |
| AC-035 | T-15 | SearchContactsTests.ListContacts_SearchNoMatches_ReturnsEmptyAndZero_AC035 |
| AC-036 | T-15 | SearchContactsTests.ListContacts_SearchWithPaging_FiltersThenSortsThenPages_AC036 |
| AC-037 | T-01, T-03, T-04, T-06, T-09, T-10, T-14 | Via ProblemAssert in: GetContactByIdTests (AC019, AC018), ErrorHandlingTests (AC038), CreateContactValidationTests (AC007, AC015), CreateContactConflictTests (AC011), ListContactsPagingTests (AC028), SearchContactsTests (AC034: `ListContacts_SearchTooLong_Returns400WithSearchError_AC034`), ErrorHandlingTests (AC038: `ListContacts_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC038`) |
| AC-038 | T-04 | ErrorHandlingTests.GetContact_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC038; ErrorHandlingTests.CreateContact_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC038; ErrorHandlingTests.ListContacts_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC038; NonUniqueConstraintFailureTests.CreateContact_WhenNonUniqueConstraintFails_Returns500Problem_AC038 |
| AC-039 | T-03, T-11 | GetContactByIdTests.GetContact_FromNewClientAndConnection_ReturnsPersistedContact_AC039; ListContactsTests.ListContacts_FromNewClient_IncludesPersistedContact_AC039 |
| NFR-001 | T-17 | OpenApiTests.OpenApi_ContactsEndpoints_HaveOperationIds_NFR001, OpenApi_ContactsEndpoints_HaveNonEmptySummaries_NFR001, OpenApi_CreateContact_DocumentsStatusCodes201_400_409_NFR001, OpenApi_GetContactById_DocumentsStatusCodes200_404_NFR001 (guard), OpenApi_ListContacts_DocumentsStatusCodes200_400_NFR001 (guard) |
| NFR-002 | T-02 | CreateContactTests.CreateContact_Json_IsCamelCaseWithNullsAndUtcOffsets_NFR002 |
| NFR-003 | T-18 | **Manual check**, recorded in review.md (not automated, not a CI gate) |
| NFR-004 | all tasks (reviewer), T-10 (guard) | **Reviewer inspection** of logging code on every task; automated guard CreateContactConflictTests.CreateContact_DuplicateEmail_EmailNotLoggedAtInformationOrAbove_NFR004 |
