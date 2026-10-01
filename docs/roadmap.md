# Roadmap / Spec backlog

Suggested order. Each line becomes one `/spec`. Keep specs small; the planner may split further.

| # | Spec | Side | Notes |
|---|---|---|---|
| 001 | Contacts API: create, get by id, list (paging + search by name/email) | API | Establishes DbContext, first migration, ProblemDetails, test factory |
| 002 | Contacts API: update and delete | API | Decide what deleting a contact does to its to-dos (ask in /spec) |
| 003 | To-dos API: CRUD, optional contact link, due date, complete/reopen | API | Includes `GET /api/contacts/{id}/todos` |
| 004 | Web: contacts list + create form | Web | Establishes api client, MSW setup, providers, routing |
| 005 | Web: contact detail, edit, delete, and that contact's to-dos | Web | |
| 006 | Web: to-dos page with filters (open / done / overdue) | Web | |
| later | E2E smoke (Playwright), OpenAPI → TS types, auth, deployment | — | Each needs an ADR |

## Seed for spec 001
> /spec Contacts API, first slice. A contact has a first name (required), last name, email (optional but must be valid and unique if given), phone, company, and notes. I need to create a contact, get one by id, and list contacts with paging and a search that matches name or email. REST conventions per docs/conventions.md. No UI in this spec.
