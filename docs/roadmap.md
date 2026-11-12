# Roadmap / Spec backlog

Suggested order. Each line becomes one `/spec`. Keep specs small; the planner may split further.

| # | Spec | Side | Notes |
|---|---|---|---|
| 001 | Contacts API: create, get by id, list (paging + search by name/email) | API | Establishes DbContext, first migration, ProblemDetails, test factory |
| 002 | Contacts API: update and delete | API | Done (spec 002). Decision: **Unlink**. Deleting a contact keeps its to-dos and sets their `ContactId` to null |
| 003 | To-dos API: CRUD, optional contact link, due date, complete/reopen | API | Done (spec 003). Includes `GET /api/contacts/{id}/todos` and the Unlink rule from spec 002, enforced by an FK `ON DELETE SET NULL` (ADR-0008). `dueDate` is a date-only `YYYY-MM-DD` string (ADR-0007); to-do timestamps are stored as UTC ticks (ADR-0009) |
| 004 | Web: contacts list + create form | Web | Done (spec 004). Establishes the api client, MSW setup, providers and routing. Decisions: **React Router**, declarative mode (ADR-0010); plain CSS modules; search term and page held in the URL (`/contacts?search=&page=`); page size fixed at 20 |
| 005 | Web: contact detail, edit, delete, and that contact's to-dos | Web | Carry-overs from spec 004: Nit 24 (`type="email"` on the Email input; reconsider with a test, jsdom sanitizes email values); Nit 28 (add the blank-`firstName` unit row when `contactFormRules` gets an update mapper); a persistent live region in `AppLayout` if one-off notices multiply (check the post-create `status` with a screen reader); push vs replace after create (or navigate to the detail page, spec 004 Q7); disable Cancel while saving, or refresh the list after a cancelled save (Finding 31); alert styling, a shared `ErrorAlert` style (Finding 32) |
| 006 | Web: to-dos page with filters (open / done / overdue) | Web | |
| follow-up | Web: TanStack Query retry policy | Web | `main.tsx` uses the defaults (3 retries with backoff), so a failed list shows its error after about 7 s. Set `retry` with a test (for example none on 4xx, one on 5xx). Small spec or chore |
| follow-up | Chore: replace the Vite favicon | Web | `web/public/favicon.svg` is still the Vite logo. No spec needed |
| follow-up | Harness hardening | Harness | Add a Bash `PreToolUse` guard that denies `git checkout`, `restore`, `stash`, `reset` and `clean`, and writes outside a role's paths, for subagents (the T-08 incident: a destructive git command bypassed the role boundaries; `path-guard.sh` covers only the file tools). Then document it in `docs/harness-guide.md` and `docs/agent-contracts.md` |
| later | E2E smoke (Playwright), OpenAPI → TS types, auth, deployment | — | Each needs an ADR |

## Seed for spec 001
> /spec Contacts API, first slice. A contact has a first name (required), last name, email (optional but must be valid and unique if given), phone, company, and notes. I need to create a contact, get one by id, and list contacts with paging and a search that matches name or email. REST conventions per docs/conventions.md. No UI in this spec.
