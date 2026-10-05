# 002: Review log

## T-01: 2026-10-03: APPROVE

**Scope:** Align the validation messages on create and list to one style (AC-039, the create and list parts). I reviewed the uncommitted diff on `feat/002-contacts-api-update-delete`. I read it against the spec, plan design point 6, tasks.md T-01 and ADR-0006. The human approved Q-A option a: `"Must be a valid email address."` is compliant, and the tests check the style rules plus the exact messages rather than doing a plain no-key-substring check.

**Checks:**
- **Tests:** `dotnet test --solution MicroCrm.slnx` passed, 184/184 (0 failed, 0 skipped).
- **Lint:** `dotnet format --verify-no-changes` exit 0, and `npm --prefix web run lint` is clean.
- **Typecheck:** `tsc -b` is clean (the web side is unchanged).

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-039 (create) | `ContactInputTests.Parse_MissingFirstName_MessageIsRequired_AC039`, `ContactInputTests.Parse_ValidationMessages_FollowStyle_AC039` (guard), `CreateContactValidationTests.CreateContact_ValidationMessages_FollowStyle_AC039` | Yes. See the first note below. |
| AC-039 (list) | `ListQueryTests.Parse_InvalidPage_MessageIsSentenceCaseWithoutName_AC039` (4 rows: page 0/abc, pageSize 0/101), `ListQueryTests.Parse_SearchTooLong_MessageIsSentenceCaseWithoutName_AC039`, `ListContactsPagingTests.ListContacts_ValidationMessages_FollowStyle_AC039` | Yes. See the first note below. |
| AC-039 (update) | Not in scope. It is scheduled for T-03. | n/a |

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Nit | implementer | `src/MicroCrm.Api/Common/Paging.cs:11-12` | The comment says "otherwise a named error is reported". After ADR-0006 the message no longer names the parameter. Only the `errors` key does. The comment now slightly misleads. | Reword it to "...otherwise an error is reported under that key", or similar. This is optional. |

**Notes:**
- **Every production change is pinned by an exact-message assertion.** That covers `firstName` → `Required.`, page and pageSize (different min and max values, so a hard-coded bound would fail), and search → `Must be 254 characters or fewer.`. Each is pinned at both the unit and the integration level. The reported RED (8 failures on the wording) is consistent with this.
- **The style helper is not weaker than ADR-0006 requires.** `AssertValidationMessageStyle` fails on both old message shapes: `'page' must…` fails on the lowercase start and the quoted key, and `First name is required.` fails on the humanized-prefix check. It also asserts that `errors` exists and is non-empty, so it can't pass vacuously. Its known blind spot is a mid-sentence bare key, which the human accepted under Q-A. The exact-message assertions cover that spot.
- **Guards are real.** The email and max-length message assertions pass at RED, because they already complied. Each would fail if its message regressed, since they are exact `Assert.Equal` checks.
- **No test was removed or weakened.** The diff only adds tests. `IsProblemAsync` is unchanged, so the exact key-set check still holds and the status codes and `errors` keys are unchanged (as the spec requires).
- **Minimality is clean.** There is no `UpdateContactRequest`, no `Parse` overload, and no PUT/DELETE code. The only production change is message text in the two files the plan lists.
- **No new logging.** The NFR-003 inspection is clean: the diff adds no logging at all.
- **No stale references to the old wording** in `docs/*.md`, `web/src` or the `.http` file. Spec 001's review.md and tasks.md still quote the old messages, but they are historical records and are left as they are.
- **Doc follow-through:** `docs/conventions.md` (the Errors row) still lacks the rule. That is D-01, which belongs to `/document 002`, not this task.

## T-02: 2026-10-03: CHANGES_REQUESTED

**Scope:** Update a contact: full replace, persisted, visible in list and search (AC-001..AC-011, NFR-002). I reviewed the uncommitted T-02 diff on `feat/002-contacts-api-update-delete`: `ContactDtos.cs`, `ContactInput.cs`, `ContactsEndpoints.cs`, `MicroCrm.Api.http`, the `Paging.cs` comment, and the new `UpdateContactTests.cs` and `UpdateContactListEffectsTests.cs`. I read them against the spec, plan design points 1 and 5, and the tasks.md "Minimal implementation" rules. Known deferrals (unknown id → 500 via `FirstAsync` until T-04, no 409 until T-05, no race catch until T-08) were not judged.

**Checks:**
- **Tests:** `dotnet test --solution MicroCrm.slnx` passed, 216/216 (0 failed, 0 skipped).
- **Lint:** `dotnet format --verify-no-changes` exit 0, and `npm --prefix web run lint` exit 0.
- **Typecheck:** `npm --prefix web run typecheck` exit 0.
- **Mutation (scratch copy, source untouched):** see the table below.

| Mutant | Failing tests |
|---|---|
| Remove each of the six field assignments | firstName 6, lastName 10, email 12, phone 10, company 10, notes 9 |
| `Notes = input.Notes ?? contact.Notes` (keep old value when blank) | 5 |
| `Email = request.Email` (untrimmed raw) | 5 |
| Remove `UpdatedAt = time.GetUtcNow()` | 2 |
| Also set `CreatedAt = UpdatedAt` | 2 |
| Remove `SaveChangesAsync` | 27 |
| **Remove the validation-error branch** (`var input = parsed!;`) | **0** |
| **Remove `.WithName("UpdateContact").WithSummary(...)` from PUT** | **0** |
| Both of the two above | **0** |

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-001 | `UpdateContact_WithValidBody_Returns200WithUpdatedContact_AC001` | Yes |
| AC-002 | `UpdateContact_ThenRead_ReturnsPersistedValuesOnNewConnections_AC002` (new client GET, search list, direct `SqliteConnection` row read) | Yes. The no-save mutant fails it. |
| AC-003 | `UpdateContact_OptionalFieldOmittedNullOrBlank_StoredAsNull_AC003` (5 fields × omitted/null/""/whitespace, on a fully populated seed) | Yes. The keep-old-value mutant fails it. |
| AC-004 | `UpdateContact_WithSurroundingWhitespace_StoresTrimmed_AC004` | Yes |
| AC-005 | `UpdateContact_IgnoresBodyIdAndTimestamps_KeepsIdAndCreatedAt_AC005` (also proves the body `id` wasn't created) | Yes |
| AC-006 | `UpdateContact_SetsUpdatedAtFromClock_EvenWhenValuesUnchanged_AC006` | Yes |
| AC-007 | `UpdateContact_FieldsAtMax_Returns200_AC007` (exact max, and max wrapped in whitespace) | Yes |
| AC-008 | `UpdateContactListEffectsTests.UpdateContact_ChangedNames_ReorderListByNewValues_AC008` (last-name move and first-name move within a shared last name) | Yes |
| AC-009 | `UpdateContactListEffectsTests.UpdateContact_ChangedEmail_SearchFindsNewNotOld_AC009` (with an old-email precondition) | Yes |
| AC-010 | `UpdateContact_WithUppercaseEmail_PreservesCasing_AC010` | Yes |
| AC-011 | `UpdateContactListEffectsTests.UpdateContact_LeavesOtherContactsUnchanged_AC011` (raw JSON before/after, clock advanced) | Yes |
| NFR-002 | `UpdateContact_Json_IsCamelCaseWithNullsAndUtcOffsets_NFR002` | Yes |

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Blocking | implementer | `src/MicroCrm.Api/Features/Contacts/ContactsEndpoints.cs:135-139` | The validation-error branch (`if (input is null) return TypedResults.ValidationProblem(errors!);`) is added at T-02. The tasks.md "Minimal implementation" rule says: "the PUT handler gets no validation-error branch before T-03". No T-02 test demands it (mutation: 0 failures). With it in place, T-03's Expected RED ("invalid bodies reach the save path ... → 500") can't happen, so T-03's tests would all pass at RED. That breaks the "every task has a failing test" rule and constitution §2. | Use the spec 001 shape: `var (parsed, _) = ContactInput.Parse(request); var input = parsed!;`, with the return type `Task<Ok<ContactResponse>>` (or `Results<Ok<ContactResponse>>`). That compiles under TreatWarningsAsErrors, and the full suite stays 216/216 (verified in scratch). T-03 adds the branch back. |
| 2 | Blocking | implementer | `src/MicroCrm.Api/Features/Contacts/ContactsEndpoints.cs:25-27` | `.WithName("UpdateContact").WithSummary("Replace a contact")` is on PUT at T-02. The tasks.md rule says: "no OpenAPI metadata (`WithName`/`WithSummary`/`Produces*`) on PUT or DELETE before T-09". No test demands it (mutation: 0 failures), and it pre-satisfies part of T-09's RED (`OpenApi_UpdateAndDelete_HaveOperationIdsAndSummaries_NFR001`). This repeats the spec 001 T-02 `WithName` drift. | Map with `group.MapPut("/{id:guid}", UpdateContact);` only. T-09 adds the metadata. |
| 3 | Nit | implementer | `src/MicroCrm.Api/MicroCrm.Api.http:5` | The sample PUT targets the all-zero GUID, which can never exist (500 today, 404 after T-04), so it can't demonstrate a successful update. The host port (5185) also differs from `DEV_API_CMD` (5080). That's carried-over spec 001 drift. | Optional: add a `@contactId = ...` variable with a "paste an id from POST" comment, and align the port. Otherwise leave it for `/document`. |

**Notes:**
- **Shared core is correct (NFR-004 groundwork).** Both public `Parse` overloads delegate to one private `Parse(string? ×6)`, so create and update can't drift. The T-01 message changes are unchanged. The NFR-004 test itself belongs to T-03.
- **Full replace is correct.** All six fields are assigned from `ContactInput`. `UpdateContactRequest` has no `Id`/`CreatedAt`/`UpdatedAt`, so body values for them are dropped at binding (AC-005 proves it).
- **Nothing that later tasks must undo, beyond findings 1 and 2.** `FirstAsync` on a tracked query is the right base for T-04 (swap to `FirstOrDefaultAsync` + 404) and T-08 (the `DbUpdateConcurrencyException` comes from a tracked save). There's no try/catch, no pre-check query, and no schema change.
- **NFR-003:** the diff adds no logging. There's no `ILogger` in `Features/Contacts/`.
- **Test quality:** the tests are deterministic. `UpdateContactTests` uses unique tags per test on a shared fixture, and `ListEffects` resets in `InitializeAsync`. They assert real HTTP and DB state with no mocks. No existing test was edited or weakened, and the T-01 test diffs are unchanged.
- **The T-01 Nit is closed.** The `Paging.cs:12` comment now says "an error is reported under that key".
- **After the fix:** a re-review only needs to confirm findings 1 and 2 are removed and the suite is still 216/216. No test changes are needed.

### T-02 fix cycle 1: 2026-10-03: APPROVE

**Checks:**
- **Tests:** `dotnet test --solution MicroCrm.slnx` passed, 216/216 (0 failed, 0 skipped).
- **Lint:** `dotnet format --verify-no-changes` exit 0, and `npm --prefix web run lint` exit 0.
- **Typecheck:** `npm --prefix web run typecheck` exit 0.

**Findings closed:**
- **Finding 1 is closed.** `UpdateContact` now returns `Task<Ok<ContactResponse>>` and uses `var (parsed, _) = ContactInput.Parse(request); var input = parsed!;`. There is no validation branch (`ContactsEndpoints.cs:131-132`), so T-03's 500 RED is restored.
- **Finding 2 is closed.** PUT is mapped as `group.MapPut("/{id:guid}", UpdateContact);` with no metadata (`ContactsEndpoints.cs:25`). A grep shows `WithName`/`WithSummary`/`Produces*` only on the spec 001 endpoints, so T-09's RED is restored.

**Other checks:**
- **Tests are untouched.** Only `ContactsEndpoints.cs` changed in the fix. `UpdateContactTests.cs` and `UpdateContactListEffectsTests.cs` haven't been modified since the first review (mtime 14:54, before the review at 14:59). The test count is unchanged.
- **Mutation evidence still holds.** The handler body below the parse is identical to the version I mutation-tested in the first review.
- **The handler has no try/catch and no 404 branch,** which is consistent with the T-04/T-05/T-08 deferrals.

**Remaining finding:** Finding 3 (the `.http` sample uses the all-zero id and port 5185) is a Nit that was left as is by choice. I've routed it to `/document 002`.

## T-03: 2026-10-03: APPROVE

**Scope:** Update rejects invalid fields with 400, using the same errors as create. ACs: AC-017..AC-021, AC-024, AC-039 (update), NFR-004, and AC-037 (400). I reviewed the uncommitted T-03 changes on top of the approved T-01 and T-02:
- `ContactsEndpoints.cs` (validation branch before the lookup);
- the new `UpdateContactValidationTests.cs`;
- the NFR-004 theory in `ContactInputTests.cs`.

**Checks:**
- **Tests:** `dotnet test --solution MicroCrm.slnx` passed, 241/241 (0 failed, 0 skipped). The count reconciles: 216, plus 20 new integration rows, plus 5 new unit rows. No test was removed.
- **Lint:** `dotnet format --verify-no-changes` exit 0, and `npm --prefix web run lint` exit 0.
- **Typecheck:** `npm --prefix web run typecheck` exit 0.
- **Reported RED:** 15 failures (500 instead of 400). This matches the tasks.md Expected RED. The validation branch arrived with failing tests, which closes the T-02 follow-up.
- **Mutation (scratch copy, source untouched):**

| Mutant | Failing tests |
|---|---|
| DB lookup before `Parse` | 1 (AC-024) |
| Update overload treats a blank firstName as `"x"` | 7 |
| Update overload drops phone | 27 |
| `ValidationProblem` with only the first error | 4 |

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-017 | `UpdateContact_WithoutFirstName_Returns400WithFirstNameError_AC017` (missing, null, "", whitespace; exact key set; direct row read unchanged, including `UpdatedAt`) | Yes |
| AC-018 | `UpdateContact_FieldOverMax_Returns400WithFieldError_AC018` (6 fields at max+1; row unchanged) | Yes |
| AC-019 | `UpdateContact_WithInvalidEmail_Returns400WithEmailError_AC019` | Yes |
| AC-020 | `UpdateContact_WithMultipleInvalidFields_ReturnsAllCamelCaseKeys_AC020` (exact 6-key set) | Yes. The first-error-only mutant fails it. |
| AC-021 | `UpdateContact_WithMalformedBody_Returns400Problem_AC021` (5 raw bodies; this is a guard through the spec 001 binding pipeline; row unchanged) | Yes, as a guard |
| AC-024 | `UpdateContact_InvalidBodyToUnknownId_Returns400_AC024` | Yes. The order mutant fails it. |
| AC-039 (update) | `UpdateContact_ValidationMessages_FollowStyle_AC039` (style helper, plus exact `Required.`) | Yes. See note 1. |
| NFR-004 | `ContactInputTests.Parse_UpdateAndCreateRequests_ProduceIdenticalErrors_NFR004` (5 rows, a guard through the shared core); `UpdateContact_SameInvalidPayload_SameErrorsAsCreate_NFR004` (raw `errors` JSON of POST vs PUT) | Yes. Both overload-divergence mutants are killed. |
| AC-037 (400) | Every test above goes through `ProblemAssert.IsProblemAsync` (content type, `type`/`title`/`status`) | Yes |

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Nit | test-writer | `tests/MicroCrm.Api.Tests/Integration/Contacts/UpdateContactValidationTests.cs` (AC-039 test) | Only the `firstName` message is compared exactly. The `email` and `phone` messages are pinned only by the style helper. They are pinned transitively, though: the NFR-004 integration test compares PUT's raw `errors` with POST's, and T-01 pins POST's exact messages. So nothing is uncovered. | Optional: add exact asserts for `Must be a valid email address.` and `Must be 50 characters or fewer.`. |
| 2 | Nit | test-writer | `UpdateContactValidationTests.cs` (AC-021 seed tag) | `Math.Abs(raw.GetHashCode())` is used as a uniqueness tag. A hash collision between two rows would cause a duplicate seed email (409), and `Math.Abs(int.MinValue)` throws. Both are vanishingly unlikely. | Optional: pass an explicit tag through `InlineData`. |

**Notes:**
- **Minimality is clean.** The PUT handler has no 404 branch, no 409 catch, no `DbUpdateConcurrencyException` catch, and no OpenAPI metadata (the grep shows `WithName`/`WithSummary`/`Produces*` only on the spec 001 endpoints). The return type only gains `ValidationProblem`, which the new 400 demands. `ContactInput.cs` and `ContactDtos.cs` are unchanged since T-02.
- **Check order matches spec Q2.** Binding comes first (before the handler), then `Parse`, then the lookup. The lookup-first mutant proves AC-024 pins this order.
- **The NFR-004 unit theory is a declared guard.** It passes because of T-02's shared core. Both divergence mutants kill it as well as the integration test.
- **"Contact unchanged" is checked strictly.** `ReadRowAsync` compares every field plus `UpdatedAt` through an independent connection.
- **NFR-003:** the diff adds no logging. There's no `ILogger` in `Features/Contacts/`.
- **No skipped tests, and no existing test was changed.**

## T-04: 2026-10-03: APPROVE

**Scope:** Update of an unknown or non-GUID id returns 404 and never creates. ACs: AC-022, AC-023, AC-025, AC-037 (404). I reviewed the uncommitted T-04 changes on top of the approved T-01..T-03:
- `ContactsEndpoints.cs`: a tracked `FirstOrDefaultAsync`, then a null check that returns `TypedResults.Problem(statusCode: 404)`;
- three tests appended to `UpdateContactTests.cs`.

**Checks:**
- **Tests:** `dotnet test --solution MicroCrm.slnx` passed, 245/245 (0 failed, 0 skipped). That's 241 plus 4 new rows. The file grew only by the appended tests.
- **Lint:** `dotnet format --verify-no-changes` exit 0, and `npm --prefix web run lint` exit 0.
- **Typecheck:** `npm --prefix web run typecheck` exit 0.
- **Reported RED:** AC-022 and AC-025 failed with 500 instead of 404, which matches the tasks.md Expected RED. AC-023 is a planned guard: `{id:guid}` doesn't match, so status-code pages return 404 problem+json.
- **Mutation (scratch copy, source untouched):**

| Mutant | Failing tests | Meaning |
|---|---|---|
| Upsert on null (add a new contact with the URL id) | 2 | Killed. |
| `TypedResults.NotFound()` instead of `Problem(404)` | 0 | Equivalent at runtime: status-code pages fill an empty 404 with the same problem+json (spec 001 fact). Not drift. The handler must return something on this path, and plan design point 5 prescribes `ProblemHttpResult` for the OpenAPI shape that T-09 tests. |

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-022 | `UpdateContact_UnknownGuid_Returns404ProblemAndCreatesNothing_AC022` (404 problem+json; a later GET still returns 404; row count unchanged) | Yes. The upsert mutant fails it. |
| AC-023 | `UpdateContact_NonGuidId_Returns404Problem_AC023` (`not-a-guid`, `123`; row count unchanged) | Yes, as a guard. It would fail if the `:guid` constraint were dropped (a binding 400). |
| AC-025 | `UpdateContact_ConflictingEmailToUnknownId_Returns404_AC025` (another contact's email in a different case; row count unchanged) | Yes. It pins existence-before-conflict for T-05: a pre-check or a 409 mapping placed before the lookup would fail it. |
| AC-037 (404) | All three, through `ProblemAssert.IsProblemAsync` (content type and `type`/`title`/`status`) | Yes |

**Findings:** none.

**Notes:**
- **Minimality is clean.** On PUT there is no unique-violation catch, no `DbUpdateConcurrencyException` catch, and no `WithName`/`WithSummary`/`Produces*`. The only `catch` and `ProducesProblem` in the file belong to `CreateContact` (spec 001). The return type gains only `ProblemHttpResult`.
- **Check order matches spec Q2.** Binding comes first, then `Parse` (400; AC-024 still passes), then the lookup (404), then save. The lookup stays tracked, which T-08's `DbUpdateConcurrencyException` path needs.
- **NFR-003:** the diff adds no logging.

## T-05: 2026-10-03: APPROVE

**Scope:** Update email uniqueness, including concurrent creates and updates. ACs: AC-012..AC-016, AC-034, AC-037 (409), AC-038 (update), NFR-003. I reviewed the uncommitted T-05 changes on top of the approved T-01..T-04:
- `ContactsEndpoints.cs`: a filtered unique-violation catch around the PUT save, and a `DuplicateEmailProblem()` helper now shared with create;
- the new `UpdateContactConflictTests.cs`;
- two AC-038 tests in `ErrorHandlingTests.cs`, one of them in the new `NonUniqueUpdateConstraintFailureTests` fixture.

**Checks:**
- **Tests:** `dotnet test --solution MicroCrm.slnx` passed 254/254 on each of two consecutive runs I made. AC-034 is a race test, so I repeated it. The tracked test diffs are insertions only (226+, 0−).
- **Lint:** `dotnet format --verify-no-changes` exit 0, and `npm --prefix web run lint` exit 0.
- **Typecheck:** `npm --prefix web run typecheck` exit 0.
- **Reported RED:** AC-014 and AC-034 failed with 500, which matches the tasks.md Expected RED. The other seven tests are the planned guards.
- **Mutation (scratch copy, source untouched):**

| Mutant | Failing tests | Meaning |
|---|---|---|
| Update catch with **no filter** (`catch (DbUpdateException)`) | 1: `NonUniqueUpdateConstraintFailureTests…_AC038` | Killed. The broader filter turns the trigger's 1811 into a 409. |
| Update filter **broadened** to base code 19 (any constraint) | 1 (same test) | Killed. This closes the spec 001 T-10 lesson for update. |
| Update catch removed | 2 (AC-014, AC-034) | Killed. |
| Email pre-check query placed **before** the lookup, returning 409 | 1: `UpdateContact_ConflictingEmailToUnknownId_Returns404_AC025` | Killed. Existence-before-conflict is pinned, and AC-025 still returns 404 on the real code. |
| `DuplicateEmailProblem` title changed, or title removed | 0 | Survives. See finding 1. |

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-012 | `UpdateContact_SameEmailDifferentCaseOrWhitespace_Returns200_AC012` (3 variants, with a peer contact present) | Yes, as a guard. It would fail if the update didn't exclude its own row, for example with a pre-check that has no `Id !=`. |
| AC-013 | `UpdateContact_ChangeOnlyCaseOfOwnEmail_Returns200AndStoresNewCasing_AC013` (response and direct row) | Yes, as a guard |
| AC-014 | `UpdateContact_EmailOfAnotherContact_Returns409ProblemAndLeavesContactUnchanged_AC014` (case- and whitespace-variant; body doesn't echo the email; direct row and GET unchanged; holder count 1) | Yes |
| AC-015 | `UpdateContact_ToNoEmail_AlwaysSucceeds_AC015` (3 contacts set to null) | Yes, as a guard |
| AC-016 | `UpdateContact_OldEmailCanBeReusedAfterChangeOrRemoval_AC016` (reuse by a case-variant create and by an update) | Yes, as a guard |
| AC-034 | `CreateAndUpdate_ConcurrentSameEmail_AtMostOneHoldsIt_AC034` (5 PUT + 5 POST behind a ready barrier and gate, mixed casing) | Yes. It asserts zero 5xx, exactly one success, the rest 409 problem+json, one row holding the email, and losers keeping their original email. It was stable on two runs. |
| AC-037 (409) | AC-014 and AC-034 through `ProblemAssert.IsProblemAsync` | Yes |
| AC-038 (update) | `ErrorHandlingTests.UpdateContact_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC038` (DROP TABLE, guard); `NonUniqueUpdateConstraintFailureTests.UpdateContact_WhenNonUniqueConstraintFails_Returns500AndLeavesContactUnchanged_AC038` (BEFORE UPDATE RAISE(ABORT); not 409; no internals; row unchanged) | Yes. Both filter-broadening mutants fail the second test. |
| NFR-003 | `UpdateContact_DuplicateEmail_EmailNotLoggedAtInformationOrAbove_NFR003` (marker email; the non-vacuity check requires EF command logs at Information or above). I also inspected the code: no `ILogger` in `Features/Contacts/`. | Yes, as a guard |

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Nit | test-writer | `src/MicroCrm.Api/Features/Contacts/ContactsEndpoints.cs` (`DuplicateEmailProblem`) | Changing or removing the 409 title fails no test. This was already true for create in spec 001, and no AC fixes the title text. AC-037 only requires that a `title` exists, and the framework default "Conflict" satisfies that. The extraction itself is behavior-preserving: the diff moves the identical text. | Optional: assert the title in one create and one update 409 test, if the UI (spec 005) will show it. |

**Notes:**
- **Minimality is clean.** There's no `DbUpdateConcurrencyException` catch (T-08) and no `WithName`/`WithSummary`/`Produces*` on PUT (T-09). The grep shows metadata only on the spec 001 endpoints. There's no pre-check query: the database stays the arbiter, as plan design point 2 says.
- **Create's 409 is unchanged.** It goes through the same helper with the same status and title, and the spec 001 create-conflict tests still pass.
- **Check order is intact.** It's 400 → 404 → 409, and the catch sits only around `SaveChangesAsync`, after the lookup.
- **The AC-038 trigger test runs in its own fixture,** so the BEFORE UPDATE trigger can't leak into other classes. It verifies the row through an independent connection.

## T-06: 2026-10-03: APPROVE

**Scope:** Delete a contact. ACs: AC-026, AC-027, AC-028, AC-029, AC-032. I reviewed the uncommitted T-06 changes on top of the approved T-01..T-05:
- `ContactsEndpoints.cs`: `MapDelete("/{id:guid}", DeleteContact)`, which runs `Where(c => c.Id == id).ExecuteDeleteAsync(ct)` and then returns `NoContent`;
- a sample DELETE in `MicroCrm.Api.http`;
- the new `DeleteContactTests.cs` (5 tests).

**Checks:**
- **Tests:** `dotnet test --solution MicroCrm.slnx` passed, 259/259 (0 failed, 0 skipped). That's 254 plus 5.
- **No earlier tests touched:** the tracked test diffs are insertions only, and no earlier test file's mtime changed.
- **Lint:** `dotnet format --verify-no-changes` exit 0, and `npm --prefix web run lint` exit 0.
- **Typecheck:** `npm --prefix web run typecheck` exit 0.
- **Reported RED:** all 5 tests failed with 405, which matches the tasks.md Expected RED.
- **Mutation (scratch copy, source untouched):**

| Mutant | Failing tests | Meaning |
|---|---|---|
| No-op delete (`NoContent` only) | 4 (AC-027, AC-028, AC-029, AC-032) | Killed |
| Delete all rows | 3 (AC-028, AC-029, AC-032) | Killed |
| Wrong predicate (`Id != id`) | 4 | Killed |
| Load-then-remove (`FindAsync` + `Remove` + `SaveChangesAsync`) | 0 | Expected at T-06: sequential deletes behave identically. The code complies with plan design point 3. **Carry-forward:** T-07's `DeleteContact_ConcurrentSameId_ExactlyOne204Rest404_AC036` must kill this mutant, because the losers' zero-row save would throw `DbUpdateConcurrencyException` and return 500. |

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-026 | `DeleteContact_Existing_Returns204WithEmptyBody_AC026` | Yes. It pins the status and empty body. AC-027 pins persistence. |
| AC-027 | `DeleteContact_ThenGet_Returns404OnNewConnections_AC027` (new client GET → 404 problem+json; row count 0 via an independent connection) | Yes |
| AC-028 | `DeleteContact_ExcludedFromListAndSearch_TotalCountDrops_AC028` (list total 2→1; search items and totalCount 0; has a precondition) | Yes |
| AC-029 | `DeleteContact_EmailCanBeReusedByCreateOrUpdate_AC029` (case- and whitespace-variant reuse by create → 201 and by update → 200) | Yes |
| AC-032 | `DeleteContact_LeavesOtherContactsUnchanged_AC032` (raw JSON of the others before and after, with the clock advanced) | Yes |

**Findings:** none new. The `.http` Nit from T-02 extends to the sample DELETE, which uses the all-zero GUID. It's still routed to `/document 002`.

**Notes:**
- **Minimality is clean.** DELETE has no zero-rows → 404 branch (T-07), no catch, and no `WithName`/`WithSummary`/`Produces*` (T-09). The return type is just `NoContent`. A DELETE to an unknown id currently returns 204; that is the known T-07 deferral.
- **The delete is a single statement,** as plan design point 3 requires. That gives T-07 the affected-row count it needs.
- **NFR-003:** the diff adds no logging, and DELETE takes no request body.
- **Tests are isolated.** `DeleteContactTests` resets in `InitializeAsync`, so the absolute totals in AC-028 are deterministic.

## T-07: 2026-10-03: APPROVE

**Scope:** Delete of unknown, already-deleted, or non-GUID ids; no resurrection by PUT; concurrent deletes. ACs: AC-030, AC-031, AC-033, AC-036, AC-037 (404 on delete), AC-038 (delete). I reviewed the uncommitted T-07 changes on top of the approved T-01..T-06:
- `ContactsEndpoints.cs`: `DeleteContact` now returns `Results<NoContent, ProblemHttpResult>`, with a single `ExecuteDeleteAsync` where 0 rows gives `Problem(404)`;
- 4 tests appended to `DeleteContactTests.cs`;
- 2 AC-038 tests in `ErrorHandlingTests.cs`, one of them in the new `DeleteRejectedByDatabaseTests` fixture.

**Checks:**
- **Tests:** `dotnet test --solution MicroCrm.slnx` passed 266/266 on each of two consecutive runs I made. That's 259 plus 7 rows.
- **Lint:** `dotnet format --verify-no-changes` exit 0, and `npm --prefix web run lint` exit 0.
- **Typecheck:** `npm --prefix web run typecheck` exit 0.
- **No tests removed:** the tracked test diffs are 273 insertions and 0 deletions.
- **Reported RED:** AC-030 got 204 and AC-036 got ten 204s, which matches the tasks.md Expected RED. AC-031, AC-033 and both AC-038 tests are the planned guards.
- **Mutation (scratch copy, source untouched):**

| Mutant | Failing tests | Meaning |
|---|---|---|
| Load-then-remove (`FindAsync`; null → 404; `Remove` + `SaveChangesAsync`) | 1 on each of **5 of 5** runs: `DeleteContact_ConcurrentSameId_ExactlyOne204Rest404_AC036` | Killed reliably. This closes the T-06 carry-forward. |
| Broad `catch (Exception)` → 404 on DELETE | 2: both delete AC-038 tests | Killed |
| `catch (SqliteException)` → 409 on DELETE | 2 (same tests) | Killed |

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-030 | `DeleteContact_UnknownOrAlreadyDeleted_Returns404Problem_AC030` (an unknown v7 id, and the same id deleted twice) | Yes |
| AC-031 | `DeleteContact_NonGuidId_Returns404AndDeletesNothing_AC031` (`not-a-guid`, `123`; total unchanged) | Yes, as a guard through the `:guid` constraint and status-code pages |
| AC-033 | `UpdateContact_AfterDelete_Returns404AndDoesNotRecreate_AC033` (PUT 404 problem+json; GET 404; row count 0; list total 0) | Yes, as a guard. The T-04 upsert mutant would fail it. |
| AC-036 | `DeleteContact_ConcurrentSameId_ExactlyOne204Rest404_AC036` (10 DELETEs behind a ready barrier and gate; zero 5xx; exactly one 204; nine 404 problem+json; row gone) | Yes. Stable on two clean runs, and it kills load-then-remove on every run. |
| AC-037 (404 on delete) | AC-030 and AC-031 through `ProblemAssert.IsProblemAsync` | Yes |
| AC-038 (delete) | `ErrorHandlingTests.DeleteContact_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC038` (DROP TABLE); `DeleteRejectedByDatabaseTests.DeleteContact_WhenDatabaseRejects_Returns500AndContactStillExists_AC038` (BEFORE DELETE RAISE(ABORT); no internals; row still present) | Yes. Both catch mutants fail them. |

**Findings:** none.

**Notes:**
- **Minimality is clean.** DELETE has no catch and no `WithName`/`WithSummary`/`Produces*` (T-09). The only two `catch` blocks in the file are the filtered unique-violation catches on create and update. PUT still has no `DbUpdateConcurrencyException` catch (T-08).
- **The delete is a single statement,** so 204 vs 404 comes from the affected-row count, as plan design point 3 requires. There's no read-then-delete window.
- **The migration log line is pre-existing noise, not a concern for this task.** The line reads "The migration operation 'PRAGMA foreign_keys = 0;' from migration 'AddContactEmailUniqueIndex' cannot be executed in a transaction". I reproduced it with `dotnet ef database update` on an empty scratch DB. It is EF Core's standard warning for the SQLite table rebuild that `AlterColumn` produces. It is emitted for both spec 001 `AlterColumn` migrations (`AddContactNameCollation` and `AddContactEmailUniqueIndex`). `src/MicroCrm.Api/Data/` is unchanged against `main`, and spec 002 adds no migration. The only risk it describes is a partially applied migration if the process dies mid-rebuild. That's acceptable for a local SQLite dev DB. It could be noted in `docs/architecture.md` at `/document`, but it isn't a spec 002 finding.
- **NFR-003:** the diff adds no logging.

## T-08: 2026-10-03: APPROVE

**Scope:** An update racing a delete never returns 500 or recreates the contact (AC-035). I reviewed the uncommitted T-08 changes on top of the approved T-01..T-07:
- `ContactsEndpoints.cs`: PUT gains `catch (DbUpdateConcurrencyException)` → `Problem(404)`, placed before the unchanged unique-violation catch;
- the new `UpdateDeleteRaceTests.cs`: a deterministic interceptor test plus a 10-pair concurrent smoke.

**Checks:**
- **Tests:** `dotnet test --solution MicroCrm.slnx` passed 268/268 on each of two consecutive runs I made, on top of the implementer's three. That's 266 plus 2.
- **Lint:** `dotnet format --verify-no-changes` exit 0, and `npm --prefix web run lint` exit 0.
- **Typecheck:** `npm --prefix web run typecheck` exit 0.
- **No tests or dependencies touched:** the tracked test diffs have 0 deleted lines, no earlier test file changed since T-07, and the test csproj is unchanged. No new dependency: `ConfigureDbContext` is EF Core 10, which plan design point 3 already covers.
- **Reported RED:** the deterministic test got 500 on every run. The smoke failed on some runs, as tasks.md anticipated for a timing-dependent guard.
- **Mutation (scratch copy, source untouched):**

| Mutant | Failing tests | Meaning |
|---|---|---|
| Concurrency catch broadened to `catch (DbUpdateException)` → 404 (swallows everything) | 3: AC-014, AC-034, `NonUniqueUpdateConstraintFailureTests…_AC038` | Killed. 409 and 500 stay separate from 404. |
| Broadened to `catch (DbUpdateException ex) when (!IsUniqueConstraintViolation(ex))` → 404, placed first | 1: `NonUniqueUpdateConstraintFailureTests…_AC038` | Killed. A non-unique constraint failure stays 500, not 404. |
| Concurrency catch removed | 2: both AC-035 tests | Killed. On this run the smoke also hit the window. |
| Catch order swapped (unique first, then concurrency) | 0 | Equivalent, as plan line 105 predicts. `DbUpdateConcurrencyException` has no `SqliteException` inner exception, so the 2067 filter can't match it. The implemented order is the one the plan prescribes. Not a finding. |

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-035 | `UpdateContact_DeletedBetweenLoadAndSave_Returns404AndStaysDeleted_AC035`: deterministic. A test-local `SaveChangesInterceptor`, registered via `ConfigureDbContext` on a derived host that shares the fixture DB, deletes the row once through a separate connection. It asserts PUT 404 problem+json, then GET 404, then row count 0. | Yes. It's the reliable killer of the missing catch. |
| AC-035 | `UpdateAndDelete_Concurrent_ReturnDocumentedCodes_AC035`: 10 PUT/DELETE pairs behind a ready barrier and gate. PUT must be 200 or 404, DELETE 204 or 404, and every 204 must be followed by a GET 404. | Yes, as a timing-dependent guard. The "never recreates" half of the AC is covered by both tests. |

**Findings:** none.

**Notes:**
- **Minimality is clean.** `WithName`/`WithSummary`/`Produces*` still appear only on the three spec 001 endpoints, so PUT and DELETE are bare until T-09. No other handler changed.
- **The order of checks matches spec Q2, plus the race.** It's binding 400 → `Parse` 400 → lookup 404 → save, with the concurrency catch giving 404 and the unique catch giving 409. Everything else propagates to the exception handler as 500.
- **The failed save rolls back cleanly.** The tracked entity dies with the request scope, and the deterministic test proves the row is not re-inserted.
- **NFR-003:** the diff adds no logging. The catch comment holds no data.

## T-09: 2026-10-03: APPROVE

**Scope:** OpenAPI describes update and delete, with ProblemDetails error responses. ACs: NFR-001, AC-037 (documented content type). I reviewed the uncommitted T-09 changes on top of the approved T-01..T-08:
- metadata on the PUT and DELETE mappings in `ContactsEndpoints.cs`;
- 4 tests appended to `OpenApiTests.cs`.

**Checks:**
- **Tests:** `dotnet test --solution MicroCrm.slnx` passed, 272/272 (0 failed, 0 skipped). That's 268 plus 4. The tracked test diffs have 0 deleted lines.
- **Lint:** `dotnet format --verify-no-changes` exit 0, and `npm --prefix web run lint` exit 0.
- **Typecheck:** `npm --prefix web run typecheck` exit 0.
- **Reported RED:** all 4 tests failed (null operationIds, missing 404/409, missing problem+json), which matches the tasks.md Expected RED.
- **Mutation (scratch copy, source untouched), each metadata call removed on its own:**

| Removed | Failing tests |
|---|---|
| PUT `.WithName("UpdateContact")` | 1: `…HaveOperationIdsAndSummaries_NFR001` |
| PUT `.WithSummary(...)` | 1 (same test) |
| PUT `.ProducesProblem(404)` | 2: `…UpdateContact_DocumentsStatusCodes200_400_404_409_NFR001` and `…ErrorResponsesAreProblemJson_NFR001` |
| PUT `.ProducesProblem(409)` | 2 (same two tests) |
| DELETE `.WithName("DeleteContact")` | 1: `…HaveOperationIdsAndSummaries_NFR001` |
| DELETE `.WithSummary(...)` | 1 (same test) |
| DELETE `.ProducesProblem(404)` | 2: `…DeleteContact_DocumentsStatusCodes204_404_NFR001` and `…ErrorResponsesAreProblemJson_NFR001` |

Every call is demanded by a test. Nothing extra was added: there's no `.ProducesValidationProblem()` (the 400 problem+json comes from the `ValidationProblem` typed result), and no `.WithTags`. That's consistent with the spec 001 T-17 OpenAPI facts.

- **Generated document (probe in scratch):**
  - PUT `UpdateContact` "Replace a contact": 200 `application/json`; 400, 404 and 409 `application/problem+json`.
  - DELETE `DeleteContact` "Delete a contact": 204 with no content; 404 `application/problem+json`.

  Each has a single 404 entry with problem+json content, as plan design point 4 intends.

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| NFR-001 | `OpenApi_UpdateAndDelete_HaveOperationIdsAndSummaries_NFR001`, `OpenApi_UpdateContact_DocumentsStatusCodes200_400_404_409_NFR001`, `OpenApi_DeleteContact_DocumentsStatusCodes204_404_NFR001` | Yes |
| AC-037 (documented content type) | `OpenApi_UpdateAndDelete_ErrorResponsesAreProblemJson_NFR001` (PUT 400/404/409, DELETE 404) | Yes. The PUT 400 part is a guard through `ValidationProblem` metadata. |

**Findings:** none.

**Notes:**
- **The diff is metadata-only.** The only change in `ContactsEndpoints.cs` is the two mapping chains. The handlers are unchanged since T-08, and `GetContactById` is untouched.
- **GET `/api/contacts/{id}` still documents a content-less 404** (spec 001 T-17 behavior; the runtime body is problem+json). That's out of scope for spec 002. It could be harmonized in a later spec if the UI's generated client needs it. Recorded as a follow-up, not a finding.

## FINAL: 2026-10-03: APPROVE

**Scope:** Whole-spec review of 002 (AC-001..AC-039, NFR-001..NFR-004). I reviewed `git diff main` plus the untracked test files on `feat/002-contacts-api-update-delete`. Tasks T-01..T-09 are each approved above. D-01 (docs) runs after this review via `/document 002`, so doc drift is listed as documenter items below, not as findings.

**Checks:**
- **Backend tests:** `dotnet test --solution MicroCrm.slnx` passed, 272/272 (0 failed, 0 skipped).
- **Frontend tests:** `npm --prefix web test` passed, 1/1. The web side is unchanged by this spec.
- **Lint:** `dotnet format --verify-no-changes` exit 0, and `npm --prefix web run lint` exit 0.
- **Typecheck:** `npm --prefix web run typecheck` exit 0.
- **Skipped tests:** a grep for `Skip =` / `Explicit` under `tests/` finds nothing.

**Traceability (two-way, scripted):**
- **Forward:** all 56 `Class.Method` entries in the tasks.md Traceability table resolve to real test methods in the named class files. `ProblemAssert.IsProblemAsync` (the AC-037 row) is the shared helper, which is accepted under constitution §2 "names or comments", as in spec 001.
- **Reverse:** every `_ACnnn`/`_NFRnnn` method added on this branch (56) appears in the table, and the table names no method outside that set. I scoped this to branch additions because spec 001 reuses AC numbers.
- **Every ID is covered:** all 43 spec IDs (AC-001..AC-039, NFR-001..NFR-004) have at least one test in the table. NFR-003 also has reviewer inspection, recorded on every task.

**Spec conformance (end to end):**
- **Check order for PUT (spec Q2):**
  1. Binding/unreadable body → 400 (status-code pages; 415 for a wrong content type).
  2. `ContactInput.Parse` → 400 validation.
  3. Tracked lookup → 404.
  4. Save: `DbUpdateConcurrencyException` → 404, unique violation (2067) → 409, everything else → safe 500.

  Mutants pinned each step at T-03 (AC-024), T-04/T-05 (AC-025), T-05 (AC-038 trigger) and T-08 (AC-035).
- **Full replace and no upsert:** all six fields are assigned; `UpdateContactRequest` has no system fields; a missing contact is never created (T-04 upsert mutant killed; AC-022/AC-033).
- **DELETE:** a single `ExecuteDeleteAsync`, where the affected-row count gives 204 or 404. There's no read-then-delete (the load-then-remove mutant is killed by AC-036 on 5/5 runs), and a request body is ignored (probe: DELETE with body → 204).
- **One source of field rules (NFR-004):** both `Parse` overloads delegate to one private core. Messages follow ADR-0006 on create, update and list.
- **Unlink rule for spec 003:** recorded as a user decision in spec.md "Decision: deleting a contact and its to-dos", with the minimum ACs spec 003 must carry. It isn't yet reflected in `docs/roadmap.md` (documenter item 5).
- **No schema change:** `git diff main -- src/MicroCrm.Api/Data/` is empty, and no migration was added.
- **Error-class probe** (scratch, beyond the ACs). Every response below is `application/problem+json` with no internals:
  - PUT with text/plain → 415;
  - PUT with no body → 400;
  - PATCH or POST on `/{id}` → 405 with `Allow: DELETE,GET,PUT`;
  - PUT or DELETE on the collection → 405 with `Allow: GET,POST`;
  - wrong JSON type → 400;
  - 2 MB `notes` → 400 validation `Must be 4000 characters or fewer.`.

**Constitution:**
- **§2 tests:** `git diff main -- tests/` has 0 deleted lines, so no test was removed, edited or weakened. `ProblemAssert` was only extended (the style helper), and `IsProblemAsync` is unchanged. Every task had a reported failing test before its code.
- **§3 separation of duties:** the reviewer was read-only throughout. All mutation work was done in scratch copies.
- **§4 small steps:** each task was one red-green cycle. The T-02 early-code drift was caught and reverted in fix cycle 1. Commits are left to the human (`COMMIT_PER_TASK=0`).
- **§5 quality floor:**
  - No new dependencies: no `*.csproj`, `*.props`, `web/package*.json` or `.config/` changes.
  - No secrets.
  - No logging: there's no `ILogger` or `EnableSensitiveDataLogging` in `src/`, and the NFR-003 guard passes.
  - Errors are handled explicitly: only filtered catches, and everything else goes to the exception handler.
- **§6 docs:** pending D-01 (see the documenter items below).

**Findings:** none Blocking, none Should-fix. Open Nits carried from task reviews, all optional:
| # | Severity | Owner | Location | Issue |
|---|---|---|---|---|
| 1 | Nit | test-writer | `UpdateContactValidationTests.cs` (AC-039) | Only `Required.` is compared exactly. The email and phone messages are pinned transitively through NFR-004 and T-01 (from T-03). |
| 2 | Nit | test-writer | `UpdateContactValidationTests.cs` (AC-021 seed tag) | `Math.Abs(raw.GetHashCode())` is used as a uniqueness tag, with a vanishingly small collision/overflow risk (from T-03). |
| 3 | Nit | test-writer | `ContactsEndpoints.cs` `DuplicateEmailProblem` | The 409 title text is unpinned for create and update. That's spec 001 legacy, and no AC fixes it (from T-05). |
| 4 | Nit | documenter | `src/MicroCrm.Api/MicroCrm.Api.http` | The sample PUT and DELETE use the all-zero GUID. The host port 5185 differs from `DEV_API_CMD` (5080) (from T-02/T-06). |

**Documenter items (D-01, `/document 002`):**
1. **`docs/conventions.md` Errors row:** add the ADR-0006 message style (sentence case, uppercase start, period end, no field/parameter name or quoted key; canonical messages; reference ADR-0006).
2. **`docs/architecture.md`:**
   - Contacts row: "update/delete planned (spec 002)" → built.
   - Key flows: add Update and Delete. The order is 400 → 404 → 409. Concurrency → 404. Delete is a single statement with row count → 204/404.
   - "Last updated".
   - Optionally note the EF "PRAGMA foreign_keys = 0 ... cannot be executed in a transaction" warning from the spec 001 `AlterColumn` migrations (pre-existing, see T-07).
3. **`CHANGELOG.md` [Unreleased]:**
   - Added: `PUT /api/contacts/{id}` (200/400/404/409) and `DELETE /api/contacts/{id}` (204/404), plus the OpenAPI operations `UpdateContact` and `DeleteContact`.
   - Changed: the old → new validation messages (create `firstName`; list `page`/`pageSize`/`search`).
   - Re-check the spec 001 bullet "return 400 naming the parameter": the message no longer names it; only the `errors` key does.
4. **ADR-0006:** already `Accepted (2026-10-03)`. No action.
5. **`docs/roadmap.md`:** row 002's note "Decide what deleting a contact does to its to-dos" is now decided (Unlink). Add to row 003 that spec 003 must enforce the Unlink rule from spec 002.
6. **The `.http` file:** Nit 4 above.
7. **Spec 002 Implementation notes:** link the modules and tests. Set spec.md status → Done and mark D-01 [x] once the docs are verified.

**Notes:**
- **AC-037:** traced through the shared `ProblemAssert.IsProblemAsync` on every PUT/DELETE 400, 404, 409 and 500 test, and documented in OpenAPI (T-09).
- **GET `/api/contacts/{id}`:** still documents a content-less 404 in OpenAPI (spec 001). Out of scope here; a candidate for a later spec.
