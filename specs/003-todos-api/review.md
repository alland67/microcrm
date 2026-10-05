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
