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
