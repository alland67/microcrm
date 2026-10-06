# Changelog

All notable changes to this project are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versioning: [SemVer](https://semver.org/).

## [Unreleased]
### Added
- Spec 003 (To-dos API):
  - `POST /api/todos` creates a to-do: 201 with a `Location` header and the to-do. `title` is required (max 200); `notes` (max 4000), `dueDate` and `contactId` are optional. Text is trimmed and blank optional values become `null`. A new to-do is always open; `id`, `createdAt`, `updatedAt`, `isDone` and `completedAt` in the body are ignored.
    - Response members, always present (`null` when empty): `id`, `title`, `notes`, `contactId`, `dueDate`, `isDone`, `completedAt`, `createdAt`, `updatedAt`.
    - `dueDate` is a calendar date sent and returned as exactly `YYYY-MM-DD` (any real date from `0001-01-01` to `9999-12-31`, past dates included). It is the one date that is not an ISO-8601 UTC instant (ADR-0007).
    - Validation errors return 400 with an `errors` dictionary listing every invalid field. A well-formed `contactId` that matches no contact returns 400 with `contactId: ["Must refer to an existing contact."]`, reported only when every field rule passes; a contact deleted concurrently gives the same 400, never 500.
  - `GET /api/todos/{id}`: 200, or 404 for an unknown or non-GUID id.
  - `PUT /api/todos/{id}` replaces `title`, `notes`, `dueDate` and `contactId` (full replace): 200 with the to-do. Omitting an optional field, including `contactId`, sets it to `null`. `id`, `createdAt`, `isDone` and `completedAt` never change; `updatedAt` is set on every successful update.
    - Check order: body 400, field rules 400, to-do not found 404, contact not found 400. An update racing a delete returns 404.
  - `POST /api/todos/{id}/complete` and `POST /api/todos/{id}/reopen`: 200 with the to-do, 404 for an unknown or non-GUID id. They are idempotent: completing a done to-do or reopening an open one changes nothing and keeps `completedAt` and `updatedAt`. Request bodies are ignored. `isDone` and `completedAt` always agree (also enforced by a database CHECK).
  - `DELETE /api/todos/{id}`: 204 with no body, or 404 for an unknown, already deleted or non-GUID id.
  - `GET /api/todos?page=&pageSize=&status=&contactId=` returns `{ items, page, pageSize, totalCount }`.
    - Paging rules and messages are the same as the contacts list (page 1, pageSize 20, at most 100).
    - `status` is trimmed and case-insensitive: `open` (includes overdue), `done`, or `overdue` (open, has a due date, due date before today). Blank means no filter; anything else returns 400 `Must be one of: open, done, overdue.`
    - Today is the UTC calendar date of the server clock, so a to-do due today is not overdue and one turns overdue at midnight UTC.
    - `contactId` must be a GUID (400 `Must be a valid GUID.` otherwise); a GUID that matches no contact returns an empty page.
    - Order: due date ascending (no due date last), then `createdAt`, then `id`. Every parameter error is reported together.
  - `GET /api/contacts/{id}/todos`: the same envelope, paging, `status` filter and order for one contact's to-dos; 404 for an unknown or non-GUID contact (checked after the query parameters, so an invalid parameter gives 400 first).
  - OpenAPI operations `CreateTodo`, `ListTodos`, `GetTodoById`, `UpdateTodo`, `CompleteTodo`, `ReopenTodo`, `DeleteTodo` and `ListContactTodos`, with `application/problem+json` error responses.
  - SQLite `Todos` table (migration `CreateTodos`, applied at startup; `Contacts` is not touched): foreign key `ContactId` to `Contacts(Id)` with `ON DELETE SET NULL`, indexes `IX_Todos_ContactId` and `IX_Todos_IsDone_DueDate`, check constraint `CK_Todos_DoneState`. `CreatedAt`, `UpdatedAt` and `CompletedAt` are stored as UTC ticks (`INTEGER`) so lists can order by them in SQL (ADR-0009).
  - New validation messages (ADR-0006 amended): `Must be a valid date in YYYY-MM-DD format.`, `Must be a valid GUID.`, `Must refer to an existing contact.`, `Must be one of: open, done, overdue.`
  - ADR-0007 (date-only `dueDate`), ADR-0008 (contact link enforced by a foreign key), ADR-0009 (to-do timestamps as UTC ticks).
- Spec 002 (Contacts API: update and delete):
  - `PUT /api/contacts/{id}` replaces a contact (full replace) and returns 200 with the contact; 400 for invalid or unreadable bodies, 404 for an unknown or non-GUID id, 409 for an email used by another contact.
    - Same field rules as create. Optional fields that are omitted, `null` or blank become `null`. `id` and `createdAt` never change (body values are ignored); `updatedAt` is set on every successful update.
    - Check order: body 400, validation 400, not found 404, email conflict 409. A contact never conflicts with its own email (case changes are fine).
    - Concurrent email races return 409, and an update racing a delete returns 404; neither returns 500, and PUT never recreates a deleted contact.
  - `DELETE /api/contacts/{id}` permanently deletes a contact: 204 with no body, or 404 for an unknown, already deleted or non-GUID id. The email can be reused afterwards.
  - OpenAPI operations `UpdateContact` and `DeleteContact`, with `application/problem+json` error responses.
  - ADR-0006 (validation message style). Decision recorded for spec 003: deleting a contact keeps its to-dos and sets their contact link to `null` (see `specs/002-contacts-api-update-delete/spec.md`).
- Spec 001 (Contacts API: create, get by id, list):
  - `POST /api/contacts` returns 201 with a `Location` header and the contact. `firstName` is required. `lastName`, `email`, `phone`, `company` and `notes` are optional.
    - Text is trimmed, and blank optional fields are stored as `null`.
    - Max lengths after trimming: 100/100/254/50/200/4000. Email must have exactly one `@`, text on both sides, and no whitespace.
    - Duplicate emails (ignoring case and surrounding whitespace) return 409. Concurrent duplicates also return 409, never 500.
    - Validation errors return 400 with an `errors` dictionary keyed by camelCase field, listing every invalid field. An unreadable body returns a plain 400 ProblemDetails without `errors`.
  - `GET /api/contacts/{id}` returns 200, or 404 for an unknown or non-GUID id.
  - `GET /api/contacts?page=&pageSize=&search=` returns `{ items, page, pageSize, totalCount }`.
    - Defaults are page 1 and pageSize 20; pageSize is at most 100. Invalid or non-integer values return 400 with an `errors` entry keyed by the parameter name.
    - Order is last name, first name, id, ignoring ASCII case. Contacts without a last name sort last.
    - `search` is a trimmed, case-insensitive substring match on first name, last name or email (max 254 characters). `%`, `_` and `\` match literally.
  - Every 4xx/5xx response is `application/problem+json`. 500 responses never include exception details.
  - OpenAPI operations `CreateContact`, `ListContacts` and `GetContactById` are documented at `/openapi/v1.json` in Development.
  - SQLite persistence (EF Core) with three migrations, applied automatically at API startup:
    - `CreateContacts`
    - `AddContactEmailUniqueIndex`: unique index `IX_Contacts_Email` on `Email` (NOCASE)
    - `AddContactNameCollation`: NOCASE collation on `FirstName` and `LastName` (rebuilds the table)
  - ADR-0003 (case-insensitive text in SQLite), ADR-0004 (validation and error pipeline), ADR-0005 (integration test database).
### Changed
- Spec 003: deleting a contact now keeps its to-dos and sets their `contactId` to `null`, atomically in the same database statement and on every delete path (enforced by the foreign key; `DELETE /api/contacts/{id}` is unchanged: still 204 or 404, and the to-dos never block it). The to-dos' other fields, including `updatedAt`, don't change.
- Spec 003: the API now forces `Foreign Keys=True` on its SQLite connection string, so foreign keys are enforced even if `ConnectionStrings:MicroCrm` says otherwise.
- `docs/conventions.md`: the JSON row now allows `YYYY-MM-DD` calendar days next to UTC instants; the Errors row lists the new messages; the EF Core section records forced foreign keys, the `Contacts` rebuild hazard, the 787 mapping, and tick timestamps.
- Validation messages follow ADR-0006 (sentence case, ends with a period, doesn't name the field; the `errors` key identifies it). Only the text changed; status codes and keys are the same.
  - Create: `"First name is required."` is now `"Required."`.
  - List: `"'page' must be an integer between 1 and 2147483647."` is now `"Must be an integer between 1 and 2147483647."`; `"'pageSize' must be an integer between 1 and 100."` is now `"Must be an integer between 1 and 100."`; `"'search' must be at most 254 characters."` is now `"Must be 254 characters or fewer."`.
- `docs/conventions.md` Errors row records the message style.
- Removed the template `weatherforecast` sample endpoint.
- `docs/conventions.md` validation and integration-test database lines now match ADR-0004 and ADR-0005.
### Fixed

### Upgrade notes
- **Spec 003 migration:** `CreateTodos` runs at startup and only adds the `Todos` table and its indexes; existing contact data is unchanged. Don't open `microcrm.db` with a tool that has foreign keys off and delete contacts: the to-dos would keep dangling contact ids (see `docs/architecture.md`, Known risks).
- **Existing dev `microcrm.db`:** the `AddContactEmailUniqueIndex` migration fails at startup if the file holds emails that differ only in letter case, because the unique index is case-insensitive. Delete the dev `microcrm.db`, or remove the duplicates by hand, before starting the API. Spec 001 is the first schema, so a file from an earlier scaffold is the only likely case.

### Follow-ups (spec 003, not yet scheduled)
- Any migration that rebuilds `Contacts` (for example `AlterColumn`) will unlink every to-do. Add a survival test and a safe technique first (ADR-0008).
- `TodoListQuery` could use `TextNormalization.TrimToNull` for `status` and `contactId` instead of `Trim()` + `IsNullOrEmpty` (same behavior).
- Shared test helpers: extract the gate/`RunGatedAsync` block (repeated in about 6 test classes) and the raw-SQL helpers on `TodoStoreTests`, plus the NFR-002 `UtcTimestamp` helper, into `Integration/Infrastructure/`.
- EF Core logs a `fail:` entry with a stack trace for each expected unknown-contact 400 and duplicate-email 409. No values leak; consider filtering `Microsoft.EntityFrameworkCore.Update` in `appsettings.json`.

### Follow-ups (spec 001, not yet scheduled)
- Package version drift: `Microsoft.AspNetCore.OpenApi` (API) and `Microsoft.Extensions.TimeProvider.Testing` (tests) are 10.0.0, while the other .NET packages are 10.0.12. Align them in a `chore:` commit with its own test run.
- See also the follow-ups in `specs/001-contacts-api-create-get-list/spec.md` (Implementation notes).
