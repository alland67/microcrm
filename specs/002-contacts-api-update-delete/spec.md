# 002: Contacts API: update and delete

**Status:** Approved
**Type:** Feature
**Author:** Allan Downs with planner
**Created:** 2026-10-02  **Approved:** 2026-10-02
**Related:** `docs/roadmap.md` (row 002), `specs/001-contacts-api-create-get-list/spec.md` (field rules, error format, read endpoints), `docs/conventions.md` (REST shape: full-replace update, delete, ProblemDetails, no concurrency in v1), ADR-0002, ADR-0003 (case-insensitive text), ADR-0004 (validation and error pipeline). Followed by spec 003 (to-dos), which must enforce the delete rule in "Decision: deleting a contact and its to-dos".

## Problem
After spec 001, the CRM owner can create and read contacts but can't correct a typo, record a new email address, or remove someone who no longer belongs in the CRM. Records go stale and duplicates pile up with no way to clean them. The web UI's contact detail page (spec 005) needs edit and delete. Spec 003 (to-dos) also needs a settled rule for what happens to a contact's to-dos when the contact is deleted, so this spec records that decision before to-dos exist.

## Goals
- A client can replace all editable fields of an existing contact in one request and get the updated contact back.
- Updates follow exactly the same field rules as create (spec 001): trimming, empty optional to `null`, maximum lengths, email validity, and case-insensitive email uniqueness.
- A contact's identity and creation time never change. Its last-modified time reflects the latest update.
- A client can permanently delete a contact. Afterwards it can't be read, doesn't appear in the list or search, and its email can be used again.
- Errors use the project's standard ProblemDetails format, as in spec 001.
- Validation messages follow one style on every contacts endpoint, so the UI can show them next to the field as they are.

## Non-goals / Out of scope
- Partial updates (PATCH or "only send the fields you change"). `PUT` is a full replace per conventions.
- Creating a contact through `PUT` to an unknown id (no upsert).
- Optimistic concurrency, ETags, `If-Match`, or lost-update detection. Concurrent updates to the same contact are last-write-wins (conventions: no concurrency in v1).
- Soft delete, undo/restore, trash, or archive. Delete is a hard delete.
- Bulk update or bulk delete.
- Audit history or change log of edits.
- To-dos and their behavior. The delete-to-to-dos rule is recorded here as a decision, but it's implemented and tested in spec 003.
- Any web UI (spec 005).
- Authentication and authorization (none in v1 per ADR-0002).
- Any change to the field rules, list ordering, search, or paging defined in spec 001. The only spec 001 change is validation message wording, aligned to one style (AC-039).

## Users & scenarios
- *As the CRM owner, I want to edit a contact's details, so that I can fix mistakes and keep information current.*
- *As the CRM owner, I want to clear a field (for example remove an old phone number), so that the contact doesn't show outdated data.*
- *As the CRM owner, I want the system to stop me from giving a contact an email that another contact already has, so that I don't end up with duplicates.*
- *As the CRM owner, I want to fix the capitalization of a contact's own email without the system calling it a duplicate of itself.*
- *As the CRM owner, I want to delete a contact I no longer need, so that my list stays clean, and I want to be able to re-add that person later with the same email.*
- *As a client developer (the future web UI), I want update and delete to use the same status codes and ProblemDetails shape as create and read, so that I can reuse my error handling.*

## Definitions and defaults used by the criteria
The planner proposed these defaults, and the user accepted them on 2026-10-02 (Open questions Q1 to Q6, all resolved).

- **Editable fields:** `firstName` (required), `lastName`, `email`, `phone`, `company`, `notes`. These are the same fields create accepts.
- **Field rules:** identical to spec 001's Definitions: trimming, empty or whitespace-only optional field becomes `null`, maximum lengths after trimming (firstName 100, lastName 100, email 254, phone 50, company 200, notes 4000), the valid-email rule, and email stored with the casing sent.
- **Full replace** (Q3): an update request describes the whole editable state of the contact. Any optional field that is missing from the body, `null`, empty, or whitespace-only is stored as `null`, whatever its previous value. Fields are never "kept because omitted".
- **System fields:** `id` and `createdAt` never change on update. Any `id`, `createdAt`, or `updatedAt` in the request body is ignored. The id in the URL identifies the contact.
- **`updatedAt` on update:** set to the current UTC time from the system clock on every successful update, even if no field value changed (Q1).
- **Email uniqueness on update:** the new email conflicts only when another contact (not the one being updated) has an email equal to it after trimming and ignoring letter case. Contacts without an email never conflict. Case-insensitivity is guaranteed for ASCII letters only, as in spec 001.
- **Check order for update** (Q2): (1) an unreadable body gives 400; (2) field validation errors give 400; (3) an unknown contact gives 404; (4) an email conflict gives 409. So an invalid body sent to an unknown id gets 400, and a valid body with a conflicting email sent to an unknown id gets 404.
- **No upsert** (Q5): `PUT` to an id that matches no contact never creates one; it responds 404.
- **Validation message style** (Q4): every message in a 400 validation `errors` entry, on every contacts endpoint (create, update, list), is a sentence in sentence case. It starts with an uppercase letter, ends with a period, and doesn't name or quote the field or parameter, because the `errors` key already identifies it. Examples: `"Required."`, `"Must be 100 characters or fewer."`, `"Must be a valid email address."`, `"Must be an integer between 1 and 2147483647."`, `"Must be 254 characters or fewer."`. The rule is recorded in `docs/conventions.md` (Errors row). Spec 001 messages that break it are aligned: create's `"First name is required."` becomes `"Required."`, and the list query messages `"'page' must be an integer between 1 and 2147483647."`, `"'pageSize' must be an integer between 1 and 100."` and `"'search' must be at most 254 characters."` lose the quoted name and become sentence case. Only message text changes; status codes and `errors` keys stay the same. The plan schedules that alignment as a refactor task.
- **Delete:** permanent removal of the contact record. The request body, if any, is ignored.
- **Concurrency:** no ETags. Two updates to the same contact are last-write-wins. Races on email uniqueness and update/delete races must never produce a 500 or break the uniqueness rule.

## Decision: deleting a contact and its to-dos
**User decision (2026-10-02): Unlink.** When a contact is deleted, every to-do linked to that contact is kept, and its contact link is cleared (set to `null`), so it becomes an unlinked to-do. To-dos are never deleted as a side effect of deleting a contact, and a contact with to-dos can be deleted (deletion is never blocked because of to-dos). The link is a plain `null`; no placeholder or "deleted contact" record is used (Q6). The rule applies to every path that deletes a contact, including any bulk or nested operations spec 003 or later specs add.

To-dos don't exist yet, so this spec has no ACs that need them. **Spec 003 must** include acceptance criteria that enforce and test this rule. At minimum:
- after deleting a contact, its former to-dos still exist and are returned with a `null` contact link;
- the delete still returns 204 when the contact has to-dos;
- to-dos linked to other contacts are unaffected;
- delete and unlink happen together: either the contact is removed and all its to-dos are unlinked, or nothing changes;
- any bulk or nested operation that deletes contacts applies the same rule.

## Acceptance criteria
EARS format. Each must be independently testable. IDs are stable; never renumber.

### Update: success
| ID | Criterion |
|---|---|
| AC-001 | WHEN a client sends `PUT /api/contacts/{id}` for an existing contact with a valid body THE SYSTEM SHALL respond 200 with a body of the same shape as spec 001's AC-017 (`id`, `firstName`, `lastName`, `email`, `phone`, `company`, `notes`, `createdAt`, `updatedAt`) holding the updated values. |
| AC-002 | WHEN a contact has been updated THE SYSTEM SHALL return the updated values from later `GET /api/contacts/{id}` requests and in list results, including on new connections (the change is persisted). |
| AC-003 | WHEN a client updates a contact with a body that omits an optional field, or sets it to `null`, an empty string, or whitespace-only THE SYSTEM SHALL store and return that field as `null`, even if it previously had a value. |
| AC-004 | WHEN a client updates a contact with text fields that have leading or trailing whitespace THE SYSTEM SHALL store and return those fields trimmed. |
| AC-005 | WHEN a client updates a contact THE SYSTEM SHALL keep the contact's `id` and `createdAt` unchanged, ignoring any `id`, `createdAt`, or `updatedAt` in the request body, including an `id` that differs from the URL. |
| AC-006 | WHEN a client updates a contact THE SYSTEM SHALL set `updatedAt` to the current UTC time from the system clock, even when the submitted values equal the stored values. |
| AC-007 | WHEN a client updates a contact with a field exactly at its maximum length after trimming THE SYSTEM SHALL accept it and respond 200. |
| AC-008 | WHEN a client updates a contact so its last or first name changes THE SYSTEM SHALL place the contact in list results by the new values, following spec 001's ordering (AC-022, AC-023). |
| AC-009 | WHEN a client updates a contact's email THE SYSTEM SHALL make the contact findable by search on the new email and no longer findable on the old email (unless the old value still matches another field). |
| AC-010 | WHEN a client updates a contact with an email containing uppercase letters THE SYSTEM SHALL store and return the email with the casing as sent, only trimmed. |
| AC-011 | WHEN a client updates a contact THE SYSTEM SHALL leave every other contact unchanged. |

### Update: email uniqueness
| ID | Criterion |
|---|---|
| AC-012 | WHEN a client updates a contact with the same email it already has (equal ignoring case and surrounding whitespace) THE SYSTEM SHALL accept the update and respond 200 (a contact never conflicts with itself). |
| AC-013 | WHEN a client updates a contact changing only the letter case of its own email (e.g. `ada@example.com` to `Ada@Example.com`) THE SYSTEM SHALL respond 200 and store and return the new casing. |
| AC-014 | WHEN a client updates a contact with an email that matches a different contact's email ignoring case and surrounding whitespace THE SYSTEM SHALL respond 409 Conflict with a ProblemDetails body and SHALL leave the contact being updated unchanged. |
| AC-015 | WHEN a client updates a contact to have no email THE SYSTEM SHALL accept it, even if other contacts also have no email. |
| AC-016 | WHEN a contact's email has been changed or removed by an update THE SYSTEM SHALL allow a different contact to be created or updated with the old email. |

### Update: invalid requests
| ID | Criterion |
|---|---|
| AC-017 | WHEN a client updates a contact with the first name missing, `null`, empty, or whitespace-only THE SYSTEM SHALL respond 400 with a validation ProblemDetails whose `errors` contains a `firstName` entry, and SHALL leave the contact unchanged. |
| AC-018 | WHEN a client updates a contact with any field longer than its maximum length after trimming THE SYSTEM SHALL respond 400 with a validation ProblemDetails whose `errors` contains an entry for each offending field, and SHALL leave the contact unchanged. |
| AC-019 | WHEN a client updates a contact with a non-empty email that is not a valid email (per spec 001 Definitions) THE SYSTEM SHALL respond 400 with a validation ProblemDetails whose `errors` contains an `email` entry, and SHALL leave the contact unchanged. |
| AC-020 | WHEN an update request has multiple invalid fields THE SYSTEM SHALL report all of them in a single 400 response's `errors`, keyed by camelCase field name. |
| AC-021 | WHEN a client sends an update request whose body is not valid JSON, is empty, or has a field of the wrong JSON type THE SYSTEM SHALL respond 400 with a ProblemDetails body and SHALL leave the contact unchanged. |
| AC-022 | WHEN a client sends `PUT /api/contacts/{id}` with a valid body for a well-formed GUID that matches no contact THE SYSTEM SHALL respond 404 with a ProblemDetails body and SHALL NOT create a contact. |
| AC-023 | WHEN a client sends `PUT /api/contacts/{id}` with an id that is not a GUID THE SYSTEM SHALL respond 404 and SHALL NOT create or change any contact. |
| AC-024 | WHEN a client sends an update with field validation errors to a well-formed GUID that matches no contact THE SYSTEM SHALL respond 400 (validation is checked before existence). |
| AC-025 | WHEN a client sends an update with a valid body whose email belongs to another contact to a well-formed GUID that matches no contact THE SYSTEM SHALL respond 404 (existence is checked before email conflicts). |

### Delete
| ID | Criterion |
|---|---|
| AC-026 | WHEN a client sends `DELETE /api/contacts/{id}` for an existing contact THE SYSTEM SHALL respond 204 No Content with an empty body. |
| AC-027 | WHEN a contact has been deleted THE SYSTEM SHALL respond 404 with a ProblemDetails body to `GET /api/contacts/{id}` for that id, including on new connections (the removal is persisted). |
| AC-028 | WHEN a contact has been deleted THE SYSTEM SHALL exclude it from list and search results, and `totalCount` SHALL no longer count it. |
| AC-029 | WHEN a contact with an email has been deleted THE SYSTEM SHALL allow a new contact to be created, or another contact to be updated, with that email (ignoring case). |
| AC-030 | WHEN a client sends `DELETE /api/contacts/{id}` for a well-formed GUID that matches no contact, including one already deleted THE SYSTEM SHALL respond 404 with a ProblemDetails body. |
| AC-031 | WHEN a client sends `DELETE /api/contacts/{id}` with an id that is not a GUID THE SYSTEM SHALL respond 404 and SHALL NOT delete any contact. |
| AC-032 | WHEN a contact is deleted THE SYSTEM SHALL leave every other contact unchanged. |
| AC-033 | WHEN a contact has been deleted THE SYSTEM SHALL respond 404 with a ProblemDetails body to `PUT /api/contacts/{id}` with a valid body for that id, and SHALL NOT recreate it. |

### Concurrency
| ID | Criterion |
|---|---|
| AC-034 | WHEN two requests that would give two different contacts the same email (ignoring case) are processed concurrently, in any mix of create and update, THE SYSTEM SHALL let at most one of them hold that email, and SHALL respond 409 Conflict ProblemDetails (not a 500) to the other. |
| AC-035 | WHEN an update and a delete of the same contact are processed concurrently THE SYSTEM SHALL respond to each with one of its documented status codes (update: 200 or 404; delete: 204 or 404), never a 500, and, if the delete responded 204, a later `GET /api/contacts/{id}` SHALL respond 404 (the update never recreates the contact). |
| AC-036 | WHEN two deletes of the same contact are processed concurrently THE SYSTEM SHALL respond 204 to exactly one and 404 to the other. |

### Errors
| ID | Criterion |
|---|---|
| AC-037 | WHEN the system returns any 4xx or 5xx error from the update or delete endpoints THE SYSTEM SHALL use content type `application/problem+json` with at least `type`, `title`, and `status` members. |
| AC-038 | WHEN an unexpected server error occurs while handling an update or delete request THE SYSTEM SHALL respond 500 with a ProblemDetails body that contains no exception message, type name, or stack trace, and SHALL leave stored data unchanged. |

### Validation message style
| ID | Criterion |
|---|---|
| AC-039 | WHEN any contacts endpoint (create, update, or list) responds 400 with a validation ProblemDetails THE SYSTEM SHALL make every message in `errors` a sentence that starts with an uppercase letter, ends with a period, and doesn't contain the field or parameter name (camelCase key or quoted form). For example, a missing first name gives `"Required."` and `page=0` gives `"Must be an integer between 1 and 2147483647."`. |

## Non-functional requirements
| ID | Requirement | How verified |
|---|---|---|
| NFR-001 | The update and delete endpoints appear in the OpenAPI document at `/openapi/v1.json` in Development, each with a name and summary. Their documented response status codes include those in the ACs (update: 200/400/404/409; delete: 204/404). | Integration test fetching and inspecting the OpenAPI document. |
| NFR-002 | JSON property names are camelCase, `null` values are serialized (never omitted), and timestamps are ISO-8601 with a UTC offset, the same as spec 001 NFR-002. | Integration tests asserting raw JSON of the update response. |
| NFR-003 | No request body values (including email addresses) and no stored contact field values are written to logs at Information level or above by the update or delete paths. | Reviewer inspection of logging code. |
| NFR-004 | The 400 validation messages for update are identical to those for create for the same invalid input (one source of field rules). | Integration or unit test comparing create and update error dictionaries for the same invalid payload. |

## Constraints & assumptions
- Follows `docs/conventions.md`: `PUT /api/contacts/{id}` is a full replace returning 200 and the resource or 404; `DELETE /api/contacts/{id}` returns 204 or 404; ProblemDetails errors; camelCase JSON; `TimeProvider`-sourced UTC timestamps; no concurrency control in v1.
- Reuses spec 001's field rules, error pipeline (ADR-0004), and case-insensitive text handling (ADR-0003). This spec doesn't redefine them; if spec 001's rules change, update follows.
- Single-user, no auth (ADR-0002). Any caller can update or delete any contact.
- The response shape is unchanged from spec 001; `updatedAt` already exists for this purpose.
- To-dos don't exist yet. The delete rule above binds spec 003. Until then, deleting a contact removes only the contact.
- Last-write-wins for concurrent updates to the same contact is accepted for v1 and isn't tested beyond "no 500" (AC-035).
- The planner expects no schema change; the existing unique email constraint (spec 001) also guards updates.
- AC-039 changes only the wording of existing spec 001 validation messages (create and list). Status codes, `errors` keys, and spec 001's ACs stay the same, and no existing test checks message text. As part of this spec, `docs/conventions.md` (Errors row) records the message style. The plan schedules aligning the existing messages as a refactor task.

## Open questions
None open. The user accepted every recommendation on 2026-10-02 ("accept all recommendations"), and the outcomes are folded into Definitions, the Decision section, and the ACs.

- [x] Q1: `updatedAt` on a no-op update. **Resolved: (a)** set it on every successful `PUT`, even when no value changes (AC-006). Rejected: (b) change it only when a stored value changes.
- [x] Q2: Check order for `PUT`. **Resolved: (a)** unreadable body 400, then field validation 400, then existence 404, then email conflict 409 (AC-021, AC-024, AC-025). Rejected: (b) existence before validation.
- [x] Q3: Missing optional fields on `PUT`. **Resolved: (a)** a missing optional field becomes `null` (true full replace) (AC-003). Rejected: (b) 400 on missing fields; (c) keep the old value.
- [x] Q4: Validation message style. **Resolved: (a)** sentence-case messages that don't name the field, on every contacts endpoint (create, update, list). Tested by AC-039. The rule goes into `docs/conventions.md`, and the existing create and list messages are aligned in a refactor task scheduled by the plan. Rejected: (b) quoted field-name prefix; (c) leave as is.
- [x] Q5: `PUT` to a non-existent id. **Resolved: (a)** 404, no upsert (AC-022, AC-033). Rejected: (b) create with the client-supplied id.
- [x] Q6: Shape and reach of the Unlink rule. **Resolved: yes.** The to-do link becomes plain `null` with no placeholder contact, and the rule applies to every contact-deleting path in spec 003 and later, including bulk and nested operations (see "Decision: deleting a contact and its to-dos").

## Implementation notes
_Added by documenter after completion: links to main modules and tests._
