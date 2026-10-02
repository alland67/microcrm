# 001: Contacts API: create, get by id, list (paging + search)

**Status:** Done
**Type:** Feature
**Author:** Allan Downs with planner
**Created:** 2026-10-01  **Approved:** 2026-10-01
**Related:** `docs/roadmap.md` (row 001), `docs/conventions.md` (REST shape, paging, ProblemDetails, IDs), ADR-0002 (stack and technical defaults). Followed by spec 002 (update and delete).

## Problem
MicroCRM has no way to store or retrieve contacts yet. Contacts are the core record of the product; to-dos (spec 003) and the web UI (spec 004+) both depend on them. The owner needs a REST API that can create a contact, fetch it again by id, and browse or search the contact list one page at a time. This first slice also sets up the shared foundations later specs reuse: persistence, the standard error format, and an integration test host.

## Goals
- A client can create a contact with a required first name and optional last name, email, phone, company, and notes.
- Email addresses, when given, are valid and unique, ignoring case.
- A client can fetch a single contact by its id.
- A client can list contacts in a stable, predictable order, one page at a time, and narrow the list with a search term that matches first name, last name, or email.
- All errors are returned in the project's standard error format (RFC 9457 ProblemDetails).

## Non-goals / Out of scope
- Updating or deleting contacts (spec 002).
- To-dos and linking to-dos to contacts (spec 003).
- Any web UI (specs 004+).
- Authentication, authorization, multi-user or tenant separation (none in v1 per ADR-0002).
- Optimistic concurrency / ETags (not in v1 per conventions).
- Full-name search (e.g. "Ada Lovelace" matching first + last name together), fuzzy or accent-insensitive search, and search on phone, company, or notes.
- Client-selectable sort order or sort fields.
- Phone number format validation or normalization (phone is free text within its length limit).
- Email deliverability checks (MX lookup, verification emails).
- Bulk import/export, duplicate detection beyond the email rule, soft delete, audit history.

## Users & scenarios
- *As the CRM owner, I want to record a new contact with just a first name, so that I can capture people quickly and fill in details later.*
- *As the CRM owner, I want the system to reject a second contact with an email I already have, so that I don't create duplicate records for the same person.*
- *As the CRM owner, I want to open a contact by its id, so that I can see all of its details.*
- *As the CRM owner, I want to page through my contacts sorted by name, and search by name or email, so that I can find someone quickly even when I have many contacts.*
- *As a client developer (the future web UI), I want consistent status codes and ProblemDetails errors with per-field messages, so that I can show useful validation feedback.*

## Definitions and defaults used by the criteria
These are decisions recorded for this spec. The ones marked *(default)* were chosen by the planner and should be confirmed at approval (see Open questions).

- **Contact fields:** `firstName` (required), `lastName`, `email`, `phone`, `company`, `notes` (all optional), plus system-assigned `id`, `createdAt`, `updatedAt`.
- **Trimming** *(default)*: all text fields in a create request have leading and trailing whitespace removed before validation and storage. An optional field that is empty or whitespace-only after trimming is stored and returned as `null`.
- **Maximum lengths** (measured in characters after trimming): firstName 100, lastName 100, email 254, phone 50, company 200, notes 4000.
- **Valid email** *(default)*: after trimming, contains exactly one `@`, has at least one character before and after the `@`, and contains no whitespace. Stored with the casing the client sent.
- **Email uniqueness:** two emails are duplicates when they are equal after trimming and ignoring letter case. Contacts without an email never conflict with each other.
- **Timestamps:** `createdAt` and `updatedAt` are UTC ISO-8601 values taken from the system clock at creation; on creation they are equal.
- **Sort order:** last name ascending, then first name ascending, then id ascending. Name comparison ignores letter case *(default)*. Contacts with no last name sort after all contacts that have one *(default)*.
- **Paging:** `page` defaults to 1, `pageSize` defaults to 20. Valid values: `page` ≥ 1, `pageSize` 1–100. Out-of-range or non-integer values are rejected with 400, not clamped *(default)*.
- **Search:** the `search` value is trimmed. Empty or whitespace-only search means "no filter" *(default)*. Otherwise it is a case-insensitive substring match against first name, last name, or email (any one matching is enough). Characters in the term are matched literally (no wildcard meaning). Maximum search length is 254 characters after trimming; longer is rejected with 400 *(default)*.

## Acceptance criteria
EARS format. Each must be independently testable. IDs are stable; never renumber.

### Create
| ID | Criterion |
|---|---|
| AC-001 | WHEN a client creates a contact with a valid first name and all optional fields valid THE SYSTEM SHALL respond 201 Created with a `Location` header pointing to `/api/contacts/{id}` and a body containing `id`, `firstName`, `lastName`, `email`, `phone`, `company`, `notes`, `createdAt`, and `updatedAt` reflecting the stored values. |
| AC-002 | WHEN a client creates a contact with only a first name THE SYSTEM SHALL respond 201 Created and return `lastName`, `email`, `phone`, `company`, and `notes` as `null` (present in the JSON, not omitted). |
| AC-003 | WHEN a contact is created THE SYSTEM SHALL assign a new, unique, non-empty GUID id, ignoring any id supplied in the request body. |
| AC-004 | WHEN a contact is created THE SYSTEM SHALL set `createdAt` and `updatedAt` to the current UTC time from the system clock, with both values equal, ignoring any timestamps supplied in the request body. |
| AC-005 | WHEN a client creates a contact with text fields that have leading or trailing whitespace THE SYSTEM SHALL store and return those fields trimmed. |
| AC-006 | WHEN a client creates a contact with an optional text field that is an empty string or whitespace-only THE SYSTEM SHALL store and return that field as `null`. |
| AC-007 | WHEN a client creates a contact with the first name missing, `null`, empty, or whitespace-only THE SYSTEM SHALL respond 400 with a validation ProblemDetails whose `errors` contains a `firstName` entry, and SHALL NOT create a contact. |
| AC-008 | WHEN a client creates a contact with any field longer than its maximum length after trimming (firstName 100, lastName 100, email 254, phone 50, company 200, notes 4000) THE SYSTEM SHALL respond 400 with a validation ProblemDetails whose `errors` contains an entry for each offending field, and SHALL NOT create a contact. |
| AC-009 | WHEN a client creates a contact with a field exactly at its maximum length THE SYSTEM SHALL accept it and respond 201 Created. |
| AC-010 | WHEN a client creates a contact with a non-empty email that is not a valid email (per Definitions) THE SYSTEM SHALL respond 400 with a validation ProblemDetails whose `errors` contains an `email` entry, and SHALL NOT create a contact. |
| AC-011 | WHEN a client creates a contact whose email matches an existing contact's email ignoring case and surrounding whitespace THE SYSTEM SHALL respond 409 Conflict with a ProblemDetails body, and SHALL NOT create a contact. |
| AC-012 | WHEN a client creates a contact with an email containing uppercase letters (e.g. `Ada.Lovelace@Example.com`) THE SYSTEM SHALL store and return the email with the casing as sent, only trimmed (not lower-cased). |
| AC-013 | WHEN a client creates multiple contacts that have no email THE SYSTEM SHALL accept each of them (absence of email never triggers a conflict). |
| AC-014 | WHEN a request has multiple invalid fields THE SYSTEM SHALL report all of them in a single 400 response's `errors`, keyed by camelCase field name. |
| AC-015 | WHEN a client sends a create request whose body is not valid JSON, is empty, or has a field of the wrong JSON type THE SYSTEM SHALL respond 400 with a ProblemDetails body and SHALL NOT create a contact. |
| AC-016 | WHEN two create requests with the same email (ignoring case) are processed concurrently THE SYSTEM SHALL create at most one contact, and SHALL respond 409 Conflict ProblemDetails (not a 500) to the other. |

### Get by id
| ID | Criterion |
|---|---|
| AC-017 | WHEN a client requests `GET /api/contacts/{id}` for an existing contact THE SYSTEM SHALL respond 200 with the same body shape and values returned at creation. |
| AC-018 | WHEN a client requests `GET /api/contacts/{id}` for a well-formed GUID that matches no contact THE SYSTEM SHALL respond 404 with a ProblemDetails body. |
| AC-019 | WHEN a client requests `GET /api/contacts/{id}` with an id that is not a GUID THE SYSTEM SHALL respond 404. |

### List, paging, sorting
| ID | Criterion |
|---|---|
| AC-020 | WHEN a client requests `GET /api/contacts` without query parameters THE SYSTEM SHALL respond 200 with `{ items, page, pageSize, totalCount }` where `page` is 1, `pageSize` is 20, `items` holds at most 20 contacts in the same shape as AC-017, and `totalCount` is the total number of contacts. |
| AC-021 | WHEN no contacts exist THE SYSTEM SHALL respond to a list request with 200, an empty `items` array, and `totalCount` 0. |
| AC-022 | WHEN a client lists contacts THE SYSTEM SHALL order `items` by last name, then first name, then id, all ascending, comparing names without regard to letter case. |
| AC-023 | WHEN a client lists contacts and some contacts have no last name THE SYSTEM SHALL place those contacts after all contacts that have a last name, ordered among themselves by first name then id. |
| AC-024 | WHEN a client requests a valid `page` and `pageSize` THE SYSTEM SHALL return the corresponding slice of the sorted (and filtered) list, echo the requested `page` and `pageSize`, and report `totalCount` as the number of all matching contacts, not just the page. |
| AC-025 | WHEN a client pages through the full list with a fixed `pageSize` and no data changes in between THE SYSTEM SHALL return every contact exactly once across the pages, with no duplicates or gaps, including when contacts share the same first and last name. |
| AC-026 | WHEN a client requests a `page` beyond the last page THE SYSTEM SHALL respond 200 with an empty `items` array and the correct `totalCount`. |
| AC-027 | WHEN a client requests `pageSize` of 100 THE SYSTEM SHALL accept it and return up to 100 contacts. |
| AC-028 | WHEN a client requests `page` less than 1, `pageSize` less than 1, `pageSize` greater than 100, or a non-integer `page` or `pageSize` THE SYSTEM SHALL respond 400 with a validation ProblemDetails naming the offending parameter in `errors`. |

### Search
| ID | Criterion |
|---|---|
| AC-029 | WHEN a client lists contacts with a `search` term THE SYSTEM SHALL return only contacts whose first name, last name, or email contains the term as a substring, ignoring letter case, and `totalCount` SHALL count only matching contacts. |
| AC-030 | WHEN a client searches with a term that spans first and last name (e.g. "Ada Love" for Ada Lovelace) THE SYSTEM SHALL NOT match on the combined full name; only matches within a single field count. |
| AC-031 | WHEN a client lists contacts with a `search` that is empty or whitespace-only THE SYSTEM SHALL return the same result as a request without `search`. |
| AC-032 | WHEN a client searches with a term that has leading or trailing whitespace THE SYSTEM SHALL match using the trimmed term. |
| AC-033 | WHEN a client searches with a term containing characters such as `%`, `_`, or `\` THE SYSTEM SHALL treat them literally, matching only contacts whose fields contain those exact characters. |
| AC-034 | WHEN a client searches with a term longer than 254 characters after trimming THE SYSTEM SHALL respond 400 with a validation ProblemDetails naming `search` in `errors`. |
| AC-035 | WHEN a search matches no contacts THE SYSTEM SHALL respond 200 with an empty `items` array and `totalCount` 0. |
| AC-036 | WHEN a client combines `search` with `page` and `pageSize` THE SYSTEM SHALL apply the filter first, then sorting (AC-022/AC-023), then paging. |

### Errors and persistence
| ID | Criterion |
|---|---|
| AC-037 | WHEN the system returns any 4xx or 5xx error from these endpoints THE SYSTEM SHALL use content type `application/problem+json` with at least `type`, `title`, and `status` members. |
| AC-038 | WHEN an unexpected server error occurs while handling a contacts request THE SYSTEM SHALL respond 500 with a ProblemDetails body that contains no exception message, type name, or stack trace. |
| AC-039 | WHEN a contact has been created THE SYSTEM SHALL return it from later get-by-id and list requests made on new connections (it is persisted, not held only in request memory). |

## Non-functional requirements
| ID | Requirement | How verified |
|---|---|---|
| NFR-001 | All three endpoints (create, get by id, list) appear in the OpenAPI document at `/openapi/v1.json` in Development, each with a name and summary, and their documented response status codes include those in the ACs (201/400/409; 200/404; 200/400). | Integration test fetching and inspecting the OpenAPI document. |
| NFR-002 | JSON property names are camelCase and `null` values are serialized (never omitted). Timestamps are ISO-8601 with a UTC offset (`Z` or `+00:00`). | Integration tests asserting raw JSON. |
| NFR-003 | With 10,000 contacts stored, a list or search request for one page of 100 completes in under 500 ms on a developer machine (local SQLite file DB). | Manual or scripted check recorded in the review; not a CI gate. |
| NFR-004 | No request body values (including email addresses) are written to logs at Information level or above. | Reviewer inspection of logging code. |

## Constraints & assumptions
- Follows `docs/conventions.md`: base path `/api/contacts`, GUID v7 ids, `{ items, page, pageSize, totalCount }` list envelope, ProblemDetails errors, camelCase JSON, `TimeProvider`-sourced UTC timestamps.
- Single-user, no auth (ADR-0002). All callers can create and read all contacts.
- This is the first spec: it is expected to introduce the persistence store and first schema version, the standard ProblemDetails error handling, and the shared integration test host. Those are enablers, not separately visible behavior; they are verified through the ACs above.
- Case-insensitive matching (email uniqueness, search, sort) is guaranteed for ASCII letters (A–Z/a–z). Behavior for non-ASCII letters is an open question (Q1).
- `phone`, `company`, and `notes` are free text; only length is validated.
- `updatedAt` is included now so spec 002 (update) can change it without a response-shape change.

## Open questions
None open. All recorded defaults (Q1–Q7) were accepted as written at approval on 2026-10-01.

- [x] Q1: Case-insensitivity for non-ASCII letters (e.g. `ÉMILE@x.com` vs `émile@x.com`, searching "é" vs "É"). Options: (a) guarantee ASCII-only case-insensitivity in v1 and document it; (b) require full Unicode case folding for email uniqueness, search, and sort. Recommendation: (a). Full Unicode folding is awkward with SQLite and rare for this single-user app; revisit if it bites.
- [x] Q2: Invalid paging values. Options: (a) reject with 400 (current default, AC-028); (b) clamp to the valid range silently. Recommendation: (a). Rejection surfaces client bugs and is easy to test.
- [x] Q3: Trimming and empty optional fields. Default: trim all text fields; empty/whitespace-only optional fields become `null` (AC-005, AC-006). Alternative: store as sent. Recommendation: keep the default, which keeps search, uniqueness, and sort predictable.
- [x] Q4: Contacts with no last name in the sort. Options: (a) after all named contacts (current default, AC-023); (b) before them. Recommendation: (a), so people with full names come first in the list.
- [x] Q5: Name sort ignores case (AC-022). Alternative: ordinal (uppercase before lowercase, so "Smith" before "adams"). Recommendation: case-insensitive, which matches what a user expects.
- [x] Q6: Email validity rule. Default: exactly one `@`, non-empty parts on both sides, no whitespace (AC-010). Alternative: stricter (require a dot in the domain, RFC 5322 syntax). Recommendation: keep it permissive; strict rules reject real addresses and deliverability is out of scope.
- [x] Q7: Search term length cap of 254 (AC-034). Recommendation: keep it. It bounds input (constitution §5) and no field being searched is longer than 254 characters, so a longer term can never match anyway.

## Implementation notes
Modules (under `src/MicroCrm.Api/`):
- `Program.cs`: error pipeline, migrate at startup, `MapContactsEndpoints()`
- `Features/Contacts/`: `ContactsEndpoints.cs`, `ContactInput.cs`, `ContactDtos.cs`, `Contact.cs`
- `Common/Paging.cs` (`ListQuery`, `PagedResponse<T>`), `Common/LikePattern.cs`
- `Data/`: `AppDbContext.cs`, `ContactConfiguration.cs`, `SqliteErrors.cs`, `Migrations/` (3 migrations)

Tests (under `tests/MicroCrm.Api.Tests/`): `Integration/Contacts/*`, `Integration/ErrorHandlingTests.cs`, `Integration/OpenApiTests.cs`, `Unit/`. Infrastructure: `Integration/Infrastructure/ApiFactory.cs`, `ProblemAssert.cs`. AC to test mapping: `tasks.md` Traceability. Docs: `docs/architecture.md`, ADR-0003/0004/0005. NFR-003 timings: `review.md` (T-18). AC-037 has no `_AC037` test by design; it is checked through `ProblemAssert` in every error-path test.

Open follow-ups (none block this spec):
- Validation message style differs between create (`"Must be a valid email address."`) and list (`"'page' must be an integer between 1 and 2147483647."`). Pick one style before spec 002 and record it in conventions.
- `src/MicroCrm.Api/MicroCrm.Api.http` is a stub with port 5185, while `DEV_API_CMD` uses 5080. Add sample requests and align the port.
- The OpenAPI document shows the get-by-id 404 without a `application/problem+json` content type, while runtime returns problem+json.
- Migrations run at API startup. Revisit before any deployment.
- `SqliteErrors` does not check the index name for unique violations. Revisit when a second unique index is added.
- Package drift: `Microsoft.AspNetCore.OpenApi` and `Microsoft.Extensions.TimeProvider.Testing` are 10.0.0, the rest 10.0.12.
- Process: T-04..T-17 are uncommitted (`COMMIT_PER_TASK=0`); decide on per-task or one accepted commit. `docs/.claude/` is untracked.
