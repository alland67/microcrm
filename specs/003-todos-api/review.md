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
