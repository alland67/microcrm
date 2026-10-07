# 004: Web: contacts list and create form

**Status:** Approved (Done pending the owner's NFR-006 browser check; see review.md FINAL)
**Type:** Feature
**Author:** Allan Downs with planner
**Created:** 2026-10-06  **Approved:** 2026-10-06
**Amended:** 2026-10-06: added AC-049 (over-long search term in the address), owner decision Q-A (b), approved by the owner.
**Related:** `docs/roadmap.md` (row 004), `specs/001-contacts-api-create-get-list/spec.md` (contact fields, field rules, paging, search, 409 duplicate email, error format), `specs/002-contacts-api-update-delete/spec.md` (shared field rules), `docs/conventions.md` (Frontend and Frontend tests sections), ADR-0002 (stack), ADR-0004 (validation and error pipeline), ADR-0006 (validation message style). Followed by spec 005 (contact detail, edit, delete, and that contact's to-dos) and spec 006 (to-dos page).

## Problem
The contacts API (specs 001 and 002) works, but the only way to use it is with an HTTP client. The web app is still the Vite starter page. The CRM owner needs to browse and find contacts and add new ones from a browser. This is also the first web spec, so it sets up the shared pieces later web specs reuse: the app shell with page navigation, one place that talks to the API and turns its errors into something the UI can show, and the test setup that mocks the API.

## Goals
- The CRM owner can see contacts in a table (name, email, company, phone), one page at a time, in the API's order.
- The owner can search contacts by name or email and move between pages.
- The owner always knows what the list is doing: loading, failed (with a way to retry), empty, or showing results.
- The owner can add a contact on its own page, gets the same field rules as the API before anything is sent, sees the API's own errors next to the right field when it rejects the contact (including a duplicate email), and returns to the list after saving.
- The owner can cancel adding a contact and go back to the list.
- Later web specs can add pages and API calls without redoing the shell, error handling, or test setup.

## Non-goals / Out of scope
- Contact detail page, editing, and deleting contacts (spec 005). Rows in the list do not link anywhere yet.
- To-dos of any kind (specs 005 and 006).
- Authentication, authorization, multi-user (none in v1 per ADR-0002).
- End-to-end tests across the real API and browser (roadmap "later"; needs an ADR).
- Generating TypeScript types from the OpenAPI document (roadmap "later"; needs an ADR). Types are hand-written for now.
- Any change to the API: field rules, messages, sorting, paging, or search behavior stay exactly as in specs 001 and 002.
- Showing `notes`, `createdAt`, or `updatedAt` in the list.
- Client-selectable sort order, column sorting, or a page-size selector.
- Search on phone, company, or notes, or full-name search (the API doesn't support them).
- Warning about unsaved changes when leaving the create page.
- A UI component library, CSS framework, design system, dark mode, or responsive/mobile-specific layouts beyond the browser default.
- Production hosting concerns such as serving the SPA from the API or deep-link fallback on a production server (deployment is not decided).
- Internationalization; all text is English.

## Users & scenarios
- *As the CRM owner, I want to see my contacts in a table, so that I can scan who is in the CRM.*
- *As the CRM owner, I want to search by name or email, so that I can find one person quickly.*
- *As the CRM owner, I want to page through a long list, so that the page stays readable.*
- *As the CRM owner, I want to add a contact on a form that tells me what's wrong before and after I save, so that I don't lose what I typed.*
- *As the CRM owner, I want to be told when an email already belongs to another contact, so that I don't create duplicates.*

## Definitions and defaults used by the criteria
Defaults proposed by the planner were accepted by the owner on 2026-10-06 (resolved questions Q1 to Q8 below).

- **Contacts list page:** the page at `/contacts`. **Create page:** the page at `/contacts/new`.
- **List state in the address:** the list page's current search term and page number are part of its address (for example `/contacts?search=ada&page=2`), so reloading, sharing, or using the browser's Back button returns to the same view (Q1). Absent `page` means page 1; absent or blank `search` means no filter.
- **Page size:** fixed at 20, the API default; there is no page-size selector (Q2). Total pages = `ceil(totalCount / 20)`, minimum 1.
- **Name column:** the contact's first name and last name joined by one space; just the first name when there is no last name. Email, company, and phone are shown as returned; a `null` value shows as an empty cell.
- **Order:** rows appear in the order the API returns them (last name, then first name; spec 001 AC-022/AC-023). The web app does not re-sort.
- **Search submission:** a search runs when the owner submits the search form (Enter in the search box or the Search button), not on every keystroke (Q3). Submitting a search always goes to page 1. The term is trimmed; a blank term means "no filter".
- **Create form fields:** First name (required), Last name, Email, Phone, Company, Notes (all six API fields; Q4).
- **Client-side field rules** (mirror spec 001, measured after trimming): first name required; maximum lengths first name 100, last name 100, email 254, phone 50, company 200, notes 4000; email, when not blank, must contain exactly one `@` with at least one character on each side and no whitespace. Search term maximum 254.
- **Messages** (ADR-0006 canonical text, shown under the field): `Required.`, `Must be {max} characters or fewer.`, `Must be a valid email address.`. Server messages from a 400 `errors` dictionary are shown verbatim.
- **Duplicate email message:** `A contact with this email already exists.`, shown under the Email field. It is the web app's own text and does not depend on the API's `title` (Q5).
- **Client validation timing:** fields are validated when the owner submits the form. After a failed submit, each field's error is updated as the owner edits that field (Q6).
- **Unexpected failure:** a network failure, a 5xx response, or any response the page can't interpret (for example a 400 without an `errors` dictionary).

## Acceptance criteria
EARS format. Each must be independently testable. IDs are stable; never renumber. IDs restart at AC-001 per spec, as in specs 001 to 003. "The API" below means the mocked API in component tests.

### App shell and navigation
| ID | Criterion |
|---|---|
| AC-001 | WHEN the owner opens the app at `/` THE SYSTEM SHALL show the contacts list page, with the address changed to `/contacts`. |
| AC-002 | WHEN any page is shown THE SYSTEM SHALL show an app header containing the app name "MicroCRM" and a navigation link "Contacts" that leads to the contacts list page. |
| AC-003 | WHEN the owner opens an address that matches no page THE SYSTEM SHALL show a "Page not found" heading and a link back to the contacts list page, and SHALL NOT call the API. |
| AC-004 | WHEN the owner opens `/contacts/new` directly (not by clicking through from the list) THE SYSTEM SHALL show the create page. |

### Contacts list: rendering and states
| ID | Criterion |
|---|---|
| AC-005 | WHEN the contacts list page loads with no search or page in the address THE SYSTEM SHALL request `GET /api/contacts` for page 1 with page size 20 and no search term. |
| AC-006 | WHEN the API returns contacts THE SYSTEM SHALL show a table with column headers Name, Email, Company, and Phone, and one row per returned contact in the order returned, with cell values per Definitions (including empty cells for `null` values and a first-name-only Name when there is no last name). |
| AC-007 | WHEN the API returns contacts THE SYSTEM SHALL show the total number of matching contacts as reported by the API's `totalCount` (for example "45 contacts"; "1 contact" for one). |
| AC-008 | WHILE the list request for the current search and page has not completed, THE SYSTEM SHALL show a loading indicator announced to assistive technology (role `status`, text "Loading contacts…"), and SHALL NOT show the empty-state or error message. |
| AC-009 | WHEN the list request fails with an unexpected failure THE SYSTEM SHALL show an error message (role `alert`) saying the contacts couldn't be loaded, plus a "Try again" button, and SHALL NOT show the table or an empty-state message. |
| AC-010 | WHEN the owner activates "Try again" after a failed list request THE SYSTEM SHALL repeat the same list request (same search and page) and, if it succeeds, show the results in place of the error. |
| AC-011 | WHEN the API returns no contacts and no search term is active THE SYSTEM SHALL show the message "No contacts yet." and a link to the create page, and SHALL NOT show the table or paging controls. |
| AC-012 | WHEN the contacts list page is shown THE SYSTEM SHALL show a "New contact" link that leads to the create page. |

### Contacts list: search
| ID | Criterion |
|---|---|
| AC-013 | WHEN the owner types a term in the search box (labelled "Search contacts") and submits it THE SYSTEM SHALL request the list with that term trimmed as `search` and page 1, and show the returned contacts. |
| AC-014 | WHEN the owner submits a search while on a page other than 1 THE SYSTEM SHALL show page 1 of the new results. |
| AC-015 | WHEN the owner submits a blank or whitespace-only search term THE SYSTEM SHALL request the list without a `search` parameter (no filter) at page 1. |
| AC-016 | WHEN a search returns no contacts THE SYSTEM SHALL show the message `No contacts match "<term>".` (with the trimmed term) and a "Clear search" button, and SHALL NOT show the table or paging controls. |
| AC-017 | WHEN the owner activates "Clear search" THE SYSTEM SHALL empty the search box and show page 1 of the unfiltered list. |
| AC-018 | WHEN the owner submits a search term longer than 254 characters after trimming THE SYSTEM SHALL show the message `Must be 254 characters or fewer.` linked to the search box, and SHALL NOT send a list request for that term. |
| AC-019 | WHEN the list page is opened with a search term in the address THE SYSTEM SHALL show that term in the search box and request the list filtered by it. |
| AC-020 | WHEN the owner submits a search and the response for an earlier search or page arrives after the response for the latest one THE SYSTEM SHALL show only the results for the latest search and page. |
| AC-049 | WHEN the list page is opened with a search term in the address that is longer than 254 characters after trimming THE SYSTEM SHALL show that term in the search box with the message `Must be 254 characters or fewer.` linked to the search box, and SHALL NOT send a list request (no loading indicator, error message, empty-state message, table, or paging controls are shown). Added 2026-10-06 (Q-A). |

### Contacts list: paging
| ID | Criterion |
|---|---|
| AC-021 | WHEN the API reports more contacts than fit on one page THE SYSTEM SHALL show paging controls with "Previous" and "Next" buttons and the text "Page {page} of {totalPages}". |
| AC-022 | WHEN the owner activates "Next" THE SYSTEM SHALL request and show the next page, keeping the current search term. |
| AC-023 | WHEN the owner activates "Previous" THE SYSTEM SHALL request and show the previous page, keeping the current search term. |
| AC-024 | WHILE the first page is shown, THE SYSTEM SHALL disable "Previous"; WHILE the last page is shown, THE SYSTEM SHALL disable "Next". |
| AC-025 | WHEN all matching contacts fit on one page THE SYSTEM SHALL NOT show the paging controls. |
| AC-026 | WHEN the owner pages and then uses the browser's Back action THE SYSTEM SHALL show the previous page and search again. |
| AC-027 | WHEN the list page is opened with a `page` in the address that is not a whole number of 1 or more THE SYSTEM SHALL treat it as page 1 and SHALL NOT send an invalid `page` to the API. |
| AC-028 | WHEN the API returns no items for a page beyond the last page while `totalCount` is greater than 0 THE SYSTEM SHALL show the message "This page is empty." and a "Go to first page" button that shows page 1, instead of the "No contacts yet." or no-match message. |

### Create form: rendering and client-side validation
| ID | Criterion |
|---|---|
| AC-029 | WHEN the create page is shown THE SYSTEM SHALL show a "New contact" heading and a form with labelled fields First name, Last name, Email, Phone, Company, and Notes, a "Save" button, and a "Cancel" control, with First name marked as required. |
| AC-030 | WHEN the owner submits the form with First name empty or whitespace-only THE SYSTEM SHALL show `Required.` under First name and SHALL NOT send a create request. |
| AC-031 | WHEN the owner submits the form with any field longer than its maximum length after trimming (first name 100, last name 100, email 254, phone 50, company 200, notes 4000) THE SYSTEM SHALL show `Must be {max} characters or fewer.` under each offending field and SHALL NOT send a create request. |
| AC-032 | WHEN the owner submits the form with a non-blank email that is not valid per Definitions THE SYSTEM SHALL show `Must be a valid email address.` under Email and SHALL NOT send a create request. |
| AC-033 | WHEN the owner submits the form with several invalid fields THE SYSTEM SHALL show an error under every invalid field at once. |
| AC-034 | WHEN the owner submits fields exactly at their maximum lengths, or leaves every optional field blank, THE SYSTEM SHALL NOT show a client-side error and SHALL send the create request. |
| AC-035 | WHEN a field shows an error THE SYSTEM SHALL mark that field invalid (`aria-invalid="true"`) and link the error text to it (`aria-describedby`), so the error is part of the field's accessible description. |
| AC-036 | WHEN a submit is blocked by client-side or server-side field errors THE SYSTEM SHALL move keyboard focus to the first field, in form order, that shows an error. |
| AC-037 | WHILE a submit has failed with a field error, WHEN the owner edits that field so it satisfies its rules THE SYSTEM SHALL remove that field's error without another submit. |

### Create form: submitting and server responses
| ID | Criterion |
|---|---|
| AC-038 | WHEN the owner submits a valid form THE SYSTEM SHALL send one `POST /api/contacts` with `firstName`, `lastName`, `email`, `phone`, `company`, and `notes` taken from the form. |
| AC-039 | WHILE a create request is in progress, THE SYSTEM SHALL disable the "Save" button and show that saving is in progress, so a second click or Enter does not send a second create request. |
| AC-040 | WHEN the API responds 400 with a validation `errors` dictionary THE SYSTEM SHALL show each message verbatim under the field whose name matches the `errors` key (`firstName`, `lastName`, `email`, `phone`, `company`, `notes`), keep everything the owner typed, and stay on the create page. |
| AC-041 | WHEN the API responds 400 with an `errors` key that matches no form field THE SYSTEM SHALL show that message in a form-level error (role `alert`) rather than drop it. |
| AC-042 | WHEN the API responds 409 Conflict to a create request THE SYSTEM SHALL show `A contact with this email already exists.` under Email, mark Email invalid, keep everything the owner typed, and stay on the create page. |
| AC-043 | WHEN the create request ends in an unexpected failure THE SYSTEM SHALL show a form-level error (role `alert`) saying the contact couldn't be saved and to try again, keep everything the owner typed, re-enable "Save", and stay on the create page. |
| AC-044 | WHEN the owner submits again after a failed create THE SYSTEM SHALL clear the previous server errors before showing the outcome of the new attempt. |
| AC-045 | WHEN the API responds 201 Created THE SYSTEM SHALL navigate to the contacts list page (page 1, no search) and show a confirmation (role `status`) naming the new contact, for example `Contact "Ada Lovelace" created.` |
| AC-046 | WHEN the owner returns to the list after a successful create THE SYSTEM SHALL fetch the list from the API again rather than show a list cached before the create, so a new contact that belongs on page 1 appears in the table. |

### Create form: cancel
| ID | Criterion |
|---|---|
| AC-047 | WHEN the owner activates "Cancel" on the create page THE SYSTEM SHALL navigate to the contacts list page without sending a create request, whether or not the form has input. |
| AC-048 | WHEN the owner cancels and later opens the create page again THE SYSTEM SHALL show an empty form with no errors. |

## Non-functional requirements
| ID | Requirement | How verified |
|---|---|---|
| NFR-001 | Every AC above is covered by a component test that renders through the shared test providers and mocks HTTP at the network level (MSW), never by mocking `fetch` or the data hooks; error responses in tests are shaped as ProblemDetails (`application/problem+json`). | Reviewer checks tests against `docs/conventions.md` (Frontend tests). |
| NFR-002 | Every interactive element and message used in the ACs can be found by role, label, or visible text (no test ids needed): form fields by label, buttons and links by name, table by role `table` with column headers, messages by role `status`/`alert` or text. | Component tests query only by role, label, or text. |
| NFR-003 | All HTTP calls go through the single shared API client with relative `/api/...` URLs; no component builds fetch calls itself. | Reviewer inspection. |
| NFR-004 | Lint, typecheck, frontend tests, and the production build pass (`LINT_CMD`, `TYPECHECK_CMD`, `FRONTEND_TEST_CMD`, `BUILD_CMD` in `.claude/harness.env`). The Vite starter content (counter, logos, starter links) is gone from the app. | Commands run in each task's evidence. |
| NFR-005 | No contact field values are written to the browser console or logs by the app. | Reviewer inspection. |
| NFR-006 | Manual smoke check against the real API (`DEV_API_CMD` + `DEV_WEB_CMD`): create a contact, see it in the list, search for it, trigger a 409 by creating the same email again. | Recorded in the final review; not a CI gate. |

## Constraints & assumptions
- Follows `docs/conventions.md` Frontend and Frontend tests sections: TanStack Query for server state, `useState` for local UI state, no global store, Vitest + Testing Library + user-event + MSW, tests co-located and named with the AC ID.
- Styling is plain CSS with no UI library (user decision). Conventions say "plain CSS modules"; CSS modules are plain CSS and need no new dependency, so the two agree. The plan picks the exact file layout.
- **Routing needs a new dependency.** The web app has no router today, and this spec needs addresses for the list and create pages, address-held list state, Back behavior, and a not-found page. The owner chose React Router (`react-router`, declarative mode) on 2026-10-06 (Q9); the plan records it with a plan entry and an ADR (ADR-0002, constitution §5).
- The API is the source of truth. Client-side rules exist to give fast feedback; the form must still handle a 400 from the API for any field (AC-040), since the two can drift.
- The API's contact field rules, messages, sort order, and paging rules are those of specs 001/002 and ADR-0006 as shipped. The canonical messages match the API's current text, so client and server errors read the same.
- Single user, no auth. The web app runs behind the Vite dev proxy for `/api` (architecture.md).
- No timestamps are displayed in this spec, so no date formatting rules are needed yet.

## Open questions
None.

### Resolved (accepted by the owner on 2026-10-06, all as recommended)
- [x] Q1: The list's search term and page number live in the address (`/contacts?search=ada&page=2`), so reload, sharing, and Back restore the view (AC-019, AC-026, AC-027).
- [x] Q2: Page size is fixed at 20 (the API default), with no selector.
- [x] Q3: Search runs only on submit (Enter or the Search button), not while typing (AC-013). AC-020 (late responses) still applies.
- [x] Q4: The create form includes Notes: all six API fields (AC-029).
- [x] Q5: The duplicate-email message is the web app's own fixed text `A contact with this email already exists.`, not the API's 409 `title` (AC-042).
- [x] Q6: Fields are validated on submit; after a failed submit, each field's error updates as it is edited (AC-037).
- [x] Q7: After a successful create, go to page 1 of the list (no search) and show a confirmation naming the new contact (AC-045, AC-046). The new contact appears in the table only if it sorts onto page 1. Spec 005 may later go to the new contact's detail page instead.
- [x] Q8: The not-found page (AC-003) and the `/` to `/contacts` redirect (AC-001) stay in this spec.
- [x] Q9: Routing uses React Router (`react-router`, declarative mode: `<Routes>`, `useSearchParams`, `useNavigate`, `MemoryRouter` in tests). Owner decision on 2026-10-06; the plan adds the dependency with an ADR.
- [x] Q-A: A search term longer than 254 characters after trimming that arrives in the address (a hand-edited or shared link) is handled like AC-018: show the 254-character message on the search box and send no list request, rather than sending it and showing the load error from the API's 400. Owner chose (b) on 2026-10-06 (AC-049).

## Implementation notes
Modules (under `web/src/`):
- `App.tsx`: the one `<Routes>` (`/` redirects to `/contacts`, `contacts`, `contacts/new`, `*` not found) inside the `components/AppLayout.tsx` layout route; `main.tsx`: `BrowserRouter` + `QueryClientProvider` (TanStack defaults)
- `api/client.ts` (`apiGet`, `apiPost`, `ApiError` with kind `validation` | `conflict` | `unexpected`), `api/types.ts` (DTOs)
- `features/contacts/`: `ContactsPage`, `ContactsTable`, `ContactSearchForm`, `NewContactPage`, `ContactForm`; hooks `useContacts.ts` (`useContactsList`, `useCreateContact`, `contactKeys`, `CONTACTS_PAGE_SIZE` = 20); pure rules `listParams.ts` (address <-> params), `contactFormRules.ts`, `contactFormErrors.ts`, `contactName.ts`
- `components/` (`AppLayout`, `Pagination`, `NotFoundPage`, `FieldError.module.css`), `lib/validationMessages.ts` (ADR-0006 texts, once)

Tests: co-located `*.test.ts(x)` (13 files, 301 tests); helpers in `web/src/test/`. AC to test mapping: `tasks.md` Traceability. Review: `review.md` (FINAL: APPROVE).

Open follow-ups (none block this spec; recorded in `docs/roadmap.md`):
- Cancel stays enabled while saving (Finding 31); alert styling (Finding 32); `type="email"` on the Email input (Nit 24); blank-`firstName` unit row (Nit 28); persistent live region; push vs replace after create.
- TanStack default retry (about 7 s before the list error); Vite favicon; `path-guard.sh` gap for Bash `git checkout`/`restore`.
- The owner's manual browser check (FINAL review, steps 1 to 7) was pending at review time.
