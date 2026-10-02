# Changelog

All notable changes to this project are documented here.
Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). Versioning: [SemVer](https://semver.org/).

## [Unreleased]
### Added
- Spec 001 (Contacts API: create, get by id, list):
  - `POST /api/contacts` returns 201 with a `Location` header and the contact. `firstName` is required. `lastName`, `email`, `phone`, `company` and `notes` are optional.
    - Text is trimmed, and blank optional fields are stored as `null`.
    - Max lengths after trimming: 100/100/254/50/200/4000. Email must have exactly one `@`, text on both sides, and no whitespace.
    - Duplicate emails (ignoring case and surrounding whitespace) return 409. Concurrent duplicates also return 409, never 500.
    - Validation errors return 400 with an `errors` dictionary keyed by camelCase field, listing every invalid field. An unreadable body returns a plain 400 ProblemDetails without `errors`.
  - `GET /api/contacts/{id}` returns 200, or 404 for an unknown or non-GUID id.
  - `GET /api/contacts?page=&pageSize=&search=` returns `{ items, page, pageSize, totalCount }`.
    - Defaults are page 1 and pageSize 20; pageSize is at most 100. Invalid or non-integer values return 400 naming the parameter.
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
- Removed the template `weatherforecast` sample endpoint.
- `docs/conventions.md` validation and integration-test database lines now match ADR-0004 and ADR-0005.
### Fixed

### Upgrade notes
- **Existing dev `microcrm.db`:** the `AddContactEmailUniqueIndex` migration fails at startup if the file holds emails that differ only in letter case, because the unique index is case-insensitive. Delete the dev `microcrm.db`, or remove the duplicates by hand, before starting the API. Spec 001 is the first schema, so a file from an earlier scaffold is the only likely case.

### Follow-ups (spec 001, not yet scheduled)
- Package version drift: `Microsoft.AspNetCore.OpenApi` (API) and `Microsoft.Extensions.TimeProvider.Testing` (tests) are 10.0.0, while the other .NET packages are 10.0.12. Align them in a `chore:` commit with its own test run.
- See also the follow-ups in `specs/001-contacts-api-create-get-list/spec.md` (Implementation notes).
