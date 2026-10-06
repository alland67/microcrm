# ADR-0008: To-do → contact link enforced by a SQLite foreign key (`ON DELETE SET NULL`)

**Status:** Accepted (2026-10-05, with the spec 003 plan)
**Date:** 2026-10-05
**Spec:** specs/003-todos-api (enforces the "Decision: deleting a contact and its to-dos" from specs/002-contacts-api-update-delete)

## Context
A to-do may link to one contact (`contactId`). Spec 002 decided, and spec 003 must enforce, that deleting a contact **keeps** its to-dos and sets their `contactId` to `null`:
- atomically with the contact removal (AC-065);
- on **every** delete path, including deletes that bypass the application (AC-066);
- without blocking the delete (AC-062) or changing any other to-do field, including `updatedAt` (AC-063, Q6).

The contact delete endpoint (spec 002) is a single `ExecuteDeleteAsync` statement. That is a set-based `DELETE` that **bypasses EF Core's client-side cascade**, so an EF-only `DeleteBehavior.ClientSetNull` would leave dangling ids, or fail the delete if the database had a plain `NO ACTION` foreign key.

A to-do create or update with a well-formed `contactId` that matches no contact must return **400** with `contactId: ["Must refer to an existing contact."]` (Q1), checked after field rules (and after to-do existence on `PUT`), including when the contact is deleted concurrently: never a 500 (AC-016, AC-029, AC-067).

Verified on 2026-10-05 with the project's package versions (Microsoft.Data.Sqlite / EF Core 10.0.12, SQLite 3.53.3 from the bundled e_sqlite3):
- `PRAGMA foreign_keys` is `1` on every new Microsoft.Data.Sqlite connection (file and shared-cache memory) **because the bundled native library is compiled with `SQLITE_DEFAULT_FOREIGN_KEYS`**. Neither EF Core nor our code turns it on. A connection string containing `Foreign Keys=False` turns it **off**. A different native SQLite (system library, sqlite3 CLI) defaults to off.
- With FKs on, a raw `DELETE FROM Contacts` sets the child `ContactId` to `NULL`. An `INSERT` or `UPDATE` with a dangling `ContactId` fails with `SqliteException` extended code **787** (`SQLITE_CONSTRAINT_FOREIGNKEY`), wrapped in `DbUpdateException` by EF Core.
- If a contact delete statement fails partway, SQLite rolls back the whole statement, both the row removal and the `SET NULL` action. This was checked two ways. (a) An `AFTER DELETE ON Contacts` trigger that aborts only when the unlink has already happened leaves the contact present and the to-do still linked. (b) A `BEFORE UPDATE ON Todos` trigger also fires for the FK action, and its abort leaves both rows unchanged. Trigger aborts report extended code **1811**, not 787.
- A table rebuild of `Contacts` inside a transaction (the pattern EF Core's SQLite migrations use for `AlterColumn`; `PRAGMA foreign_keys = 0` is ignored inside a transaction) runs `DROP TABLE Contacts`, which performs an implicit `DELETE` and **unlinks every to-do**.

## Options considered
1. **Application code unlinks** (`UPDATE Todos SET ContactId = NULL WHERE ContactId = @id` in a transaction with the contact delete).
   - Cons: only covers the API's delete path (fails AC-066); every future delete path must remember it. Rejected.
2. **Database foreign key `ON DELETE SET NULL`, relying on the native library's default FK setting.**
   - Pros: enforced by the store in the same statement.
   - Cons: silently disabled by a `Foreign Keys=False` connection string or a different native SQLite build.
3. **Database foreign key `ON DELETE SET NULL`, and the API forces FK enforcement on every connection it opens.** For create and update, the FK is the arbiter of "contact exists": no pre-check query, and code 787 maps to the Q1 400.
   - Pros: one rule in the schema covers every path; no check-then-act race window on create or update; same "database is the arbiter" pattern as email uniqueness (ADR-0003).
   - Cons: SQLite reports *that* a foreign key failed, not *which* one, so the 787 mapping is correct only while `Todos.ContactId` is the only FK on `Todos`.
4. **Pre-check query plus FK.**
   - Cons: two code paths for one rule; the race still needs the 787 mapping. Rejected as redundant.

## Decision
We choose **option 3**.
- `Todos.ContactId` (`TEXT NULL`) has a foreign key to `Contacts(Id)` with **`ON DELETE SET NULL`** (EF Core `HasOne<Contact>().WithMany().HasForeignKey(t => t.ContactId).OnDelete(DeleteBehavior.SetNull)`), created in the same migration as the table. SQLite can't add a foreign key to an existing table without a rebuild. `Contact` gets no navigation property, so contact responses don't change.
- The API builds its connection string with `SqliteConnectionStringBuilder { ForeignKeys = true }` on top of `ConnectionStrings:MicroCrm`. Microsoft.Data.Sqlite then issues `PRAGMA foreign_keys = 1` on every open, whatever the configuration or native build says.
- Contact delete stays a single `ExecuteDeleteAsync`. The FK action runs inside that statement, so removal and unlink are one atomic change. Do **not** switch to load-then-remove.
- To-do create and update **don't pre-check** contact existence. They save, and `DbUpdateException` whose inner `SqliteException` has extended code **787** maps to `400` `errors.contactId = ["Must refer to an existing contact."]`. Any other constraint failure (for example 1811 from a trigger, or a CHECK failure) is **not** mapped and stays a 500. The helper sits next to the unique-violation check (`SqliteErrors`, ADR-0003).
- On `PUT`, `ContactId` is always written (marked modified), even when the value equals the loaded one. Otherwise EF Core leaves the column out of the `UPDATE`, and a contact deleted between load and save would make the API return `200` with a `contactId` that the store has already cleared.
- `IX_Todos_ContactId` indexes the child key (EF Core creates it for the FK). Without it, every contact delete scans `Todos` to apply the action.

## Consequences
- AC-066 holds for every connection opened through Microsoft.Data.Sqlite with the bundled library (the API, tests, and scripts using the same packages). A tool that opens the file with FKs off (for example the `sqlite3` CLI without `PRAGMA foreign_keys = ON`) can still delete a contact and leave dangling ids. This is a SQLite limitation, documented in `docs/architecture.md` during `/document 003`.
- **Migration hazard (must be handled by any future spec):** a migration that rebuilds the `Contacts` table (for example an `AlterColumn` on a contact column) will unlink every to-do, because the rebuild's `DROP TABLE Contacts` fires `ON DELETE SET NULL` while FKs can't be turned off inside the migration transaction. Before adding such a migration, a spec must include a characterization test (linked to-dos survive the migration) and choose a safe technique (for example, a hand-written migration that disables FKs outside the transaction, or a rebuild that saves and restores the links).
- If a second foreign key is added to `Todos`, the 787 → `contactId` mapping must be revisited (SQLite doesn't name the failing key). Recorded next to the ADR-0003 note about the unique-violation check.
- The `foreign_keys` pragma is per connection and costs one statement per open. That's negligible.
