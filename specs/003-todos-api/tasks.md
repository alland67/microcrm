# 003: To-dos API: CRUD, optional contact link, due date, complete/reopen: Tasks

Legend: `[ ]` todo · `[~]` in progress · `[x]` done
Each task = one red → green → refactor cycle and leaves the suite green. All tasks are **API-side only** (`BACKEND_TEST_CMD`).

Paths are abbreviated as follows:
- `T/` = `tests/MicroCrm.Api.Tests/`
- `TI/` = `tests/MicroCrm.Api.Tests/Integration/Todos/`
- `S/` = `src/MicroCrm.Api/`

Test names follow `Method_Scenario_Expected_ACnnn`.

**Read the plan first.** `plan.md` defines the schema (column names and types, tick timestamps, uppercase GUID text), the check orders, the messages, and the race techniques these tests rely on.

**Guard tests.** A guard is a test expected to **pass at RED**, because an earlier task, the database schema from T-01, or the existing pipeline already produces the behavior. Each guard is listed under its task. The test-writer reports guards in the RED evidence, separately from the failing tests. The reviewer confirms each guard would fail if its behavior regressed. Every task has at least one test that fails for the right reason.

**Minimal implementation.** Implementers add only what the task's failing tests demand. In particular:
- No OpenAPI metadata (`WithName`/`WithSummary`/`Produces*`) on any to-do operation before T-15.
- **Create:**
  - no validation-error branch before T-03;
  - no `contactId` handling (parse, assign) before T-04;
  - no FK-violation (787) catch before T-04.
- **`PUT`:**
  - no validation-error branch before T-06;
  - no not-found branch and no 787 catch before T-07;
  - no forced "`ContactId` modified" before T-13;
  - no `DbUpdateConcurrencyException` catch before T-14.
- **Complete/reopen:** the null-load → 404 branch is part of T-08, but no `DbUpdateConcurrencyException` catch before T-14.
- **Delete:** a single `ExecuteDeleteAsync` from T-09 on (never load-then-remove).
- **Contact pre-check:** no contact-existence pre-check query on create or update, ever (ADR-0008). The FK decides.

**One schema change.** Only T-01 adds a migration (`CreateTodos`, via `MIGRATIONS_ADD_CMD`). Its `Up` must only create `Todos` and its indexes, with no `AlterColumn` or rebuild of `Contacts` (ADR-0008 migration hazard). If any later task seems to need a schema change, stop and report `BLOCKED`.

**Direct SQL in tests.**
- EF stores GUIDs as **uppercase** text. Match ids with `lower(Id) = $id`, and write FK values as `id.ToString().ToUpperInvariant()` (or read them from `Contacts`).
- To-do timestamps are **UTC ticks** (`INTEGER`, ADR-0009).
- `DueDate` is `TEXT` `yyyy-MM-dd`.
- `IsDone` is `0`/`1`.

---

- [x] T-01: To-do table with a contact link enforced by the database. Done: CreateTodos migration, FK SetNull, CHECK, 2 indexes, ForeignKeys forced; 12 tests; review APPROVE (1 nit: redundant index name, kept)
  - **ACs:** AC-062, AC-065 (failed delete rolls back), AC-066 (store level), AC-067 (store level), AC-069 (store level), NFR-003 (indexes exist), AC-044 (sortable column types)
  - **Depends on:** none
  - **Tests:**
    - `TI/TodoStoreTests.cs` (new; shared fixture, unique data per test; seeds contacts through `POST /api/contacts` and to-do rows with direct SQL):
      - `Store_TodosColumns_MatchPlannedTypes_AC044`: `PRAGMA table_info(Todos)` gives names, declared types, and NOT NULL per the plan's schema. Order is not checked.
      - `Store_TodosContactId_IsForeignKeyToContactsWithSetNull_AC066`: `PRAGMA foreign_key_list(Todos)` gives table `Contacts`, from `ContactId`, to `Id`, `on_delete` `SET NULL`.
      - `Store_TodosIndexes_SupportUnlinkAndFilters_NFR003`: `PRAGMA index_list(Todos)` contains `IX_Todos_ContactId` and `IX_Todos_IsDone_DueDate`, and `index_info` shows the planned columns in order.
      - `Store_TestConnections_HaveForeignKeysOn_AC066`: `PRAGMA foreign_keys` = 1 on a raw `SqliteConnection` from `factory.ConnectionString`.
      - `Store_DeletingContactDirectly_UnlinksTodosAndKeepsOtherColumns_AC066`:
        - seed two linked rows;
        - snapshot `SELECT *`;
        - `factory.ExecuteSqlAsync("DELETE FROM Contacts WHERE ...")`;
        - assert both rows exist, `ContactId IS NULL`, and every other column is unchanged.
      - `Store_LinkToMissingContact_IsRejectedByStore_AC067`: a raw `INSERT` with an unknown `ContactId` throws `SqliteException` with extended code 787.
      - `Store_InconsistentDoneState_IsRejectedByStore_AC069`: Theory over `(IsDone=1, CompletedAt NULL)` and `(IsDone=0, CompletedAt set)`. A raw `INSERT` throws `SqliteException` with extended code 275 (CHECK).
      - `DeleteContact_WithLinkedTodos_Returns204AndUnlinks_AC062`: rows seeded directly; `DELETE /api/contacts/{id}` → 204; the rows read directly have `ContactId IS NULL`.
      - `Api_ForeignKeysDisabledInConnectionString_StillEnforced_AC066`:
        - Build a derived host: `factory.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:MicroCrm", factory.ConnectionString + ";Foreign Keys=False"))`. If that doesn't override the factory's setting, use a small `ApiFactory` subclass (test-only).
        - (a) `PRAGMA foreign_keys` is 1 on the `AppDbContext` connection resolved from the derived host's services.
        - (b) `DELETE /api/contacts/{id}` through the derived host unlinks a directly seeded to-do.
    - `TI/ContactDeleteAtomicityTests.cs` (new; **own fixture**; each test installs and drops its trigger in `try/finally`):
      - `DeleteContact_FailsAfterUnlink_RollsBackRemovalAndUnlink_AC065`:
        - Trigger: `AFTER DELETE ON Contacts WHEN (SELECT COUNT(*) FROM Todos WHERE ContactId = OLD.Id) = 0 BEGIN SELECT RAISE(ABORT, 'x'); END`.
        - `DELETE /api/contacts/{id}` → 500 problem+json, no internals.
        - Read directly: the contact row is present and every seeded to-do still holds its id.
      - `DeleteContact_FailsDuringUnlink_RollsBackRemoval_AC065`: same, with the trigger `BEFORE UPDATE ON Todos BEGIN SELECT RAISE(ABORT, 'x'); END`.
  - **Likely source files:**
    - `S/Features/Todos/Todo.cs` (properties only)
    - `S/Data/TodoConfiguration.cs`: FK `HasOne<Contact>().WithMany().HasForeignKey(t => t.ContactId).OnDelete(DeleteBehavior.SetNull)`; tick converters on `CreatedAt`/`UpdatedAt`/`CompletedAt`; `CK_Todos_DoneState`; `IX_Todos_IsDone_DueDate`
    - `S/Data/AppDbContext.cs`
    - `S/Data/Migrations/*_CreateTodos.cs` + `.Designer.cs` + `AppDbContextModelSnapshot.cs` (generated)
    - `S/Program.cs` (`SqliteConnectionStringBuilder { ForeignKeys = true }` around the configured connection string)
  - **Guards:** `Store_TestConnections_HaveForeignKeysOn_AC066` (bundled native default).
  - **Expected RED:**
    - every other test fails with `no such table: Todos` (schema, seeding, triggers);
    - in `Api_ForeignKeysDisabled...`, the seed fails the same way; once the table exists without the `Program.cs` change, (a) reads 0 and (b) leaves a dangling `ContactId`.
  - **Done when:**
    - the `Todos` table exists exactly as planned, with the FK `ON DELETE SET NULL`, the CHECK constraint, and both indexes;
    - deleting a contact (through the API, directly in the store, or through a host configured with `Foreign Keys=False`) keeps its to-dos and nulls their link;
    - a failing contact delete changes nothing;
    - the migration doesn't touch `Contacts`.

    Suite green.

- [x] T-02: Create a to-do and get it by id. Done: POST + GET by id, TodoInput core, relative Location; review APPROVE after 1 pre-review fix (absolute Host-based Location replaced) (1 nit: TrimToNull duplicated)
  - **ACs:** AC-001, AC-002, AC-003, AC-004, AC-005, AC-006, AC-009, AC-018, AC-019, NFR-002, NFR-004 (guard), AC-073 (create, get), AC-072 (404)
  - **Depends on:** T-01
  - **Tests:**
    - `T/Integration/Infrastructure/ApiFactory.cs` (extend): `ResetAsync` runs `DELETE FROM Todos` and then `DELETE FROM Contacts`.
    - `TI/CreateTodoTests.cs` (new; unique data per test):
      - `CreateTodo_WithTitleNotesAndDueDate_Returns201WithLocationAndBody_AC001`: all nine members; `contactId` is null for now.
      - `CreateTodo_ThenGet_ReturnsSameValuesOnNewConnections_AC001`: new `HttpClient`.
      - `CreateTodo_WithOnlyTitle_ReturnsNullsAndIsDoneFalse_AC002`: raw JSON; members present.
      - `CreateTodo_IgnoresBodyIdTimestampsAndDoneState_AC003`:
        - body with `id`, `createdAt`, `updatedAt`, `isDone: true`, `completedAt`;
        - response: new non-empty id, `isDone` false, `completedAt` null;
        - `createdAt == updatedAt == factory.Time.GetUtcNow()`;
        - two creates produce different ids.
      - `CreateTodo_WithSurroundingWhitespace_StoresTrimmed_AC004`: Theory; also notes `""`/`"  "` → null.
      - `CreateTodo_WithValidDueDate_ReturnsSameString_AC005`: Theory over today and yesterday (from `factory.Time`), `0001-01-01`, `9999-12-31`, `2024-02-29`, and `" 2026-10-05 "` → `"2026-10-05"`.
      - `CreateTodo_DueDateOmittedNullOrBlank_IsNull_AC006`
      - `CreateTodo_FieldsAtMax_Returns201_AC009`: 200/4000 exactly, and wrapped in whitespace.
      - `CreateTodo_Json_IsCamelCaseWithNullsDateOnlyAndUtcOffsets_NFR002`: raw string: `"dueDate":"2026-10-05"`, `"isDone":false`, timestamps end with `Z` or `+00:00`, nulls present.
      - `CreateTodo_TitleAndNotesNotLoggedAtInformationOrAbove_NFR004`: capturing `ILoggerProvider`, as in `CreateContactConflictTests`.
    - `TI/GetTodoByIdTests.cs` (new):
      - `GetTodo_Existing_Returns200WithShape_AC018`
      - `GetTodo_UnknownGuid_Returns404Problem_AC019`
      - `GetTodo_NonGuidId_Returns404Problem_AC019`: Theory over `not-a-guid`, `123`.
    - `T/Unit/Todos/TodoInputTests.cs` (new):
      - `Parse_TrimsTitleAndNotes_BlankNotesBecomeNull_AC004`
      - `Parse_ValidDueDate_ReturnsDateOnly_AC005`
      - `Parse_BlankDueDate_ReturnsNull_AC006`
    - `TI/TodoErrorHandlingTests.cs` (new file; class `TodoErrorHandlingTests` with its **own fixture**; each test runs `DROP TABLE IF EXISTS Todos` first):
      - `CreateTodo_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC073`
      - `GetTodo_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC073`
  - **Likely source files:**
    - `S/Features/Todos/TodoDtos.cs` (`CreateTodoRequest`, `TodoResponse`)
    - `S/Features/Todos/TodoInput.cs` (trimming, null mapping, `DueDate` parse; no error rules yet)
    - `S/Features/Todos/TodosEndpoints.cs` (POST, GET by id; 404 = `TypedResults.Problem(statusCode: 404)`)
    - `S/Program.cs` (`app.MapTodosEndpoints()`)
    - `S/MicroCrm.Api.http`
  - **Guards:** `GetTodo_UnknownGuid...` and `GetTodo_NonGuidId...` (no route yet → 404 problem+json from status code pages). The two AC-073 tests are **not** guards: with no route they get 404 instead of 500, so they fail at RED.
  - **Size note:** many tests, one small handler pair. The shared parse gives the field behaviors at once.
  - **Expected RED:** `POST /api/todos` → 404 (no route); GET of a created id can't run.
  - **Done when:**
    - a valid create returns 201 with Location and the full camelCase body (dates as `YYYY-MM-DD`, nulls present, open, server-assigned id and equal timestamps from the clock);
    - values are trimmed, and blank optional fields are null;
    - GET by id returns it from new connections, and 404 problem+json for unknown or non-GUID ids;
    - database failures give safe 500s.

    Suite green.

- [x] T-03: Create rejects invalid title, notes, due date, and unreadable bodies. Done: TodoInput validation + ValidationProblem, strict TryParseExact dueDate, TrimToNull moved to Common/TextNormalization; review APPROVE after 1 fix cycle (redundant date pre-checks removed)
  - **ACs:** AC-010, AC-011, AC-012, AC-014, AC-015 (create), AC-074 (create), AC-072 (400)
  - **Depends on:** T-02
  - **Tests:**
    - `T/Unit/Todos/TodoInputTests.cs` (extend):
      - `Parse_MissingTitle_ReturnsRequired_AC010`: Theory over null, `""`, `"  "`.
      - `Parse_OverMax_ReturnsMaxMessage_AC011`: title 201, notes 4001; exact messages.
      - `Parse_InvalidDueDate_ReturnsDateMessage_AC012`: Theory over `2026-13-01`, `2026-02-30`, `05/10/2026`, `2026-10-5`, `2026-10-05T00:00:00Z`, `226-10-05`, `02026-10-05`, `２０２６-10-05`, `2026/10/05`.
      - `Parse_MultipleErrors_ReturnsAllKeys_AC014`
    - `TI/CreateTodoValidationTests.cs` (new; each test asserts the to-do row count is unchanged, read directly):
      - `CreateTodo_WithoutTitle_Returns400Required_AC010`: Theory over missing, `null`, `""`, `"  "`.
      - `CreateTodo_FieldOverMax_Returns400_AC011`
      - `CreateTodo_InvalidDueDate_Returns400WithDueDateError_AC012`: Theory, same values as the unit test.
      - `CreateTodo_MultipleInvalidFields_ReportsAllCamelCaseKeys_AC014`
      - `CreateTodo_MalformedBody_Returns400Problem_AC015`: raw bodies `{`, empty, `null`, `[]`, `{"title":1}`, `{"title":"x","dueDate":5}`.
      - `CreateTodo_ValidationMessages_FollowStyle_AC074`: `ProblemAssert.AssertValidationMessageStyle` plus exact messages.
  - **Likely source files:**
    - `S/Features/Todos/TodoInput.cs` (rules and messages)
    - `S/Features/Todos/TodosEndpoints.cs` (`TypedResults.ValidationProblem(errors)`)
  - **Guards:** AC-015 (binding failures are 400 problem+json before the handler runs).
  - **Expected RED:**
    - a missing title reaches the insert and fails NOT NULL → 500;
    - an invalid due date is stored as null or ignored → 201;
    - over-max values → 201.
  - **Done when:**
    - every invalid create returns 400 validation ProblemDetails listing all offending fields by camelCase key with the exact ADR-0006-style messages;
    - unreadable bodies return 400 ProblemDetails;
    - nothing is stored.

    Suite green.

- [x] T-04: Link a new to-do to a contact; deleting the contact unlinks it. Done: TodoInput.ContactId, 787-only FK mapping to 400 contactId, no pre-check; review APPROVE (1 nit: stale guard comments, deferred to T-05)
  - **ACs:** AC-007, AC-008, AC-013 (create), AC-014 (with `contactId`), AC-016, AC-017, AC-062, AC-063 (get), AC-064, AC-066 (through the API), AC-073 (create, non-FK constraint), AC-074 (create, `contactId`)
  - **Depends on:** T-03
  - **Tests:**
    - `T/Unit/Todos/TodoInputTests.cs` (extend):
      - `Parse_ContactId_ParsesGuidOrBlankToNull_AC008`
      - `Parse_MalformedContactId_ReturnsGuidMessage_AC013`: Theory over `abc`, `123`, `0f8fad5b-d9cb-469f-a165`.
    - `TI/CreateTodoContactLinkTests.cs` (new):
      - `CreateTodo_WithExistingContactId_LinksAndReturnsIt_AC007`: also verified with GET.
      - `CreateTodo_ContactIdOmittedNullOrBlank_IsUnlinked_AC008`
      - `CreateTodo_MalformedContactId_Returns400ValidGuid_AC013`
      - `CreateTodo_UnknownContactId_Returns400MustReferToExistingContact_AC016`: a unique v7 GUID; row count unchanged.
      - `CreateTodo_FieldErrorAndUnknownContact_ReportsOnlyFieldErrors_AC017`
      - `CreateTodo_InvalidFieldsIncludingContactId_ReportsAll_AC014`
      - `CreateTodo_ContactIdMessages_FollowStyle_AC074`
    - `TI/ContactDeleteUnlinksTodosTests.cs` (new; each test **asserts the to-do is linked before deleting the contact**):
      - `DeleteContact_WithLinkedTodos_Returns204_AC062`
      - `DeleteContact_LinkedTodosRemainWithNullContactAndOtherFieldsUnchanged_AC063`:
        - open to-dos with title, notes, and due date; complete/reopen don't exist yet, and a completed to-do is covered in T-10;
        - `factory.Time.Advance` before the delete;
        - GET with a new client shows `contactId` null and every other member identical, including `isDone`, `completedAt`, and `updatedAt`.
      - `DeleteContact_LeavesOtherContactsTodosAndUnlinkedTodosUnchanged_AC064`
      - `DeleteContactDirectlyInStore_ApiReturnsTodosUnlinked_AC066`: `factory.ExecuteSqlAsync("DELETE FROM Contacts ...")`, then GET.
    - `TI/TodoErrorHandlingTests.cs` (extend; new class `CreateTodoNonForeignKeyFailureTests` with its **own fixture**):
      - `CreateTodo_WhenNonForeignKeyConstraintFails_Returns500NotContactError_AC073`:
        - trigger `BEFORE INSERT ON Todos ... RAISE(ABORT, 'x')` (extended code 1811);
        - a create with a valid existing `contactId` → 500 problem+json, **not** 400, no internals;
        - row count unchanged.
  - **Likely source files:**
    - `S/Features/Todos/TodoInput.cs` (`contactId` parse)
    - `S/Features/Todos/TodosEndpoints.cs` (assign `ContactId`; catch `DbUpdateException` when `SqliteErrors.IsForeignKeyViolation` → validation problem `contactId`)
    - `S/Data/SqliteErrors.cs` (`IsForeignKeyViolation`, extended code 787 exactly)
  - **Guards:**
    - AC-008 (blank `contactId` is ignored today);
    - the AC-073 trigger test (unhandled → 500 today; it pins that the new catch matches 787 only).
  - **Expected RED:**
    - AC-007: `contactId` comes back null;
    - AC-013/AC-016: 201 instead of 400;
    - AC-062..AC-066: the "linked before delete" precondition fails.
  - **Done when:**
    - creates link to existing contacts; a malformed `contactId` → 400 `Must be a valid GUID.`; an unknown one (after field rules pass) → 400 `Must refer to an existing contact.` with nothing stored;
    - non-FK constraint failures stay safe 500s;
    - deleting a contact (through the API or directly in the store) returns 204 and leaves its to-dos readable with `contactId` null and every other field unchanged, other to-dos untouched.

    Suite green.

- [x] T-05: Update a to-do (full replace). Done: PUT full replace via shared Parse core, updatedAt from TimeProvider; review APPROVE after 1 pre-review fix (early validation branch removed) (1 nit: AC-030 snapshot lacks a to-do already linked to the new contact, deferred to T-06)
  - **ACs:** AC-020 (get), AC-021, AC-022, AC-023, AC-024, AC-025, AC-030
  - **Depends on:** T-04
  - **Tests:**
    - `TI/UpdateTodoTests.cs` (new; unique data per test):
      - `UpdateTodo_WithValidBody_Returns200WithUpdatedTodo_AC020`
      - `UpdateTodo_ThenGet_ReturnsPersistedValuesOnNewConnections_AC020`
      - `UpdateTodo_OptionalFieldOmittedNullOrBlank_StoredAsNull_AC021`:
        - Theory over `notes`/`dueDate`/`contactId` × omitted/`null`/`""`/`"  "`;
        - the to-do had values before (linked to a contact, with a due date and notes).
      - `UpdateTodo_IgnoresBodyIdTimestampsAndDoneState_AC022`: a different `id`, bogus `createdAt`/`updatedAt`, `isDone: true`, `completedAt`; the response and GET keep the original id, `createdAt`, `isDone` false, and `completedAt` null.
      - `UpdateTodo_SetsUpdatedAtFromClock_EvenWhenUnchanged_AC023`: `factory.Time.Advance(...)`, then PUT identical values.
      - `UpdateTodo_LinksToContact_FromUnlinkedOrOtherContact_AC024`: Theory.
      - `UpdateTodo_AtMaxWithWhitespace_StoresTrimmed_AC025`
      - `UpdateTodo_LeavesOtherTodosAndContactsUnchanged_AC030`: raw JSON snapshots before and after.
  - **Likely source files:**
    - `S/Features/Todos/TodoDtos.cs` (+ `UpdateTodoRequest`)
    - `S/Features/Todos/TodoInput.cs` (+ `Parse(UpdateTodoRequest)`; both overloads share one private core)
    - `S/Features/Todos/TodosEndpoints.cs` (PUT: parse → tracked load → assign four fields + `UpdatedAt` → save → `Ok`)
    - `S/MicroCrm.Api.http`
  - **Expected RED:** 405 problem+json (only GET is mapped on `/api/todos/{id}`).
  - **Done when:**
    - a valid PUT to an existing to-do returns 200 with the full body;
    - all four editable fields are replaced (omitted optional → null, omitted `contactId` unlinks) and trimmed;
    - `id`, `createdAt`, `isDone`, and `completedAt` never change; `updatedAt` equals the clock;
    - linking and relinking work; nothing else changes.

    Suite green.

- [x] T-06: Update rejects invalid fields (same errors as create). Done: PUT validation branch before lookup, shared Parse core; AC-030 snapshot strengthened (T-05 nit); review APPROVE
  - **ACs:** AC-010, AC-011, AC-012, AC-013, AC-014, AC-015 (update), AC-027, AC-074 (update), NFR-005
  - **Depends on:** T-05
  - **Tests:**
    - `T/Unit/Todos/TodoInputTests.cs` (extend):
      - `Parse_UpdateAndCreateRequests_ProduceIdenticalErrors_NFR005`: Theory over several invalid payloads; compare key by key and message by message.
    - `TI/UpdateTodoValidationTests.cs` (new; each test seeds a to-do and, after the 400, reads it with GET on a new client or directly, to prove it's unchanged):
      - `UpdateTodo_WithoutTitle_Returns400Required_AC010`
      - `UpdateTodo_FieldOverMax_Returns400_AC011`
      - `UpdateTodo_InvalidDueDate_Returns400_AC012`
      - `UpdateTodo_MalformedContactId_Returns400_AC013`
      - `UpdateTodo_MultipleInvalidFields_ReportsAll_AC014`
      - `UpdateTodo_MalformedBody_Returns400Problem_AC015`: raw bodies as in T-03, plus `{"title":"x","contactId":7}`.
      - `UpdateTodo_InvalidBodyToUnknownId_Returns400_AC027`
      - `UpdateTodo_ValidationMessages_FollowStyle_AC074`
      - `UpdateTodo_SameInvalidPayload_SameErrorsAsCreate_NFR005`: POST and PUT the same payload; compare `errors`.
  - **Likely source files:** `S/Features/Todos/TodosEndpoints.cs` (validation branch before the lookup)
  - **Guards:** AC-015 (binding); the NFR-005 unit test (shared core from T-05).
  - **Expected RED:** invalid bodies reach the save path → 500 (null input) instead of 400.
  - **Done when:**
    - every invalid update returns 400 validation ProblemDetails identical to create's for the same payload;
    - unreadable bodies → 400;
    - validation precedes existence;
    - the stored to-do never changes.

    Suite green.

- [ ] T-07: Update of an unknown to-do (404) or to a missing contact (400)
  - **ACs:** AC-026, AC-028, AC-029, AC-073 (update), AC-072 (404)
  - **Depends on:** T-06
  - **Tests:**
    - `TI/UpdateTodoTests.cs` (extend):
      - `UpdateTodo_UnknownGuid_Returns404AndCreatesNothing_AC026`: GET on that id still 404; row count unchanged.
      - `UpdateTodo_NonGuidId_Returns404_AC026`: Theory over `not-a-guid`, `123`; row count unchanged.
      - `UpdateTodo_UnknownContactToUnknownTodo_Returns404_AC028`
      - `UpdateTodo_UnknownContactId_Returns400AndLeavesTodoUnchanged_AC029`: exact `Must refer to an existing contact.`; GET shows the original values (including the original link).
    - `TI/TodoErrorHandlingTests.cs` (extend):
      - class `TodoErrorHandlingTests`: `UpdateTodo_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC073`
      - new class `UpdateTodoNonForeignKeyFailureTests` (**own fixture**): `UpdateTodo_WhenNonForeignKeyConstraintFails_Returns500AndLeavesTodoUnchanged_AC073`:
        - trigger `BEFORE UPDATE ON Todos ... RAISE(ABORT, 'x')`;
        - PUT with a valid existing `contactId` → 500, not 400;
        - direct read shows the row unchanged.
  - **Likely source files:** `S/Features/Todos/TodosEndpoints.cs`:
    - null lookup → `TypedResults.Problem(statusCode: 404)`;
    - catch `DbUpdateException` when `IsForeignKeyViolation` → validation problem `contactId`.
  - **Guards:** AC-026 non-GUID (no route); both AC-073 tests.
  - **Expected RED:** AC-026/AC-028 get 500 (null dereference); AC-029 gets 500 (unhandled 787).
  - **Done when:**
    - a PUT to a missing to-do → 404 problem+json, nothing created, and to-do existence is checked before contact existence;
    - a PUT linking a missing contact → 400 `contactId` with the to-do unchanged;
    - other DB failures → safe 500s with data unchanged.

    Suite green.

- [ ] T-08: Complete and reopen
  - **ACs:** AC-031, AC-032, AC-033, AC-034, AC-035, AC-036, AC-037, AC-073 (complete/reopen)
  - **Depends on:** T-07
  - **Tests:**
    - `T/Unit/Todos/TodoTests.cs` (new):
      - `Complete_Open_SetsDoneAndTimestamps_ReturnsTrue_AC031`
      - `Complete_AlreadyDone_ChangesNothing_ReturnsFalse_AC032`
      - `Reopen_Done_ClearsCompletedAtAndSetsUpdatedAt_ReturnsTrue_AC033`
      - `Reopen_AlreadyOpen_ChangesNothing_ReturnsFalse_AC034`
    - `TI/CompleteReopenTodoTests.cs` (new; advance the clock between steps so timestamps differ):
      - `CompleteTodo_Open_Returns200DoneWithCompletedAtNow_AC031`: also persisted (GET on a new client).
      - `CompleteTodo_AlreadyDone_Returns200Unchanged_AC032`
      - `ReopenTodo_Done_Returns200OpenWithUpdatedAtNow_AC033`: also persisted.
      - `ReopenTodo_AlreadyOpen_Returns200Unchanged_AC034`
      - `CompleteAndReopen_ChangeOnlyDoneStateAndIgnoreBody_AC035`:
        - bodies such as `{"title":"x","isDone":false,"completedAt":null}` and malformed `{`;
        - other fields and other to-dos unchanged.
      - `CompleteOrReopen_UnknownOrNonGuidId_Returns404_AC036`: Theory over action × (unknown GUID, `not-a-guid`); row count unchanged.
      - `UpdateTodo_WhenDone_KeepsDoneAndCompletedAt_AC037`
    - `TI/TodoErrorHandlingTests.cs` (extend):
      - class `TodoErrorHandlingTests`: `CompleteTodo_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC073`, `ReopenTodo_WhenDatabaseFails_..._AC073`
      - class `UpdateTodoNonForeignKeyFailureTests`: `CompleteTodo_WhenUpdateRejected_Returns500AndLeavesTodoOpen_AC073`
  - **Likely source files:**
    - `S/Features/Todos/Todo.cs` (`Complete(now)`, `Reopen(now)`)
    - `S/Features/Todos/TodosEndpoints.cs`:
      - two POST actions, no body parameter;
      - null → 404 problem;
      - no-op → 200 without `SaveChanges`.
    - `S/MicroCrm.Api.http`
  - **Guards:** AC-036 (no route yet → 404). AC-037 can't run at RED because there is no complete endpoint yet, so it fails; after GREEN it passes without extra code, because PUT never assigns the done state. The AC-073 tests fail at RED (404 from the missing route instead of 500).
  - **Expected RED:** `POST /api/todos/{id}/complete` → 404 (no route) where 200 is expected.
  - **Done when:**
    - complete and reopen return 200 with the new state and persist it;
    - repeating either is a no-op that keeps `completedAt` and `updatedAt`;
    - only the done-state fields change, and bodies are ignored;
    - unknown or non-GUID ids → 404;
    - PUT keeps a done to-do done.

    Suite green.

- [ ] T-09: Delete a to-do
  - **ACs:** AC-038 (get), AC-039, AC-040, AC-041, AC-071, AC-073 (delete)
  - **Depends on:** T-08
  - **Tests:**
    - `TI/DeleteTodoTests.cs` (new):
      - `DeleteTodo_Existing_Returns204WithEmptyBody_AC038`
      - `DeleteTodo_ThenGet_Returns404OnNewConnections_AC038`: row count 0, read directly.
      - `DeleteTodo_UnknownOrAlreadyDeleted_Returns404Problem_AC039`
      - `DeleteTodo_NonGuidId_Returns404AndDeletesNothing_AC039`
      - `DeleteTodo_LeavesContactAndOtherTodosUnchanged_AC040`
      - `UpdateCompleteReopen_AfterDelete_Return404AndDoNotRecreate_AC041`
      - `DeleteTodo_ConcurrentSameId_ExactlyOne204Rest404_AC071`: 10 DELETEs behind a shared gate; zero 5xx.
    - `TI/TodoErrorHandlingTests.cs` (extend):
      - class `TodoErrorHandlingTests`: `DeleteTodo_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC073`
      - new class `DeleteTodoRejectedByDatabaseTests` (**own fixture**): `DeleteTodo_WhenDatabaseRejects_Returns500AndTodoStillExists_AC073` (trigger `BEFORE DELETE ON Todos`)
  - **Likely source files:** `S/Features/Todos/TodosEndpoints.cs` (DELETE: `ExecuteDeleteAsync`; 0 → 404 problem, else 204), `S/MicroCrm.Api.http`
  - **Guards:** AC-039 non-GUID (no route). AC-041 fails at RED only because the delete in its arrange step gets 405; its 404 branches already exist from T-07/T-08. The AC-073 tests fail at RED (405 instead of 500).
  - **Expected RED:** 405 (DELETE isn't mapped on `/api/todos/{id}`).
  - **Done when:**
    - deleting an existing to-do → 204 with an empty body, gone for GET on new connections;
    - missing, already-deleted, or non-GUID ids → 404, deleting nothing;
    - the contact and other to-dos are untouched;
    - later PUT/complete/reopen → 404 without recreating;
    - concurrent deletes → exactly one 204;
    - DB failures → safe 500s with the row kept.

    Suite green.

- [ ] T-10: List to-dos: envelope, order, paging
  - **ACs:** AC-042, AC-043, AC-044, AC-045, AC-046, AC-056 (paging), AC-020 (list), AC-038 (list), AC-063 (list), AC-074 (list paging), AC-073 (list)
  - **Depends on:** T-09
  - **Tests:**
    - `T/Unit/Todos/TodoListQueryTests.cs` (new):
      - `Parse_Defaults_Page1PageSize20NoFilters_AC042`
      - `Parse_InvalidPagingValues_ReturnSameMessagesAsContacts_AC046`: Theory over `0`, `-1`, `101`, `abc`, `1.5`, `" 1"`; messages exactly as `ListQuery`.
    - `TI/ListTodosTests.cs` (new; `IAsyncLifetime` calls `ResetAsync`):
      - `ListTodos_NoParameters_ReturnsDefaultEnvelope_AC042`: 25 to-dos → 20 items, `page` 1, `pageSize` 20, `totalCount` 25.
      - `ListTodos_Empty_ReturnsEmptyItemsAndZeroTotal_AC043`
      - `ListTodos_OrdersByDueDateNullsLastThenCreatedAtThenId_AC044`:
        - mixed due dates, some null;
        - equal due dates with distinct `createdAt` (`factory.Time.Advance`) and with equal `createdAt` (no advance);
        - expected ids for the tiebreak sorted with `string.CompareOrdinal` on the lowercase string, **not** `Guid.CompareTo`.
      - `ListTodos_ShowsUpdatedValues_AC020`
      - `ListTodos_ExcludesDeletedTodo_AC038`
      - `ListTodos_AfterContactDelete_ShowsTodosUnlinkedAndOtherwiseUnchanged_AC063`: includes a **completed** linked to-do; its `isDone`, `completedAt`, and `updatedAt` are unchanged after the contact delete (clock advanced before the delete).
    - `TI/ListTodosPagingTests.cs` (new; `ResetAsync`):
      - `ListTodos_PageSlices_EchoPagingAndTotal_AC045`
      - `ListTodos_PageBeyondLast_ReturnsEmptyItems_AC045`
      - `ListTodos_PageSize100_IsAccepted_AC045`
      - `ListTodos_AllPages_ReturnEveryTodoExactlyOnceWithTies_AC045`
      - `ListTodos_InvalidPageOrPageSize_Returns400_AC046`
      - `ListTodos_InvalidPageAndPageSize_ReportsBoth_AC056`
      - `ListTodos_ValidationMessages_FollowStyle_AC074`
    - `TI/TodoErrorHandlingTests.cs` (extend; class `TodoErrorHandlingTests`): `ListTodos_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC073`
  - **Likely source files:**
    - `S/Features/Todos/TodoListQuery.cs` (paging via `ListQuery.Parse`)
    - `S/Features/Todos/TodosEndpoints.cs` (`GET /api/todos` + private list helper: count, order, skip/take with `TryGetSkip`)
  - **Guards:** none expected (no GET on `/api/todos` yet).
  - **Expected RED:** `GET /api/todos` → 405 (only POST is mapped).
  - **Done when:**
    - the list returns the paged envelope in the specified order (no due date last, then `createdAt`, then id) with exact `totalCount`;
    - paging rules and messages match the contacts list;
    - updates, deletes, and contact unlinks are reflected;
    - DB failure → safe 500.

    Suite green.

- [ ] T-11: List filters: status (open, done, overdue) and contact
  - **ACs:** AC-047, AC-048, AC-049, AC-050, AC-051, AC-052, AC-053, AC-054, AC-055, AC-056 (all parameters), AC-068 (filter), AC-074 (status, `contactId`)
  - **Depends on:** T-10
  - **Tests:**
    - `T/Unit/Todos/TodoListQueryTests.cs` (extend):
      - `Parse_Status_TrimmedCaseInsensitiveBlankMeansNone_AC051`
      - `Parse_UnknownStatus_ReturnsOneOfMessage_AC052`
      - `Parse_ContactId_GuidOrBlank_AC053`
      - `Parse_MalformedContactId_ReturnsGuidMessage_AC054`
      - `Parse_AllInvalid_ReportsEveryParameter_AC056`
    - `TI/ListTodosFilterTests.cs` (new; `ResetAsync`; every date derived from `factory.Time.GetUtcNow()`):
      - `ListTodos_StatusOpen_ReturnsOnlyOpenIncludingOverdue_AC047`
      - `ListTodos_StatusDone_ReturnsOnlyDone_AC048`
      - `ListTodos_StatusOverdue_ReturnsOnlyOpenPastDue_AC049`: seed yesterday-open, today-open, tomorrow-open, no-date-open, yesterday-done; only the first is returned.
      - `ListTodos_StatusBlankOrDifferentCase_AC051`: Theory over `""`, `"  "`, `"OPEN"`, `" Done "`, `"OverDue"`.
      - `ListTodos_StatusUnknown_Returns400_AC052`: Theory over `pending`, `opened`, `1`.
      - `ListTodos_ContactIdFilter_ReturnsOnlyThatContactsTodos_AC053`: also an unknown GUID → empty page with `totalCount` 0, and blank → no filter.
      - `ListTodos_MalformedContactId_Returns400_AC054`
      - `ListTodos_CombinedFilters_MatchAllOrderedAndPaged_AC055`
      - `ListTodos_AllQueryParametersInvalid_ReportsAll_AC056`: `page`, `pageSize`, `status`, `contactId`.
      - `ListTodos_ContactIdOfDeletedContact_ExcludesFormerTodos_AC068`
      - `ListTodos_FilterMessages_FollowStyle_AC074`
    - `TI/OverdueClockTests.cs` (new; **own fixture**; single test):
      - `ListTodos_Overdue_IncludesTodoDueYesterdayAfterUtcMidnight_AC050`:
        1. advance the fake clock to 23:59:59 UTC of day D;
        2. create an open to-do due D;
        3. `?status=overdue` excludes it;
        4. `Advance(1 s)`;
        5. `?status=overdue` includes it.
  - **Likely source files:**
    - `S/Features/Todos/TodoListQuery.cs` (`TodoStatus`, status and `contactId` parsing)
    - `S/Features/Todos/TodosEndpoints.cs`:
      - filters in the list helper;
      - `today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)`.
  - **Guards:** none. AC-068 fails at RED because the ignored filter returns the former to-dos.
  - **Expected RED:** filters are ignored (all to-dos returned); invalid `status`/`contactId` → 200 instead of 400.
  - **Done when:**
    - `status` (trimmed, case-insensitive, blank = none) and `contactId` filters work alone and combined with paging, in the specified order;
    - overdue uses the UTC date from the injected clock and flips at UTC midnight;
    - invalid values → 400 with every offending parameter and exact messages.

    Suite green.

- [ ] T-12: List one contact's to-dos
  - **ACs:** AC-057, AC-058, AC-059, AC-060, AC-061, AC-068 (nested), AC-072, AC-073 (nested), AC-074 (nested)
  - **Depends on:** T-11
  - **Tests:**
    - `TI/ListContactTodosTests.cs` (new; `ResetAsync`):
      - `ListContactTodos_ExistingContact_ReturnsOnlyItsTodosOrdered_AC057`
      - `ListContactTodos_PagingAndStatus_AppliedLikeGlobalList_AC058`: Theory over valid combinations, plus invalid `page`/`pageSize`/`status` → the same 400s and messages as `GET /api/todos`.
      - `ListContactTodos_NoTodos_ReturnsEmptyItemsAndZeroTotal_AC059`
      - `ListContactTodos_UnknownOrNonGuidContact_Returns404Problem_AC060`
      - `ListContactTodos_InvalidQueryForUnknownContact_Returns400_AC061`
      - `ListContactTodos_DeletedContact_Returns404_AC068`
      - `ListContactTodos_ValidationMessages_FollowStyle_AC074`
    - `TI/TodoErrorHandlingTests.cs` (extend; class `TodoErrorHandlingTests`): `ListContactTodos_WhenDatabaseFails_Returns500ProblemWithoutDetails_AC073`. Drop `Todos` only; the contact must exist.
  - **Likely source files:** `S/Features/Todos/TodosEndpoints.cs` (`GET /api/contacts/{id:guid}/todos`: parse query → contact `AnyAsync` → 404 problem → shared list helper)
  - **Guards:** AC-060 and AC-068 (no route yet → 404 problem+json).
  - **Expected RED:** 404 where 200 (AC-057, AC-058, AC-059) or 400 (AC-061) is expected.
  - **Done when:**
    - the nested endpoint returns the contact's to-dos in the same envelope, order, paging, and status rules as the global list;
    - query errors come before the 404;
    - unknown, deleted, or non-GUID contacts → 404 problem+json.

    Suite green.

- [ ] T-13: Races between linking a to-do and deleting the contact
  - **ACs:** AC-067, AC-065 (completed delete never shows a half state)
  - **Depends on:** T-12
  - **Tests:** `TI/TodoContactLinkRaceTests.cs` (new; uses the `SaveChangesInterceptor` technique from `UpdateDeleteRaceTests`, registered with `ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(...))` on a derived host; the interceptor deletes the **contact** through a separate connection once):
    - `CreateTodo_ContactDeletedBeforeSave_Returns400AndCreatesNothing_AC067`
    - `UpdateTodo_NewContactDeletedBeforeSave_Returns400AndLeavesTodoUnchanged_AC067`
    - `UpdateTodo_KeptContactDeletedBeforeSave_Returns400AndTodoIsUnlinked_AC067`:
      - the to-do is linked to C; the PUT keeps `contactId` C and changes the title;
      - the interceptor deletes C;
      - expect 400 `contactId` `Must refer to an existing contact.`;
      - GET shows `contactId` null and the **original** title.
    - `CreateUpdateAndContactDelete_Concurrent_NoDanglingLinks_AC067`:
      - K contacts, each with one DELETE plus several creates and relinking PUTs, released behind a shared gate;
      - create ∈ {201, 400}, PUT ∈ {200, 400, 404}, DELETE ∈ {204, 404}; zero 5xx;
      - afterwards the dangling-reference query returns 0, and `PRAGMA foreign_key_check(Todos)` returns no rows.
    - `DeleteContacts_ConcurrentObserver_NeverSeesDanglingLink_AC065`:
      - K contacts × M linked to-dos;
      - a reader on its own connection repeatedly runs the single-statement dangling-reference query while all K contacts are deleted concurrently through the API;
      - every read is 0.
  - **Likely source files:** `S/Features/Todos/TodosEndpoints.cs` (PUT: `db.Entry(todo).Property(t => t.ContactId).IsModified = true` before save; plan design point 3)
  - **Guards:** create/relink deterministic tests (same 787 path as AC-016/AC-029); both concurrent tests (the FK guarantees the invariants).
  - **Expected RED:** `UpdateTodo_KeptContactDeletedBeforeSave...` gets 200 with the stale `contactId` C, because EF omits the unchanged column from the `UPDATE`.
  - **Done when:**
    - creates and updates racing a contact delete only return documented codes, never a 500;
    - a PUT that keeps a link to a contact deleted mid-request returns 400 and leaves the to-do unlinked and otherwise unchanged;
    - no reader ever sees a to-do pointing at a missing contact.

    Suite green.

- [ ] T-14: Races between writing a to-do and deleting it
  - **ACs:** AC-070, AC-069
  - **Depends on:** T-13
  - **Tests:** `TI/TodoRaceTests.cs` (new; the interceptor deletes the **to-do** through a separate connection, once):
    - `UpdateTodo_DeletedBetweenLoadAndSave_Returns404AndStaysDeleted_AC070`
    - `CompleteTodo_DeletedBetweenLoadAndSave_Returns404AndStaysDeleted_AC070`: on an open to-do.
    - `ReopenTodo_DeletedBetweenLoadAndSave_Returns404AndStaysDeleted_AC070`: on a done to-do.
    - `WritesAndDelete_Concurrent_ReturnDocumentedCodes_AC070`:
      - per to-do, one of PUT/complete/reopen plus one DELETE, released together;
      - PUT ∈ {200, 400, 404}, complete/reopen ∈ {200, 404}, DELETE ∈ {204, 404}; zero 5xx;
      - every 204 is followed by GET 404.
    - `CompleteAndReopen_Concurrent_AllOkAndStateConsistent_AC069`:
      - N to-dos, each with interleaved complete and reopen requests released together;
      - all 200;
      - afterwards each to-do is either (done, `completedAt` set) or (open, `completedAt` null), checked via GET and the direct query `SELECT COUNT(*) FROM Todos WHERE (IsDone = 1) <> (CompletedAt IS NOT NULL)` = 0.
  - **Likely source files:** `S/Features/Todos/TodosEndpoints.cs` (catch `DbUpdateConcurrencyException` → 404 problem in PUT, complete, and reopen; in PUT, place it before the 787 catch)
  - **Guards:** both concurrent tests (timing-dependent; the invariants hold for any order).
  - **Expected RED:** the three deterministic tests get 500 (`DbUpdateConcurrencyException`: zero rows affected).
  - **Done when:**
    - PUT, complete, and reopen that lose a race with a delete return 404 ProblemDetails, and the to-do stays deleted;
    - concurrent complete/reopen always return 200 and leave a valid done state.

    Suite green.

- [ ] T-15: OpenAPI describes every to-do operation
  - **ACs:** NFR-001, AC-072 (documented content type)
  - **Depends on:** T-14
  - **Tests:** `T/Integration/OpenApiTests.cs` (extend):
    - `OpenApi_TodoEndpoints_HaveOperationIdsAndSummaries_NFR001`:
      - `CreateTodo`, `ListTodos` on `/api/todos`;
      - `GetTodoById`, `UpdateTodo`, `DeleteTodo` on `/api/todos/{id}`;
      - `CompleteTodo` on `/api/todos/{id}/complete`, `ReopenTodo` on `/api/todos/{id}/reopen`;
      - `ListContactTodos` on `/api/contacts/{id}/todos`;
      - non-empty summaries.
    - `OpenApi_TodoEndpoints_DocumentStatusCodes_NFR001`: Theory per operation:
      - create ⊇ {201, 400}; get ⊇ {200, 404}; list ⊇ {200, 400};
      - update ⊇ {200, 400, 404}; delete ⊇ {204, 404};
      - complete/reopen ⊇ {200, 404}; contact's to-dos ⊇ {200, 400, 404}.
    - `OpenApi_TodoEndpoints_ErrorResponsesAreProblemJson_NFR001`: every documented 400 and 404 above lists `application/problem+json` under `content`.
  - **Likely source files:** `S/Features/Todos/TodosEndpoints.cs` (`.WithName`, `.WithSummary`, `.ProducesProblem(404)` where an operation can 404)
  - **Guards:** 201/200/204 and the 400 codes (from `TypedResults` / `ValidationProblem` metadata).
  - **Expected RED:** missing operationIds and summaries; 404s absent from the document.
  - **Done when:** the Development OpenAPI document lists all eight to-do operations with names, summaries, the NFR-001 status codes, and problem+json content for every documented error. Suite green.

NFR-004 has no task of its own. The reviewer inspects every task's logging (no request bodies or stored to-do values at Information or above; `EnableSensitiveDataLogging` never enabled), including the contact delete path, and T-02 adds an automated guard for create.

NFR-003 has no CI gate. T-01 pins the indexes, and the reviewer runs and records the manual measurement from `plan.md` (Test strategy) in `review.md` at the final review.

### Documentation task (documenter, during `/document 003`; not a red-green cycle)
- [ ] D-01: Record the to-dos API, the date-only exception, and the FK rules in the docs
  - **ACs:** spec Constraints ("`dueDate` as a date-only value ... the plan must record it"), AC-074 (canonical messages)
  - **Depends on:** T-01..T-15
  - **Files:**
    - `docs/conventions.md`:
      - JSON row: instants are UTC `DateTimeOffset`; calendar days are `DateOnly` as `YYYY-MM-DD` (ADR-0007).
      - Errors row: the new canonical messages.
      - EF Core bullet: FKs are forced on; never rebuild `Contacts` in a migration without the ADR-0008 safeguard; to-do timestamps are ticks (ADR-0009).
    - `docs/adr/0006-validation-message-style.md`: amendment note adding rows for date, GUID, existing reference, and one-of.
    - `docs/adr/0007..0009`: status → Accepted (if the human accepted them with the plan).
    - `docs/architecture.md`:
      - Todos row → built;
      - data model for `Todos`;
      - key flows: create, update, complete/reopen, delete, lists, contact delete unlink;
      - known risks: the `Contacts` rebuild hazard, external tools with FKs off, the 787 mapping assumes one FK.
    - `CHANGELOG.md`:
      - Added: to-dos API;
      - Changed: contact delete unlinks to-dos; FK enforcement forced on.
    - `docs/roadmap.md` row 003; spec `Implementation notes`.
  - **Done when:** conventions, ADRs, architecture, CHANGELOG, and roadmap match the code, verified against it.

## Traceability
| AC | Task(s) | Test(s) (filled in during build) |
|---|---|---|
| AC-001 | T-02 | |
| AC-002 | T-02 | |
| AC-003 | T-02 | |
| AC-004 | T-02 | |
| AC-005 | T-02 | |
| AC-006 | T-02 | |
| AC-007 | T-04 | |
| AC-008 | T-04 (guard at RED) | |
| AC-009 | T-02 | |
| AC-010 | T-03 (create), T-06 (update) | |
| AC-011 | T-03 (create), T-06 (update) | |
| AC-012 | T-03 (create), T-06 (update) | |
| AC-013 | T-04 (create), T-06 (update) | |
| AC-014 | T-03, T-04 (with `contactId`), T-06 | |
| AC-015 | T-03 (guard), T-06 (guard) | |
| AC-016 | T-04 | |
| AC-017 | T-04 | |
| AC-018 | T-02 | |
| AC-019 | T-02 (guard at RED) | |
| AC-020 | T-05 (get), T-10 (list) | |
| AC-021 | T-05 | |
| AC-022 | T-05 | |
| AC-023 | T-05 | |
| AC-024 | T-05 | |
| AC-025 | T-05 | |
| AC-026 | T-07 | |
| AC-027 | T-06 | |
| AC-028 | T-07 | |
| AC-029 | T-07 | |
| AC-030 | T-05 | |
| AC-031 | T-08 | |
| AC-032 | T-08 | |
| AC-033 | T-08 | |
| AC-034 | T-08 | |
| AC-035 | T-08 | |
| AC-036 | T-08 (guard at RED) | |
| AC-037 | T-08 (guard) | |
| AC-038 | T-09 (get), T-10 (list) | |
| AC-039 | T-09 | |
| AC-040 | T-09 | |
| AC-041 | T-09 (guard) | |
| AC-042 | T-10 | |
| AC-043 | T-10 | |
| AC-044 | T-10 (order), T-01 (sortable column types) | |
| AC-045 | T-10 | |
| AC-046 | T-10 | |
| AC-047 | T-11 | |
| AC-048 | T-11 | |
| AC-049 | T-11 | |
| AC-050 | T-11 | |
| AC-051 | T-11 | |
| AC-052 | T-11 | |
| AC-053 | T-11 | |
| AC-054 | T-11 | |
| AC-055 | T-11 | |
| AC-056 | T-10 (paging), T-11 (all parameters) | |
| AC-057 | T-12 | |
| AC-058 | T-12 | |
| AC-059 | T-12 | |
| AC-060 | T-12 (guard at RED) | |
| AC-061 | T-12 | |
| AC-062 | T-01 (seeded directly), T-04 (through the API) | |
| AC-063 | T-04 (get), T-10 (list) | |
| AC-064 | T-04 | |
| AC-065 | T-01 (failed delete rolls back), T-13 (observer) | |
| AC-066 | T-01 (store, FKs forced), T-04 (through the API) | |
| AC-067 | T-01 (store rejects dangling link), T-13 (races) | |
| AC-068 | T-11 (filter), T-12 (nested) | |
| AC-069 | T-01 (CHECK constraint), T-14 (concurrent complete/reopen) | |
| AC-070 | T-14 | |
| AC-071 | T-09 | |
| AC-072 | T-02, T-03, T-07, T-12, T-15 (+ every error test via `ProblemAssert.IsProblemAsync`) | |
| AC-073 | T-02 (create, get), T-04 (create, non-FK constraint), T-07 (update), T-08 (complete/reopen), T-09 (delete), T-10 (list), T-12 (nested) | |
| AC-074 | T-03, T-04, T-06, T-10, T-11, T-12, D-01 (ADR-0006 amendment) | |
| NFR-001 | T-15 | |
| NFR-002 | T-02 | |
| NFR-003 | T-01 (indexes), reviewer measurement at final review | |
| NFR-004 | all tasks (reviewer), T-02 (guard) | |
| NFR-005 | T-06 | |
