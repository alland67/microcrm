# 004: Web: contacts list and create form: Tasks

Legend: `[ ]` todo · `[~]` in progress · `[x]` done
Each task is one red → green → refactor cycle and leaves the suite green. Every task is **web-side only**:
- Run a single file with `TEST_ONE_FRONTEND_CMD`.
- Run the suite with `FRONTEND_TEST_CMD`.
- Every task's evidence also includes `LINT_CMD`, `TYPECHECK_CMD`, and `BUILD_CMD` (NFR-004).

Paths below are relative to `web/src/`. `C/` = `web/src/features/contacts/`.

**Read the plan first.** `plan.md` defines the signatures, the exact UI text and roles, the list rendering order, the form behavior, and the test utilities these tasks rely on. Test names end with the AC ID, for example `it('shows "No contacts yet." with a link to the create page [AC-011]')`.

**Test rules (NFR-001, NFR-002):**
- Page tests render `<App />` through `renderWithProviders(<App />, { route })`.
- HTTP is mocked only with MSW, through `server.use(...)` per test, with ProblemDetails bodies from `test/msw/problems.ts`.
- Queries go by role, label, or text only.
- "SHALL NOT send" assertions use `recordRequests()`. `onUnhandledRequest: 'error'` stays on.

**Guard tests.** A guard is a test expected to **pass at RED**, because an earlier task already produces the behavior. Guards are listed under their task. The test-writer reports guards in the RED evidence, separately from the failing tests. Every task has at least one test that fails for the right reason.

**Minimal implementation.** Implementers add only what the task's failing tests demand. In particular:
- No paging controls before T-07, and no "empty page" state before T-08.
- No search UI before T-05.
- No `POST` from the form before T-11.
- No navigation after a create before T-12.
- No server-error display before T-13, and no unexpected-error form message before T-14.
- No `console.*` anywhere (NFR-005).
- No `fetch` outside `api/client.ts` (NFR-003).

**Dependency.** `react-router` is added only by the T-02 pre-RED chore (ADR-0010). Any other new package means stop and report `BLOCKED`.

---

- [ ] T-01: API client with typed ProblemDetails errors, plus the MSW test harness
  - **ACs:** foundation for AC-009, AC-040, AC-041, AC-042, AC-043; NFR-003, NFR-005
  - **Depends on:** none
  - **Tests (test-writer):**
    - `test/setup.ts` (modified): MSW `listen({ onUnhandledRequest: 'error' })`, `resetHandlers` after each test, `close` after all. Keep `cleanup`.
    - `test/msw/server.ts`, `test/msw/handlers.ts` (new). The default handlers may be empty in this task.
    - `test/msw/problems.ts` (new): `problemJson`, `validationProblem`, `conflictProblem`.
    - `api/client.test.ts` (new):
      - `apiGet` returns the parsed JSON body for a 200 from a relative `/api/...` URL.
      - `apiPost` sends a JSON body with `Content-Type: application/json` and returns the parsed 201 body.
      - A 400 with an `errors` dictionary rejects with `kind: 'validation'`, `status: 400`, and `fieldErrors` equal to the dictionary, keys unchanged.
      - A 400 without `errors`, and a 400 whose `errors` isn't a string-array dictionary, reject with `kind: 'unexpected'`.
      - A 409 problem rejects with `kind: 'conflict'`, whatever its `title`.
      - A 500 problem, a 404 problem, and a 502 with a `text/plain` body reject with `kind: 'unexpected'` and that status.
      - `HttpResponse.error()` (network failure) rejects with `kind: 'unexpected'` and `status: null`.
      - A 200 with a non-JSON body rejects with `kind: 'unexpected'`.
      - An aborted request rejects with an `AbortError`, not an `ApiError`.
      - NFR-005: an error from a POST whose body contains `ada@example.com` has a `message` that doesn't contain it.
      - A path not starting with `/api/` throws.
    - `test/smoke.test.tsx` still passes.
  - **Likely source files:** `api/client.ts`
  - **Done when:** `client.test.ts` passes. The suite, lint, typecheck, and build are green. `ApiError` and the functions match the plan's signatures.

- [ ] T-02: App shell, routes, test providers, starter removed
  - **Pre-RED chore (implementer, before the test-writer starts):** `npm --prefix web install react-router@^8.4.0`. Commit as `chore(004): add react-router dependency (ADR-0010) [T-02]`. The suite must stay green.
  - **ACs:** AC-001, AC-002, AC-003, AC-004; NFR-004 (starter gone)
  - **Depends on:** T-01
  - **Tests (test-writer):**
    - `test/renderWithProviders.tsx` (new): fresh `QueryClient` (`retry: false`), `MemoryRouter`, probe with `getLocation()` and `goBack()`, and `user` (plan signature).
    - `test/msw/requests.ts` (new): `recordRequests()`.
    - `App.test.tsx` (new):
      - `/` shows the heading `Contacts` and `getLocation().pathname === '/contacts'` [AC-001].
      - On `/contacts`, `/contacts/new`, and `/nope`, the `banner` contains `MicroCRM`, and the `Main` navigation has a `Contacts` link. Clicking it from `/contacts/new` shows the list page [AC-002].
      - `/nope` and `/contacts/123/whatever` show the heading `Page not found` and a `Go to contacts` link that leads to the list. `recordRequests()` stays empty [AC-003].
      - Opening `/contacts/new` directly shows the heading `New contact` [AC-004].
  - **Likely source files:**
    - `App.tsx` (replaced), `main.tsx`
    - `components/AppLayout.tsx` (+ `.module.css`), `components/NotFoundPage.tsx`
    - `C/ContactsPage.tsx` and `C/NewContactPage.tsx`: heading-only stubs. The list stub must **not** fetch yet.
    - `index.css`, `index.html`
    - Deleted: `App.css`, `assets/*`, `public/icons.svg`
  - **Done when:** the four ACs pass. No starter counter, logos, or links remain in `web/src` or `index.html`. The suite, lint, typecheck, and build are green.

- [ ] T-03: Contacts list: request and table
  - **ACs:** AC-005, AC-006, AC-007, AC-012
  - **Depends on:** T-02
  - **Tests (test-writer):**
    - `test/msw/fixtures.ts` (new): `aContact`, `contactsPage`.
    - `test/msw/handlers.ts` (modified): default `GET /api/contacts` (empty page that echoes page and pageSize) and default `POST /api/contacts` (201 echo).
    - `C/contactName.test.ts` (new): `"Ada Lovelace"`; `"Cher"` when `lastName` is null [AC-006].
    - `C/ContactsPage.test.tsx` (new):
      - At `/contacts`, exactly one GET is sent, with `page=1`, `pageSize=20`, and no `search` param [AC-005].
      - The `Contacts` table has column headers `Name`, `Email`, `Company`, `Phone` in that order, and one row per contact in the returned order (deliberately not alphabetical, to prove there's no re-sort). A contact with null email, company, phone, and last name shows empty cells and a first-name-only Name [AC-006].
      - `45 contacts` for `totalCount` 45, and `1 contact` for 1 [AC-007].
      - A `New contact` link leads to `/contacts/new` [AC-012].
  - **Likely source files:** `api/types.ts`, `C/useContacts.ts`, `C/ContactsPage.tsx`, `C/ContactsTable.tsx`, `C/contactName.ts`
  - **Done when:** the ACs pass, and the list renders through `useContactsList` → `apiGet` with query key `['contacts', { page, search }]`. The suite, lint, typecheck, and build are green.

- [ ] T-04: Contacts list: loading, error with retry, empty
  - **ACs:** AC-008, AC-009, AC-010, AC-011
  - **Depends on:** T-03
  - **Tests (test-writer):**
    - `test/deferred.ts` (new).
    - `C/ContactsPage.test.tsx` (extended):
      - While the GET is held, `status` `Loading contacts…` shows, with no `alert`, no `No contacts yet.`, and no table [AC-008].
      - A 500 problem, and separately a network error, each show an `alert` with `Couldn't load contacts.` and a `Try again` button, with no table and no empty message [AC-009].
      - `Try again` after a 500: the second request has the same query as the first (`page=1&pageSize=20`; T-05 and T-07 add search and page to the address later, and that test file then covers the same query with them). While it's held, the loading status shows and the alert is gone. When it succeeds, the table replaces the error [AC-010].
      - `totalCount` 0 with no search shows `No contacts yet.` and an `Add a contact` link to `/contacts/new`, with no table and no `Pagination` navigation [AC-011].
  - **Likely source files:** `C/ContactsPage.tsx` (+ `.module.css`)
  - **Done when:** the ACs pass, and the state order matches the plan's rendering rules (loading checks `isFetching` before error). The suite, lint, typecheck, and build are green.

- [ ] T-05: Search: submit, address, late responses
  - **ACs:** AC-013, AC-014, AC-015, AC-019, AC-020
  - **Depends on:** T-04
  - **Tests (test-writer):** `C/ContactsPage.search.test.tsx` (new):
    - Typing `  ada  ` in `Search contacts` and pressing Enter, and separately clicking `Search`: the request has `search=ada` and `page=1`, the address is `/contacts?search=ada`, and the returned rows show [AC-013].
    - Starting at `/contacts?page=3`, submitting `ada` requests page 1 and shows page 1 results. The address has no `page` [AC-014].
    - Starting at `/contacts?search=ada&page=2`, submitting `   ` requests `page=1` with **no** `search` param, and the address is `/contacts` [AC-015].
    - Opening `/contacts?search=lovelace` shows `lovelace` in the search box, and the first request has `search=lovelace` [AC-019].
    - Search `a` (held), then search `b` (answered). Release `a`. Only `b`'s rows are ever shown afterwards, and the box shows `b` [AC-020].
    - Typing without submitting sends no request (Q3) [AC-013].
  - **Likely source files:** `C/listParams.ts` (`readListParams`/`toSearchParams`, search part), `C/ContactSearchForm.tsx`, `C/ContactsPage.tsx`
  - **Done when:** the ACs pass. The search box is keyed by the address term, so it resets when the address changes. The suite, lint, typecheck, and build are green.

- [ ] T-06: Search: no match, clear, too long (submitted or in the address)
  - **ACs:** AC-016, AC-017, AC-018, AC-049
  - **Depends on:** T-05
  - **Tests (test-writer):**
    - `C/listParams.test.ts` (new): `readListParams` gives `searchTooLong: false` for a 254-character term and for a 254-character term with surrounding spaces, and `true` for 255 characters after trimming. `params.search` is the trimmed term in each case [AC-049].
    - `C/ContactsPage.search.test.tsx` (extended):
      - A search for ` zed ` that returns `totalCount` 0 shows `No contacts match "zed".` and a `Clear search` button, with no table, no Pagination, and no `No contacts yet.` [AC-016].
      - `Clear search` empties the box, the address becomes `/contacts`, and the next request has page 1 and no `search` [AC-017].
      - Pasting a term that is 255 characters after trimming (with surrounding spaces) and submitting shows `Must be 254 characters or fewer.` as the box's accessible description, with `aria-invalid="true"`. No request contains that term, and the address is unchanged. A 254-character term is sent (boundary). Submitting a valid term afterwards removes the message [AC-018].
      - Opening `/contacts?search=<255 characters>` (and separately the same term padded with spaces) shows the term in `Search contacts` with `Must be 254 characters or fewer.` as its accessible description and `aria-invalid="true"`. `recordRequests()` stays empty. There is no `Loading contacts…` status, no `alert`, no `No contacts yet.` or no-match text, no table, and no `Pagination` navigation. The `New contact` link is still shown [AC-049].
      - From that state, submitting `ada` sends one request with `search=ada&page=1`, shows the rows, and removes the message [AC-049].
      - Opening `/contacts?search=<254 characters>` sends a request with that term (boundary) [AC-049].
  - **Likely source files:** `lib/validationMessages.ts`, `C/listParams.ts` (`searchTooLong`, `MAX_SEARCH_LENGTH`), `C/useContacts.ts` (`enabled` option), `C/ContactSearchForm.tsx` (`initialError`), `C/ContactsPage.tsx`
  - **Done when:** the ACs pass. With an over-long address term, the list query is disabled (no request), and the page shows the AC-049 state before any loading state (plan rendering rule 0). The submit check and the address check share `MAX_SEARCH_LENGTH` and `maxLengthMessage`. The suite, lint, typecheck, and build are green.

- [ ] T-07: Paging controls and page from the address
  - **ACs:** AC-021, AC-022, AC-023, AC-024, AC-025, AC-027
  - **Depends on:** T-06
  - **Tests (test-writer):**
    - `C/listParams.test.ts` (extended): `page` absent, `1`, `2`, `0`, `-1`, `2.5`, `abc`, ` 2`, `+2`, `2147483647`, and `2147483648` → 1, 1, 2, 1, 1, 1, 1, 1, 1, 2147483647, 1. `search` blank → null and trimmed. `toSearchParams` leaves out page 1 and null search [AC-027].
    - `C/ContactsPage.paging.test.tsx` (new):
      - `totalCount` 45 on page 1 shows the `Pagination` navigation with `Previous`, `Next`, and `Page 1 of 3` [AC-021].
      - `Next` from `/contacts?search=ada` requests `page=2&search=ada` and shows `Page 2 of 3` and page 2's rows [AC-022].
      - `Previous` from `/contacts?search=ada&page=3` requests `page=2&search=ada` [AC-023].
      - On page 1 `Previous` is disabled and `Next` is enabled. On page 3 of 3 `Next` is disabled [AC-024].
      - `totalCount` 20 shows no `Pagination` navigation. `totalCount` 21 shows `Page 1 of 2` (boundary) [AC-025].
      - Opening `?page=0`, `?page=abc`, `?page=-1`, `?page=2.5`, and `?page=99999999999` sends `page=1` [AC-027].
  - **Likely source files:** `components/Pagination.tsx` (+ `.module.css`), `C/listParams.ts`, `C/ContactsPage.tsx`
  - **Done when:** the ACs pass. The suite, lint, typecheck, and build are green.

- [ ] T-08: Back restores the view; empty page beyond the last
  - **ACs:** AC-026, AC-028
  - **Depends on:** T-06, T-07
  - **Tests (test-writer):** `C/ContactsPage.paging.test.tsx` (extended):
    - **Guard expected:** from `/contacts`, search `ada`, then `Next` to page 2, then `goBack()`. That shows page 1 of `ada` with `ada` in the box. Another `goBack()` shows the unfiltered page 1 with an empty box [AC-026].
    - `/contacts?page=9` with `items: []` and `totalCount: 45` shows `This page is empty.` and a `Go to first page` button, without `No contacts yet.`, no-match text, or a table. Clicking the button requests `page=1`, keeps the search when one is set, and shows the rows [AC-028].
  - **Likely source files:** `C/ContactsPage.tsx`
  - **Done when:** the ACs pass (AC-026 may be a guard; AC-028 fails at RED). The suite, lint, typecheck, and build are green.

- [ ] T-09: Create form: fields, client-side rules, accessible errors, cancel
  - **ACs:** AC-029, AC-030, AC-031, AC-032, AC-033, AC-035, AC-047, AC-048
  - **Depends on:** T-06 (`lib/validationMessages.ts`), T-02
  - **Tests (test-writer):**
    - `C/contactFormRules.test.ts` (new). It covers `validateContactForm`, `validateContactField`, and `isValidEmail`:
      - First name blank or whitespace gives `Required.`.
      - Each field at max, at max+1, and at max with surrounding spaces (the last is valid).
      - Email: `a@b`, `@b`, `a@`, `a@@b`, `a@b@c`, `a b@c`, and blank (valid).
      - An over-long invalid email gives only `Must be a valid email address.`.
      - [AC-030, AC-031, AC-032, AC-034]
    - `C/NewContactPage.validation.test.tsx` (new):
      - The `New contact` heading; textboxes labelled `First name` (required, `toBeRequired()`), `Last name`, `Email`, `Phone`, `Company`, `Notes`; a `Save` button; a `Cancel` link [AC-029].
      - Submitting with First name empty, and separately with only spaces, shows `Required.` as First name's description. No POST is recorded [AC-030].
      - For each field, pasting max+1 characters shows `Must be {max} characters or fewer.` under that field only, with no POST [AC-031].
      - `ada@` shows `Must be a valid email address.` with no POST [AC-032].
      - Blank first name plus 101-character last name plus `x@` shows three errors at once [AC-033].
      - Each errored field has `aria-invalid="true"` and `toHaveAccessibleDescription(<message>)` [AC-035].
      - Typing in every field, then `Cancel`, lands on `/contacts` with no POST. Doing the same from an errored form also gives no POST [AC-047].
      - Cause errors, `Cancel`, then click `New contact`: every field is empty, and no error text or `aria-invalid` remains [AC-048].
  - **Likely source files:** `C/contactFormRules.ts`, `C/ContactForm.tsx` (+ `.module.css`), `C/NewContactPage.tsx`
  - **Done when:** the ACs pass. The form has `noValidate` and no `maxLength`/`required` attributes. `onSubmit` is not wired to the API yet. The suite, lint, typecheck, and build are green.

- [ ] T-10: Create form: focus the first error; live revalidation after a failed submit
  - **ACs:** AC-036 (client-side errors), AC-037 (client-side errors)
  - **Depends on:** T-09
  - **Tests (test-writer):** `C/NewContactPage.validation.test.tsx` (extended):
    - With errors on Email and Company only, submitting moves focus to Email. With errors on First name and Notes, focus goes to First name [AC-036].
    - After a submit fails with `Required.` on First name, typing `A` removes the error and `aria-invalid` without another submit [AC-037].
    - After a failed submit, typing `x@` into an Email that was valid shows `Must be a valid email address.` live, and fixing it removes the error (Q6) [AC-037].
    - Before any submit, typing `x@` shows no error (validation happens on submit) [AC-037].
  - **Likely source files:** `C/ContactForm.tsx`
  - **Done when:** the ACs pass. The suite, lint, typecheck, and build are green.

- [ ] T-11: Create form: send a valid form; saving state
  - **ACs:** AC-034, AC-038, AC-039
  - **Depends on:** T-10
  - **Tests (test-writer):**
    - `C/contactFormRules.test.ts` (extended): `toCreateContactRequest` trims every field and maps blank optional fields to `null` [AC-038].
    - `C/NewContactPage.test.tsx` (new):
      - All six fields filled (with surrounding spaces) and saved: exactly one `POST /api/contacts` with a JSON body equal to the trimmed values [AC-038].
      - Only First name filled: one POST with the other five fields `null`, and no error text [AC-034].
      - Every field pasted at exactly its maximum length: no error and one POST [AC-034].
      - While the POST is held, the button is `Saving…` and disabled. A second click and an Enter in First name record no second POST [AC-039].
  - **Likely source files:** `C/useContacts.ts` (`useCreateContact`), `C/NewContactPage.tsx`, `C/ContactForm.tsx`, `C/contactFormRules.ts`
  - **Done when:** the ACs pass. The suite, lint, typecheck, and build are green.

- [ ] T-12: Create success: back to the list with a confirmation and a fresh list
  - **ACs:** AC-045, AC-046
  - **Depends on:** T-11, T-04
  - **Tests (test-writer):** `C/NewContactPage.test.tsx` (extended):
    - Starting at `/contacts?search=zed&page=2`, click `New contact`, then fill and save `Ada` / `Lovelace`. The 201 response leads to `getLocation()` showing pathname `/contacts` with empty `search`, and a `status` with `Contact "Ada Lovelace" created.`. A first-name-only contact gives `Contact "Cher" created.` [AC-045].
    - Start at `/contacts`. The GET handler counts calls: the first returns `[Babbage]` at once, and the second awaits a `deferred()` and then returns `[Babbage, Lovelace]`. Click `New contact`, then fill and save `Ada Lovelace`. Back on the list, while the second GET is held, the loading status shows and no table (in particular no stale `Babbage`-only table) is shown. After release, the `Lovelace` row is present, and exactly two GETs are recorded [AC-046].
  - **Likely source files:** `C/useContacts.ts` (`removeQueries` on success), `C/NewContactPage.tsx`, `C/ContactsPage.tsx` (reads `location.state`)
  - **Done when:** the ACs pass. The suite, lint, typecheck, and build are green.

- [ ] T-13: Create: server validation and duplicate-email errors
  - **ACs:** AC-040, AC-041, AC-042, AC-036 (server-side errors), AC-037 (server-side errors)
  - **Depends on:** T-12
  - **Tests (test-writer):**
    - `C/contactFormErrors.test.ts` (new):
      - A `validation` error with known and unknown keys splits into `fields` and `form`.
      - Two messages for one key are joined with a space.
      - `conflict` gives the email message (and ignores the title).
      - [AC-040, AC-041, AC-042]
      - The `unexpected` mapping is **not** in this task (T-14).
    - `C/NewContactPage.test.tsx` (extended):
      - A 400 `{ lastName: ['Must be 100 characters or fewer.'], email: ['Must be a valid email address.'] }` (client-valid input) shows each message verbatim under its field with `aria-invalid`. Every typed value is kept, the heading is still `New contact`, and the address is still `/contacts/new` [AC-040].
      - A 400 `{ nickname: ['Something odd.'] }` shows an `alert` containing `Something odd.` [AC-041].
      - A 409 with title `Conflict!` shows `A contact with this email already exists.` under Email with `aria-invalid="true"`. Values are kept, the page stays, and the text `Conflict!` doesn't appear [AC-042].
      - After the 409, focus is on Email. After a 400 on `lastName` and `company`, focus is on Last name [AC-036].
      - After the 409, editing Email to a different valid address removes the duplicate message without a submit [AC-037].
  - **Likely source files:** `C/contactFormErrors.ts`, `C/NewContactPage.tsx`, `C/ContactForm.tsx`
  - **Done when:** the ACs pass. The suite, lint, typecheck, and build are green.

- [ ] T-14: Create: unexpected failure and resubmitting
  - **ACs:** AC-043, AC-044
  - **Depends on:** T-13
  - **Tests (test-writer):**
    - `C/contactFormErrors.test.ts` (extended): `unexpected` and a plain `Error` give `{ fields: {}, form: [SAVE_FAILED_MESSAGE] }` [AC-043].
    - `C/NewContactPage.test.tsx` (extended):
      - A 500 problem, a network error, and a 400 without `errors` each show an `alert` with `The contact couldn't be saved. Try again.`. Values are kept, `Save` is enabled again, and the page stays [AC-043].
      - A first attempt gets a 409. The second attempt (Email unchanged, POST held) shows no duplicate message and no `alert` while it's pending. When it's then answered with a 400 on `phone`, only the Phone error shows [AC-044].
      - A first attempt gets a 500 and the second succeeds: no `alert` remains, and the list shows the confirmation [AC-044].
  - **Likely source files:** `C/NewContactPage.tsx`, `C/ContactForm.tsx`
  - **Done when:** the ACs pass. AC-043 fails at RED (there's no unexpected-error message before this task). AC-044 may be a guard, because `mutate()` resets the previous error. The suite, lint, typecheck, and build are green.

## FINAL (reviewer)
- Full `TEST_CMD`, `LINT_CMD`, `TYPECHECK_CMD`, and `BUILD_CMD` pass.
- NFR-001, NFR-002, NFR-003, and NFR-005 are checked by inspection:
  - no `fetch(` outside `api/client.ts`;
  - no `console.` in `web/src` outside tests;
  - no test-id or class queries;
  - no `vi.mock` of modules or hooks.
- **NFR-006 manual smoke**, recorded in `review.md`:
  1. Start `DEV_API_CMD` and `DEV_WEB_CMD`.
  2. Open `/`, which redirects to `/contacts`.
  3. Create a contact with a unique email. You land on the list and see the confirmation.
  4. Search for the contact by email. It is listed.
  5. Create another contact with the same email. The duplicate message shows under Email.
  6. Reload a `/contacts?search=…&page=…` address and use Back.

## Traceability
| AC / NFR | Task(s) | Test file(s) (test names filled in during build) |
|---|---|---|
| AC-001 | T-02 | `App.test.tsx` |
| AC-002 | T-02 | `App.test.tsx` |
| AC-003 | T-02 | `App.test.tsx` |
| AC-004 | T-02 | `App.test.tsx` |
| AC-005 | T-03 | `C/ContactsPage.test.tsx` |
| AC-006 | T-03 | `C/ContactsPage.test.tsx`, `C/contactName.test.ts` |
| AC-007 | T-03 | `C/ContactsPage.test.tsx` |
| AC-008 | T-04 | `C/ContactsPage.test.tsx` |
| AC-009 | T-04 (client basis T-01) | `C/ContactsPage.test.tsx`, `api/client.test.ts` |
| AC-010 | T-04 | `C/ContactsPage.test.tsx` |
| AC-011 | T-04 | `C/ContactsPage.test.tsx` |
| AC-012 | T-03 | `C/ContactsPage.test.tsx` |
| AC-013 | T-05 | `C/ContactsPage.search.test.tsx` |
| AC-014 | T-05 | `C/ContactsPage.search.test.tsx` |
| AC-015 | T-05 | `C/ContactsPage.search.test.tsx` |
| AC-016 | T-06 | `C/ContactsPage.search.test.tsx` |
| AC-017 | T-06 | `C/ContactsPage.search.test.tsx` |
| AC-018 | T-06 | `C/ContactsPage.search.test.tsx` |
| AC-019 | T-05 | `C/ContactsPage.search.test.tsx` |
| AC-020 | T-05 | `C/ContactsPage.search.test.tsx` |
| AC-049 | T-06 | `C/ContactsPage.search.test.tsx`, `C/listParams.test.ts` |
| AC-021 | T-07 | `C/ContactsPage.paging.test.tsx` |
| AC-022 | T-07 | `C/ContactsPage.paging.test.tsx` |
| AC-023 | T-07 | `C/ContactsPage.paging.test.tsx` |
| AC-024 | T-07 | `C/ContactsPage.paging.test.tsx` |
| AC-025 | T-07 | `C/ContactsPage.paging.test.tsx` |
| AC-026 | T-08 | `C/ContactsPage.paging.test.tsx` |
| AC-027 | T-07 | `C/ContactsPage.paging.test.tsx`, `C/listParams.test.ts` |
| AC-028 | T-08 | `C/ContactsPage.paging.test.tsx` |
| AC-029 | T-09 | `C/NewContactPage.validation.test.tsx` |
| AC-030 | T-09 | `C/NewContactPage.validation.test.tsx`, `C/contactFormRules.test.ts` |
| AC-031 | T-09 | `C/NewContactPage.validation.test.tsx`, `C/contactFormRules.test.ts` |
| AC-032 | T-09 | `C/NewContactPage.validation.test.tsx`, `C/contactFormRules.test.ts` |
| AC-033 | T-09 | `C/NewContactPage.validation.test.tsx` |
| AC-034 | T-11 (rules in T-09) | `C/NewContactPage.test.tsx`, `C/contactFormRules.test.ts` |
| AC-035 | T-09 | `C/NewContactPage.validation.test.tsx` |
| AC-036 | T-10 (client), T-13 (server) | `C/NewContactPage.validation.test.tsx`, `C/NewContactPage.test.tsx` |
| AC-037 | T-10 (client), T-13 (server) | `C/NewContactPage.validation.test.tsx`, `C/NewContactPage.test.tsx` |
| AC-038 | T-11 | `C/NewContactPage.test.tsx`, `C/contactFormRules.test.ts` |
| AC-039 | T-11 | `C/NewContactPage.test.tsx` |
| AC-040 | T-13 (client basis T-01) | `C/NewContactPage.test.tsx`, `C/contactFormErrors.test.ts`, `api/client.test.ts` |
| AC-041 | T-13 | `C/NewContactPage.test.tsx`, `C/contactFormErrors.test.ts` |
| AC-042 | T-13 (client basis T-01) | `C/NewContactPage.test.tsx`, `C/contactFormErrors.test.ts`, `api/client.test.ts` |
| AC-043 | T-14 (client basis T-01) | `C/NewContactPage.test.tsx`, `C/contactFormErrors.test.ts`, `api/client.test.ts` |
| AC-044 | T-14 | `C/NewContactPage.test.tsx` |
| AC-045 | T-12 | `C/NewContactPage.test.tsx` |
| AC-046 | T-12 | `C/NewContactPage.test.tsx` |
| AC-047 | T-09 | `C/NewContactPage.validation.test.tsx` |
| AC-048 | T-09 | `C/NewContactPage.validation.test.tsx` |
| NFR-001 | all tasks; FINAL review | all test files (MSW + `renderWithProviders`; ProblemDetails from `test/msw/problems.ts`) |
| NFR-002 | all tasks; FINAL review | all component test files (role, label, and text queries only) |
| NFR-003 | T-01; FINAL review | `api/client.test.ts`; inspection |
| NFR-004 | T-02 (starter removed); every task's evidence | command evidence (`LINT_CMD`, `TYPECHECK_CMD`, `FRONTEND_TEST_CMD`, `BUILD_CMD`) |
| NFR-005 | T-01; FINAL review | `api/client.test.ts`; inspection (no `console.`) |
| NFR-006 | FINAL review (manual) | `review.md` smoke record |
