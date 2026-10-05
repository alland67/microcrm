# 003: Review log

## T-01: 2026-10-05: APPROVE

**Checks:** tests pass, backend 284/284 (scratch-copy baseline also 284/284) · lint pass (`dotnet format --verify-no-changes` exit 0; `oxlint` clean, no web changes) · typecheck pass (`tsc -b`, no web changes)

**Migration checks:**
- `dotnet ef migrations has-pending-model-changes`: no changes since the last migration.
- `migrations script AddContactNameCollation CreateTodos`: one `CREATE TABLE "Todos"`, with the PK, `CK_Todos_DoneState`, and `FK_Todos_Contacts_ContactId ... ON DELETE SET NULL` inline. Then `IX_Todos_ContactId` and `IX_Todos_IsDone_DueDate (IsDone, DueDate)`, and the history insert. This matches the plan's schema block column for column.
- **No statement touches `Contacts`:** no `AlterColumn`, no rebuild, no `PRAGMA foreign_keys` toggle, so the ADR-0008 hazard is not triggered.
- `database update` on an empty scratch DB gives the planned `.schema Todos`, and `PRAGMA foreign_key_list(Todos)` gives `Contacts|ContactId|Id|NO ACTION|SET NULL`.
- `database update AddContactNameCollation` (rollback) drops only `Todos`, leaving `Contacts` and the history for the 3 earlier migrations intact.

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-062 | `DeleteContact_WithLinkedTodos_Returns204AndUnlinks_AC062` | yes: killed by the CASCADE and RESTRICT mutants |
| AC-065 (failed delete rolls back) | `DeleteContact_FailsAfterUnlink_RollsBackRemovalAndUnlink_AC065`, `DeleteContact_FailsDuringUnlink_RollsBackRemoval_AC065` | yes: a non-atomic app-side unlink before `ExecuteDeleteAsync` (M10) is killed by the after-unlink variant. The "never a half state on success" half is T-13 per plan. |
| AC-066 (store level) | `Store_TodosContactId_IsForeignKeyToContactsWithSetNull_AC066`, `Store_DeletingContactDirectly_UnlinksTodosAndKeepsOtherColumns_AC066`, `Api_ForeignKeysDisabledInConnectionString_StillEnforced_AC066`, guard `Store_TestConnections_HaveForeignKeysOn_AC066` | yes: see mutants M1–M4 |
| AC-067 (store level) | `Store_LinkToMissingContact_IsRejectedByStore_AC067` | yes: 787 asserted exactly. Killed when test connections have FKs off (M2). |
| AC-069 (store level) | `Store_InconsistentDoneState_IsRejectedByStore_AC069` (2 rows) | yes: both rows killed by a weakened CHECK (M5) |
| NFR-003 (indexes exist) | `Store_TodosIndexes_SupportUnlinkAndFilters_NFR003` | yes: column order pinned (M6 killed). The timing check is at FINAL per plan. |
| AC-044 (sortable column types) | `Store_TodosColumns_MatchPlannedTypes_AC044` | yes: names, declared types, and NOT NULL for all 9 columns. Tick timestamps (`INTEGER`) can only come from the converters. |

**Guard verification (`Store_TestConnections_HaveForeignKeysOn_AC066`):**
- The guard doesn't depend on `Todos`, so it passes at RED, as reported.
- The real regression it protects against is the bundled native SQLite defaulting to `foreign_keys = 0`. That can't be swapped in, so I simulated it by appending `;Foreign Keys=False` to `ApiFactory.ConnectionString` in a scratch copy (M2).
- Result: the guard fails, together with `Store_DeletingContactDirectly_..._AC066` and `Store_LinkToMissingContact_..._AC067`. The guard is real, and it also gives the explicit diagnosis the plan wants, rather than leaving AC-066/AC-067 failing for an unclear reason.

**Mutation evidence (scratch copy; source untouched):**
| # | Mutant | Failing tests |
|---|---|---|
| M1 | `Program.cs`: drop `ForeignKeys = true` | 1: `Api_ForeignKeysDisabled..._AC066` (shows the `UseSetting` override reaches the derived host) |
| M2 | Test connection string `;Foreign Keys=False` | 3: guard, direct-delete AC-066, AC-067 |
| M3 | `SET NULL` → `CASCADE` (config + migration + designer + snapshot) | 5: FK pragma, AC-062, direct AC-066, derived-host AC-066, AC-065 during-unlink |
| M4 | `SET NULL` → `RESTRICT` (all four files) | 4: FK pragma, AC-062, direct AC-066, derived-host AC-066 |
| M5 | CHECK weakened to ignore `CompletedAt` (all four files) | 2: both AC-069 rows |
| M6 | Index columns swapped to `(DueDate, IsDone)` (all four files) | 1: NFR-003 |
| M8 | Remove `DbSet<Todo> Todos` | 188 (the table name falls back to `Todo`, which raises a pending-model-changes error). The `DbSet` is load-bearing; it determines the table name. |
| M9 | Remove `.HasDatabaseName("IX_Todos_IsDone_DueDate")` | 0 (equivalent: EF's default name is identical; see finding 1) |
| M10 | `ContactsEndpoints.DeleteContact`: app-side `ExecuteUpdateAsync` unlink before the delete (non-atomic) | 1: `DeleteContact_FailsAfterUnlink_..._AC065` |

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Nit | implementer | src/MicroCrm.Api/Data/TodoConfiguration.cs:26 | `.HasDatabaseName("IX_Todos_IsDone_DueDate")` equals EF's conventional name, so removing it changes nothing (M9: 0 failures, no pending-model change). It's harmless, and it does pin the plan's name against a future convention change. | None required. Keep or drop. |

**Notes:**
- **Minimality is clean:**
  - `Todo.cs` is properties only.
  - There are no endpoints, no `MapTodosEndpoints`, no OpenAPI metadata, and no 787 or concurrency catch.
  - `ApiFactory.ResetAsync` is not extended (that's T-02).
  - The contacts endpoints are unchanged.
- The tick converters are demanded by the AC-044 column-type test. Their read-side round-trip is first exercised through the API at T-02 (AC-001/NFR-002).
- `Program.cs` wraps the configured string exactly as the plan prescribes (`SqliteConnectionStringBuilder { ForeignKeys = true }`). `appsettings.json` is unchanged.
- **Test isolation:**
  - Both new classes use their own `IClassFixture<ApiFactory>`, which gives a unique in-memory DB.
  - Emails are unique within each class.
  - The triggers are dropped in `finally`.
  - The derived host in the `Foreign Keys=False` test is disposed with `using`, and it shares the parent's in-memory DB through the parent's keep-alive connection.
- `TodoStoreTests` exposes `internal static` seeding helpers that `ContactDeleteAtomicityTests` reuses. That's acceptable for now. If later to-do test classes need the same raw-SQL seeding, moving the helpers to `Integration/Infrastructure` would be a reasonable test-writer refactor (not a finding).

## T-02: 2026-10-05: APPROVE

**Checks:** tests pass, backend 322/322 · lint pass (`dotnet format --verify-no-changes` exit 0; `oxlint` exit 0, no web changes) · typecheck pass (`tsc -b` exit 0, no web changes)

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-001 | `CreateTodo_WithTitleNotesAndDueDate_Returns201WithLocationAndBody_AC001`, `CreateTodo_ThenGet_ReturnsSameValuesOnNewConnections_AC001` | yes: all nine members, Location path (relative or absolute, the same as `CreateContactTests`), raw-body equality on GET from a new client. Killed by M1 (no save) and M7 (GET ignores id). |
| AC-002 | `CreateTodo_WithOnlyTitle_ReturnsNullsAndIsDoneFalse_AC002` | yes: members present as JSON null, `isDone` is JSON `false` |
| AC-003 | `CreateTodo_IgnoresBodyIdTimestampsAndDoneState_AC003` | yes: killed by M5 (`updatedAt` ≠ `createdAt`) and M6 (wall clock instead of `TimeProvider`) |
| AC-004 | `CreateTodo_WithSurroundingWhitespace_StoresTrimmed_AC004` (4 rows), `Parse_TrimsTitleAndNotes_BlankNotesBecomeNull_AC004` | yes: killed by M3 (blank notes not nulled) and M4 (title not trimmed). Stored values are checked through GET. |
| AC-005 | `CreateTodo_WithValidDueDate_ReturnsSameString_AC005` (5 rows), `CreateTodo_WithDueDateTodayOrYesterday_ReturnsSameString_AC005` (2 rows), `Parse_ValidDueDate_ReturnsDateOnly_AC005` | yes: killed by M2 (no trim) and M9 (due date dropped) |
| AC-006 | `CreateTodo_DueDateOmittedNullOrBlank_IsNull_AC006` (4 rows), `Parse_BlankDueDate_ReturnsNull_AC006` | yes |
| AC-009 | `CreateTodo_FieldsAtMax_Returns201_AC009` (exact, and wrapped in whitespace) | yes for T-02. The max-length *rejection* side is T-03 (AC-011). |
| AC-018 | `GetTodo_Existing_Returns200WithShape_AC018` | yes: killed by M7 and M10 (GET body differs from create) |
| AC-019 | guards `GetTodo_UnknownGuid_Returns404Problem_AC019`, `GetTodo_NonGuidId_Returns404Problem_AC019` (2 rows) | yes: the unknown-GUID guard is killed by M7, and the non-GUID guard by M11 (route constraint removed, which gives a binding 400) |
| AC-072 (404) | the AC-019 tests via `ProblemAssert.IsProblemAsync` | yes |
| AC-073 (create, get) | `CreateTodo_WhenDatabaseFails_..._AC073`, `GetTodo_WhenDatabaseFails_..._AC073` (own fixture, `DROP TABLE IF EXISTS Todos`) | yes: problem+json 500 with no internals. M1 also kills the create test (no DB call, so 201). The "row count unchanged" half is the T-04 trigger test, per tasks.md. |
| NFR-002 | `CreateTodo_Json_IsCamelCaseWithNullsDateOnlyAndUtcOffsets_NFR002` | yes: raw string for `dueDate`, `isDone`, nulls, and UTC offsets. The tick converters round-trip through GET (AC-001 raw equality). |
| NFR-004 (guard) | `CreateTodo_TitleAndNotesNotLoggedAtInformationOrAbove_NFR004` | yes: it asserts 201 plus a live EF command-log canary, so it can't pass vacuously. Killed by M8 (an `LogInformation` of title and notes). Inspection: no logging code in `src/`, and `EnableSensitiveDataLogging` is not used. |

**Mutation evidence (scratch copy; source untouched):**
| # | Mutant | Failing tests |
|---|---|---|
| M1 | Remove `SaveChangesAsync` | 12 |
| M2 | Due date not trimmed | 2 (integration + unit AC-005) |
| M3 | Notes `Trim()` without null mapping | 4 |
| M4 | Title not trimmed | 4 (incl. AC-009 whitespace row) |
| M5 | `UpdatedAt = now.AddTicks(10)` | 1 (AC-003) |
| M6 | `DateTimeOffset.UtcNow` instead of `TimeProvider` | 1 (AC-003) |
| M7 | GET `FirstOrDefaultAsync(ct)` (ignores id) | 11 |
| M8 | `LogInformation` of title and notes on create | 1 (NFR-004) |
| M9 | `DueDate` not assigned | 10 |
| M10 | GET body differs from stored (notes upper-cased) | 3 |
| M11 | Route `/{id}` instead of `/{id:guid}` | 2 (non-GUID guard rows) |
| M12 | `DateOnly.TryParse` (lenient) instead of `TryParseExact` | 0: expected. AC-012 strictness is T-03's RED target. |

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Nit | implementer | src/MicroCrm.Api/Features/Todos/TodoInput.cs:22 | `TrimToNull` is a byte-for-byte copy of the private helper in `ContactInput.cs:74`. Two 3-line copies are tolerable, and conventions say to extract only when duplication justifies it. | None now. If a third copy appears (for example `TodoListQuery` for `status`/`contactId` at T-11), extract a shared helper into `Common/`. |

**Notes:**
- **The interim items the orchestrator asked about are accepted:**
  - `var (input, _) = TodoInput.Parse(request);` with `input!` is the same accepted shape as spec 001 T-02/T-05 and spec 002 T-02. `Errors` is always null here, so the handler has no validation branch, as tasks.md requires before T-03.
  - An invalid `dueDate` currently parses to `null` and gives 201. This is the planned placeholder, not drift. T-03's AC-012 tests will fail for the right reason (201 instead of 400), and M12 confirms that strictness isn't yet pinned. T-03 must make both the strict format and the error branch arrive red-first.
- **Minimality is clean:**
  - There's no OpenAPI metadata, no validation branch, no `contactId` parse or assign, no 787 catch, and no contact pre-check.
  - `CreateTodoRequest.ContactId` exists as a DTO member only. The unit tests' four-argument constructor demands it, and nothing reads it. A side effect is that `{"contactId":7}` is now a binding 400, which is the behavior AC-015 wants anyway.
  - `TodoResponse.ContactId` is demanded by the AC-001/AC-002 shape.
  - `TodoInput` omits `ContactId` and the `TitleMax`/`NotesMax` constants. Both are correctly deferred (T-04 and T-03).
- **Location:** the relative `/api/todos/{id}` matches `ContactsEndpoints.cs:129`. It's better than building from the Host header, which a client controls. Relaxing the test to accept a relative or absolute URI isn't a weakening, because the AC requires the path, not an absolute URI, and the path is still asserted exactly (case-insensitive only for the GUID).
- `ApiFactory.ResetAsync` clears `Todos` before `Contacts`, as the plan specifies. No existing class creates to-dos, so the existing contact tests are unaffected (322/322).
- The 404 is `TypedResults.Problem(statusCode: 404)`, as the plan prescribes (runtime-equivalent to `NotFound()`, as accepted at spec 002 T-04).
- `.http` additions match the plan (dev convenience, no tests).

## T-03: 2026-10-05: CHANGES_REQUESTED → APPROVE (fix cycle 1)

**Checks:** tests pass, backend 361/361 · lint pass (`dotnet format --verify-no-changes` exit 0; no web changes) · typecheck n/a (no web changes)

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-010 (create) | `CreateTodo_WithoutTitle_Returns400Required_AC010` (missing, `null`, `""`, `"  "`), `Parse_MissingTitle_ReturnsRequired_AC010` (3 rows) | yes: exact message and row count unchanged. Killed by M5 (no Required) and M14 (`title is null` only). |
| AC-011 (create) | `CreateTodo_FieldOverMax_Returns400_AC011`, `CreateTodo_OnlyNotesOverMax_ReportsOnlyNotes_AC011`, `Parse_OverMax_ReturnsMaxMessage_AC011`, `Parse_AtMax_AfterTrimming_IsAccepted_AC011` | yes: killed by M6 (no title max), M7 (no notes max), M10 (`>=`), and M11 (max on the untrimmed title) |
| AC-012 (create) | `CreateTodo_InvalidDueDate_Returns400WithDueDateError_AC012` (9 rows), `Parse_InvalidDueDate_ReturnsDateMessage_AC012` (9 rows) | yes: killed by M4 (lenient `TryParse`) and M8 (bad date silently null). The 0001/9999 accept boundaries are pinned by the T-02 AC-005 rows. |
| AC-014 (create, no contactId) | `CreateTodo_MultipleInvalidFields_ReportsAllCamelCaseKeys_AC014`, `Parse_MultipleErrors_ReturnsAllKeys_AC014` | yes: killed by an early return after title (M9) and an early return after notes (M9b) |
| AC-015 (create) | guard `CreateTodo_MalformedBody_Returns400Problem_AC015` (6 rows) | yes: 400 problem+json, no `errors`, row count unchanged. It passed at RED as a guard, as tasks.md expects. |
| AC-072 (400) | `CreateTodo_Returns400AsProblemJson_AC072` plus every 400 above via `ProblemAssert.IsProblemAsync` | yes: killed by M12 (no validation branch, which gives 500) |
| AC-074 (create) | `CreateTodo_ValidationMessages_FollowStyle_AC074` | yes: style helper plus exact messages for all four rules |

**Mutation (scratch copy, full backend suite per mutant):**
| # | Mutant | Tests failed |
|---|---|---|
| M1 | drop `value.Length == DateFormat.Length` | **0** |
| M2 | drop the ASCII `[0-9-]` check | **0** |
| M3 | both dropped (the plan/ADR-0007 form: `TryParseExact` alone) | **0** |
| M4 | lenient `DateOnly.TryParse` | 11 |
| M5 | no `Required.` on title | 11 |
| M6 | no title max | 3 |
| M7 | no notes max | 6 |
| M8 | invalid date silently null | 21 |
| M9 / M9b | early return after title / after notes | 5 / 3 |
| M10 | `>=` max | 3 |
| M11 | title max checked before trim | 2 |
| M12 | handler validation branch removed | 18 |
| M13 | due date not trimmed | 2 |
| M14 | whitespace-only title accepted | 8 |

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Blocking | implementer | src/MicroCrm.Api/Features/Todos/TodoInput.cs:55-56 | `TryParseDate` adds a length-10 pre-check and an ASCII `[0-9-]` pre-check in front of `DateOnly.TryParseExact`. No test demands either (M1, M2, and M3 each fail 0 tests), and the code differs from the rule that plan.md ("`TodoInput.Parse` rules": `TryParseExact(...)`, "strictness verified") and ADR-0007 prescribe. ADR-0007's claim is confirmed: on .NET 10, `TryParseExact` alone rejects `226-10-05`, `02026-10-05`, full-width and Arabic-Indic digits, Unicode hyphens, zero-width/NUL/ideographic-space suffixes, and `0000-01-01`, and it accepts `0001-01-01` and `9999-12-31`. A brute force over every single BMP character inserted into or replacing a character of `2026-10-05` found **0** inputs that `TryParseExact` accepts and the pre-check rejects. So this is untested defensive code with no observable effect, which constitution §2 rules out (the same category as the spec 001 T-04 no-op config). | Reduce `TryParseDate` to the plan form, or inline it: `DateOnly.TryParseExact(rawDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)`. No test changes. The suite stays 361/361 (M3 evidence). |

**Notes:**
- **Contacts refactor:** `TrimToNull` moved verbatim to `Common/TextNormalization.cs` (same body), and `ContactInput` now uses `using static`. That's a pure move with no behavior change: all contact tests pass and no contact test file changed. It closes the T-02 Nit. Its location in `Common/` matches conventions.
- **Minimality is clean:**
  - `contactId` is not parsed or assigned; `TodoInput` still has three members.
  - There's no 787/`SqliteException`/`DbUpdateException` catch.
  - There's no `.WithName`/`.WithSummary`/`Produces*`/`.WithTags`.
  - There's no contact pre-check and no new migration.
  - `TitleMax`/`NotesMax` arrive now, as T-02's review anticipated.
- The handler shape `if (input is null) return TypedResults.ValidationProblem(errors!);` is the accepted pattern (spec 001 T-06). The return type `Results<Created<TodoResponse>, ValidationProblem>` is demanded for the 400 path.
- Title uses `rawTitle?.Trim()` + `IsNullOrEmpty` rather than `TrimToNull`. That's equivalent and mirrors `ContactInput`'s firstName. Not a finding.
- Test isolation: `CreateTodoValidationTests` has its own `IClassFixture<ApiFactory>`. Tests within a class run sequentially, so the before/after row count is reliable.
- Extra tests beyond the tasks.md list (`Parse_AtMax_AfterTrimming_IsAccepted_AC011`, `CreateTodo_OnlyNotesOverMax_ReportsOnlyNotes_AC011`, `CreateTodo_Returns400AsProblemJson_AC072`) need to be added to the traceability table at `/document`.

### T-03 re-review (fix cycle 1): 2026-10-05: APPROVE

**Checks:** tests pass, backend 361/361 · lint pass (`dotnet format --verify-no-changes` exit 0) · typecheck n/a (no web changes)

- **Finding 1 is closed.** `TryParseDate` (`TodoInput.cs:53-54`) is now only `DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)`. That's the plan.md/ADR-0007 form, and its strictness was verified by the probe above.
- **AC-012 strictness is still pinned.** M4 (lenient `TryParse`) was run without the pre-checks and failed 11 tests. The other mutants didn't touch the date path, so their results stand.
- **No tests were changed.** `TodoInputTests.cs` has the same diff (+76 lines), `CreateTodoValidationTests.cs` is untouched, and the test count is unchanged at 361.
- No new findings.

## T-04: 2026-10-05: APPROVE

**Checks:** tests pass, backend 399/399 · lint pass (`dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exit 0; no web changes) · typecheck n/a (no web changes)

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-007 | `CreateTodo_WithExistingContactId_LinksAndReturnsIt_AC007` (201 body + GET + raw row), `CreateTodo_ContactIdInOtherGuidFormats_LinksAndReturnsCanonical_AC007` (N/B/upper, padded) | yes: killed by `no_assign`, `drop_contact_on_success`, `exact_D` |
| AC-008 | `CreateTodo_ContactIdOmittedNullOrBlank_IsUnlinked_AC008` (4 bodies, raw row NULL), `Parse_ContactId_ParsesGuidOrBlankToNull_AC008` (8 rows) | yes: killed by `no_trim` and `trim_only` (the "" → null only variant) |
| AC-013 (create) | `CreateTodo_MalformedContactId_Returns400ValidGuid_AC013` (4 rows, row count unchanged), `Parse_MalformedContactId_ReturnsGuidMessage_AC013` (3 rows) | yes: exact message, killed by `guid_msg` |
| AC-014 (with contactId) | `CreateTodo_InvalidFieldsIncludingContactId_ReportsAll_AC014`, `CreateTodo_ContactIdPlusOneOtherInvalidField_ReportsBoth_AC014` (2 rows), `Parse_ContactIdWithOneOtherInvalidField_ReportsBoth_AC014` (3 rows), `Parse_AllFourFieldsInvalid_ReturnsAllFourKeys_AC014` | yes: the T-03 follow-up is met. An early return before contactId, a contactId short-circuit after the other rules, and a contactId check placed first that returns early each fail 7 tests |
| AC-016 | `CreateTodo_UnknownContactId_Returns400MustReferToExistingContact_AC016` (v7 GUID, row count unchanged) | yes: killed by `fk_msg`, `fk_key`, `no_assign` |
| AC-017 | guard `CreateTodo_FieldErrorAndUnknownContact_ReportsOnlyFieldErrors_AC017` | yes: `ProblemAssert.IsProblemAsync` compares the exact key set, so any `contactId` entry fails it |
| AC-062 | `DeleteContact_WithLinkedTodos_Returns204_AC062` | yes: linked precondition asserted (API + raw row) |
| AC-063 (get) | `DeleteContact_LinkedTodosRemainWithNullContactAndOtherFieldsUnchanged_AC063` | yes: two to-dos, clock advanced 5 h, fresh client, every member except contactId compared by raw JSON (including `updatedAt`, `isDone`, `completedAt`) |
| AC-064 | `DeleteContact_LeavesOtherContactsTodosAndUnlinkedTodosUnchanged_AC064` | yes: other contact's and unlinked to-dos compared byte-for-byte |
| AC-066 (through the API) | `DeleteContactDirectlyInStore_ApiReturnsTodosUnlinked_AC066` | yes: raw `DELETE FROM Contacts`, then GET shows null with other fields unchanged |
| AC-073 (create, non-FK) | `CreateTodo_WhenNonForeignKeyConstraintFails_Returns500NotContactError_AC073` (own fixture, BEFORE INSERT trigger dropped in `finally`, Todos+Contacts snapshot before/after) | yes: kills `fk_primary19` (1 fail), `fk_anySqlite` (2), `fk_anyDbUpdate` (2) |
| AC-074 (create, contactId) | `CreateTodo_ContactIdMessages_FollowStyle_AC074` | yes: style helper plus exact message for both new messages |

**Mutation (scratch copy, full backend suite per mutant):**
| Mutant | Tests failed |
|---|---|
| `fk_primary19`: `IsForeignKeyViolation` matches primary code 19 | 1 |
| `fk_anySqlite`: any inner `SqliteException` | 2 |
| `fk_anyDbUpdate`: unfiltered `catch (DbUpdateException)` | 2 |
| `no_assign`: handler doesn't set `ContactId` | 10 |
| `drop_contact_on_success`: Parse returns null contactId | 14 |
| `fk_msg` / `fk_key`: wrong FK message / key | 2 / 2 |
| `early_before_contact`: early return before the contactId block | 7 |
| `contact_short_circuit`: a malformed contactId returns only its own error | 7 |
| `contact_first_return`: contactId checked first, returns early | 7 |
| `exact_D`: `Guid.TryParseExact(..., "D")` | 4 |
| `no_trim` / `trim_only` | 5 / 3 |
| `guid_msg` | 15 |
| `precheck`: add `db.Contacts.AnyAsync` before save | 0 (expected: only a race tells it apart from the FK path, and T-13 owns AC-067 races. The pre-check is NOT in the code; ADR-0008 forbids it) |

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Nit | test-writer | tests/MicroCrm.Api.Tests/Integration/Todos/CreateTodoContactLinkTests.cs:96, :137 | The guard comments describe the RED state ("the field isn't read yet", "contactId isn't read yet ... today"). Now that contactId is parsed they're stale and misleading. | Reword to the behavior pinned, e.g. "blank contactId means unlinked" and "contact existence is checked only after field rules pass". Optional. |

**Notes:**
- **787 mapping is exact.** `SqliteErrors.IsForeignKeyViolation` matches `SqliteExtendedErrorCode: 787` only (`SqliteErrors.cs:9,15`), next to the 2067 helper, as ADR-0008 and plan §3 require. Broadening it to primary code 19, any SqliteException, or any DbUpdateException is killed by the trigger test (1811).
- **No contact pre-check query.** The handler adds, saves, and catches. There's no `Contacts` lookup anywhere in `Features/Todos/`.
- **Minimality is clean.** There's no `MapPut`, no `UpdateTodoRequest` overload of Parse, no concurrency catch, and no `.WithName`/`.WithSummary`/`Produces*`/`.WithTags`. `Data/Migrations` has no changes since T-01 (only `CreateTodos`).
- `TodoInput` matches the plan signature (`Guid? ContactId` as the 4th member) and parse rule (trim-to-null, `Guid.TryParse`, `Must be a valid GUID.`). The FK-path message is `Must refer to an existing contact.`. Both are ADR-0006-style and pinned by AC-074.
- No existing test was changed or weakened. `TodoErrorHandlingTests.cs` and `TodoInputTests.cs` only add lines; the count went 361 → 399.
- Test isolation: the trigger test has its own `IClassFixture<ApiFactory>` and drops the trigger in `finally`. The snapshot includes a linked pre-existing to-do, so "stored data unchanged" is non-trivial.
- Extra tests beyond the tasks.md list (`CreateTodo_ContactIdInOtherGuidFormats_LinksAndReturnsCanonical_AC007`, `CreateTodo_ContactIdPlusOneOtherInvalidField_ReportsBoth_AC014`, `Parse_ContactIdWithOneOtherInvalidField_ReportsBoth_AC014`, `Parse_AllFourFieldsInvalid_ReturnsAllFourKeys_AC014`) need to be added to the traceability table at `/document`.

## T-05: 2026-10-05: APPROVE

**Checks:** tests pass, backend 419/419 (run 3 times, stable) · lint pass (`dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exit 0; `npm --prefix web run lint` exit 0, no web changes) · typecheck pass (`npm --prefix web run typecheck` exit 0, no web changes)

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-020 (get) | `UpdateTodo_WithValidBody_Returns200WithUpdatedTodo_AC020`, `UpdateTodo_ThenGet_ReturnsPersistedValuesOnNewConnections_AC020` | yes: checks all 9 members, content type, every edited field, PUT body == GET body (raw) on a fresh client, and the raw row. Killed by `no_save` (18), `no_title`, `no_notes`, `no_due`, `no_contact`. The list half belongs to T-10. |
| AC-021 | `UpdateTodo_OptionalFieldOmittedNullOrBlank_StoredAsNull_AC021` (12 rows: 3 fields × omitted/null/""/whitespace, starting from a linked to-do with notes and a due date) | yes: checks the response, a fresh GET, and the raw column, and the other two fields are kept. The keep-old-value mutants (`?? todo.Notes`, `?? todo.DueDate`, `?? todo.ContactId`) each fail 4. |
| AC-022 | `UpdateTodo_IgnoresBodyIdTimestampsAndDoneState_AC022` | yes: sends a different `id` (no row is created for it), bogus `createdAt`/`updatedAt`, `isDone: true`, and `completedAt`. Killed by `created_now`, `isdone_true`, `no_updated`. |
| AC-023 | `UpdateTodo_SetsUpdatedAtFromClock_EvenWhenUnchanged_AC023` | yes: PUTs identical values after `Time.Advance`. Killed by `updated_if_changed` (UpdatedAt set only when some property is modified). |
| AC-024 | `UpdateTodo_LinksToContact_FromUnlinkedOrOtherContact_AC024` (2 rows) | yes: the precondition link is asserted, then the response, a fresh GET, and the raw uppercase FK are checked. Killed by `no_contact`. |
| AC-025 | `UpdateTodo_AtMaxWithWhitespace_StoresTrimmed_AC025` | yes: title 200 and notes 4000 surrounded by tab, newline, and spaces, plus a padded dueDate. The response and a fresh GET are trimmed. Killed by a non-trimming update overload. |
| AC-030 | `UpdateTodo_LeavesOtherTodosAndContactsUnchanged_AC030` | yes, with a Nit (finding 1): the clock is advanced 3 h, then raw JSON of 2 other to-dos and both contacts is compared. Killed by an all-rows `ExecuteUpdate` of Title and by touching the old contact. |

**Mutation (scratch copy, full backend suite per mutant):**
| Mutant | Tests failed |
|---|---|
| `no_save` | 18 |
| `no_title` / `no_notes` / `no_due` / `no_contact` | 4 / 7 / 7 / 8 |
| `no_updated` / `updated_if_changed` | 2 / 1 |
| `notes_keep` / `due_keep` / `contact_keep` (`?? old`) | 4 / 4 / 4 |
| `created_now` / `isdone_true` | 2 / 2 |
| `update_all_title` (ExecuteUpdate Title on every row) | 1 |
| `touch_old_contact` (bump the old contact's UpdatedAt) | 1 |
| `no_trim_title_upd` (update overload returns an untrimmed title) | 4 |
| `update_all_updatedat` (bump UpdatedAt of other to-dos linked to the target's *new* contact) | 0 (finding 1) |
| `nf_branch` (FirstOrDefault + throw) | 0 (equivalent: still a 500 until T-07) |

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Nit | test-writer | tests/MicroCrm.Api.Tests/Integration/Todos/UpdateTodoTests.cs:283-290 | AC-030's snapshot covers a sibling of the target's *old* contact (A) and an unlinked to-do, but no to-do already linked to the *new* contact (B). A side effect on B's existing to-dos survives (`update_all_updatedat`, 0 fails). It's contrived, so this is optional. | Seed one more to-do linked to `contactB` before the PUT and add it to `todoUrls`. |

**Notes:**
- **Minimality is clean.** `UpdateTodo` returns `Task<Ok<TodoResponse>>` only. It has no validation branch (`input!`, T-06), no null→404 branch (`FirstAsync`, T-07), no 787 catch (T-07), no `IsModified` on ContactId (T-13), and no `DbUpdateConcurrencyException` catch (T-14). There's no `.WithName`/`.WithSummary`/`Produces*`/`.WithTags` (T-15) and no `Contacts` pre-check query anywhere in `Features/Todos/` (ADR-0008). `Data/` hasn't changed, so no migration was added after T-01. The validation branch the implementer first added was removed before this review, as the orchestrator reported.
- **Shared core.** `Parse(UpdateTodoRequest)` delegates to the same private `Parse(rawTitle, rawNotes, rawDueDate, rawContactId)` core as create (plan §TodoInput, NFR-005). `UpdateTodoRequest` has exactly the 4 editable members, so `isDone`/`completedAt`/`id`/timestamps in the body can't bind (plan: "PUT never touches IsDone/CompletedAt").
- **Later tasks can still fail first.** A scratch probe against the current handler gave:
  - T-06 RED: invalid bodies (no title, bad date, invalid body to an unknown id) → 500 problem+json (null `input`), and they expect 400. Malformed `{` and `"contactId":7` → 400 already, which is the expected AC-015 guard.
  - T-07 RED: an unknown GUID → 500 (`FirstAsync` throws), unknown contact + unknown to-do → 500, and an unknown contact on an existing to-do → 500 (unhandled 787). Each expects 404 or 400. Non-GUID → 404 already (guard, no route).
  - Every interim 500 body is `type`/`title`/`status`/`traceId` only (no exception text), and the stored row stayed unchanged.
  - T-13 RED holds: assigning an equal `ContactId` to a tracked entity doesn't mark it modified, so a keep-link PUT racing a contact delete still returns 200 with the stale id until `IsModified = true` is added.
- **NFR-004.** The update path adds no logging.
- **`input!` used 4 times.** A single `!` is enough (proven by building with the other three removed: 0 warnings), but T-06 replaces this with the `if (input is null)` branch. No action needed.
- **T-04 Nit closed.** `CreateTodoContactLinkTests.cs:96,137` changes comments only, and no assertion or code line changed. The test count went 399 → 419 (+20 new, 0 removed).
- `MicroCrm.Api.http` gained one PUT sample (a dev convenience, plan file table).

## T-06: 2026-10-05: APPROVE

**Checks:** tests pass, backend 457/457 · lint pass (`dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exit 0; `npm --prefix web run lint` exit 0, no web changes) · typecheck pass (`npm --prefix web run typecheck` exit 0, no web changes)

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-010 (update) | `UpdateTodo_WithoutTitle_Returns400Required_AC010` (omitted / null / "" / whitespace) | yes: exact key set, exact `Required.`, and the to-do is unchanged (fresh-client GET body plus raw row, column by column). |
| AC-011 (update) | `UpdateTodo_FieldOverMax_Returns400_AC011`, `UpdateTodo_OnlyNotesOverMax_ReportsOnlyNotes_AC011` | yes: 201/4001 with exact messages; notes-only row pins "an entry for each offending field" (only `notes`). |
| AC-012 (update) | `UpdateTodo_InvalidDueDate_Returns400_AC012` (9 rows: all 5 spec examples plus 3-digit year, 5-digit year, full-width digits, slashes) | yes: exact message, unchanged row. |
| AC-013 (update) | `UpdateTodo_MalformedContactId_Returns400_AC013` (`abc`, `123`, truncated GUID) | yes: exact message, unchanged row (the seeded link survives). |
| AC-014 (update) | `UpdateTodo_MultipleInvalidFields_ReportsAll_AC014` | yes: all 4 keys and messages. Killed by `take1` (6 fail). |
| AC-015 (update) | `UpdateTodo_MalformedBody_Returns400Problem_AC015` (7 rows incl. `{"title":"x","contactId":7}`), guard | yes: passes at RED as planned; `ThrowOnBadRequest = true` mutant fails all 7. Asserts no `errors` and an unchanged row. |
| AC-027 | `UpdateTodo_InvalidBodyToUnknownId_Returns400_AC027` | yes: 400 with all 4 keys, row count unchanged, no row for the id. Killed by `branch_after_lookup` (1 fail, the only test that distinguishes order). |
| AC-074 (update) | `UpdateTodo_ValidationMessages_FollowStyle_AC074` | yes: `AssertValidationMessageStyle` plus exact messages for all 5 messages (incl. `Required.`). |
| NFR-005 | `Parse_UpdateAndCreateRequests_ProduceIdenticalErrors_NFR005` (unit, 6 rows, guard), `UpdateTodo_SameInvalidPayload_SameErrorsAsCreate_NFR005` (integration, 4 rows) | yes: key-by-key and message-by-message. Diverging update-overload mutants are killed by both (see below). |
| AC-030 (T-05 Nit) | `UpdateTodo_LeavesOtherTodosAndContactsUnchanged_AC030` now snapshots `otherLinkedToB` | yes: `touch_B_updated` (bump UpdatedAt on the new contact's other to-dos), 0 fails at T-05, now fails 1; `touch_B_title` fails 1. Nit closed. |

**Mutation (scratch copy, full backend suite per mutant):**
| Mutant | Tests failed |
|---|---|
| `no_branch` (the T-05 shape: lookup, then throw on null input) | 25 (exactly the RED set) |
| `branch_after_lookup` (validation after `FirstAsync`) | 1 (AC-027) |
| `take1` (only the first error returned on PUT) | 6 |
| `upd_no_contact` / `upd_no_notes` / `upd_no_date` (update overload drops a field) | 21 / 19 / 28 (incl. the unit NFR-005 guard each time) |
| `upd_msgs` (update overload rewrites messages) | 31 (incl. the unit NFR-005 guard) |
| `invalid_touch_notes` (on invalid input, null the stored notes and save, then 400) | 24 |
| `invalid_touch_updated` (on invalid input, bump stored UpdatedAt, then 400) | 24 |
| `touch_B_title` / `touch_B_updated` | 1 / 1 |
| `ThrowOnBadRequest = true` (AC-015 guard, class-filtered run) | 7 of 7 AC-015 rows |

**Findings:** none.

**Notes:**
- **Minimality is clean.** The only production change is in `UpdateTodo`: `var (input, errors) = TodoInput.Parse(request); if (input is null) return TypedResults.ValidationProblem(errors!);` before `FirstAsync`, the return type widened to `Results<Ok<TodoResponse>, ValidationProblem>` (demanded by the 400 path), and the four `input!` removed. Identical in shape to create (spec 001 T-06 accepted pattern). Still absent: a null→404 branch (`FirstAsync` stays, T-07), a 787 catch (T-07), `IsModified` on ContactId (T-13), a `DbUpdateConcurrencyException` catch (T-14), `.WithName`/`.WithSummary`/`Produces*` (T-15), and any `Contacts` pre-check / `AnyAsync` in `Features/Todos/` (ADR-0008). No change under `Data/`, so no migration after T-01. `TodoInput.cs` is unchanged.
- **T-07 can still fail first.** By reading the handler (unchanged after the branch): a valid PUT to an unknown GUID hits `FirstAsync` and throws → 500 (AC-026/AC-028 expect 404); a valid PUT linking an unknown contact reaches `SaveChangesAsync` with no catch → unhandled 787 → 500 (AC-029 expects 400). The AC-028 shape (unknown contact + unknown to-do) is also 500 today. AC-026 non-GUID and both AC-073 tests remain the planned guards.
- **No test removed or weakened.** 419 → 457 = +38: 32 in `UpdateTodoValidationTests` (25 red + 7 AC-015 guard rows) and 6 unit NFR-005 rows. The only edit to an existing test is the AC-030 snapshot growing by one URL.
- `UpdateTodo_OnlyNotesOverMax_ReportsOnlyNotes_AC011` is not in the tasks.md test list; it's an extra, justified by AC-011's "each offending field" wording. Add it to the Traceability table at `/document`.
- The seeded to-do is linked, has notes and a due date, so the column-by-column unchanged check is meaningful for every field. The fixed fake clock means a bare `UpdatedAt = now` touch on invalid input would be value-equivalent (not observable); only a changed value is a real mutant, and that one is killed.

## T-07: 2026-10-05: APPROVE

**Checks:** tests pass, backend 464/464 · lint pass (`dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exit 0; `npm --prefix web run lint` exit 0, no web changes) · typecheck pass (`npm --prefix web run typecheck` exit 0, no web changes)

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-026 (GUID) | `UpdateTodo_UnknownGuid_Returns404AndCreatesNothing_AC026` | yes: 404 problem, follow-up GET still 404, row count unchanged. Killed by `upsert` and `upsert_then_404`. |
| AC-026 (non-GUID) | `UpdateTodo_NonGuidId_Returns404_AC026` (`not-a-guid`, `123`), guard | yes for status and "no change" (row count). Only the status code is asserted, not problem+json (see Should-fix). |
| AC-028 | `UpdateTodo_UnknownContactToUnknownTodo_Returns404_AC028` | yes: 404 problem with no `errors`, row count unchanged. Killed by `contact_precheck` (an `AnyAsync` contact check before the lookup). It's the only test that pins to-do-before-contact order. |
| AC-029 | `UpdateTodo_UnknownContactId_Returns400AndLeavesTodoUnchanged_AC029` | yes: exact key set `contactId`, exact message, fresh-client GET body and raw row both unchanged after a clock advance, original link kept. Killed by `no_catch`, `fk_partial_save` and `fk_wrong_msg`. |
| AC-073 (update) | `UpdateTodo_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC073` (dropped table, guard), `UpdateTodo_WhenNonForeignKeyConstraintFails_Returns500AndLeavesTodoUnchanged_AC073` (own fixture, BEFORE UPDATE trigger, guard) | yes: the trigger test pins the catch scope. Broadened-filter mutants are all killed by it (see below). It uses a valid existing `contactId`, so the 787 path is not hit by accident. The trigger is dropped in `finally`, and the full Todos+Contacts snapshot plus the raw row are unchanged. |
| AC-072 (404) | AC-026 GUID and AC-028 via `ProblemAssert.IsProblemAsync` (type/title/status, content type) | yes for the handler 404. The route-level non-GUID 404 isn't asserted as a problem on PUT (see Should-fix). |

**Mutation (scratch copy, full backend suite per mutant; `UpdateTodo` only, the create path is untouched):**
| Mutant | Tests failed |
|---|---|
| `primary19` (filter on `SqliteErrorCode == 19`) | 1 (BEFORE UPDATE trigger test) |
| `any_sqlite` (filter on any `SqliteException`) | 1 (trigger test) |
| `unfiltered` (`catch (DbUpdateException)`) | 1 (trigger test) |
| `no_catch` (filter never matches) | 1 (AC-029) |
| `contact_precheck` (`AnyAsync` on Contacts before the to-do lookup → 400) | 1 (AC-028) |
| `upsert` (null lookup → insert the to-do) | 2 (AC-026, AC-028) |
| `upsert_then_404` (insert and save, then return 404) | 2 (AC-026, AC-028) |
| `fk_partial_save` (on 787, null ContactId, save, then 400) | 1 (AC-029) |
| `fk_wrong_msg` (different contactId message) | 1 (AC-029) |

**Findings:**
- **Should-fix (test-writer)** `tests/MicroCrm.Api.Tests/Integration/Todos/UpdateTodoTests.cs:341`: `UpdateTodo_NonGuidId_Returns404_AC026` only checks `HttpStatusCode.NotFound`. plan.md section 8 and the traceability row (line 339) say every error test goes through `ProblemAssert.IsProblemAsync` (AC-072). The sibling tests already do this: `GetTodo_NonGuidId_Returns404Problem_AC019` and contacts `UpdateContact_NonGuidId_Returns404Problem_AC023`. The behaviour is already correct. A scratch probe replaced the assert with `using var problem = await ProblemAssert.IsProblemAsync(response, 404);` and passed 2/2 (`UseStatusCodePages` is global). Because of that, and because GET non-GUID already covers the same pipeline, this is not Blocking. Fixed means: the theory uses `ProblemAssert.IsProblemAsync(response, 404)`, and the name can become `..._Returns404Problem_AC026`. It can be folded into any later test-writer task.

**Notes:**
- **Carry-forwards closed.** The null→404 branch comes after validation and before both the field assignments and the `try`/`SaveChangesAsync`. The 787 catch only wraps the save. The BEFORE UPDATE trigger test kills every broadened filter (`primary19`, `any_sqlite`, `unfiltered`).
- **Minimality is clean.** The production diff is limited to: `FirstAsync` → `FirstOrDefaultAsync` plus a null → `TypedResults.Problem(404)` branch, the return type widened with `ProblemHttpResult`, a 787-filtered catch around the save, and `UnknownContact()` extracted verbatim from create (same key and message, so this is a behaviour-preserving refactor). These are still absent: `IsModified` on ContactId (T-13), a `DbUpdateConcurrencyException` catch (T-14), `.WithName`/`.WithSummary`/`Produces*` (T-15), and any `Contacts` pre-check or `AnyAsync` in `Features/Todos/` (ADR-0008). There's no change under `Data/` and no new migration.
- **No test removed or weakened.** 457 → 464 = +7 (AC-026 ×3, AC-028, AC-029, AC-073 ×2). Existing tests are untouched; only a private `TodoCountAsync` helper was added.
- The RED claim is consistent with the code at e256540. `FirstAsync` throws on an unknown id → 500 for AC-026 GUID and AC-028. With no catch, the 787 → 500 for AC-029. Both AC-073 tests and the non-GUID theory are guards.
- The `contact_precheck` mutant (the T-04 survivor pattern, now on update) is killed here only because of the ordering AC-028. A pre-check placed *after* the lookup would still survive until T-13's race tests, which is expected.

## T-08: 2026-10-05: APPROVE

**Checks:** tests pass, backend 484/484 · lint pass (`dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exit 0; `npm --prefix web run lint` exit 0, no web changes) · typecheck pass (`npm --prefix web run typecheck` exit 0, no web changes)

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-031 | unit `Complete_Open_SetsDoneAndTimestamps_ReturnsTrue_AC031`; `CompleteTodo_Open_Returns200DoneWithCompletedAtNow_AC031` | yes: clock advanced 1h, `completedAt == updatedAt == now`, fresh-client GET identical. Killed by `complete_no_updated`, `no_save`, `untracked_load`. |
| AC-032 | unit `Complete_AlreadyDone_ChangesNothing_ReturnsFalse_AC032`; `CompleteTodo_AlreadyDone_Returns200Unchanged_AC032` | yes: clock advanced 2h before the repeat; response body, later GET and raw row all equal the pre-repeat state. Killed by `complete_no_guard` and `complete_noop_restamp_only_updated`. |
| AC-033 | unit `Reopen_Done_..._AC033`; `ReopenTodo_Done_Returns200OpenWithUpdatedAtNow_AC033` | yes: persisted via fresh-client GET. Killed by `reopen_no_updated`, `no_save`. |
| AC-034 | unit `Reopen_AlreadyOpen_..._AC034`; `ReopenTodo_AlreadyOpen_Returns200Unchanged_AC034` | yes: body and raw row unchanged after a 2h advance. Killed by `reopen_no_guard`. |
| AC-035 | `CompleteAndReopen_ChangeOnlyDoneStateAndIgnoreBody_AC035` (complete/reopen × done-state body and malformed `{`) | yes: linked target with notes and due date, six other fields compared raw, bystander body and row unchanged. Killed by `bind_body` (adding an `UpdateTodoRequest?` parameter) and `swap_routes`. |
| AC-036 | `CompleteOrReopen_UnknownOrNonGuidId_Returns404_AC036` (action × unknown GUID, `not-a-guid`) | yes for status and "no change". The GUID rows go through `ProblemAssert.IsProblemAsync(response, 404)` and are now genuinely handler-driven: `no_null_404` (`FirstAsync`, no null branch) fails exactly the 2 GUID rows (500). The non-GUID rows only check the status (see Should-fix). |
| AC-037 | `UpdateTodo_WhenDone_KeepsDoneAndCompletedAt_AC037` | yes: PUT body tries `isDone:false, completedAt:null`; response and fresh GET keep done + original `completedAt`. |
| AC-073 (complete/reopen) | `CompleteOrReopenTodo_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC073` (Theory: complete, reopen), `CompleteTodo_WhenUpdateRejected_Returns500AndLeavesTodoOpen_AC073` (BEFORE UPDATE trigger, own fixture) | yes. The trigger test drops the trigger in `finally`, asserts no-leak strings and the raw row unchanged (still open, `CompletedAt` null); it fails under `no_save`, `untracked_load` and `swap_routes`. |

**Mutation (scratch copy, full backend suite per mutant):**
| Mutant | Tests failed |
|---|---|
| `complete_no_guard` / `reopen_no_guard` (no-op re-stamps) | 2 / 2 (unit + integration AC-032 / AC-034) |
| `complete_noop_restamp_only_updated` (done → bump UpdatedAt, return true) | 2 |
| `complete_no_updated` / `reopen_no_updated` | 4 / 4 |
| `reopen_noop_save_true` (open → return true, nothing changed) | 1 (unit AC-034; equivalent at HTTP level) |
| `no_save` (never `SaveChangesAsync`) | 7 |
| `untracked_load` (`AsNoTracking`) | 7 |
| `no_null_404` (`FirstAsync`, no null branch) | 2 (AC-036 GUID rows) |
| `swap_routes` (complete ↔ reopen) | 10 |
| `bind_body` (complete binds `UpdateTodoRequest?`) | 1 (AC-035 malformed-body row) |
| `always_save` (drop the `if`, always save) | 0, equivalent (see Notes) |

**Findings:**
- **Should-fix (test-writer)** `tests/MicroCrm.Api.Tests/Integration/Todos/CompleteReopenTodoTests.cs:206`: `if (Guid.TryParse(id, out _))` skips `ProblemAssert.IsProblemAsync` for the `not-a-guid` rows. That's the same gap fixed on PUT in this task, now in a new test. plan.md section 8 and the AC-072 traceability row say every error test goes through `ProblemAssert`. The behaviour is already correct: a scratch probe ran `ProblemAssert.IsProblemAsync(r, 404)` on `POST /api/todos/{not-a-guid|123}/{complete|reopen}` and passed 3/3, so it isn't Blocking. Fixed means: drop the condition so every row asserts the problem, and delete or correct the comment at line 194. That comment says the problem body "is only asserted after GREEN", but the GUID rows also pass `ProblemAssert` at RED, because status code pages turn the missing-route 404 into problem+json. What changed at GREEN is the handler now producing the 404, which `no_null_404` proves. Can be folded into any later test-writer task.

**Notes:**
- **The no-op path skips SaveChanges, and `updatedAt` stays unchanged.** `ChangeDoneState` saves only when `Complete`/`Reopen` returns true, and both return false before touching any field. AC-032/AC-034 advance the clock 2h and compare the response, a later GET and the raw row, so any re-stamp is caught. The `always_save` mutant is equivalent: with no tracked changes, EF issues no UPDATE. The plan prescribes the branch (design point 5, tasks.md "no-op → 200 without `SaveChanges`"), and the unit tests demand the bool, so it isn't drift. It stays unobservable at T-14 too, because a no-op `SaveChanges` can't raise a concurrency exception.
- **Minimality is clean.** No `DbUpdateConcurrencyException` catch (T-14), no `IsModified` (T-13), no `.WithName`/`.WithSummary`/`Produces*` (T-15), no `AnyAsync`/contact pre-check, and no body parameter on either action. Nothing changed under `Data/`, so there's no new migration. `Complete`/`Reopen` match the plan signatures (plan lines 139-140).
- **The AC-073 merge is acceptable.** tasks.md names `CompleteTodo_WhenDatabaseFails_..._AC073` and `ReopenTodo_WhenDatabaseFails_..._AC073`, and the single Theory over `complete`/`reopen` gives one result row per action with the same assertions. The cost is only that the Traceability table now needs the Theory name. The PUT rename (`UpdateTodo_NonGuidId_Returns404Problem_AC026`) also needs updating there. Route both to `/document`.
- **No test removed or weakened.** 464 → 484 = +20 (4 unit, 13 in `CompleteReopenTodoTests`, 3 in `TodoErrorHandlingTests`). The only edit to an existing test is the T-07 Should-fix: the PUT non-GUID theory now uses `ProblemAssert.IsProblemAsync` (strengthened). That closes the T-07 Should-fix.
- The RED claim is consistent: before this diff there was no route, so the GUID AC-036 rows passed as guards and everything else expecting 200/500 got 404.

## T-09: 2026-10-05: APPROVE

**Checks:** tests pass, backend 494/494 · lint pass (`dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exit 0; `npm --prefix web run lint` exit 0, no web changes) · typecheck pass (`npm --prefix web run typecheck` exit 0, no web changes)

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-038 (get) | `DeleteTodo_Existing_Returns204WithEmptyBody_AC038`; `DeleteTodo_ThenGet_Returns404OnNewConnections_AC038` | yes: 204 with an empty body; a fresh client's GET goes through `ProblemAssert` 404; the raw row count goes 1 → 0. The list/`totalCount` half belongs to T-10 (AC-038 (list)). |
| AC-039 | `DeleteTodo_UnknownOrAlreadyDeleted_Returns404Problem_AC039`; `DeleteTodo_NonGuidId_Returns404AndDeletesNothing_AC039` (`not-a-guid`, `123`) | yes: unknown and already-deleted both go through `ProblemAssert`. Non-GUID rows assert problem 404, an unchanged total, and that a survivor still exists. Killed by `always_204` and `no_filter`, and by `no_guid_constraint` (route `/{id}` → 400 binding failure, fails both non-GUID rows). |
| AC-040 | `DeleteTodo_LeavesContactAndOtherTodosUnchanged_AC040` | yes: the linked contact, a sibling linked to the same contact, and an unrelated to-do are compared by API body and raw row, with the clock advanced 10 min; total − 1. Killed by `no_filter`. |
| AC-041 | `UpdateCompleteReopen_AfterDelete_Return404AndDoNotRecreate_AC041` | yes: PUT, complete and reopen each give `ProblemAssert` 404; afterwards the row count is 0 and the total is unchanged (no upsert). |
| AC-071 | `DeleteTodo_ConcurrentSameId_ExactlyOne204Rest404_AC071` | yes, with a caveat (see Should-fix). The correct code passed 10/10 isolated runs plus every full-suite run, so it's deterministic for a correct implementation. It's also genuinely concurrent: 10 clients wait on an all-ready latch, then a shared gate. |
| AC-073 (delete) | `DeleteTodo_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC073` (drop table); `DeleteTodoRejectedByDatabaseTests.DeleteTodo_WhenDatabaseRejects_Returns500AndTodoStillExists_AC073` (BEFORE DELETE RAISE(ABORT), own fixture, trigger dropped in `finally`) | yes: safe 500 with no leaked internals; the snapshot of all to-dos and contacts plus the raw row are unchanged. Both tests kill `catch_all_404` and `catch_sqlite_404`. |

**Mutation (scratch copy, full backend suite per mutant unless noted):**
| Mutant | Tests failed |
|---|---|
| `load_remove` (`FirstOrDefaultAsync` + `Remove` + `SaveChangesAsync`) | AC-071 only: 9 of 11 full-suite runs, 10 of 10 isolated (`--filter-method "*AC071*"`) runs |
| `precheck_any` (`AnyAsync` → 404, then `ExecuteDeleteAsync`, always 204) | 1 (AC-071), 2 of 2 runs |
| `always_204` (ignore the row count) | 2 (AC-039 already-deleted, AC-071) |
| `no_filter` (`db.Todos.ExecuteDeleteAsync`) | 2 (AC-039, AC-040) |
| `catch_all_404` / `catch_sqlite_404` (swallow the failure as 0 rows) | 2 / 2 (both AC-073 delete tests) |
| `no_guid_constraint` (`MapDelete("/{id}")`) | 2 (AC-039 non-GUID rows) |

**Findings:**
- **Should-fix (test-writer, can be deferred to T-14)** `tests/MicroCrm.Api.Tests/Integration/Todos/DeleteTodoTests.cs:155`: "never load-then-remove" (plan section 6, tasks.md line 29) is pinned only by the probabilistic AC-071 race. Run alone it kills `load_remove` every time (10/10). Under the parallel full suite it missed 2 of 11 runs: thread-pool contention sometimes serialises the 10 requests, so every loser loads after the winner commits and gets a legitimate 404. The test mirrors spec 002's `DeleteContact_ConcurrentSameId_..._AC036` exactly as tasks.md prescribes, and the production code is correct by inspection, so this isn't Blocking. Fixed means: a deterministic pin that uses the T-14 interceptor technique (plan section 6). A test-local `SaveChangesInterceptor` deletes the to-do through a separate connection in `SavingChangesAsync`, then `DELETE` must still give 204 or 404 and never 500. The correct handler never calls `SaveChanges`, so the interceptor never fires; `load_remove` would throw `DbUpdateConcurrencyException` → 500 every time. Alternatively, a `DbCommandInterceptor` could assert that no `SELECT` precedes the `DELETE`.
- **Nit (test-writer)** `DeleteTodoTests.cs:83`: the comment calls the AC-039 non-GUID rows a "Guard" that already passes because "no route matches". At RED they failed with 405 (see Notes), so it should say that they fail with 405 until DELETE is mapped. The tasks.md T-09 **Guards** line has the same inaccuracy; route that to `/document`.
- **Nit (test-writer)** `DeleteTodoTests.cs:149`: the AC-041 follow-up GET checks only `HttpStatusCode.NotFound`, not `ProblemAssert`. It's a supplementary "not recreated" check, backed by the raw row count, and the GET 404 problem body is already pinned in the AC-038 test. No action is needed unless the test is touched again.

**Notes:**
- **The 405 RED on the non-GUID rows is confirmed, and the explanation is correct.** A scratch probe with `MapDelete` removed returned `DELETE /api/todos/not-a-guid` → 405 `Allow: GET, PUT` (the same for `123` and for a valid GUID), while `DELETE /api/todos/not-a-guid/x` → 404. Endpoint routing's DFA matches the path to the `{id}` parameter node, and `HttpMethodMatcherPolicy` picks its 405 endpoint before route constraints are checked on the candidates, so the `:guid` constraint never gets a say. With `MapDelete` in place, the same requests give 404 problem+json, and `no_guid_constraint` proves the constraint matters. So these rows were red for the right reason (DELETE not mapped), and they're sound tests after GREEN. A framework side-effect (`PATCH /api/todos/not-a-guid` → 405) is pre-existing and outside AC-039.
- **The delete is never load-then-remove.** The handler is the plan's single `db.Todos.Where(t => t.Id == id).ExecuteDeleteAsync(ct)`, 0 → `TypedResults.Problem(404)`, otherwise `NoContent`. Its return type is `Results<NoContent, ProblemHttpResult>`, as plan line 152 specifies.
- **Minimality is clean, with nothing from T-10+ added.** `git diff --stat HEAD -- src` shows only `TodosEndpoints.cs` (+13) and `MicroCrm.Api.http` (+4). There's no list route, no `.WithName`/`.WithSummary`/`Produces*` (T-15), no `DbUpdateConcurrencyException` catch (T-14), no `IsModified` (T-13), no `AnyAsync`, and no new migration.
- **The T-08 Should-fix is closed.** `CompleteOrReopen_UnknownOrNonGuidId_Returns404_AC036` now runs `ProblemAssert.IsProblemAsync(response, 404)` on every row, and the comment correctly attributes the RED-time 404 to the route (unknown GUID) or to status code pages (non-GUID). That's a strengthening.
- **No test removed or weakened.** 484 → 494 = +10 (8 in `DeleteTodoTests`, 1 in `TodoErrorHandlingTests`, 1 in `DeleteTodoRejectedByDatabaseTests`), which matches the 10 RED failures. The trigger test's hand-rolled problem/no-leak assertions match the existing `UpdateTodoNonForeignKeyFailureTests` and `CreateTodo_WhenNonForeignKeyConstraintFails...` style.

## T-10: 2026-10-05: CHANGES_REQUESTED

**Checks:** tests pass, backend 536/536 · lint pass (`dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exit 0; `npm --prefix web run lint` exit 0, no web changes) · typecheck pass (`npm --prefix web run typecheck` exit 0, no web changes)

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-042 | `Parse_Defaults_Page1PageSize20NoFilters_AC042`; `ListTodos_NoParameters_ReturnsDefaultEnvelope_AC042` | yes: 25 seeded → 20 items, page 1, pageSize 20, totalCount 25, item member set = AC-001. Killed by `count_items`. |
| AC-043 | `ListTodos_Empty_ReturnsEmptyItemsAndZeroTotal_AC043` | yes (the "none match the filters" half is T-11). |
| AC-044 | `ListTodos_OrdersByDueDateNullsLastThenCreatedAtThenId_AC044` | **no**: the due-date, nulls-last and id keys are pinned, but the `createdAt` key isn't. See Blocking finding 1. |
| AC-045 | `ListTodos_PageSlices_EchoPagingAndTotal_AC045`, `..._PageBeyondLast_ReturnsEmptyItems_AC045`, `..._PageSize100_IsAccepted_AC045`, `..._AllPages_ReturnEveryTodoExactlyOnceWithTies_AC045` | yes: slices, echo, totalCount, beyond-last including the `int.MaxValue` overflow rows, 100 accepted with 101 seeded, and exactly-once over 10 full ties. Killed by `skip0`, `no_tryskip`, `take_default`, `echo_page1` and `count_items`. |
| AC-046 | `Parse_InvalidPagingValues_ReturnSameMessagesAsContacts_AC046` (11 rows); `ListTodos_InvalidPageOrPageSize_Returns400_AC046` (17 rows) | yes: the unit rows compare `errors[parameter]` with `ListQuery.Parse`'s output AND the exact literal. The integration rows go through `ProblemAssert` (exact key set) plus the exact message. |
| AC-056 (paging) | `ListTodos_InvalidPageAndPageSize_ReportsBoth_AC056` | yes: `ProblemAssert` with exact keys `page`, `pageSize`. |
| AC-074 (list paging) | `ListTodos_ValidationMessages_FollowStyle_AC074` | yes: style helper plus the exact messages. |
| AC-020 (list) | `ListTodos_ShowsUpdatedValues_AC020` | yes: a fresh-client list item is raw-equal to GET, and the new due date re-orders it. |
| AC-038 (list) | `ListTodos_ExcludesDeletedTodo_AC038` | yes: excluded from items and totalCount on a new client. |
| AC-063 (list) | `ListTodos_AfterContactDelete_ShowsTodosUnlinkedAndOtherwiseUnchanged_AC063` | yes: includes a completed linked to-do. The clock is advanced 2h before the contact delete, and every member except `contactId` is raw-equal before and after. |
| AC-073 (list) | `TodoErrorHandlingTests.ListTodos_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC073` | yes: shared `AssertSafe500Async`. |

**Mutation (scratch copy, full backend suite per mutant):**
| Mutant | Tests failed |
|---|---|
| `no_created` (drop `.ThenBy(t => t.CreatedAt)`) | **0** |
| `swap_created_id` (`ThenBy(Id).ThenBy(CreatedAt)`) | **0** |
| `no_id` / `desc_id` | 1 / 1 (AC-044) |
| `nulls_first` (`DueDate != null`) / `no_nullkey` | 1 / 1 (AC-044) |
| `desc_due` / `no_due` | 4 / 4 |
| `skip0` / `no_tryskip` / `take_default` (20) / `echo_page1` / `count_items` | 3 / 1 / 3 / 1 / 5 |
| handler doesn't bind `status`/`contactId` (passes `null, null`) | 0, equivalent (see Nit 2) |

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Blocking | test-writer | `tests/MicroCrm.Api.Tests/Integration/Todos/ListTodosTests.cs:79-105` | AC-044's "then `createdAt` ascending" key isn't tested. `Guid.CreateVersion7(now)` takes its millisecond prefix from the same fake clock as `createdAt`. Every distinct-`createdAt` pair in the fixture differs by ≥ 1 s, so id order always equals `createdAt` order. Dropping `ThenBy(CreatedAt)`, or swapping it with `ThenBy(Id)`, passes 536/536. ADR-0009 rejects option 2 precisely because ordering by id is "not equivalent" within a millisecond, and this is the spec 001 T-12 vacuous-tiebreak pattern. | Add rows (in this test or a sibling `_AC044` test) with the same due date and `createdAt` values that differ by less than 1 ms, e.g. 8 creates with `factory.Time.Advance(TimeSpan.FromTicks(1))` between them. The ids then share a millisecond prefix and are random, so `createdAt` order and id order diverge, while `createdAt` stays distinct at 100 ns (ADR-0009 ticks). Expect `OrderBy(CreatedAt)`. Verified in scratch: such a test (8 rows, assert distinct `createdAt`) fails `no_created` and `swap_created_id` on 3 of 3 runs each (survival odds 1/8!), and passes on the current code. A raw-SQL insert with ticks and a high-id-first pair also works, but the API route is simpler. |
| 2 | Nit | implementer | `src/MicroCrm.Api/Features/Todos/TodosEndpoints.cs:69-70` | The handler binds `status`/`contactId` and passes them to a `Parse` that ignores them. That's an equivalent mutant (no test changes when they're unbound). It's harmless plumbing for T-11's signature (the 4-arg `Parse` is the plan signature and the unit tests demand it), and T-11 makes it load-bearing. | None required; T-11 covers it. |

**Notes:**
- **Order shape.** The SQL keys match plan section 7 and ADR-0009 exactly: `DueDate == null`, `DueDate`, `CreatedAt`, `Id`, count before the order, and `Skip`/`Take` only when `TryGetSkip` succeeds. It mirrors `ListContacts`. The nulls placement is pinned (`nulls_first` and `no_nullkey` killed). SQLite would put NULLs first without the `IS NULL` key, and the seed's two "no date" rows created early catch that.
- **GUID comparer (the test-writer's worry).** Not a real gap. A `dotnet run` probe over 200,000 same-millisecond v7 pairs found 0 disagreements between `string.CompareOrdinal` on lowercase, ordinal on uppercase (what SQLite BINARY compares on EF's uppercase TEXT), and `Guid.CompareTo` on .NET 10. A little-endian byte comparer (`ToByteArray`, e.g. a BLOB storage change) disagrees on 49.8% of pairs, so with two tie groups of 6 random ids such a mutant survives with odds of about 1/720². The expected-order construction in the test is correct. plan.md line 281 and tasks.md line 376 claim `Guid.CompareTo` "orders bytes differently". That's false on .NET 10 (it compares `_a`/`_b`/`_c` unsigned, then bytes, which matches the text). It's harmless guidance; route it to `/document` as a correction.
- **Paging parity with contacts.** Real: `TodoListQuery.Parse` delegates to `ListQuery.Parse(page, pageSize, search: null)`, and the unit theory asserts equality with `ListQuery`'s own messages. The integration theory adds `""`, `"  "`, `2147483648` and `+1`, which match spec 002's contacts rows.
- **T-11 can fail first.** `Parse` returns `Status: null, ContactId: null` unconditionally, and the handler applies no filter. So T-11's unit tests (`Parse_Status_...` and the others) get null/no errors, and its integration tests get unfiltered lists and 200 instead of 400, matching T-11's "Expected RED". The `TodoStatus` enum exists only because the record's plan signature needs it, and its values are unused.
- **Minimality is clean, with nothing from T-11+ added.** No status/contactId parsing, no filter `Where`, no `today`, no nested `/api/contacts/{id}/todos` route (T-12), no `IsModified` (T-13), no concurrency catch (T-14), no `.WithName`/`.WithSummary`/`Produces*` (T-15), no new migration.
- **T-09 Nits are closed.** The `DeleteTodoTests.cs:83` comment now correctly explains the 405 at RED. The AC-041 follow-up GET now uses `ProblemAssert.IsProblemAsync(..., 404)`, which strengthens it.
- **No test removed or weakened.** 494 → 536 = +42 (12 unit rows, 6 in `ListTodosTests`, 23 in `ListTodosPagingTests`, 1 in `TodoErrorHandlingTests`), which agrees with the reported RED: a build break on `TodoListQuery`, then all 30 new integration tests (6 + 23 + 1) failing with 405.
- The "Should-fix, deferrable to T-14" from T-09 (deterministic delete pin) is still open, as expected.

## T-10 fix cycle 1: 2026-10-05: APPROVE

**Checks:** tests pass, backend 537/537 · lint pass (`dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exit 0; no web changes) · typecheck n.a. (no web changes)

**Finding 1 (Blocking, test-writer) is closed.** `ListTodos_SameDueDate_OrdersByCreatedAtNotId_AC044` (`ListTodosTests.cs:131`) does three things:
- it aligns the fake clock to a millisecond boundary, so all 8 creates share one v7 millisecond prefix and their ids are random relative to each other;
- it advances 1 tick per create and asserts 8 distinct `createdAt` values;
- it expects insertion order, which equals `createdAt` order because the clock is monotonic.

| Mutant (scratch copy, full backend suite) | Tests failed |
|---|---|
| `no_created` (drop `.ThenBy(t => t.CreatedAt)`) | 1 (the new test), 5 of 5 runs |
| `swap_created_id` (`ThenBy(Id).ThenBy(CreatedAt)`) | 1 (the new test), 5 of 5 runs |

**Notes:**
- Production is unchanged (`TodosEndpoints.cs:84-88`). The original AC-044 test is untouched; this is a pure addition (536 → 537).
- The survival odds for either mutant are 1/8! per run. The millisecond alignment removes the only way two ids could straddle a millisecond and fall back into `createdAt` order.
- Nit 2 (status/contactId binding) and the `/document` note on the `Guid.CompareTo` wording stand as before. Neither blocks.

## T-11: 2026-10-05: CHANGES_REQUESTED

**Checks:** tests pass, backend 577/577, web 1/1 · lint pass (`dotnet format ... --verify-no-changes` exit 0, web lint exit 0) · typecheck pass (no web changes)

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-047 | `ListTodos_StatusOpen_ReturnsOnlyOpenIncludingOverdue_AC047` | yes: includes the overdue, today, tomorrow and undated open rows. Killed by `open_all`. |
| AC-048 | `ListTodos_StatusDone_ReturnsOnlyDone_AC048` | yes |
| AC-049 | `ListTodos_StatusOverdue_ReturnsOnlyOpenPastDue_AC049` | yes: seeds yesterday-open, today-open, tomorrow-open, undated-open, yesterday-done and undated-done. Killed by `le` (`<=`), `overdue_no_isdone`, `today_minus1` and `today_wall` (`DateTime.UtcNow`). |
| AC-050 | `OverdueClockTests.ListTodos_Overdue_IncludesTodoDueYesterdayAfterUtcMidnight_AC050` | yes: own fixture, 23:59:59Z, then +1 s, with a date assertion on the clock. |
| AC-051 | `Parse_Status_TrimmedCaseInsensitiveBlankMeansNone_AC051` (10 rows); `ListTodos_StatusBlankOrDifferentCase_AC051` (5 rows) | yes. Killed by `case_sensitive` and `no_trim_status`. |
| AC-052 | `Parse_UnknownStatus_ReturnsOneOfMessage_AC052` (4 rows); `ListTodos_StatusUnknown_Returns400_AC052` (3 rows) | **no**: comma-separated lists of valid names are accepted with 200. See Blocking finding 1. |
| AC-053 | `Parse_ContactId_GuidOrBlank_AC053`; `ListTodos_ContactIdFilter_ReturnsOnlyThatContactsTodos_AC053` | yes: covers the known contact, the other contact, an unknown GUID (empty page, totalCount 0), blank, whitespace and padded values. Killed by `no_contact` and `no_trim_cid`. |
| AC-054 | `Parse_MalformedContactId_ReturnsGuidMessage_AC054`; `ListTodos_MalformedContactId_Returns400_AC054` | yes |
| AC-055 | `ListTodos_CombinedFilters_MatchAllOrderedAndPaged_AC055` | yes: overdue + contact + page 1/2 of size 2, with decoys for each filter. Killed by `count_unfiltered`. |
| AC-056 | `Parse_AllInvalid_ReportsEveryParameter_AC056`; `ListTodos_AllQueryParametersInvalid_ReportsAll_AC056` | yes: the exact key set (through `ProblemAssert`) and every message. Killed by `status_shortcircuit` (15 tests fail). |
| AC-068 (filter) | `ListTodos_ContactIdOfDeletedContact_ExcludesFormerTodos_AC068` | yes |
| AC-074 (status, contactId) | `ListTodos_FilterMessages_FollowStyle_AC074` | yes |

**Mutation (scratch copy, full backend suite per mutant):**
| Mutant | Tests failed |
|---|---|
| `le` (`DueDate <= today`) / `today_minus1` / `today_wall` (`DateTime.UtcNow`) | 4 / 4 / 4 |
| `overdue_no_isdone` / `open_all` / `no_contact` / `count_unfiltered` | 3 / 2 / 3 / 9 |
| `case_sensitive` / `no_trim_status` / `no_trim_cid` / `no_inttry` | 13 / 2 / 2 / 2 |
| `status_shortcircuit` (paging-only null check) | 15 |
| `no_notnull` (drop `DueDate != null`) | 0, equivalent: SQL `NULL < @today` is not true. It's plan-prescribed, so this is a note only. |
| `no_isdefined` | **0**: only reachable through the comma-list path (`done,overdue` → 3). See finding 1. |

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Blocking | test-writer, then implementer | `src/MicroCrm.Api/Features/Todos/TodoListQuery.cs:19-21`; `tests/MicroCrm.Api.Tests/Unit/Todos/TodoListQueryTests.cs:72-76` | `Enum.TryParse` accepts comma-separated name lists even for a non-`[Flags]` enum, and ORs their values. Proven against the real `TodoListQuery.Parse` in scratch: `open,done` → `Done`, `open, overdue` → `Overdue`, `done,done` → `Done`, `Open , Done` → `Done`. Each gives 200 with a silently chosen filter. `done,overdue` (= 3) is rejected only by `IsDefined`. This violates AC-052 ("any other `status` value → 400"). It also deviates from plan.md §8 line 169, which prescribes comparing the trimmed value with `open`/`done`/`overdue` using `StringComparison.OrdinalIgnoreCase`. The `IsDefined` and `int.TryParse` guards patch two known holes in the wrong primitive. That is the T-03 "belt and braces around a framework parse" pattern, and the comma hole slipped through. | **test-writer:** add the rows `open,done`, `open, overdue` and `done,done` to `Parse_UnknownStatus_ReturnsOneOfMessage_AC052`, and at least `open,done` (URL-escaped) to `ListTodos_StatusUnknown_Returns400_AC052`. These fail now with 200 or a non-null query. **implementer:** replace lines 19-21 with the plan form: an `OrdinalIgnoreCase` comparison of the trimmed value against the three words, mapped to `TodoStatus`, with no `Enum.TryParse`, `IsDefined` or `int.TryParse`. A brute force over every BMP char inserted into or replacing a char of each word found 0 differences between the plan form and the current code. Comma lists are the only divergence, so the existing rows (`1`, ASCII-only casing) keep passing. |

**Notes:**
- **Overdue boundaries are correct and pinned.** Due today is not overdue, and due yesterday is (`le` is killed). A done to-do is never overdue (`overdue_no_isdone` is killed). An undated to-do is never overdue: the explicit `!= null` is equivalent in SQL, and the undated open and undated done rows are asserted absent.
- **Today really comes from the injected clock as a UTC date.** It is `DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)` (`TodosEndpoints.cs:107`), exactly as plan §4 says. The wall-clock mutant is killed (the fixture is fixed at 2026-01-02). A `GetLocalNow()` mutant would be equivalent under `FakeTimeProvider` (its LocalTimeZone defaults to UTC), so the code itself is the evidence here, and it is correct.
- **Filters come before count/order/paging.** `ApplyFilters` runs before `CountAsync` (line 79), with no contact pre-check query. This is ADR-0008 / Q8: an unknown GUID gives an empty page.
- **Error merging.** Status and contactId errors are added to the `ListQuery.Parse` dictionary (`errors ??= []`), and `paging is null || errors.Count > 0` stops a paging-valid, filter-invalid request (`status_shortcircuit` is killed by 15 tests). The messages match spec line 62 and ADR-0006 exactly.
- **Minimality.** Nothing from T-12+ appears: no nested `/api/contacts/{id}/todos` route, no `IsModified`, no concurrency catch, no `.WithName`/`.WithSummary`/`Produces*`, and no migration.
- **The T-10 Nit 2 is closed.** `status`/`contactId` are now load-bearing.
- **No test removed or weakened.** The diff only adds tests to `TodoListQueryTests.cs`, and the two integration classes are new.

## T-11 fix cycle 1: 2026-10-05: APPROVE

**Checks:** tests pass, backend 583/583 · lint pass (`dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exit 0; no web changes) · typecheck n.a. (no web changes)

**Finding 1 (Blocking) is closed.**
- **test-writer:** `open,done`, `open, overdue` and `done,done` were added to `Parse_UnknownStatus_ReturnsOneOfMessage_AC052` and `ListTodos_StatusUnknown_Returns400_AC052`. The integration URL is now built with `Uri.EscapeDataString`. 577 → 583 = +6 rows, and no existing row changed or was removed.
- **implementer:** `TodoListQuery.cs:48-57` `TryParseStatus` uses `OrdinalIgnoreCase` equality against `open`/`done`/`overdue`, exactly as plan §8 line 169 says. `Enum.TryParse`, `IsDefined` and `int.TryParse` are gone. `TodosEndpoints.cs` is unchanged since the first review.

| Mutant (scratch copy, full backend suite) | Tests failed |
|---|---|
| `old_enum` (restore the `Enum.TryParse` + `IsDefined` + `!int.TryParse` form) | 6 (the new comma rows) |
| `ordinal` (`open` compared case-sensitively) | 2 |
| `swap_open_done` (`open` → Done) | 4 |
| `overdue_as_open` | 7 |
| `startswith` (`StartsWith("open")`) | 7 |
| `always_true` (`TryParseStatus` returns true) | 16 |

**Notes:**
- The `no_isdefined` survivor from the first review no longer exists, because the code it mutated was removed.
- Every other point in the first T-11 review still holds (overdue boundaries, today from the injected clock as a UTC date, error merging, nothing from T-12+). None of it was touched by the fix.

## T-12: 2026-10-05: APPROVE

**Checks:** tests pass, backend 621/621, frontend 1/1 · lint pass (`dotnet format MicroCrm.slnx --verify-no-changes --no-restore` exit 0; oxlint clean) · typecheck pass (`tsc -b`; no web changes)

**Conformance:**
- `TodosEndpoints.cs:23` maps `GET /api/contacts/{id:guid}/todos`. `ListContactTodos` (lines 84-105) follows plan §283 in order: `TodoListQuery.Parse(page, pageSize, status, null)`, then 400, then `Contacts.AnyAsync`, then 404 problem, then `ListPage` with `query with { ContactId = id }`. The return type matches the plan signature (line 155). The nested route doesn't read `contactId`, as plan line 89 says.
- **The `ListPage` refactor is a pure extraction.** The filter/count/order/skip/take body moved unchanged, and `ListTodos` now only wraps it in `TypedResults.Ok`. Every existing `GET /api/todos` test still passes, and the shared-helper mutants (below) fail the global tests as before.
- **Minimality.** No `.WithName`/`.WithSummary`/`Produces*`/`WithTags`, no `IsModified`, no concurrency catch, no migration. Nothing from T-13+ is present. The `AnyAsync` contact check is plan-prescribed for this route only, so it isn't the forbidden create/update pre-check.
- **No test removed or weakened.** The test diff only adds lines: one new `[Fact]` in `TodoErrorHandlingTests.cs` and the new `ListContactTodosTests.cs`.

**Mutants (scratch copy, full backend suite):**
| Mutant | Tests failed |
|---|---|
| `no_created` (drop `ThenBy(CreatedAt)`), carry-forward | 2 (nested AC057 + global AC044) |
| `swap_created_id`, carry-forward | 2 (nested AC057 + global AC044) |
| `old_enum` (`Enum.TryParse` + `IsDefined` + `!int.TryParse`), carry-forward | 9 (3 nested comma rows of `InvalidQuery_..._AC058`, 3 global, 3 unit) |
| `lookup_first` (contact check before Parse) | 5 (AC061 ×4, AC074 unknown-contact row) |
| `no_contact_check` | 7 (AC060 ×4, AC061, AC068) |
| `check_by_todos` (exists = has to-dos) | 1 (AC059) |
| `no_route_filter` (drop `ContactId = id`) | 14 |
| `nested_no_status` / `nested_no_page` / `nested_no_pagesize` (null passed to Parse) | 16 / 10 / 15 |
| `nested_plain_400` (`Problem(400)` instead of `ValidationProblem`) | 17 |
| `nested_swallow_500` (catch → empty 200) | 1 (nested AC073) |
| `count_after_page` (`totalCount = 0`) | 31 |
| `unmapped` (route removed) | 32; `DeletedContact_..._AC068` and the AC060 rows pass, as expected for guards |
| `nested_reads_cid` (bind `contactId` and pass it to Parse) | **0**. See finding 1. |

**Carry-forwards closed.**
- The createdAt mutants are killed through the nested route by `ExistingContact_..._AC057`. It aligns the clock to a millisecond boundary, then makes 8 same-day creates 1 tick apart, interleaved with foreign to-dos.
- The comma-list rows are killed through the nested route.
- The nested endpoint calls the same `TodoListQuery.Parse`, and its errors are compared byte-for-byte with the global list.
- **Query errors come before the 404.** `lookup_first` is killed, and AC061 checks that the same id gives 404 once the query is valid.
- **The AC-068 guard now has teeth.** It fails under `no_contact_check`. It still passes if the route is removed, because it has no nested-200 check before the delete (finding 2), but AC057/AC058/AC059 catch that case.

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Should-fix | test-writer | `tests/MicroCrm.Api.Tests/Integration/Todos/ListContactTodosTests.cs` (AC057/AC058 tests) | Plan line 89 says the nested route ignores a `contactId` query value, and AC-057 requires 200 for an existing contact. Nothing pins this: a handler that binds `contactId` and passes it to `Parse` returns 400 for `?contactId=not-a-guid` on an existing contact, and the suite stays green (0 fails). | Add rows to the AC058 valid theory (or a small fact) for `?contactId=not-a-guid` and `?contactId={otherContact}`. Assert 200 with only the route contact's to-dos. The `nested_reads_cid` mutant should then fail. |
| 2 | Nit | test-writer | `ListContactTodosTests.cs:258-273` | `DeletedContact_Returns404_AC068` checks `GET /api/contacts/{id}` is 200 before the delete, but not the nested list. On its own it can't tell "contact deleted" from "route missing". Other tests cover the route, so this is not a gap in the suite. | Optionally assert that `GET /api/contacts/{id}/todos` is 200 before the delete. |

**Notes:**
- The AC058 invalid-query rows live in a separate method, `ListContactTodos_InvalidQuery_Returns400SameAsGlobalList_AC058`, and `ListContactTodos_UnknownRandomGuid_Returns404Problem_AC060` is an extra test. Neither is in the tasks.md test list. Add both to the traceability table at `/document`.
- **AC-072.** Every nested 400/404/500 goes through `ProblemAssert.IsProblemAsync` (exact key set, problem+json). No bare status asserts were added.
- **AC-073.** The test drops `Todos` only, after the contact is created, so the 500 comes from the to-do query and not from the contact check. The swallow mutant is killed.
- A non-GUID id with an invalid query (`not-a-guid?page=0`) gives 404, because the route constraint runs before query parsing. AC-061 only covers well-formed GUIDs, so this is consistent.

## T-13: 2026-10-05: APPROVE

**Checks:** backend 631/631 pass (1 run in the repo, 9 more full runs in a scratch copy, all green) · `dotnet format --verify-no-changes` exit 0 · frontend lint/typecheck not run (no `web/` changes).

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-067 (create) | `CreateTodo_ContactDeletedBeforeSave_Returns400AndCreatesNothing_AC067` (guard), `CreateTodo_WithContact_SendsNoContactLookup_AC067` | yes. The interceptor deletes the contact in `SavingChangesAsync`, and `Fired == 1` proves it ran. The check is `ProblemAssert` with the exact key set plus the exact message, with the row count unchanged. |
| AC-067 (update, relink) | `UpdateTodo_NewContactDeletedBeforeSave_Returns400AndLeavesTodoUnchanged_AC067` (guard) | yes. The whole GET body is byte-identical before and after. |
| AC-067 (update, keep link) | `UpdateTodo_KeptContactDeletedBeforeSave_Returns400AndTodoIsUnlinked_AC067` | yes. RED → GREEN is demanded by `IsModified`. The test asserts `contactId` null, the original title, and the original `updatedAt`, with the dangling count 0. |
| AC-067 (concurrent) | `CreateUpdateAndContactDelete_Concurrent_NoDanglingLinks_AC067` (guard) | yes. Codes stay in the documented sets, the dangling query returns 0, and `foreign_key_check` returns no rows. |
| AC-065 (completed delete never half) | `DeleteContacts_ConcurrentObserver_NeverSeesDanglingLink_AC065` (guard) | yes. It kills a non-atomic delete in every run (see below). |
| AC-057 (T-12 should-fix) | `ListContactTodos_ContactIdQueryValue_IsIgnored_AC057` (3 rows) | yes. It kills `nested_reads_cid` and `nested_prefers_cid`. |

**Mutants** (scratch copy, full suite unless noted):
| Mutant | Fails |
|---|---|
| `no_ismodified` (drop the `IsModified = true` line) | 1 (`UpdateTodo_KeptContact..._AC067`) |
| `create_precheck` (`AnyAsync` before `Add`, catch kept) | 1 (`CreateTodo_WithContact_SendsNoContactLookup`) |
| `update_precheck_before_lookup` | 2 (`UpdateTodo_WithContact_SendsNoContactLookup`, `UpdateTodo_UnknownContactToUnknownTodo_Returns404_AC028`) |
| `update_precheck_after_lookup` | 1 (`UpdateTodo_WithContact_SendsNoContactLookup`) |
| `create_precheck_nocatch` (pre-check replaces the 787 catch) | 3 (deterministic create race, no-lookup, smoke) |
| `nonatomic_delete` (contact delete with FKs off, then an app-side `ExecuteUpdateAsync` unlink) | 3 (both AC-065 trigger tests and the observer). The observer alone kills it 8/8 isolated and 5/5 in full runs. |
| `nonatomic_delete_delay` (the same, plus a 5 ms gap) | 3 |
| `nested_reads_cid` / `nested_prefers_cid` (T-12 carry-forward) | 1 / 2 (the new AC057 theory) |

**Carry-forwards closed.**
- **Pre-check survivors (create; update before and after the lookup).** All three are now killed, but by the two `SendsNoContactLookup` command-recording tests, not by the interceptor race tests. With the 787 catch in place, a pre-check plus the FK gives the same HTTP outcome under any interleaving, so only a SQL-level assertion can see it. Pinning the ADR-0008 decision this way is legitimate. `Assert.NotEmpty(recorder.Commands)` stops the test passing vacuously if the interceptor is never wired up. A pre-check *without* the catch is killed deterministically by the interceptor create test.
- **`IsModified`.** It is load-bearing (1 fail), and it is placed after the assignments and before the save, as plan design point 3 prescribes.
- **Stability.** The race class passed 12/12 in isolation, and the full suite passed 10/10 (repo plus scratch).
- **Do the concurrent tests interleave?** A scratch probe logged the smoke test's status distribution over 8 runs. Every run mixed outcomes: creates came back as 201 and 400 (between 3/15 and 16/2), and PUTs as 200 and 400. So deletes really do land between the other requests. The observer kills the non-atomic delete every time, so its reads overlap the deletes. The smoke test catches `create_precheck_nocatch` only probabilistically (0/8 isolated in one batch, 2/4 in another, 1/1 in a full run). That is acceptable for a guard, because the deterministic test pins the same thing.
- **Minimality.**
  - There is no `DbUpdateConcurrencyException` catch, and no `WithName`/`WithSummary`/`Produces*`/`WithTags`.
  - The only `AnyAsync` is the nested-list contact check, which plan §283 prescribes.
  - There is no new migration.
  - The production diff is the single `IsModified` line plus its comment.
- **T-12 Nit (AC-068).** Closed: the test now asserts the nested 200 before the delete.

**Findings:** none.

**Notes:**
- Add `CreateTodo_WithContact_SendsNoContactLookup_AC067`, `UpdateTodo_WithContact_SendsNoContactLookup_AC067` and `ListContactTodos_ContactIdQueryValue_IsIgnored_AC057` to the tasks.md traceability table at `/document`. They aren't in the T-13 test list.
- The no-lookup tests match any SQL that contains `Contacts`. If a later spec adds a contact join to to-do writes (for example, returning a contact name), these tests will need revisiting together with ADR-0008.
- T-14 carry-forwards:
  - add the concurrency catch before the 787 catch on PUT, complete and reopen;
  - add a deterministic pin for load-then-remove on the to-do delete (the T-09 should-fix);
  - metadata waits for T-15.
