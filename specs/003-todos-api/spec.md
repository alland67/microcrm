# 003: To-dos API: CRUD, optional contact link, due date, complete/reopen

**Status:** Done
**Type:** Feature
**Author:** Allan Downs with planner
**Created:** 2026-10-05  **Approved:** 2026-10-05
**Related:** `docs/roadmap.md` (row 003), `specs/001-contacts-api-create-get-list/spec.md` (field rule style, paging, error format), `specs/002-contacts-api-update-delete/spec.md` (full-replace update, check order, and "Decision: deleting a contact and its to-dos", which this spec must enforce), `docs/conventions.md` (REST shape, action endpoints, nested route, ProblemDetails), ADR-0003 (case-insensitive text), ADR-0004 (validation and error pipeline), ADR-0005 (integration test database), ADR-0006 (validation message style). Followed by specs 005 (contact detail with that contact's to-dos) and 006 (to-dos page with open / done / overdue filters).

## Problem
The CRM owner can manage contacts but has nowhere to record what needs doing: call someone back, send a quote, follow up next week. Without to-dos, follow-ups live outside the CRM and get missed. The owner needs to-dos that can stand alone or belong to a contact, can carry a due date, and can be marked done and reopened, and needs to see which ones are open, done, or overdue. The web pages in specs 005 and 006 depend on this API. Spec 002 also settled that deleting a contact keeps its to-dos and unlinks them; that rule can only be enforced and tested once to-dos exist, so it is part of this spec.

## Goals
- A client can create, read, fully update, and delete to-dos.
- A to-do has a required title, optional notes, an optional due date (a calendar date), an optional link to one existing contact, and a done state with the time it was completed.
- A client can mark a to-do done and reopen it with dedicated, idempotent actions.
- A client can list to-dos one page at a time, filtered by status (open, done, overdue) and by contact, in a stable order.
- A client can list one contact's to-dos.
- Deleting a contact never deletes or blocks on its to-dos; it unlinks them, atomically, on every delete path (spec 002 Decision).
- Field rules, error format, paging rules, and validation messages match the contacts endpoints, so the web client can reuse its handling.

## Non-goals / Out of scope
- Any web UI (specs 005 and 006).
- Authentication, authorization, multi-user ownership or assignment (none in v1 per ADR-0002).
- Partial updates (PATCH or "only send what changes"). `PUT` is a full replace per conventions.
- Changing the done state through `PUT` or create; only the complete and reopen actions change it.
- Bulk operations (bulk complete, bulk delete, bulk relink).
- Linking a to-do to more than one contact, or to anything other than a contact.
- Due times, time zones per to-do, reminders, notifications, recurrence, priorities, tags, subtasks, attachments.
- Free-text search over to-dos, client-selectable sort order, and a filter for "to-dos with no contact".
- Embedding contact details (such as the contact's name) in to-do responses, or embedding to-dos in contact responses. The contact response shape from spec 001 does not change.
- Optimistic concurrency, ETags, `If-Match` (conventions: no concurrency in v1). Concurrent updates to the same to-do are last-write-wins.
- Soft delete, undo, archive, audit history.
- Any change to contact field rules, contact list ordering, search, or paging.

## Users & scenarios
- *As the CRM owner, I want to jot down a to-do with just a title, so that I can capture it quickly.*
- *As the CRM owner, I want to link a to-do to a contact and give it a due date, so that I know who it's about and when it's due.*
- *As the CRM owner, I want to mark a to-do done, and reopen it if I marked it by mistake, so that my open list stays accurate.*
- *As the CRM owner, I want to see my open, done, or overdue to-dos, so that I can decide what to work on next.*
- *As the CRM owner, I want to see all to-dos for one contact, so that I can prepare before calling them.*
- *As the CRM owner, I want to delete a contact without losing the to-dos I wrote about them, so that no follow-up silently disappears.*
- *As a client developer (the future web UI), I want the same status codes, ProblemDetails shape, paging envelope, and message style as the contacts endpoints, so that I can reuse my client code.*

## Definitions and defaults used by the criteria
The planner proposed the items marked with a question number, and the user accepted them on 2026-10-05 (Open questions Q1 to Q9, all resolved). The other items come from the user's clarification answers of 2026-10-05, spec 002, or conventions.

- **To-do fields (request):** `title` (required), `notes`, `dueDate`, `contactId` (all optional). Same trimming style as contacts: every text value is trimmed before validation and storage, and an optional value that is missing, `null`, empty, or whitespace-only becomes `null`.
- **Maximum lengths** (characters after trimming): `title` 200, `notes` 4000.
- **Response shape** (Q7): `id`, `title`, `notes`, `contactId`, `dueDate`, `isDone`, `completedAt`, `createdAt`, `updatedAt`. Every member is always present; absent values are `null` (never omitted).
- **Due date:** a calendar date with no time or time zone, sent and returned as a string in exactly the form `YYYY-MM-DD` (four-digit year, two-digit month, two-digit day). It must be a real calendar date (for example `2026-02-30` is invalid). Values with a time part, other separators, or other orderings are invalid. Any real calendar date from `0001-01-01` to `9999-12-31` is accepted, including past dates (Q9). This deliberately differs from the conventions rule that dates are UTC `DateTimeOffset` values, because a due date is a day, not an instant; the plan records this.
- **Contact link:** `contactId`, when given, must be a well-formed GUID that identifies an existing contact at the time of the request. Missing, `null`, empty, or whitespace-only means "not linked".
- **Done state:** a to-do is either open (`isDone` false, `completedAt` null) or done (`isDone` true, `completedAt` set). These two combinations are the only valid ones. A new to-do is always open; any `isDone` or `completedAt` in a create or update body is ignored (Q7).
- **Complete / reopen:** `POST /api/todos/{id}/complete` and `POST /api/todos/{id}/reopen`, both idempotent. Completing an open to-do sets `isDone` true and `completedAt` and `updatedAt` to the current UTC time from the system clock. Reopening a done to-do sets `isDone` false, `completedAt` null, and `updatedAt` to the current time. Completing a to-do that is already done, or reopening one that is already open, changes nothing (`completedAt` and `updatedAt` keep their values) and responds 200 (Q5). Any request body is ignored.
- **Full replace (`PUT`):** replaces exactly the editable fields `title`, `notes`, `dueDate`, `contactId`. Any optional field missing from the body is stored as `null`; in particular, omitting `contactId` unlinks the to-do. `id`, `createdAt`, `isDone`, and `completedAt` never change through `PUT`; values for them in the body are ignored. `updatedAt` is set to the current time on every successful `PUT`, even when nothing changed (same as spec 002 Q1).
- **Today:** the current calendar date in UTC, taken from the system clock at the time of the request (Q2).
- **Overdue:** a to-do is overdue when it is open, has a due date, and the due date is before today. A to-do due today is not overdue. A done to-do or one with no due date is never overdue.
- **Status filter** (`status` query parameter): `open` = all open to-dos (overdue ones included); `done` = all done to-dos; `overdue` = as defined above. The value is trimmed and matched ignoring letter case; empty, whitespace-only, or absent means no status filter (Q8).
- **Contact filter** (`contactId` query parameter on `GET /api/todos`): trimmed; empty or absent means no filter; otherwise it must be a well-formed GUID, and only to-dos linked to that contact are returned. A well-formed GUID that matches no contact returns an empty page, not 404 (Q8).
- **Lists of one contact's to-dos** (Q4): `GET /api/contacts/{id}/todos` uses the same paged envelope, paging rules, and `status` filter as `GET /api/todos`.
- **Paging:** identical to the contacts list (spec 001): `page` default 1, `pageSize` default 20, valid `page` ≥ 1 and `pageSize` 1–100, non-integer or out-of-range values rejected with 400 (not clamped), envelope `{ items, page, pageSize, totalCount }`.
- **List order** (Q3): due date ascending with to-dos that have no due date after all that have one, then `createdAt` ascending, then `id` ascending. The same order applies to every to-do list (global and per contact) and every filter.
- **Validation messages** (ADR-0006 style; existing canonical messages reused): `Required.`; `Must be {max} characters or fewer.`; `Must be an integer between {min} and {max}.`. New messages in the same style: dueDate `Must be a valid date in YYYY-MM-DD format.`; malformed GUID (body or query `contactId`) `Must be a valid GUID.`; contact not found `Must refer to an existing contact.`; status `Must be one of: open, done, overdue.`
- **Check order, create:** (1) unreadable body → 400 ProblemDetails without `errors`; (2) field rules (required, lengths, date format, GUID format) → 400 with `errors`, all reported together; (3) linked contact doesn't exist → 400 with a `contactId` entry (Q1). The contact-existence check runs only when every field rule passes.
- **Check order, update:** (1) unreadable body → 400; (2) field rules → 400; (3) to-do not found → 404; (4) linked contact doesn't exist → 400 with a `contactId` entry. This follows spec 002's order, with the contact-existence check in the slot spec 002 uses for its state-dependent 409.
- **Check order, lists:** query parameter errors (`page`, `pageSize`, `status`, `contactId`) → 400 with all offending parameters reported together; then, for the nested endpoint only, contact not found → 404.
- **Unlink on contact delete** (spec 002 Decision, user decision 2026-10-02): when a contact is deleted, every to-do linked to it is kept and its `contactId` becomes `null`. This happens in the same atomic operation as the contact removal, is enforced by the data store itself so it holds for every delete path (including future bulk or nested deletes and deletes that bypass the application's change tracking), and never blocks the delete. Unlinking does not change the to-do's `updatedAt` or any other field (Q6).
- **Ids and timestamps:** to-do ids are new GUIDs assigned by the server; any `id` in a body is ignored. `createdAt`, `updatedAt`, and `completedAt` are UTC ISO-8601 instants from the system clock. On creation `createdAt` equals `updatedAt`.

## Acceptance criteria
EARS format. Each must be independently testable. IDs are stable; never renumber.

### Create
| ID | Criterion |
|---|---|
| AC-001 | WHEN a client sends `POST /api/todos` with a valid body THE SYSTEM SHALL respond 201 Created with a `Location` header pointing to `/api/todos/{id}` and a body containing `id`, `title`, `notes`, `contactId`, `dueDate`, `isDone`, `completedAt`, `createdAt`, and `updatedAt` holding the stored values, and SHALL return the same values from later `GET /api/todos/{id}` requests on new connections. |
| AC-002 | WHEN a client creates a to-do with only a title THE SYSTEM SHALL respond 201 and return `notes`, `contactId`, `dueDate`, and `completedAt` as `null` (present, not omitted) and `isDone` as `false`. |
| AC-003 | WHEN a to-do is created THE SYSTEM SHALL assign a new, unique, non-empty GUID id and set `createdAt` and `updatedAt` to the current UTC time from the system clock with both values equal, ignoring any `id`, `createdAt`, `updatedAt`, `isDone`, or `completedAt` in the request body (a new to-do is always open). |
| AC-004 | WHEN a client creates a to-do with a title or notes that have leading or trailing whitespace THE SYSTEM SHALL store and return them trimmed, and WHEN notes are empty or whitespace-only THE SYSTEM SHALL store and return `notes` as `null`. |
| AC-005 | WHEN a client creates a to-do with a `dueDate` that is a valid calendar date in `YYYY-MM-DD` form, including a date in the past or today, THE SYSTEM SHALL store it and return exactly the same `YYYY-MM-DD` string. |
| AC-006 | WHEN a client creates a to-do with `dueDate` missing, `null`, empty, or whitespace-only THE SYSTEM SHALL store and return `dueDate` as `null`. |
| AC-007 | WHEN a client creates a to-do with a `contactId` that identifies an existing contact THE SYSTEM SHALL link the to-do to that contact and return that `contactId`. |
| AC-008 | WHEN a client creates a to-do with `contactId` missing, `null`, empty, or whitespace-only THE SYSTEM SHALL create an unlinked to-do with `contactId` `null`. |
| AC-009 | WHEN a client creates a to-do with `title` exactly 200 characters and `notes` exactly 4000 characters after trimming THE SYSTEM SHALL accept it and respond 201. |

### Field validation (create and update)
| ID | Criterion |
|---|---|
| AC-010 | WHEN a create or update request has `title` missing, `null`, empty, or whitespace-only THE SYSTEM SHALL respond 400 with a validation ProblemDetails whose `errors` contains `title` with the message `Required.`, and SHALL NOT create or change any to-do. |
| AC-011 | WHEN a create or update request has `title` longer than 200 or `notes` longer than 4000 characters after trimming THE SYSTEM SHALL respond 400 with a validation ProblemDetails whose `errors` contains an entry for each offending field with the message `Must be {max} characters or fewer.`, and SHALL NOT create or change any to-do. |
| AC-012 | WHEN a create or update request has a non-empty `dueDate` that is not a real calendar date in exactly `YYYY-MM-DD` form (for example `2026-13-01`, `2026-02-30`, `05/10/2026`, `2026-10-5`, or `2026-10-05T00:00:00Z`) THE SYSTEM SHALL respond 400 with a validation ProblemDetails whose `errors` contains `dueDate` with the message `Must be a valid date in YYYY-MM-DD format.`, and SHALL NOT create or change any to-do. |
| AC-013 | WHEN a create or update request has a non-empty `contactId` string that is not a well-formed GUID THE SYSTEM SHALL respond 400 with a validation ProblemDetails whose `errors` contains `contactId` with the message `Must be a valid GUID.`, and SHALL NOT create or change any to-do. |
| AC-014 | WHEN a create or update request breaks more than one field rule THE SYSTEM SHALL report every offending field in a single 400 response's `errors`, keyed by camelCase field name. |
| AC-015 | WHEN a create or update request body is not valid JSON, is empty, or has a field of the wrong JSON type THE SYSTEM SHALL respond 400 with a ProblemDetails body (no `errors` required) and SHALL NOT create or change any to-do. |
| AC-016 | WHEN a client creates a to-do with a well-formed `contactId` that matches no contact, and every field rule passes, THE SYSTEM SHALL respond 400 with a validation ProblemDetails whose `errors` contains `contactId` with the message `Must refer to an existing contact.`, and SHALL NOT create a to-do. |
| AC-017 | WHEN a create request breaks a field rule and also has a well-formed `contactId` that matches no contact THE SYSTEM SHALL respond 400 whose `errors` contains the field-rule entries and no `contactId` entry (contact existence is checked only after field rules pass). |

### Get by id
| ID | Criterion |
|---|---|
| AC-018 | WHEN a client requests `GET /api/todos/{id}` for an existing to-do THE SYSTEM SHALL respond 200 with the to-do in the shape of AC-001. |
| AC-019 | WHEN a client requests `GET /api/todos/{id}` for a well-formed GUID that matches no to-do THE SYSTEM SHALL respond 404 with a ProblemDetails body, and WHEN the id is not a GUID THE SYSTEM SHALL respond 404. |

### Update (full replace)
| ID | Criterion |
|---|---|
| AC-020 | WHEN a client sends `PUT /api/todos/{id}` for an existing to-do with a valid body THE SYSTEM SHALL respond 200 with the updated to-do in the shape of AC-001, and SHALL return the updated values from later get and list requests on new connections. |
| AC-021 | WHEN a client updates a to-do with `notes`, `dueDate`, or `contactId` missing, `null`, empty, or whitespace-only THE SYSTEM SHALL store and return that field as `null`, even if it previously had a value (omitting `contactId` unlinks the to-do). |
| AC-022 | WHEN a client updates a to-do THE SYSTEM SHALL keep its `id`, `createdAt`, `isDone`, and `completedAt` unchanged, ignoring any of those (or `updatedAt`) in the body, including an `id` that differs from the URL. |
| AC-023 | WHEN a client updates a to-do THE SYSTEM SHALL set `updatedAt` to the current UTC time from the system clock, even when the submitted values equal the stored values. |
| AC-024 | WHEN a client updates a to-do with a `contactId` of an existing contact THE SYSTEM SHALL link it to that contact, whether it was previously unlinked or linked to a different contact. |
| AC-025 | WHEN a client updates a to-do with title and notes at their maximum lengths after trimming, and with whitespace around text values, THE SYSTEM SHALL accept it and store and return the values trimmed. |
| AC-026 | WHEN a client sends `PUT /api/todos/{id}` with a valid body for a well-formed GUID that matches no to-do THE SYSTEM SHALL respond 404 with a ProblemDetails body and SHALL NOT create a to-do, and WHEN the id is not a GUID THE SYSTEM SHALL respond 404 and SHALL NOT create or change any to-do. |
| AC-027 | WHEN a client sends an update that breaks a field rule to a well-formed GUID that matches no to-do THE SYSTEM SHALL respond 400 (field rules are checked before existence). |
| AC-028 | WHEN a client sends an update with a valid body whose `contactId` matches no contact to a well-formed GUID that matches no to-do THE SYSTEM SHALL respond 404 (to-do existence is checked before contact existence). |
| AC-029 | WHEN a client updates an existing to-do with a well-formed `contactId` that matches no contact, and every field rule passes, THE SYSTEM SHALL respond 400 with a validation ProblemDetails whose `errors` contains `contactId` with the message `Must refer to an existing contact.`, and SHALL leave the to-do unchanged. |
| AC-030 | WHEN a client updates a to-do THE SYSTEM SHALL leave every other to-do and every contact unchanged. |

### Complete and reopen
| ID | Criterion |
|---|---|
| AC-031 | WHEN a client sends `POST /api/todos/{id}/complete` for an open to-do THE SYSTEM SHALL respond 200 with the to-do showing `isDone` `true` and `completedAt` and `updatedAt` equal to the current UTC time from the system clock, and SHALL persist that state. |
| AC-032 | WHEN a client sends `POST /api/todos/{id}/complete` for a to-do that is already done THE SYSTEM SHALL respond 200 with the to-do unchanged, keeping its original `completedAt` and `updatedAt`. |
| AC-033 | WHEN a client sends `POST /api/todos/{id}/reopen` for a done to-do THE SYSTEM SHALL respond 200 with the to-do showing `isDone` `false`, `completedAt` `null`, and `updatedAt` equal to the current UTC time from the system clock, and SHALL persist that state. |
| AC-034 | WHEN a client sends `POST /api/todos/{id}/reopen` for a to-do that is already open THE SYSTEM SHALL respond 200 with the to-do unchanged, keeping its `updatedAt`. |
| AC-035 | WHEN a client sends a complete or reopen request THE SYSTEM SHALL change only `isDone`, `completedAt`, and `updatedAt` of that to-do, leaving its other fields and every other to-do unchanged, and SHALL ignore any request body. |
| AC-036 | WHEN a client sends a complete or reopen request for a well-formed GUID that matches no to-do, or an id that is not a GUID, THE SYSTEM SHALL respond 404 and SHALL NOT create or change any to-do. |
| AC-037 | WHEN a client updates a done to-do with `PUT` THE SYSTEM SHALL keep it done with its original `completedAt`. |

### Delete
| ID | Criterion |
|---|---|
| AC-038 | WHEN a client sends `DELETE /api/todos/{id}` for an existing to-do THE SYSTEM SHALL respond 204 No Content with an empty body, and afterwards SHALL respond 404 to `GET /api/todos/{id}` on new connections and exclude the to-do from every list and from `totalCount`. |
| AC-039 | WHEN a client sends `DELETE /api/todos/{id}` for a well-formed GUID that matches no to-do (including one already deleted) THE SYSTEM SHALL respond 404 with a ProblemDetails body, and WHEN the id is not a GUID THE SYSTEM SHALL respond 404 and SHALL NOT delete anything. |
| AC-040 | WHEN a to-do is deleted THE SYSTEM SHALL leave its linked contact and every other to-do unchanged. |
| AC-041 | WHEN a to-do has been deleted THE SYSTEM SHALL respond 404 to `PUT`, complete, and reopen requests for that id and SHALL NOT recreate it. |

### List all to-dos
| ID | Criterion |
|---|---|
| AC-042 | WHEN a client requests `GET /api/todos` without query parameters THE SYSTEM SHALL respond 200 with `{ items, page, pageSize, totalCount }` where `page` is 1, `pageSize` is 20, `items` holds at most 20 to-dos in the shape of AC-001, and `totalCount` is the number of all to-dos. |
| AC-043 | WHEN no to-dos exist (or none match the filters) THE SYSTEM SHALL respond 200 with an empty `items` array and `totalCount` 0. |
| AC-044 | WHEN a client lists to-dos THE SYSTEM SHALL order `items` by due date ascending, placing to-dos without a due date after all to-dos that have one, then by `createdAt` ascending, then by `id` ascending. |
| AC-045 | WHEN a client requests a valid `page` and `pageSize` THE SYSTEM SHALL return the corresponding slice of the filtered and ordered list, echo `page` and `pageSize`, report `totalCount` as the number of all matching to-dos, return an empty `items` for a page beyond the last, accept `pageSize` 100, and, with no data changes in between, return every matching to-do exactly once across pages, including to-dos with equal due dates and creation times. |
| AC-046 | WHEN a client requests `page` less than 1, `pageSize` less than 1 or greater than 100, or a non-integer `page` or `pageSize` THE SYSTEM SHALL respond 400 with a validation ProblemDetails whose `errors` contains the offending parameter with the message `Must be an integer between {min} and {max}.` (the same messages as the contacts list). |
| AC-047 | WHEN a client lists to-dos with `status=open` THE SYSTEM SHALL return only to-dos with `isDone` `false`, including overdue ones. |
| AC-048 | WHEN a client lists to-dos with `status=done` THE SYSTEM SHALL return only to-dos with `isDone` `true`. |
| AC-049 | WHEN a client lists to-dos with `status=overdue` THE SYSTEM SHALL return only open to-dos whose due date is before today (UTC date from the system clock), excluding to-dos due today, to-dos without a due date, and done to-dos with past due dates. |
| AC-050 | WHEN the system clock moves past midnight UTC THE SYSTEM SHALL include an open to-do due on the previous day in `status=overdue` results that it excluded before midnight. |
| AC-051 | WHEN a client lists to-dos with `status` absent, empty, or whitespace-only THE SYSTEM SHALL apply no status filter, and WHEN `status` differs from `open`, `done`, or `overdue` only by letter case or surrounding whitespace THE SYSTEM SHALL treat it as that value. |
| AC-052 | WHEN a client lists to-dos with any other `status` value THE SYSTEM SHALL respond 400 with a validation ProblemDetails whose `errors` contains `status` with the message `Must be one of: open, done, overdue.` |
| AC-053 | WHEN a client lists to-dos with a `contactId` that is a well-formed GUID THE SYSTEM SHALL return only to-dos linked to that contact, and an empty page with `totalCount` 0 if no contact has that id; WHEN `contactId` is absent, empty, or whitespace-only THE SYSTEM SHALL apply no contact filter. |
| AC-054 | WHEN a client lists to-dos with a non-empty `contactId` that is not a well-formed GUID THE SYSTEM SHALL respond 400 with a validation ProblemDetails whose `errors` contains `contactId` with the message `Must be a valid GUID.` |
| AC-055 | WHEN a client combines `status`, `contactId`, `page`, and `pageSize` THE SYSTEM SHALL return to-dos matching all filters, ordered per AC-044, then paged, with `totalCount` counting all matches. |
| AC-056 | WHEN a list request has more than one invalid query parameter THE SYSTEM SHALL report every offending parameter in a single 400 response's `errors`. |

### List one contact's to-dos
| ID | Criterion |
|---|---|
| AC-057 | WHEN a client requests `GET /api/contacts/{id}/todos` for an existing contact THE SYSTEM SHALL respond 200 with the paged envelope of AC-042 holding only to-dos linked to that contact, ordered per AC-044, with `totalCount` counting only that contact's matching to-dos. |
| AC-058 | WHEN a client requests `GET /api/contacts/{id}/todos` with `page`, `pageSize`, or `status` THE SYSTEM SHALL apply them with the same defaults, rules, and error responses as `GET /api/todos` (AC-045 to AC-052). |
| AC-059 | WHEN a client requests `GET /api/contacts/{id}/todos` for an existing contact that has no to-dos THE SYSTEM SHALL respond 200 with an empty `items` array and `totalCount` 0. |
| AC-060 | WHEN a client requests `GET /api/contacts/{id}/todos` for a well-formed GUID that matches no contact THE SYSTEM SHALL respond 404 with a ProblemDetails body, and WHEN the id is not a GUID THE SYSTEM SHALL respond 404. |
| AC-061 | WHEN a client requests `GET /api/contacts/{id}/todos` with an invalid query parameter for a well-formed GUID that matches no contact THE SYSTEM SHALL respond 400 (query parameters are checked before contact existence). |

### Deleting a contact unlinks its to-dos (spec 002 Decision)
| ID | Criterion |
|---|---|
| AC-062 | WHEN a contact that has linked to-dos is deleted THE SYSTEM SHALL respond 204 to the delete (to-dos never block it). |
| AC-063 | WHEN a contact that had linked to-dos has been deleted THE SYSTEM SHALL still return each of those to-dos from `GET /api/todos/{id}` and `GET /api/todos` on new connections, with `contactId` `null` and every other field (including `title`, `isDone`, `completedAt`, and `updatedAt`) unchanged. |
| AC-064 | WHEN a contact is deleted THE SYSTEM SHALL leave to-dos linked to other contacts, and unlinked to-dos, unchanged. |
| AC-065 | WHEN a contact delete does not complete (the operation fails partway, for example because the data store reports an error while removing the contact) THE SYSTEM SHALL leave the contact and the `contactId` of every to-do linked to it unchanged; and WHEN it completes THE SYSTEM SHALL never expose a state in which the contact is gone but a to-do still holds its id (removal and unlinking are one atomic change). |
| AC-066 | WHEN a contact is removed through any delete path, including a delete performed directly in the data store outside the API's request handling, THE SYSTEM SHALL return that contact's former to-dos with `contactId` `null` (the rule is enforced by the data store, not only by API code). |
| AC-067 | WHEN a create or update that links a to-do to a contact is processed concurrently with the deletion of that contact THE SYSTEM SHALL respond to each request with one of its documented status codes (create: 201 or 400; update: 200, 400, or 404; delete: 204 or 404), never a 500, and afterwards no to-do SHALL hold the id of a contact that doesn't exist. |
| AC-068 | WHEN a contact has been deleted THE SYSTEM SHALL respond 404 to `GET /api/contacts/{id}/todos` for that id and SHALL exclude its former to-dos from `GET /api/todos?contactId={id}`. |

### Concurrency (to-dos)
| ID | Criterion |
|---|---|
| AC-069 | WHEN complete and reopen requests for the same to-do are processed concurrently THE SYSTEM SHALL respond 200 to each (never a 500), and the stored to-do SHALL afterwards be either done with a non-null `completedAt` or open with a `null` `completedAt`. |
| AC-070 | WHEN an update, complete, or reopen of a to-do is processed concurrently with its deletion THE SYSTEM SHALL respond to each with one of its documented status codes (update: 200, 400, or 404; complete/reopen: 200 or 404; delete: 204 or 404), never a 500, and, if the delete responded 204, a later `GET /api/todos/{id}` SHALL respond 404. |
| AC-071 | WHEN two deletes of the same to-do are processed concurrently THE SYSTEM SHALL respond 204 to exactly one and 404 to the other. |

### Errors and message style
| ID | Criterion |
|---|---|
| AC-072 | WHEN the system returns any 4xx or 5xx error from a to-do endpoint (including `GET /api/contacts/{id}/todos`) THE SYSTEM SHALL use content type `application/problem+json` with at least `type`, `title`, and `status` members. |
| AC-073 | WHEN an unexpected server error occurs while handling a to-do request THE SYSTEM SHALL respond 500 with a ProblemDetails body that contains no exception message, type name, or stack trace, and SHALL leave stored data unchanged. |
| AC-074 | WHEN any to-do endpoint responds 400 with a validation ProblemDetails THE SYSTEM SHALL make every message in `errors` a sentence that starts with an uppercase letter, ends with a period, and doesn't contain the field or parameter name (camelCase key or quoted form), per ADR-0006. |

## Non-functional requirements
| ID | Requirement | How verified |
|---|---|---|
| NFR-001 | Every to-do endpoint (create, get by id, list, update, delete, complete, reopen, list a contact's to-dos) appears in the OpenAPI document at `/openapi/v1.json` in Development with a name and summary, and documents the status codes in the ACs (create 201/400; get 200/404; list 200/400; update 200/400/404; delete 204/404; complete and reopen 200/404; contact's to-dos 200/400/404). | Integration test inspecting the OpenAPI document. |
| NFR-002 | JSON property names are camelCase, `null` values are serialized (never omitted), `createdAt`/`updatedAt`/`completedAt` are ISO-8601 with a UTC offset, `dueDate` is a `YYYY-MM-DD` string, and `isDone` is a JSON boolean. | Integration tests asserting raw JSON. |
| NFR-003 | With 10,000 to-dos stored (spread across 1,000 contacts, mixed done/open and due dates), a list request for one page of 100, with or without `status` and `contactId` filters, and a contact's to-dos request, each complete in under 500 ms on a developer machine (local SQLite file DB). | Scripted or manual check recorded in the review; not a CI gate. |
| NFR-004 | No request body values and no stored to-do field values (title, notes) are written to logs at Information level or above by any to-do path or by the contact delete path. | Reviewer inspection of logging code. |
| NFR-005 | Create and update return identical `errors` dictionaries for the same field-rule-breaking payload (one source of field rules). | Unit or integration test comparing both. |

## Constraints & assumptions
- Follows `docs/conventions.md`: base path `/api/todos`, GUID v7 ids, `{ items, page, pageSize, totalCount }` envelope, full-replace `PUT`, action sub-resources instead of PATCH, nested `GET /api/contacts/{id}/todos`, ProblemDetails errors, camelCase JSON, `TimeProvider`-sourced UTC timestamps, no concurrency control in v1.
- Reuses the validation and error pipeline of ADR-0004 and the message style of ADR-0006. The existing canonical paging messages are reused unchanged.
- `dueDate` as a date-only value is a deliberate exception to the conventions "dates are ISO-8601 UTC `DateTimeOffset`" rule; the plan must record it (ADR or conventions amendment through an ADR).
- The Unlink rule must be enforced by the data store (a foreign key that clears the link on delete), because the existing contact delete removes the row in a single database statement that bypasses any application-side cascade (spec 002, `docs/roadmap.md` row 003). Tests may inspect the database or perform a delete directly against it (ADR-0005 allows a test to open its own connection) to verify AC-065 and AC-066.
- Contact endpoints keep their spec 001/002 behavior and response shapes. Only the contact delete gains the unlink side effect, which happens in the data store.
- Single-user, no auth (ADR-0002). Any caller can read and change any to-do.
- Introducing to-dos adds a new table and a schema migration; existing contact data is unaffected.
- Case-insensitive matching of `status` values is ASCII-only, consistent with ADR-0003.

## Open questions
None open. The user accepted every recommendation on 2026-10-05 ("accept all recommendations"), and the outcomes are folded into Definitions and the ACs.

- [x] Q1: Status code when `contactId` (create or update body) is a well-formed GUID that matches no contact. **Resolved: (a)** 400 validation ProblemDetails with a `contactId` entry `Must refer to an existing contact.`, checked after field rules (and after to-do existence on `PUT`); a concurrent contact deletion maps to the same 400, never 500 (AC-016, AC-017, AC-028, AC-029, AC-067). Rejected: (b) 409 Conflict; (c) 404; (d) 422 Unprocessable Content.
- [x] Q2: How "today" is determined for "overdue". **Resolved: (a)** the UTC calendar date of the server clock (AC-049, AC-050). Accepted trade-off: for a user west of UTC a to-do turns overdue in the evening of its due date. Rejected: (b) server's local time zone; (c) configured application time zone; (d) client-supplied date.
- [x] Q3: List order. **Resolved: (a)** due date ascending, no due date last, then `createdAt` ascending, then `id` ascending, for every to-do list and filter (AC-044). Rejected: (b) newest first; (c) open before done, then (a).
- [x] Q4: Shape of `GET /api/contacts/{id}/todos`. **Resolved: (a)** the same paged envelope with `page`, `pageSize`, and `status` (AC-057 to AC-061). Rejected: (b) an unpaged array.
- [x] Q5: No-op complete/reopen. **Resolved: (a)** respond 200 and change nothing, keeping the first `completedAt` and the existing `updatedAt` (AC-032, AC-034). Rejected: (b) re-stamp `completedAt`/`updatedAt`; (c) 409.
- [x] Q6: Does unlinking (contact deleted) change the to-do's `updatedAt`? **Resolved: (a)** no; only `contactId` changes (AC-063). Rejected: (b) set `updatedAt` to the time of the contact delete.
- [x] Q7: Naming and creation of the done state. **Resolved: (a)** `isDone` boolean plus `completedAt`; a new to-do is always open, and done state in create or update bodies is ignored (AC-002, AC-003, AC-022). Rejected: (b) a `status` string in the response; (c) creating a to-do already done.
- [x] Q8: Lenient query values. **Resolved: (a)** `status` is trimmed and case-insensitive, and blank means no filter; a `contactId` filter for an unknown contact returns an empty page (AC-051, AC-053). Rejected: (b) exact lowercase `status` only and 404 for an unknown `contactId` filter.
- [x] Q9: Due date range. **Resolved: (a)** any real calendar date expressible as `YYYY-MM-DD` (years 0001 to 9999), past dates allowed (AC-005, AC-012). Rejected: (b) rejecting past dates.

## Implementation notes
Modules (under `src/MicroCrm.Api/`):
- `Features/Todos/TodosEndpoints.cs`: all eight operations (`MapGroup("/api/todos")` plus `GET /api/contacts/{id}/todos`); delete is a single `ExecuteDeleteAsync`; create and update map SQLite code 787 to the `contactId` 400 with no contact pre-check; update marks `ContactId` modified so the FK is re-checked
- `Features/Todos/Todo.cs` (entity, `Complete`/`Reopen`), `TodoDtos.cs`, `TodoInput.cs` (shared create/update core; `dueDate` parsed with `DateOnly.TryParseExact`), `TodoListQuery.cs` (paging, `status`, `contactId`)
- `Data/TodoConfiguration.cs` (tick converters, FK `SetNull`, indexes, `CK_Todos_DoneState`), `Data/Migrations/*_CreateTodos.cs`, `Data/SqliteErrors.cs` (`IsForeignKeyViolation`)
- `Program.cs`: `Foreign Keys=True` forced on the connection string; `MapTodosEndpoints()`
- `Common/TextNormalization.cs`: `TrimToNull`, moved out of `ContactInput` and shared
- `MicroCrm.Api.http`: sample to-do requests
- Contact endpoints are unchanged; the unlink happens in the database.

Tests (under `tests/MicroCrm.Api.Tests/`): `Integration/Todos/` (19 classes, from `CreateTodoTests` to `TodoStoreTests`, which pins the schema, the FK, the CHECK constraint and the indexes), `Unit/Todos/` (`TodoInputTests`, `TodoListQueryTests`, `TodoTests`), extended `Integration/OpenApiTests.cs` and `Integration/Infrastructure/ApiFactory.cs`. AC to test mapping: `tasks.md` Traceability. Review: `review.md` (FINAL, 649/649 passing; NFR-003 measured at a 32 ms maximum with 10,000 to-dos; NFR-004 inspected). Docs: `docs/conventions.md`, `docs/architecture.md`, ADR-0006 (amended), ADR-0007, ADR-0008, ADR-0009.

Open follow-ups (none block this spec):
- Any future migration that rebuilds `Contacts` unlinks every to-do (ADR-0008); it needs a survival test and a safe technique first.
- A tool that opens the database with foreign keys off can leave dangling `contactId` values (`docs/architecture.md`, Known risks).
- The 787 mapping assumes `Todos.ContactId` is the only foreign key on `Todos`.
- `IX_Todos_IsDone_DueDate` is not seeked by the status filters (EF emits `NOT ("IsDone")`); fine at 10,000 to-dos.
- Optional code nit: `TodoListQuery` could use `TextNormalization.TrimToNull`.
- Optional test debt: shared gate helper, shared raw-SQL helpers, shared `UtcTimestamp` regex helper.
- EF Core logs an error-level entry with a stack trace for each expected 787 to 400 (and the contacts 409); consider filtering `Microsoft.EntityFrameworkCore.Update`.
- The web client (specs 005, 006) must treat `dueDate` as a plain string and never pass it through `new Date(...)`.
