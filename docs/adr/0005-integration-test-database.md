# ADR-0005: Integration test database: named shared-cache in-memory SQLite

**Status:** Accepted
**Date:** 2026-10-01
**Spec:** specs/001-contacts-api-create-get-list

## Context
`docs/conventions.md` says to replace the database in integration tests with "SQLite in-memory (`DataSource=:memory:` with an open connection held for the test's lifetime)". Taken literally, that means registering **one** open `SqliteConnection` and having every `DbContext` use it. Spec 001 adds requirements that this setup can't meet reliably:
- **AC-016** needs truly concurrent create requests with the same email. `SqliteConnection` is **not thread-safe** (Microsoft.Data.Sqlite docs, "Database errors"). Two requests sharing one connection don't get serialized writes. They race on the same object, for example "SqliteConnection does not support nested transactions" when both call `BeginTransaction`. The result is intermittent 500s, which is exactly what AC-016 forbids, and they would come from the test setup, not the product.
- **AC-039** asks for evidence that data survives across connections. With a single shared connection, "a new connection" can't be shown.
- **AC-038** needs a reliable way to make the database fail mid-test.

## Options considered
1. **Single shared `:memory:` connection** (the current conventions text).
   - Pros: simplest.
   - Cons: flaky under concurrent requests; can't show persistence across connections. Rejected for this spec.
2. **Named shared-cache in-memory database per factory**: `Data Source=microcrm-test-{guid};Mode=Memory;Cache=Shared`. The factory holds one keep-alive connection open for its lifetime, and each `DbContext` opens its own connection from the connection string.
   - Pros: still in-memory and fast, and isolated per factory (one per test class). Real per-request connections behave like production. SQLite serializes writers, and Microsoft.Data.Sqlite retries busy/locked errors until the command timeout. Tests can open their own connection to inspect or sabotage the database.
   - Cons: SQLite's shared-cache mode is officially discouraged for new designs and uses table-level locks. Acceptable for tests.
3. **Temporary file database per factory** (deleted on dispose).
   - Pros: closest to production locking semantics.
   - Cons: disk I/O and cleanup.
   - Kept as the fallback if option 2 proves flaky.

## Decision
We choose **option 2**.
- The API reads its connection string `ConnectionStrings:MicroCrm` lazily, when the `DbContext` options are built (`AddDbContext((sp, options) => ...)`), so test overrides always take effect. The default in `appsettings.json` is `Data Source=microcrm.db`.
- The integration test factory (`ApiFactory`, in `tests/MicroCrm.Api.Tests/Integration/Infrastructure/`):
  - generates a unique database name;
  - opens a keep-alive `SqliteConnection` **before** the host starts;
  - overrides `ConnectionStrings:MicroCrm` via `UseSetting`;
  - replaces `TimeProvider` with a `FakeTimeProvider`;
  - stays in the `Development` environment (OpenAPI is served only there);
  - exposes the connection string so tests can open their own connections;
  - clears that database's pool and closes the keep-alive connection on dispose.
- The schema comes from the **real EF Core migrations**, which the API applies at startup. Tests therefore exercise the migrations, including the unique index and collations from ADR-0003. Tests don't need `EnsureCreated`.
- Tests share a factory and database within a test class (`IClassFixture<ApiFactory>`). Classes that assert exact counts reset the data in `InitializeAsync` through a factory helper.

## Consequences
- The integration-test line in `docs/conventions.md` should be amended to describe this setup and reference this ADR. That's a documenter follow-up when spec 001 is documented.
- Tests can assert on the database directly (row counts, schema-level uniqueness), at the cost of knowing the table name `Contacts`. That coupling is limited to the test infrastructure and a few persistence tests.
- If concurrency tests turn flaky under shared-cache locking, switch the factory to option 3 without changing production code.
