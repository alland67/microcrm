# 003: To-dos API: CRUD, optional contact link, due date, complete/reopen: Technical plan

**Status:** Planned
**Spec:** ./spec.md
**ADRs:** ADR-0007 (date-only `dueDate`), ADR-0008 (contact link enforced by a foreign key), ADR-0009 (to-do timestamps as UTC ticks), all Accepted (2026-10-05, with this plan). Builds on ADR-0003 (case-insensitive text, "database is the arbiter"), ADR-0004 (validation and error pipeline), ADR-0005 (integration test database), ADR-0006 (validation message style).

## Approach
Spec 003 adds a **Todos** feature next to Contacts, following the patterns specs 001 and 002 established:
- `Features/Todos/` holds the entity, DTOs, and a pure `TodoInput.Parse` (trim, then validate, all errors together, one private core shared by create and update).
- `TodoListQuery.Parse` handles list parameters and reuses `ListQuery.Parse` for paging, so the paging messages are the same.
- `TodosEndpoints` uses `MapGroup("/api/todos")` plus the nested `GET /api/contacts/{id}/todos`, with `TypedResults` and ProblemDetails for every error.
- One migration, `CreateTodos`, creates the table with its final shape.

No new dependencies. The contacts endpoints don't change. The contact delete gains its unlink side effect entirely in the database.

Five design choices shape the work (details below):

1. **The database enforces the contact link (ADR-0008).** `Todos.ContactId` has a foreign key to `Contacts(Id)` with `ON DELETE SET NULL`. The existing single-statement `ExecuteDeleteAsync` contact delete therefore unlinks atomically, on every delete path. The API forces `Foreign Keys=True` on every connection, so a configuration change or native-library swap can't silently disable the rule.
2. **The FK, not a pre-check, decides whether a linked contact exists.** Create and update save, and a `SqliteException` with extended code 787 maps to the Q1 400 (`contactId: "Must refer to an existing contact."`). Sequential and racing requests take the same code path, and nothing produces a 500. The mapping sits next to the 2067 unique-violation helper.
3. **`dueDate` is a `DateOnly` (ADR-0007).** It arrives as a string, is parsed strictly as `yyyy-MM-dd`, stored as `TEXT`, and returned as `"YYYY-MM-DD"`. "Today" is the UTC date from the injected `TimeProvider`, so tests control it.
4. **To-do timestamps are stored as UTC ticks (ADR-0009).** EF Core's SQLite provider can't `ORDER BY` a `DateTimeOffset`, and the spec's order (due date, then `createdAt`, then `id`) has to run in SQL.
5. **Races resolve to documented codes.** The patterns are the same as spec 002:
   - Delete is one `ExecuteDeleteAsync` (zero rows → 404).
   - Update, complete, and reopen load the tracked entity, so a delete between load and save becomes `DbUpdateConcurrencyException` → 404.
   - EF Core writes only modified columns, so complete and reopen never overwrite a concurrent unlink.
   - `PUT` always writes `ContactId`, so the FK is re-checked against a contact deleted mid-request.

Queries stay in the handlers (conventions). A to-do list query helper shared by the two list endpoints lives in `TodosEndpoints`.

## Components touched
| Component / module | Change | Notes |
|---|---|---|
| `src/MicroCrm.Api/Features/Todos/Todo.cs` | new | Entity + done-state rules (`Complete(now)`, `Reopen(now)` return whether anything changed) |
| `src/MicroCrm.Api/Features/Todos/TodoDtos.cs` | new | `CreateTodoRequest`, `UpdateTodoRequest`, `TodoResponse` |
| `src/MicroCrm.Api/Features/Todos/TodoInput.cs` | new | Pure parse/validate for create and update (one core) |
| `src/MicroCrm.Api/Features/Todos/TodoListQuery.cs` | new | Pure parse of `page`, `pageSize`, `status`, `contactId`; `TodoStatus` enum |
| `src/MicroCrm.Api/Features/Todos/TodosEndpoints.cs` | new | `MapTodosEndpoints()`: 7 operations on `/api/todos` + `GET /api/contacts/{id:guid}/todos` |
| `src/MicroCrm.Api/Data/TodoConfiguration.cs` | new | FK `SetNull`, tick converters, CHECK constraint, indexes |
| `src/MicroCrm.Api/Data/AppDbContext.cs` | modified | `DbSet<Todo> Todos`; apply `TodoConfiguration` |
| `src/MicroCrm.Api/Data/SqliteErrors.cs` | modified | + `IsForeignKeyViolation(DbUpdateException)` (extended code 787) |
| `src/MicroCrm.Api/Data/Migrations/*_CreateTodos*.cs`, `AppDbContextModelSnapshot.cs` | new / modified | Generated with `MIGRATIONS_ADD_CMD` (`{name}` = `CreateTodos`) |
| `src/MicroCrm.Api/Program.cs` | modified | Force `ForeignKeys = true` on the connection string; `app.MapTodosEndpoints()` |
| `src/MicroCrm.Api/MicroCrm.Api.http` | modified | Sample to-do requests (dev convenience, no tests) |
| `src/MicroCrm.Api/Features/Contacts/*` | **unchanged** | Contact entity, DTOs, endpoints, and delete handler stay as they are |
| `tests/.../Integration/Infrastructure/ApiFactory.cs` | modified | `ResetAsync` also clears `Todos` (from T-02, once the table exists) |
| `tests/.../Integration/Todos/*.cs` | new | See Test strategy |
| `tests/.../Unit/Todos/*.cs` | new | `TodoInputTests`, `TodoListQueryTests`, `TodoTests` |
| `tests/.../Integration/OpenApiTests.cs` | modified | NFR-001 for to-do operations |
| `docs/adr/0007..0009` | new (planner) | Accepted |
| `docs/conventions.md`, `docs/architecture.md`, `docs/adr/0006-*.md`, CHANGELOG | modified (documenter, D-01) | Date-only exception, new messages, data model, migration hazard |

## Interfaces & data

### HTTP contract
```text
To-do JSON (every member always present; camelCase):
  { "id": guid, "title": string, "notes": string|null, "contactId": guid|null,
    "dueDate": "YYYY-MM-DD"|null, "isDone": bool, "completedAt": ISO-8601 UTC|null,
    "createdAt": ISO-8601 UTC, "updatedAt": ISO-8601 UTC }

POST /api/todos                         -> 201 + Location /api/todos/{id} + To-do
  Body: { "title": string?, "notes": string?, "dueDate": string?, "contactId": string? }
        Unknown members (id, isDone, completedAt, createdAt, updatedAt, ...) are ignored.
  Check order: (1) unreadable body (malformed JSON, empty, JSON null/array, wrong JSON type, e.g. "dueDate": 5)
                   -> 400 problem+json, no errors required (existing pipeline, ADR-0004)
               (2) TodoInput.Parse field rules -> 400 validation problem+json, all fields together
               (3) INSERT fails with SQLite 787 (contact missing, or deleted concurrently)
                   -> 400 validation problem+json { errors: { contactId: ["Must refer to an existing contact."] } }
               any other exception -> 500 (existing exception handler)

GET  /api/todos/{id:guid}               -> 200 To-do | 404 problem+json (ProblemHttpResult)
PUT  /api/todos/{id:guid}               -> 200 To-do
  Check order: (1) unreadable body 400; (2) field rules 400; (3) tracked load, null -> 404;
               (4) save: DbUpdateConcurrencyException (to-do deleted since load) -> 404;
                         787 -> 400 contactId; anything else -> 500
  Writes title, notes, dueDate, contactId (contactId always marked modified), updatedAt = now.
  Never writes id, createdAt, isDone, completedAt.
POST /api/todos/{id:guid}/complete      -> 200 To-do | 404   (no body parameter; any body is ignored)
POST /api/todos/{id:guid}/reopen        -> 200 To-do | 404
  Tracked load (null -> 404). No-op (already done / already open) -> 200 without saving.
  Otherwise save; DbUpdateConcurrencyException -> 404.
DELETE /api/todos/{id:guid}             -> 204 empty | 404 problem+json   (single ExecuteDeleteAsync)

GET /api/todos?page&pageSize&status&contactId
                                        -> 200 { items, page, pageSize, totalCount } | 400 validation
GET /api/contacts/{id:guid}/todos?page&pageSize&status
                                        -> 200 envelope | 400 validation | 404 problem+json
  Check order: (1) query parameters (all errors together) -> 400; (2) contact missing -> 404.
  A contactId query parameter on the nested route is not read (the route id is the filter).

Non-GUID ids on every {id:guid} route: no route matches -> 404 problem+json (status code pages).

Validation messages (ADR-0006 style):
  title     "Required." | "Must be 200 characters or fewer."
  notes     "Must be 4000 characters or fewer."
  dueDate   "Must be a valid date in YYYY-MM-DD format."
  contactId "Must be a valid GUID." (body and query) | "Must refer to an existing contact." (body, after save)
  page      "Must be an integer between 1 and 2147483647."
  pageSize  "Must be an integer between 1 and 100."
  status    "Must be one of: open, done, overdue."
```

### C# signatures (targets for tests; names may be refined by the implementer, behavior may not)
```csharp
// Features/Todos/TodoDtos.cs
public sealed record CreateTodoRequest(string? Title, string? Notes, string? DueDate, string? ContactId);
public sealed record UpdateTodoRequest(string? Title, string? Notes, string? DueDate, string? ContactId);
public sealed record TodoResponse(Guid Id, string Title, string? Notes, Guid? ContactId, DateOnly? DueDate,
                                  bool IsDone, DateTimeOffset? CompletedAt, DateTimeOffset CreatedAt,
                                  DateTimeOffset UpdatedAt)
{ public static TodoResponse From(Todo todo); }

// Features/Todos/TodoInput.cs  (pure; unit-tested)
public sealed record TodoInput(string Title, string? Notes, DateOnly? DueDate, Guid? ContactId)
{
    public const int TitleMax = 200, NotesMax = 4000;
    public static (TodoInput? Input, Dictionary<string, string[]>? Errors) Parse(CreateTodoRequest request);
    public static (TodoInput? Input, Dictionary<string, string[]>? Errors) Parse(UpdateTodoRequest request);
    // Both delegate to one private core taking the four raw strings (NFR-005).
}

// Features/Todos/TodoListQuery.cs  (pure; unit-tested)
public enum TodoStatus { Open, Done, Overdue }
public sealed record TodoListQuery(ListQuery Paging, TodoStatus? Status, Guid? ContactId)
{
    public static (TodoListQuery? Query, Dictionary<string, string[]>? Errors) Parse(
        string? page, string? pageSize, string? status, string? contactId);
    // page/pageSize via ListQuery.Parse(page, pageSize, search: null) (same messages, same skip math);
    // status/contactId errors merged into the same dictionary.
}

// Features/Todos/Todo.cs
public sealed class Todo
{
    public Guid Id { get; set; }  public string Title { get; set; } = "";  public string? Notes { get; set; }
    public DateOnly? DueDate { get; set; }  public Guid? ContactId { get; set; }
    public bool IsDone { get; set; }  public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }  public DateTimeOffset UpdatedAt { get; set; }
    public bool Complete(DateTimeOffset now); // open -> done, CompletedAt = UpdatedAt = now, returns true; done -> false, no change
    public bool Reopen(DateTimeOffset now);   // done -> open, CompletedAt = null, UpdatedAt = now, returns true; open -> false
}

// Data/SqliteErrors.cs
public static bool IsForeignKeyViolation(DbUpdateException exception); // inner SqliteException, SqliteExtendedErrorCode == 787

// Features/Todos/TodosEndpoints.cs
public static IEndpointRouteBuilder MapTodosEndpoints(this IEndpointRouteBuilder app);
//  POST   /api/todos                      CreateTodo        Results<Created<TodoResponse>, ValidationProblem>
//  GET    /api/todos                      ListTodos         Results<Ok<PagedResponse<TodoResponse>>, ValidationProblem>
//  GET    /api/todos/{id:guid}            GetTodoById       Results<Ok<TodoResponse>, ProblemHttpResult>
//  PUT    /api/todos/{id:guid}            UpdateTodo        Results<Ok<TodoResponse>, ValidationProblem, ProblemHttpResult>
//  DELETE /api/todos/{id:guid}            DeleteTodo        Results<NoContent, ProblemHttpResult>
//  POST   /api/todos/{id:guid}/complete   CompleteTodo      Results<Ok<TodoResponse>, ProblemHttpResult>
//  POST   /api/todos/{id:guid}/reopen     ReopenTodo        Results<Ok<TodoResponse>, ProblemHttpResult>
//  GET    /api/contacts/{id:guid}/todos   ListContactTodos  Results<Ok<PagedResponse<TodoResponse>>, ValidationProblem, ProblemHttpResult>
// Query parameters bound as string? (ADR-0004). Every 404 is TypedResults.Problem(statusCode: 404).
// Handlers take TimeProvider for "now" and "today"; never DateTime.UtcNow.
```

### `TodoInput.Parse` rules (pure)
- `title`: trim; null/empty → `Required.`; length > 200 → `Must be 200 characters or fewer.`
- `notes`: trim to null; length > 4000 → `Must be 4000 characters or fewer.`
- `dueDate`: trim to null; otherwise `DateOnly.TryParseExact(v, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)`, else `Must be a valid date in YYYY-MM-DD format.` (ADR-0007; strictness verified).
- `contactId`: trim to null; otherwise `Guid.TryParse(v, out g)`, else `Must be a valid GUID.` `Guid.TryParse` accepts the same formats as the `{id:guid}` route constraint (`D`, `N`, `B`, `P`, `X`). Responses always return the canonical lowercase `D` form. Confirmed with the plan approval (2026-10-05).
- All errors are collected in one dictionary keyed `title`, `notes`, `dueDate`, `contactId` (AC-014). Contact *existence* is not checked here (it's the FK, after save), so AC-017 holds by construction.

### `TodoListQuery.Parse` rules (pure)
- `page`/`pageSize`: delegated to `ListQuery.Parse(page, pageSize, null)`. The messages and the `TryGetSkip` overflow guard are identical to the contacts list.
- `status`: trim. Null, empty, or whitespace → no filter. Otherwise `open`/`done`/`overdue`, compared with `StringComparison.OrdinalIgnoreCase`, which is safe here because none of the three words contains a letter with a non-ASCII case mapping (ADR-0003 spirit). Anything else → `Must be one of: open, done, overdue.`
- `contactId`: trim. Null, empty, or whitespace → no filter. Otherwise `Guid.TryParse`, else `Must be a valid GUID.`
- All offending parameters are reported together (AC-056).

### Schema (migration `CreateTodos`, one migration for the whole table)
```text
Table Todos
  Id           TEXT    NOT NULL  PK  (Guid v7 from Guid.CreateVersion7(now); EF stores uppercase canonical text)
  Title        TEXT    NOT NULL
  Notes        TEXT    NULL
  DueDate      TEXT    NULL      ('yyyy-MM-dd', EF DateOnly mapping; ADR-0007)
  ContactId    TEXT    NULL      FK_Todos_Contacts_ContactId -> Contacts(Id) ON DELETE SET NULL  (ADR-0008)
  IsDone       INTEGER NOT NULL  (0/1)
  CompletedAt  INTEGER NULL      (UTC ticks; ADR-0009)
  CreatedAt    INTEGER NOT NULL  (UTC ticks)
  UpdatedAt    INTEGER NOT NULL  (UTC ticks)
  CONSTRAINT CK_Todos_DoneState CHECK (("IsDone" = 0 AND "CompletedAt" IS NULL) OR ("IsDone" = 1 AND "CompletedAt" IS NOT NULL))
Indexes
  IX_Todos_ContactId        (ContactId)
  IX_Todos_IsDone_DueDate   (IsDone, DueDate)
```
(The generated column order may differ; tests check names, types, and nullability, not order.)

**Why one migration with the final shape.** Conventions ask for one migration per schema-changing task, and only T-01 changes the schema. SQLite can't add a foreign key or a CHECK constraint to an existing table without a table rebuild, and rebuilds inside EF's migration transaction are exactly the hazard ADR-0008 records. Creating the table once, complete, avoids any rebuild of `Todos` within this spec. It creates no rebuild of `Contacts` either: the migration only `CREATE`s `Todos` and its indexes. The implementer must check that the generated migration has no `AlterColumn`/rebuild of `Contacts`. If it does, stop and report `BLOCKED`.

**Index choices, against the queries this spec issues:**

| Query (AC) | Predicate / work | Index used | Why |
|---|---|---|---|
| Contact delete unlinks (AC-062..AC-066) | FK action `UPDATE Todos SET ContactId = NULL WHERE ContactId = ?` | `IX_Todos_ContactId` | Without a child-key index SQLite scans `Todos` on **every** contact delete (SQLite FK docs recommend indexing child keys). EF Core creates it for the FK anyway. |
| `?contactId=` filter (AC-053, AC-055), nested list (AC-057), `contactId` + `status` | `ContactId = ?` [+ status] | `IX_Todos_ContactId` | Highly selective (NFR-003 shape: ~10 to-dos per contact). The few matching rows are sorted in memory. |
| `status=overdue` (AC-049) | `IsDone = 0 AND DueDate IS NOT NULL AND DueDate < ?today` | `IX_Todos_IsDone_DueDate` (intended; see note) | Intended as an equality-on-the-leading-column, range-on-the-second seek. **As built it does not seek:** EF Core emits `NOT ("IsDone")` rather than `"IsDone" = 0`, so without `ANALYZE` SQLite scans the table; after `ANALYZE` it uses a skip-scan on this index. NFR-003 is still met with a wide margin (31.5 ms slowest first request, 10,000 to-dos; FINAL review section 3). This is the hot query for spec 006's to-dos page. |
| `status=open` / `status=done` (AC-047, AC-048) | `IsDone = ?` | `IX_Todos_IsDone_DueDate` (leading column; same `NOT ("IsDone")` note, so a scan without `ANALYZE`) | Intended to let `COUNT(*)` and the filter walk only one half of the table. Low selectivity, but harmless. |
| Unfiltered list (AC-042..AC-045) | `ORDER BY DueDate IS NULL, DueDate, CreatedAt, Id` + `COUNT(*)` | none (scan + sort) | The `IS NULL` sort key is an expression. EF Core can't model expression indexes, so no plain index can deliver this order. Sorting ≤ 10,000 small rows is a few milliseconds in SQLite, well inside NFR-003's 500 ms. Revisit with a measured need (for example a stored `HasDueDate` column), not speculatively. |

No index on `CreatedAt`, `UpdatedAt`, or `Title`: nothing filters on them. The primary key covers lookups by id.

**CHECK constraint `CK_Todos_DoneState`:** a store-level guard for the spec's "only two valid combinations" rule (Definitions, AC-069). The API never violates it. If a bug did, the failure is a SQLite CHECK error (extended code 275). That code isn't mapped, so the result is a safe 500, not a corrupt row.

### Config
- `ConnectionStrings:MicroCrm` is unchanged in `appsettings.json`. `Program.cs` wraps the configured value with `new SqliteConnectionStringBuilder(value) { ForeignKeys = true }` before `UseSqlite` (ADR-0008).
- `EnableSensitiveDataLogging` stays off (NFR-004).

## Design points (resolved)

### 1. Foreign keys are enforced on every connection (production and tests)
Evidence (scratch check, 2026-10-05, same package versions; not part of the repo):
- New Microsoft.Data.Sqlite connections report `PRAGMA foreign_keys = 1`, for a file database and for the named shared-cache memory database ADR-0005 uses, and EF Core's own connection reports the same. The source is the bundled e_sqlite3's compile option `DEFAULT_FOREIGN_KEYS`, not EF Core.
- `Foreign Keys=False` in a connection string makes it `0`. `SqliteConnectionStringBuilder { ForeignKeys = true }` overrides that and makes it `1`.

Therefore:
- **Production:** forced on by `Program.cs` (T-01), whatever is configured.
- **Integration tests:** the API inside `ApiFactory` gets the same forced setting, because the factory overrides only `ConnectionStrings:MicroCrm`, which `Program.cs` wraps. Test-side connections (`ExecuteSqlAsync`, raw `SqliteConnection`s) use the bundled default (`1`). The AC-066 direct-delete test therefore exercises a delete outside the API with FKs on, the same as any script using these packages. A test asserts this explicitly (`PRAGMA foreign_keys` on a raw test connection), so a future native-library change fails loudly instead of making AC-066 pass vacuously.
- **Proof that the API forces it:** a test builds a derived host whose connection string ends with `;Foreign Keys=False` (`factory.WithWebHostBuilder(b => b.UseSetting("ConnectionStrings:MicroCrm", factory.ConnectionString + ";Foreign Keys=False"))`). It asserts (a) `PRAGMA foreign_keys` is 1 on a `DbContext` connection resolved from that host, and (b) `DELETE /api/contacts/{id}` through that host still unlinks a to-do. Without the `Program.cs` change, (a) reads 0 and (b) leaves a dangling `ContactId`. If `UseSetting` in `WithWebHostBuilder` doesn't override the factory's setting, the test-writer uses a small `ApiFactory` subclass instead. That's a test-only change.

### 2. AC-065 and AC-066 test approach (store-level unlink)
- **AC-066 (delete outside the API):**
  1. Create a contact through the API.
  2. Insert one or more `Todos` rows directly. Use the contact's stored id, either read with `SELECT Id FROM Contacts WHERE lower(Id) = $id` or as `id.ToString().ToUpperInvariant()`, because EF stores GUIDs as uppercase text and FK matching is exact. Write timestamps as UTC ticks (ADR-0009).
  3. Run `DELETE FROM Contacts WHERE lower(Id) = $id` through `factory.ExecuteSqlAsync`.
  4. Assert the rows still exist, `ContactId IS NULL`, and every other column is byte-for-byte unchanged (compare a `SELECT *` snapshot before and after, minus `ContactId`).

  Later tasks repeat the observable half through the API (T-04: `GET /api/todos/{id}`).
- **AC-065, failure partway, so nothing changes** (own fixture class; triggers must not leak). Two variants, each installing and dropping its trigger inside the test:
  - (a) `CREATE TRIGGER ... AFTER DELETE ON Contacts WHEN (SELECT COUNT(*) FROM Todos WHERE ContactId = OLD.Id) = 0 BEGIN SELECT RAISE(ABORT, 'x'); END;`. This aborts only *after* both the removal and the FK unlink have happened (verified ordering), so it proves the rollback covers both.
  - (b) `CREATE TRIGGER ... BEFORE UPDATE ON Todos BEGIN SELECT RAISE(ABORT, 'x'); END;`. This fires for the FK action itself (verified), after the contact row was removed.

  In both: `DELETE /api/contacts/{id}` → 500 problem+json with no internals (existing pipeline; code 1811 is neither 2067 nor 787). Then a raw read shows the contact row present and every to-do still holding its id. Once the to-do endpoints exist, later tasks add no further obligations here.
- **AC-065, completed delete never shows a half state:** structurally guaranteed, because the removal and the FK action are one statement. T-13 adds an observer test:
  1. Seed K contacts × M linked to-dos.
  2. Start a reader on its own `SqliteConnection` that repeatedly runs one snapshot query: `SELECT COUNT(*) FROM Todos t WHERE t.ContactId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM Contacts c WHERE c.Id = t.ContactId)`.
  3. Delete all K contacts concurrently through the API.

  Every read must return 0, and `PRAGMA foreign_key_check(Todos)` must return no rows at the end. This is a guard. It would fail if the unlink were moved to application code outside the delete statement.
- **AC-062:** `DELETE /api/contacts/{id}` with linked rows → 204 (T-01, rows seeded directly; repeated in T-04 through the API).

### 3. FK violation (787) maps to the Q1 400, never a 500 (AC-016, AC-029, AC-067)
- `SqliteErrors.IsForeignKeyViolation` checks `InnerException is SqliteException { SqliteExtendedErrorCode: 787 }`, the **extended** code, never the primary code 19 (`SQLITE_CONSTRAINT`), which also covers unique (2067), CHECK (275), NOT NULL (1299), and trigger (1811) failures.
- Create: `catch (DbUpdateException ex) when (SqliteErrors.IsForeignKeyViolation(ex))` → `TypedResults.ValidationProblem(new() { ["contactId"] = ["Must refer to an existing contact."] })`. Update: the same, after the `DbUpdateConcurrencyException` catch (which derives from `DbUpdateException` and must come first).
- Guards that pin the extended code: a `BEFORE INSERT ON Todos ... RAISE(ABORT, 'x')` trigger (T-04) and a `BEFORE UPDATE ON Todos ...` trigger (T-07) must give **500**, not 400 `contactId`. Each lives in its own fixture class.
- **No pre-check query (ADR-0008).** The sequential "contact doesn't exist" case (AC-016, AC-029) and the race (AC-067) take the same path. The check order is still the spec's, because the save happens only after field rules pass and, on `PUT`, after the to-do was found (AC-017, AC-027, AC-028).
- **`PUT` always writes `ContactId`.** The handler marks `ContactId` modified even when it equals the loaded value (`db.Entry(todo).Property(t => t.ContactId).IsModified = true`). Otherwise, if the linked contact is deleted between load and save, the FK action clears the column, EF's `UPDATE` omits it, and the API answers 200 with a stale `contactId`. Forcing the column makes SQLite re-check the FK (verified: `UPDATE ... SET ContactId = <deleted id>` fails with 787), so the response is 400 and the store keeps `NULL`. T-13 pins this with a deterministic test.
- **AC-067 tests (T-13).** These use the interceptor technique from spec 002 (`UpdateDeleteRaceTests`): a test-local `SaveChangesInterceptor`, registered through `factory.WithWebHostBuilder(... ConfigureTestServices(s => s.ConfigureDbContext<AppDbContext>(o => o.AddInterceptors(i))))`, deletes the contact through a separate `SqliteConnection` in `SavingChangesAsync`, once.
  - create linking the contact → 400 `contactId`, no to-do created (guard: same path as AC-016);
  - `PUT` relinking an existing to-do to the contact → 400, to-do unchanged (guard: same path as AC-029);
  - `PUT` that keeps the existing link to the contact → **400**, and afterwards the to-do has `contactId` `null` and its other fields unchanged (**RED** until `ContactId` is forced modified: today it returns 200 with the stale id);
  - an interleaving-invariant smoke over K contacts, each with one `DELETE` plus several creates and relinking `PUT`s released behind a shared gate. Codes must be in their documented sets (create {201, 400}, `PUT` {200, 400, 404}, delete {204, 404}); zero 5xx; at the end the dangling-reference query returns 0 and `PRAGMA foreign_key_check(Todos)` returns no rows.

### 4. Due date and "today" (AC-005, AC-012, AC-049, AC-050; ADR-0007)
- Parse rules are in `TodoInput` (above). `DateOnly` values serialize as `"yyyy-MM-dd"` (including `"0001-01-01"`).
- `today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime)`, computed per list request. Overdue predicate in SQL: `!IsDone && DueDate != null && DueDate < today` (verified translation: `"DueDate" IS NOT NULL AND "DueDate" < @today`).
- **AC-050 test.**
  1. Own fixture class (it moves the clock). `FakeTimeProvider` can't go backwards, and tests in one class share it, so the test computes everything from `factory.Time.GetUtcNow()`.
  2. Advance to 23:59:59 UTC of the current day D, then create an open to-do due D.
  3. `?status=overdue` → excluded.
  4. `factory.Time.Advance(TimeSpan.FromSeconds(1))` → D+1 00:00:00 → included.
- Other overdue tests (AC-049) use due dates relative to `factory.Time.GetUtcNow()` (yesterday, today, tomorrow, none, done-and-past) and never assume the factory's start date.

### 5. Complete and reopen (AC-031..AC-037, AC-069)
- Handler: tracked load (null → 404) → `todo.Complete(now)` / `todo.Reopen(now)`. If it returns `false` (no-op, Q5): 200 with the unchanged to-do, **no `SaveChanges`**. Otherwise save, then 200.
- EF Core writes only the modified columns (`IsDone`, `CompletedAt`, `UpdatedAt`). A concurrent unlink (`ContactId` → `NULL`) or a concurrent `PUT` of other fields is therefore never overwritten with stale values. Both columns of the done state change together in every real transition, so concurrent complete/reopen (AC-069) can only leave one of the two valid states. `CK_Todos_DoneState` backs that up in the store.
- No body parameter, so any body (even malformed JSON) is ignored (AC-035).
- `PUT` never touches `IsDone`/`CompletedAt` (AC-022, AC-037), because `UpdateTodoRequest` has no such members and the handler doesn't assign them.

### 6. Update, complete, reopen, or delete racing a to-do delete (AC-070, AC-071)
- Delete: `db.Todos.Where(t => t.Id == id).ExecuteDeleteAsync(ct)`. 0 → 404 problem, otherwise 204. Never load-then-remove (spec 002 design point 3). Ten concurrent deletes → exactly one 204 (AC-071).
- `PUT`/complete/reopen: `catch (DbUpdateConcurrencyException)` → 404 (zero rows updated because the row is gone). None of them can re-insert the row.
- **Deterministic tests (T-14):** an interceptor deletes the *to-do* through a separate connection in `SavingChangesAsync`. `PUT`, complete (on an open to-do), and reopen (on a done to-do) must each return 404 problem+json, and a later GET 404. **RED** until the catch exists: today they return 500. Plus an interleaving smoke (update/complete/reopen vs delete; codes in documented sets; every 204 followed by GET 404).
- A no-op complete/reopen that races a delete returns 200 (it never saves). That's in the documented set for AC-070, and a later GET is still 404 if the delete returned 204.

### 7. Lists (AC-042..AC-061)
- One private helper builds the query: `AsNoTracking()`, optional `ContactId == x`, optional status predicate, then `CountAsync`, then `OrderBy(t => t.DueDate == null).ThenBy(t => t.DueDate).ThenBy(t => t.CreatedAt).ThenBy(t => t.Id)`, `Skip`/`Take` (only if `TryGetSkip` succeeds; otherwise an empty page with the real `totalCount`, as contacts do).
- **What "`id` ascending" means:** SQLite compares EF's uppercase GUID text with `BINARY` collation. For hex text that order equals the ordinal order of the lowercase strings the API returns, because digits sort before letters in both cases. Tests must sort expected ids with `string.CompareOrdinal(a.ToString(), b.ToString())`. (A 200,000-pair probe at T-10 found that on .NET 10 `Guid.CompareTo` agrees with this text order, so it would also work; only a little-endian `ToByteArray` comparison differs. The tests use `string.CompareOrdinal` so the expectation mirrors what SQLite does.)
- Tests create to-dos without advancing the fake clock to get equal `createdAt` (to exercise the `id` tiebreak), and with `Advance` to get distinct ones.
- Nested endpoint: parse the query first (400), then `AnyAsync(c => c.Id == id)` on `Contacts` (false → 404 problem), then the shared helper with `ContactId == id`. A contact deleted between the check and the list gives 200 with an empty page, which is in the documented set.
- `?contactId=` for an unknown contact on `GET /api/todos` → 200 empty page (Q8, AC-053). Deliberately no contact lookup.

### 8. Errors and safe 500s (AC-072, AC-073)
- Every 4xx comes from `ValidationProblem`, `TypedResults.Problem(...)`, or status code pages. Every error test goes through `ProblemAssert.IsProblemAsync` (AC-072).
- AC-073 guards per endpoint task, in classes with their own fixture:
  - `DROP TABLE Todos` → 500 problem+json without internals, for every to-do endpoint. Dropping `Todos` (the child) doesn't touch `Contacts`.
  - "Stored data unchanged" uses fault-injecting triggers: `BEFORE INSERT ON Todos` (create: 500, row count unchanged); `BEFORE UPDATE ON Todos` (`PUT`, complete: 500, row unchanged); `BEFORE DELETE ON Todos` (delete: 500, row still present).

  All are expected to pass at RED (existing exception handler), except where noted in tasks.md.

### 9. Validation message style (AC-074)
- The existing `ProblemAssert.AssertValidationMessageStyle` applies unchanged to the new messages: none starts with the key or its humanized form (`Due date`, `Contact id`, `Status`, `Title`), none quotes the key, and all are sentence case with a final period.
- Tests also assert the exact canonical message per rule (ADR-0006 practice).
- `docs/adr/0006-validation-message-style.md` gets the new canonical rows (date, GUID, existing reference, one-of) as an amendment note in `/document 003` (D-01).

### 10. OpenAPI (NFR-001)
- Each operation: `.WithName(...)` (names in the signature list above), `.WithSummary(...)`, and `.ProducesProblem(404)` on every operation that can 404 (get, update, delete, complete, reopen, nested list).
- 201/200/204 come from `TypedResults`. The 400 for create, update, and both lists comes from `ValidationProblem`'s metadata.
- The test asserts the code sets from NFR-001 and that every documented 400/404 lists `application/problem+json`.
- Added in the last code task (T-15), as spec 002 did, so earlier tasks stay minimal.

### 11. Logging (NFR-004)
No logging is added on any to-do path. The reviewer inspects every task, including the update path and the contact delete path. T-02 adds an automated guard for create: a capturing `ILoggerProvider` through `WithWebHostBuilder`, the same as the existing `CreateContactConflictTests`. It asserts that a unique title and notes value from a create never appears at Information or above.

## Test strategy
Paths abbreviated: `T/` = `tests/MicroCrm.Api.Tests/`, `TI/` = `T/Integration/Todos/`. All integration tests use `ApiFactory` (ADR-0005). Classes that assert exact counts or order call `ResetAsync()` (which clears `Todos` and `Contacts` from T-02 on) in `InitializeAsync`. Others use unique data per test. Classes that install triggers, drop tables, or move the clock use their own fixture class.

| AC | Level | Test file | Notes (fixtures, fakes) |
|---|---|---|---|
| AC-001 | integration | `TI/CreateTodoTests.cs` | All fields; Location; GET with a new `HttpClient` |
| AC-002 | integration | `TI/CreateTodoTests.cs` | Raw JSON: nulls present, `isDone` false |
| AC-003 | integration | `TI/CreateTodoTests.cs` | Body with `id`, timestamps, `isDone: true`, `completedAt`; `createdAt == updatedAt == factory.Time` |
| AC-004 | integration + unit | `TI/CreateTodoTests.cs`, `T/Unit/Todos/TodoInputTests.cs` | Trim; whitespace-only notes → null |
| AC-005 | integration + unit | same | Past, today, `0001-01-01`, `9999-12-31`, `2024-02-29`, surrounding whitespace |
| AC-006 | integration + unit | same | Omitted, `null`, `""`, `"  "` |
| AC-007 | integration | `TI/CreateTodoContactLinkTests.cs` | Existing contact |
| AC-008 | integration + unit | `TI/CreateTodoTests.cs`, `TodoInputTests.cs` | Omitted/null/""/"  " |
| AC-009 | integration | `TI/CreateTodoTests.cs` | 200/4000 exactly, also wrapped in whitespace |
| AC-010..AC-014 | integration + unit | `TI/CreateTodoValidationTests.cs`, `TI/UpdateTodoValidationTests.cs`, `TodoInputTests.cs` | Exact messages; no row created/changed (row count / direct read). AC-012 Theory over the spec's examples + `2026-10-05T00:00:00Z`, `２０２６-10-05` |
| AC-015 | integration | same | Raw bodies `{`, empty, `null`, `[]`, `{"title":1}`, `{"title":"x","dueDate":5}`, `{"title":"x","contactId":7}` |
| AC-016, AC-017 | integration | `TI/CreateTodoContactLinkTests.cs` | Unknown v7 GUID; with + without a field error |
| AC-018, AC-019 | integration | `TI/GetTodoByIdTests.cs` | Unknown GUID, `not-a-guid`, `123` |
| AC-020..AC-025, AC-030 | integration | `TI/UpdateTodoTests.cs` | Advanced clock for AC-023; snapshot of others for AC-030 |
| AC-026..AC-029 | integration | `TI/UpdateTodoTests.cs`, `TI/UpdateTodoValidationTests.cs` | Check order |
| AC-031..AC-037 | integration + unit | `TI/CompleteReopenTodoTests.cs`, `T/Unit/Todos/TodoTests.cs` | Clock advanced between actions so timestamps differ |
| AC-038..AC-041, AC-071 | integration | `TI/DeleteTodoTests.cs` | 10 concurrent deletes |
| AC-042..AC-046 | integration + unit | `TI/ListTodosTests.cs`, `TI/ListTodosPagingTests.cs`, `T/Unit/Todos/TodoListQueryTests.cs` | `ResetAsync`; ordinal id ordering |
| AC-047..AC-049, AC-051..AC-056 | integration + unit | `TI/ListTodosFilterTests.cs`, `TodoListQueryTests.cs` | Dates relative to `factory.Time` |
| AC-050 | integration | `TI/OverdueClockTests.cs` | Own fixture; advance across midnight |
| AC-057..AC-061 | integration | `TI/ListContactTodosTests.cs` | Check order: 400 before 404 |
| AC-062, AC-065 (failure), AC-066 | integration | `TI/TodoStoreTests.cs`, `TI/ContactDeleteAtomicityTests.cs` | Direct SQL seeding/deleting; triggers in own fixture |
| AC-063, AC-064 | integration | `TI/ContactDeleteUnlinksTodosTests.cs` | Through the API; `updatedAt` unchanged |
| AC-065 (observer), AC-067 | integration | `TI/TodoContactLinkRaceTests.cs` | Interceptor + observer + smoke |
| AC-068 | integration | `TI/ListTodosFilterTests.cs`, `TI/ListContactTodosTests.cs` | After contact delete |
| AC-069, AC-070 | integration | `TI/TodoRaceTests.cs` | Interceptor + smoke; CHECK backs the invariant |
| AC-072 | integration | every error test via `ProblemAssert.IsProblemAsync` | |
| AC-073 | integration | `TI/TodoErrorHandlingTests.cs` (several classes, own fixtures) | `DROP TABLE Todos`; triggers |
| AC-074 | integration + unit | validation test classes | `AssertValidationMessageStyle` + exact messages |
| NFR-001 | integration | `T/Integration/OpenApiTests.cs` | operationIds, summaries, codes, problem+json |
| NFR-002 | integration | `TI/CreateTodoTests.cs` | Raw JSON string checks |
| NFR-003 | manual (reviewer) + schema guard | `TI/TodoStoreTests.cs` (indexes exist) | Scripted timing recorded in `review.md` |
| NFR-004 | reviewer inspection + guard | `TI/CreateTodoTests.cs` | Capturing logger provider |
| NFR-005 | unit + integration | `TodoInputTests.cs`, `TI/UpdateTodoValidationTests.cs` | Same payload through both overloads / POST and PUT |

**NFR-003 measurement (manual, recorded in `review.md` at the final review):**
1. Copy a migrated database to a temp file.
2. Seed 1,000 contacts and 10,000 to-dos with one SQL script (mixed `IsDone`, due dates spanning ±1 year, about 20% without a due date; ticks for timestamps).
3. Run the API against it (`DEV_API_CMD` with `ConnectionStrings__MicroCrm` pointing at the copy).
4. Time `curl` for `/api/todos?pageSize=100`, `?status=overdue&pageSize=100`, `?contactId=<id>&status=open&pageSize=100`, and `/api/contacts/<id>/todos?pageSize=100`.

All must be under 500 ms. No script is committed unless the reviewer asks for one.

## Dependencies
None. `SqliteConnectionStringBuilder` is in Microsoft.Data.Sqlite (already referenced through EF Core Sqlite). `DateOnly` and value converters are in the BCL and EF Core. The test-side interceptors and `FakeTimeProvider` are already in use.

## Risks & mitigations
- **FK enforcement silently off** (config or native-library change). Mitigation: forced in `Program.cs`. A test proves it with `Foreign Keys=False` (T-01), and another asserts the test connections have it on, so AC-066 can't pass vacuously.
- **A future migration that rebuilds `Contacts` unlinks every to-do** (verified). Mitigation: recorded in ADR-0008 consequences and, via D-01, in `docs/architecture.md` known risks and `docs/conventions.md` (EF Core bullet). The `CreateTodos` migration must not touch `Contacts` (implementer checks; reviewer verifies the generated `Up`).
- **787 can't tell which FK failed.** Correct while `ContactId` is the only FK on `Todos`. Recorded in ADR-0008. The mapping checks the extended code exactly, and trigger guards (1811 → 500) pin that.
- **EF Core's SQLite `ORDER BY DateTimeOffset` limitation.** Mitigation: ADR-0009 tick converters. Ordering tests with equal and distinct `createdAt` would fail at runtime with `NotSupportedException` (500) if the converter were missing.
- **Stale `contactId` on `PUT` under a concurrent contact delete.** Mitigation: `ContactId` is always marked modified; a deterministic test pins it (T-13).
- **Shared-cache locking flakiness in concurrency tests (AC-065 observer, AC-067, AC-069..AC-071).** Same mitigation as specs 001/002: interleaving-invariant assertions, Microsoft.Data.Sqlite busy/locked retry, and the ADR-0005 file-DB fallback. The observer runs one statement per read (no long transactions).
- **Clock-dependent tests.** `FakeTimeProvider` only moves forward and is shared per class. The AC-050 test gets its own fixture, and every overdue test derives dates from `factory.Time.GetUtcNow()`.
- **Raw SQL in tests couples to the schema** (table and column names, uppercase GUID text, tick timestamps). Accepted and limited to store-level tests (ADR-0005 allows it); the plan's schema section is the contract.
- **`ResetAsync` change affects existing contact test classes.** It clears `Todos` first, then `Contacts`. The table exists from T-01, and contact tests create no to-dos, so their behavior is unchanged. T-02 makes the change, after the table exists.
- **Guard-heavy tasks.** tasks.md marks every guard. Each task still has at least one test that fails for the right reason. The test-writer reports guards separately, and the reviewer confirms each would fail if its behavior regressed.

## Rollout
- One additive migration (`CreateTodos`), applied at startup by `MigrateAsync`. No change to existing contact rows or columns. The dev database gets the new table on the next run.
- Behavior change to an existing endpoint: `DELETE /api/contacts/{id}` now also unlinks that contact's to-dos (inside the same statement). Status codes are unchanged. CHANGELOG "Added" (to-dos API) and "Changed" (contact delete unlinks to-dos; FK enforcement forced on).
- **Rollback:** revert the commits and run the `Down` of `CreateTodos` (`DROP TABLE Todos`) against any database that applied it (`dotnet ef database update AddContactNameCollation`). Contacts are untouched by both directions.
- **Hand-off to specs 005/006:** `dueDate` is a plain date string (ADR-0007; don't parse it with `new Date`). Overdue uses the server's UTC date. The nested endpoint is paged.

## ADRs
- **ADR-0007** Date-only `dueDate` (`docs/adr/0007-date-only-due-date.md`): Accepted. Records the exception to the conventions date rule; the conventions amendment is scheduled in D-01.
- **ADR-0008** Contact link enforced by a foreign key (`docs/adr/0008-contact-link-enforced-by-foreign-key.md`): Accepted. FK `ON DELETE SET NULL`, FKs forced on, 787 → 400, no pre-check, migration hazard.
- **ADR-0009** To-do timestamps as UTC ticks (`docs/adr/0009-todo-timestamps-as-utc-ticks.md`): Accepted. EF Core SQLite ordering limitation.
- Accept all three with this plan.
