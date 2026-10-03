# 002: Contacts API: update and delete: Tasks

Legend: `[ ]` todo · `[~]` in progress · `[x]` done
Each task = one red → green → refactor cycle and leaves the suite green. All tasks are **API-side only** (`BACKEND_TEST_CMD`).

Paths are abbreviated as follows:
- `T/` = `tests/MicroCrm.Api.Tests/`
- `S/` = `src/MicroCrm.Api/`

Test names follow `Method_Scenario_Expected_ACnnn`.

**Guard tests.** A guard is a test expected to **pass at RED**, because an earlier task (or the existing spec 001 pipeline) already produces the behavior. Each guard is listed under its task. The test-writer reports guards in the RED evidence, separately from the failing tests. The reviewer confirms each guard would fail if its behavior regressed. Every task has at least one test that fails for the right reason.

**Minimal implementation.** Implementers add only what the task's failing tests demand. In particular:
- no OpenAPI metadata (`WithName`/`WithSummary`/`Produces*`) on PUT or DELETE before T-09;
- the PUT handler gets no validation-error branch before T-03, no not-found branch before T-04, no unique-violation catch before T-05, and no `DbUpdateConcurrencyException` catch before T-08;
- DELETE uses `ExecuteDeleteAsync` from T-06 (never load-then-remove; plan design point 3), but gets no zero-rows → 404 branch before T-07.

This keeps later tasks genuinely red.

**No schema change.** No task adds a migration. If an implementer finds one is needed, stop and report `BLOCKED`.

---

- [ ] T-01: Align validation messages on create and list to one style
  - **ACs:** AC-039 (create and list)
  - **Depends on:** none
  - **Tests:**
    - `T/Integration/Infrastructure/ProblemAssert.cs` (extend): a helper (e.g. `AssertValidationMessageStyle(JsonDocument problem)`) that checks every message in `errors`: starts with an uppercase letter; ends with `.`; doesn't contain its key quoted (`'key'`, `"key"`); doesn't start with the key or its humanized form (e.g. `firstName`, `First name`), ignoring case. (Plan design point 6: no plain "substring equals key" check, because `"Must be a valid email address."` is compliant.)
    - `T/Unit/Contacts/ContactInputTests.cs` (extend): `Parse_MissingFirstName_MessageIsRequired_AC039` (exact `"Required."`); `Parse_ValidationMessages_FollowStyle_AC039` (over-max → `"Must be {max} characters or fewer."`, invalid email → `"Must be a valid email address."`)
    - `T/Unit/Common/ListQueryTests.cs` (extend): `Parse_InvalidPage_MessageIsSentenceCaseWithoutName_AC039` (exact `"Must be an integer between 1 and 2147483647."`), same for pageSize (`"... between 1 and 100."`) and search (`"Must be 254 characters or fewer."`)
    - `T/Integration/Contacts/CreateContactValidationTests.cs` (extend): `CreateContact_ValidationMessages_FollowStyle_AC039` (missing firstName + invalid email + phone too long; style helper + `errors.firstName == ["Required."]`)
    - `T/Integration/Contacts/ListContactsPagingTests.cs` (extend): `ListContacts_ValidationMessages_FollowStyle_AC039` (`page=0&pageSize=101&search=<255 chars>`; style helper + exact messages)
  - **Likely source files:** `S/Features/Contacts/ContactInput.cs`, `S/Common/Paging.cs`
  - **Guards:** the over-max and invalid-email message assertions (already compliant).
  - **Expected RED:** `"First name is required."` ≠ `"Required."`; list messages start with a quoted lowercase name.
  - **Done when:** create and list return the canonical messages from ADR-0006; status codes and `errors` keys are unchanged; every existing test still passes. Suite green.
  - **Doc follow-through (not this cycle):** the documenter records the rule in `docs/conventions.md` (Errors row) during `/document 002` (see D-01). The implementer must not edit that file.

- [ ] T-02: Update a contact: full replace, persisted, visible in list and search
  - **ACs:** AC-001, AC-002, AC-003, AC-004, AC-005, AC-006, AC-007, AC-008, AC-009, AC-010, AC-011, NFR-002
  - **Depends on:** T-01
  - **Tests:**
    - `T/Integration/Contacts/UpdateContactTests.cs` (new; unique data per test):
      - `UpdateContact_WithValidBody_Returns200WithUpdatedContact_AC001`
      - `UpdateContact_ThenRead_ReturnsPersistedValuesOnNewConnections_AC002` (new `HttpClient` GET, list via `search`, direct `SqliteConnection` read)
      - `UpdateContact_OptionalFieldOmittedNullOrBlank_StoredAsNull_AC003` (Theory: each optional field × omitted / `null` / `""` / `"  "`, on a contact that had values)
      - `UpdateContact_WithSurroundingWhitespace_StoresTrimmed_AC004`
      - `UpdateContact_IgnoresBodyIdAndTimestamps_KeepsIdAndCreatedAt_AC005`
      - `UpdateContact_SetsUpdatedAtFromClock_EvenWhenValuesUnchanged_AC006` (`factory.Time.Advance(...)` before the PUT)
      - `UpdateContact_FieldsAtMax_Returns200_AC007` (exact max, and max wrapped in whitespace)
      - `UpdateContact_WithUppercaseEmail_PreservesCasing_AC010`
      - `UpdateContact_Json_IsCamelCaseWithNullsAndUtcOffsets_NFR002`
    - `T/Integration/Contacts/UpdateContactListEffectsTests.cs` (new; `IAsyncLifetime` calls `ResetAsync`):
      - `UpdateContact_ChangedNames_ReorderListByNewValues_AC008`
      - `UpdateContact_ChangedEmail_SearchFindsNewNotOld_AC009`
      - `UpdateContact_LeavesOtherContactsUnchanged_AC011` (raw JSON of others before/after)
  - **Likely source files:**
    - `S/Features/Contacts/ContactDtos.cs` (+ `UpdateContactRequest`)
    - `S/Features/Contacts/ContactInput.cs` (+ `Parse(UpdateContactRequest)`; both overloads share one private core)
    - `S/Features/Contacts/ContactsEndpoints.cs` (PUT `/{id:guid}`: parse, load tracked, assign all six fields, `UpdatedAt = time.GetUtcNow()`, save, `Ok`)
    - `S/MicroCrm.Api.http` (sample PUT)
  - **Size note:** many tests, one small handler. Splitting would leave tasks with only guard tests, because the shared `Parse` gives every field behavior at once.
  - **Expected RED:** every test gets 405 problem+json (only GET is mapped on `/api/contacts/{id}`).
  - **Done when:** a valid PUT to an existing contact returns 200 with the full camelCase body; values are trimmed, blank/omitted optional fields are `null`, email casing is preserved; `id`/`createdAt` never change and `updatedAt` equals the clock; the change is visible to GET, list order, and search from new connections; other contacts are untouched. Suite green.

- [ ] T-03: Update rejects invalid fields with 400 (same errors as create)
  - **ACs:** AC-017, AC-018, AC-019, AC-020, AC-021, AC-024, AC-039 (update), NFR-004, AC-037 (400)
  - **Depends on:** T-02
  - **Tests:**
    - `T/Unit/Contacts/ContactInputTests.cs` (extend): `Parse_UpdateAndCreateRequests_ProduceIdenticalErrors_NFR004` (Theory over several invalid payloads; compare dictionaries key-by-key and message-by-message)
    - `T/Integration/Contacts/UpdateContactValidationTests.cs` (new): each test seeds a contact and, after the 400, reads it through a new `SqliteConnection` to prove it's unchanged:
      - `UpdateContact_WithoutFirstName_Returns400WithFirstNameError_AC017` (Theory: missing, `null`, `""`, `"  "`)
      - `UpdateContact_FieldOverMax_Returns400WithFieldError_AC018` (Theory per field at max+1)
      - `UpdateContact_WithInvalidEmail_Returns400WithEmailError_AC019`
      - `UpdateContact_WithMultipleInvalidFields_ReturnsAllCamelCaseKeys_AC020`
      - `UpdateContact_WithMalformedBody_Returns400Problem_AC021` (raw bodies `{`, empty, `null`, `[]`, `{"firstName":123}`)
      - `UpdateContact_InvalidBodyToUnknownId_Returns400_AC024`
      - `UpdateContact_ValidationMessages_FollowStyle_AC039` (style helper from T-01)
      - `UpdateContact_SameInvalidPayload_SameErrorsAsCreate_NFR004` (POST and PUT the same payload; compare `errors`)
  - **Likely source files:** `S/Features/Contacts/ContactsEndpoints.cs` (return `TypedResults.ValidationProblem(errors)` when `Parse` fails)
  - **Guards:** AC-021 (binding failures are 400 problem+json through the spec 001 pipeline before the handler runs); the NFR-004 unit test (shared core from T-02).
  - **Expected RED:** invalid bodies reach the save path without a validation branch → 500 (null input) instead of 400.
  - **Done when:** every invalid update returns 400 validation ProblemDetails listing all offending fields by camelCase key, with ADR-0006 messages identical to create's; unreadable bodies return 400 ProblemDetails; validation is checked before existence; the stored contact never changes. Suite green.

- [ ] T-04: Update of an unknown or non-GUID id returns 404 and never creates
  - **ACs:** AC-022, AC-023, AC-025, AC-037 (404)
  - **Depends on:** T-03
  - **Tests:** `T/Integration/Contacts/UpdateContactTests.cs` (extend):
    - `UpdateContact_UnknownGuid_Returns404ProblemAndCreatesNothing_AC022` (GET on that id still 404; row count unchanged)
    - `UpdateContact_NonGuidId_Returns404Problem_AC023` (Theory: `not-a-guid`, `123`; row count unchanged)
    - `UpdateContact_ConflictingEmailToUnknownId_Returns404_AC025` (email of an existing other contact)
  - **Likely source files:** `S/Features/Contacts/ContactsEndpoints.cs` (null lookup → `TypedResults.Problem(statusCode: 404)`, a `ProblemHttpResult`; plan design point 5)
  - **Guards:** AC-023 (no route matches a non-GUID id; status code pages already return 404 problem+json).
  - **Expected RED:** null entity dereference → 500 for AC-022 and AC-025.
  - **Done when:** a PUT to a well-formed GUID with no contact returns 404 `application/problem+json`, creates nothing, and takes precedence over an email conflict; a non-GUID id returns 404. Suite green.

- [ ] T-05: Update email uniqueness, including concurrent creates and updates
  - **ACs:** AC-012, AC-013, AC-014, AC-015, AC-016, AC-034, AC-037 (409), AC-038 (update), NFR-003 (guard)
  - **Depends on:** T-04
  - **Tests:**
    - `T/Integration/Contacts/UpdateContactConflictTests.cs` (new; unique emails per test):
      - `UpdateContact_SameEmailDifferentCaseOrWhitespace_Returns200_AC012`
      - `UpdateContact_ChangeOnlyCaseOfOwnEmail_Returns200AndStoresNewCasing_AC013`
      - `UpdateContact_EmailOfAnotherContact_Returns409ProblemAndLeavesContactUnchanged_AC014` (body doesn't echo the email; direct read shows the original row)
      - `UpdateContact_ToNoEmail_AlwaysSucceeds_AC015` (several contacts → no email)
      - `UpdateContact_OldEmailCanBeReusedAfterChangeOrRemoval_AC016` (by a create and by another update)
      - `CreateAndUpdate_ConcurrentSameEmail_AtMostOneHoldsIt_AC034` (plan design point 2: K PUTs + M POSTs released together; zero 5xx; exactly one 200/201; the rest 409 problem+json; one row holds the email; losers keep their original email)
      - `UpdateContact_DuplicateEmail_EmailNotLoggedAtInformationOrAbove_NFR003` (capturing `ILoggerProvider` via `WithWebHostBuilder`, as in spec 001's NFR-004 guard)
    - `T/Integration/ErrorHandlingTests.cs` (extend):
      - existing `ErrorHandlingTests` class: `UpdateContact_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC038` (`DROP TABLE`)
      - new class with its own fixture: `UpdateContact_WhenNonUniqueConstraintFails_Returns500AndLeavesContactUnchanged_AC038` (seed a contact, then install `BEFORE UPDATE ON Contacts ... RAISE(ABORT, 'x')`; PUT → 500 problem+json, not 409, no internals; direct read shows the row unchanged)
  - **Likely source files:** `S/Features/Contacts/ContactsEndpoints.cs` (catch `DbUpdateException` when `SqliteErrors.IsUniqueConstraintViolation` → `TypedResults.Problem(statusCode: 409, title: "A contact with this email already exists.")`; other exceptions re-thrown)
  - **Guards:** AC-012, AC-013, AC-015, AC-016 (the unique index already allows these); both AC-038 tests (existing exception handler; 1811 isn't 2067); NFR-003.
  - **Expected RED:** AC-014 and AC-034 get 500 from the unhandled unique violation.
  - **Done when:** an update to another contact's email (case/whitespace ignored) returns 409 ProblemDetails and changes nothing; self-matches, case-only changes, and email removal succeed; freed emails are reusable; concurrent create/update races never produce a 5xx and never let two contacts hold one email; non-unique DB failures on update stay safe 500s with data unchanged. Suite green.

- [ ] T-06: Delete a contact
  - **ACs:** AC-026, AC-027, AC-028, AC-029, AC-032
  - **Depends on:** T-05
  - **Tests:** `T/Integration/Contacts/DeleteContactTests.cs` (new; `IAsyncLifetime` calls `ResetAsync`):
    - `DeleteContact_Existing_Returns204WithEmptyBody_AC026`
    - `DeleteContact_ThenGet_Returns404OnNewConnections_AC027` (new `HttpClient`; row count 0 via new `SqliteConnection`)
    - `DeleteContact_ExcludedFromListAndSearch_TotalCountDrops_AC028`
    - `DeleteContact_EmailCanBeReusedByCreateOrUpdate_AC029` (case-variant email; create → 201, update of another contact → 200)
    - `DeleteContact_LeavesOtherContactsUnchanged_AC032`
  - **Likely source files:**
    - `S/Features/Contacts/ContactsEndpoints.cs` (DELETE `/{id:guid}` → `ExecuteDeleteAsync`, `TypedResults.NoContent()`)
    - `S/MicroCrm.Api.http` (sample DELETE)
  - **Expected RED:** 405 problem+json.
  - **Done when:** deleting an existing contact returns 204 with no body; the contact is gone for GET, list, search, and `totalCount` on new connections; its email is free again; other contacts are untouched. Suite green.

- [ ] T-07: Delete of unknown, already-deleted, or non-GUID ids; no resurrection by PUT; concurrent deletes
  - **ACs:** AC-030, AC-031, AC-033, AC-036, AC-037 (404 on delete), AC-038 (delete)
  - **Depends on:** T-06
  - **Tests:**
    - `T/Integration/Contacts/DeleteContactTests.cs` (extend):
      - `DeleteContact_UnknownOrAlreadyDeleted_Returns404Problem_AC030`
      - `DeleteContact_NonGuidId_Returns404AndDeletesNothing_AC031` (Theory: `not-a-guid`, `123`; count unchanged)
      - `UpdateContact_AfterDelete_Returns404AndDoesNotRecreate_AC033`
      - `DeleteContact_ConcurrentSameId_ExactlyOne204Rest404_AC036` (10 DELETEs behind a shared gate; zero 5xx)
    - `T/Integration/ErrorHandlingTests.cs` (extend):
      - existing `ErrorHandlingTests` class: `DeleteContact_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC038`
      - new class with its own fixture: `DeleteContact_WhenDatabaseRejects_Returns500AndContactStillExists_AC038` (`BEFORE DELETE ON Contacts ... RAISE(ABORT, 'x')`)
  - **Likely source files:** `S/Features/Contacts/ContactsEndpoints.cs` (zero affected rows → `TypedResults.Problem(statusCode: 404)`)
  - **Guards:** AC-031 (no route match); AC-033 (T-04's 404 branch); both AC-038 tests (existing exception handler).
  - **Expected RED:** AC-030 gets 204 instead of 404; AC-036 gets ten 204s.
  - **Done when:** deleting a missing or already-deleted id returns 404 ProblemDetails; non-GUID ids return 404 and delete nothing; a PUT after delete is 404 and doesn't recreate; concurrent deletes give exactly one 204; DB failures on delete are safe 500s and leave the row in place. Suite green.

- [ ] T-08: An update racing a delete never returns 500 or recreates the contact
  - **ACs:** AC-035
  - **Depends on:** T-07
  - **Tests:** `T/Integration/Contacts/UpdateDeleteRaceTests.cs` (new):
    - `UpdateContact_DeletedBetweenLoadAndSave_Returns404AndStaysDeleted_AC035`: deterministic. A test-local `SaveChangesInterceptor` (registered through `factory.WithWebHostBuilder(... ConfigureTestServices(s => s.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(...))))`) deletes the target row through a separate `SqliteConnection` in `SavingChangesAsync`, once. Assert PUT 404 problem+json, then GET 404.
    - `UpdateAndDelete_Concurrent_ReturnDocumentedCodes_AC035`: interleaving-invariant smoke over N contacts (one PUT + one DELETE each, released together): PUT ∈ {200, 404}, DELETE ∈ {204, 404}, zero 5xx, every 204 followed by GET 404.
  - **Likely source files:** `S/Features/Contacts/ContactsEndpoints.cs` (catch `DbUpdateConcurrencyException` → `TypedResults.Problem(statusCode: 404)`, placed before the unique-violation catch)
  - **Guards:** the concurrent smoke may pass at RED (timing-dependent); the deterministic test must fail.
  - **Expected RED:** the deterministic test gets 500 (`DbUpdateConcurrencyException`: zero rows affected).
  - **Done when:** an update that loses a race with a delete returns 404 ProblemDetails, the contact stays deleted, and concurrent update/delete pairs only ever return their documented codes. Suite green.

- [ ] T-09: OpenAPI describes update and delete, with ProblemDetails error responses
  - **ACs:** NFR-001, AC-037 (documented content type)
  - **Depends on:** T-08
  - **Tests:** `T/Integration/OpenApiTests.cs` (extend):
    - `OpenApi_UpdateAndDelete_HaveOperationIdsAndSummaries_NFR001` (`UpdateContact` on `put`, `DeleteContact` on `delete` of `/api/contacts/{id}`; non-empty summaries)
    - `OpenApi_UpdateContact_DocumentsStatusCodes200_400_404_409_NFR001`
    - `OpenApi_DeleteContact_DocumentsStatusCodes204_404_NFR001`
    - `OpenApi_UpdateAndDelete_ErrorResponsesAreProblemJson_NFR001` (PUT 400/404/409 and DELETE 404 list `application/problem+json` under `content`)
  - **Likely source files:** `S/Features/Contacts/ContactsEndpoints.cs` (`.WithName`, `.WithSummary`, `.ProducesProblem(404)` on both, `.ProducesProblem(409)` on PUT)
  - **Guards:** PUT 200/400 and DELETE 204 codes (from typed results); the PUT 400 problem+json content (from `ValidationProblem` metadata).
  - **Expected RED:** missing operationIds and summaries; 404 and 409 absent from the document.
  - **Done when:** the Development OpenAPI document lists `UpdateContact` and `DeleteContact` with summaries, the status codes required by NFR-001, and `application/problem+json` for every documented error response on these two operations. Suite green.

NFR-003 has no task of its own. The reviewer inspects every task's logging (no request bodies, emails, or stored field values at Information or above; `EnableSensitiveDataLogging` never enabled), and T-05 adds an automated guard.

### Documentation task (documenter, during `/document 002`; not a red-green cycle)
- [ ] D-01: Record the validation message style and the new endpoints in the docs
  - **ACs:** AC-039 (convention record), spec Constraints ("`docs/conventions.md` (Errors row) records the message style")
  - **Depends on:** T-01..T-09
  - **Files:** `docs/conventions.md` (Errors row: sentence case, uppercase start, period end, no field/parameter name or quoted key, canonical messages; reference ADR-0006), `docs/adr/0006-validation-message-style.md` (status → Accepted once the plan is approved, if not already), `docs/architecture.md` (Contacts row and Key flows: update, delete), `CHANGELOG.md` (Added: PUT/DELETE; Changed: old → new validation messages)
  - **Done when:** conventions, architecture, ADR status, and CHANGELOG match the code, verified against it.

## Traceability
| AC | Task(s) | Test(s) (filled in during build) |
|---|---|---|
| AC-001 | T-02 | |
| AC-002 | T-02 | |
| AC-003 | T-02 | |
| AC-004 | T-02 | |
| AC-005 | T-02 | |
| AC-006 | T-02 | |
| AC-007 | T-02 | |
| AC-008 | T-02 | |
| AC-009 | T-02 | |
| AC-010 | T-02 | |
| AC-011 | T-02 | |
| AC-012 | T-05 (guard) | |
| AC-013 | T-05 (guard) | |
| AC-014 | T-05 | |
| AC-015 | T-05 (guard) | |
| AC-016 | T-05 (guard) | |
| AC-017 | T-03 | |
| AC-018 | T-03 | |
| AC-019 | T-03 | |
| AC-020 | T-03 | |
| AC-021 | T-03 (guard) | |
| AC-022 | T-04 | |
| AC-023 | T-04 (guard) | |
| AC-024 | T-03 | |
| AC-025 | T-04 | |
| AC-026 | T-06 | |
| AC-027 | T-06 | |
| AC-028 | T-06 | |
| AC-029 | T-06 | |
| AC-030 | T-07 | |
| AC-031 | T-07 (guard) | |
| AC-032 | T-06 | |
| AC-033 | T-07 (guard) | |
| AC-034 | T-05 | |
| AC-035 | T-08 | |
| AC-036 | T-07 | |
| AC-037 | T-03, T-04, T-05, T-07, T-09 | Via `ProblemAssert.IsProblemAsync` in every PUT/DELETE error test (400, 404, 409, 500) |
| AC-038 | T-05 (guard), T-07 (guard) | |
| AC-039 | T-01, T-03, D-01 (convention record) | |
| NFR-001 | T-09 | |
| NFR-002 | T-02 | |
| NFR-003 | all tasks (reviewer), T-05 (guard) | **Reviewer inspection** of logging code on every task; automated guard in T-05 |
| NFR-004 | T-03 | |
