# 004: Review log

## T-01: 2026-10-06: APPROVE

**Scope reviewed:** uncommitted working tree on `feat/004-web-contacts-list-create` against base 74a2ccf: `web/src/api/client.ts`, `web/src/api/client.test.ts`, `web/src/test/setup.ts`, `web/src/test/msw/{server,handlers,problems}.ts`. No dependency changes (`package.json` / lockfile untouched).

**Checks:** `FRONTEND_TEST_CMD` pass, 21/21 (2 files, smoke test included) · `LINT_CMD` pass (dotnet format clean, oxlint clean) · `TYPECHECK_CMD` pass · `BUILD_CMD` (web) pass.

**AC coverage (foundation only; the ACs themselves are owned by later tasks):**
| AC / NFR | Test(s) in `web/src/api/client.test.ts` | Adequate? |
|---|---|---|
| AC-009 (foundation) | 200 relative URL; 502 text/plain → unexpected, problem null; network failure → status null; 200 non-JSON → unexpected; abort → AbortError | yes |
| AC-040 (foundation) | apiPost JSON body + Content-Type; 400 `errors` → validation, keys unchanged (incl. `LastName` casing, multi-message) | yes |
| AC-041 (foundation) | same validation test (keys passed through verbatim, so unknown keys survive) | yes |
| AC-042 (foundation) | 409 with three titles → conflict | yes for problem bodies; see finding 2 |
| AC-043 (foundation) | 400 without errors; 4 malformed `errors` shapes; 500; 404 | yes; see findings 1 and 2 |
| NFR-003 | Accept header; non-`/api/` path throws plain Error (relative and absolute); grep: `fetch(` only in `api/client.ts` | yes |
| NFR-005 | message excludes posted email and 409 title; grep: no `console.` in `web/src` | yes |

**Mutation (scratch copy, 20 mutants on `client.ts`):** 14 killed (409 branch removed, element type check, abort not rethrown or rethrow-all, Accept, Content-Type, path check removed or `includes`, title in message, problem not stored, 2xx non-JSON returning undefined, status null on non-JSON, fieldErrors dropped, signal dropped). Survivors:
- `status === 409 && problem` (409 with empty/non-JSON body becomes unexpected).
- `status === 400 &&` removed (a 404/500 that carries `errors` becomes validation).
- `!Array.isArray` removed from `isObject`; `problem` set for non-object JSON bodies (array/string). Plan: "parsed body when it was a JSON object, else null".
- `isObject(problem.errors) &&` removed: equivalent (finding 3).
- `instanceof Error` removed from the abort check: equivalent in practice, not a finding.

Probe (scratch): a 400 with `errors: {}` or `errors: { firstName: [] }` gives `kind: 'validation'` with nothing to show.

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 1 | Should-fix | implementer (test-writer adds the rows first) | web/src/api/client.ts:58-65, 83 | `isFieldErrors` is vacuously true for `errors: {}` and for keys with empty arrays, so those 400s become `validation` with no messages. When T-13/T-14 map that, the form will show no field error and no form-level alert. That breaks AC-043's "any response the page can't interpret" and the plan's "400 without a *usable* `errors`". The real API (ADR-0004) never sends this, so it isn't Blocking. | Test-writer: add `it.each` rows for `{}` and `{ firstName: [] }` → `unexpected`. Implementer: require at least one key with at least one string. Alternatively, decide explicitly that T-14's `contactFormErrors` treats an empty validation as `SAVE_FAILED_MESSAGE`, and record that in the plan. |
| 2 | Should-fix | test-writer | web/src/api/client.test.ts:88-131 | The plan's classification rules have unpinned boundaries, and the survivors above show it: (a) a 409 "whatever the body says" (empty or text body); (b) "400 only" for `validation` (a 500 or 404 with an `errors` dict must be `unexpected`); (c) `problem` is null for a JSON body that isn't an object (array or string). | Add three rows: `new HttpResponse(null, { status: 409 })` → conflict; `problemJson(500, { errors: { firstName: ['x'] } })` → unexpected with `fieldErrors` `{}`; `HttpResponse.json(['x'], { status: 500 })` → `problem` null. Each kills one survivor. |
| 3 | Should-fix | implementer | web/src/api/client.ts:83 | `isObject(problem.errors) &&` repeats the check `isFieldErrors` already starts with. The mutant that removes it is equivalent (0 failures). This is the same "unreachable guard" pattern rated Should-fix in specs 001-003. | Drop the `isObject(problem.errors) &&` term. |
| 4 | Nit | test-writer | web/src/api/client.test.ts:137, 162 | Not Prettier-formatted (`prettier --check` warns). `LINT_CMD` doesn't run Prettier, so nothing fails. | `scripts/format-file.sh web/src/api/client.test.ts`. |
| 5 | Nit | implementer | web/src/api/client.ts:67-73, 118-122 | The bare `catch` around `response.json()` turns an abort during the body read into an `unexpected` ApiError instead of rethrowing the AbortError. TanStack Query ignores a cancelled query's rejection, so nobody sees it. The plan only requires the rethrow when `fetch` rejects. I couldn't probe it (an MSW streamed body doesn't honour the abort). | Optional: rethrow when `options?.signal?.aborted`. |

**Notes:**
- Conforms to the plan: `ProblemDetails`, `ApiErrorKind`, `ApiError`'s public fields and `apiGet`/`apiPost` match plan section "API client". The `init`-object constructor is an allowed private detail. The message is fixed per status (`Request failed (409).` / `Request failed (no response).`), as in the plan's example. `setup.ts` matches the test strategy (`onUnhandledRequest: 'error'`, reset after each test, close after all, `cleanup` kept). `handlers.ts` is empty, which tasks.md allows.
- Minimality: no `types.ts`, hooks, routes or extra kinds. No `console.*`. `fetch` is only in the client.
- TDD discipline: the work is uncommitted, so I can't confirm the RED-before-GREEN order from history. Rely on the orchestrator's RED evidence for this.
- Test names carry AC/NFR IDs. Tagging foundation tests with the downstream AC (for example `[AC-040]` on the apiPost 201 test) is acceptable, because tasks.md lists them as "foundation for".
- Carry-forward to T-13/T-14: if finding 1 isn't fixed in the client, `contactFormErrors` must map an empty `validation` to the form-level save-failed message.

## T-01 re-review (fix cycle 1): 2026-10-06: APPROVE

**Checks:** `FRONTEND_TEST_CMD` pass, 30/30. That is the 21 earlier tests plus 9 new rows: 2 empty-`errors`, 2 for 409 with an empty or text body, 3 for 422/500/404 carrying `errors`, and 2 for a non-object JSON body. No test was removed or weakened. `LINT_CMD` pass · `TYPECHECK_CMD` pass · web build pass. No dependency changes. `fetch(` is only in `api/client.ts`. No `console.`.

**Earlier findings:**
- 1 (empty `errors`): closed. `isFieldErrors` now needs at least one key, and every value must be a non-empty string array. Mutants removing the key-count check or the message-count check each fail 1 test.
- 2 (classification boundaries): closed. `409 && problem` fails 2 tests. Dropping `status === 400` fails 3. Allowing arrays in `isObject` fails 1. Setting `problem` for a non-object body fails 2.
- 3 (duplicate `isObject` check): closed. The term is gone. `isFieldErrors` now has its own `isObject` guard, and removing that fails 1 test.
- 4 (test file formatting): closed for `client.test.ts`.
- 5 (abort during body read): deferred. It's a Nit, and I agree with reverting the change no test asked for.

**Mutation:** 11 of 12 mutants caught.

**New findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 6 | Nit | test-writer | web/src/api/client.test.ts:88-101 | Changing `lists.every(` to `lists.some(` fails no test (29/29 pass). Every malformed-`errors` row has a single key, so a dictionary with one valid and one invalid entry (for example `{ firstName: ['x'], lastName: 5 }`) would count as `validation` and go unnoticed. | Add that mixed row to the `it.each` table. |
| 7 | Nit | implementer | web/src/api/client.ts:65 | The fix line isn't Prettier-formatted (`prettier --check` warns; `LINT_CMD` doesn't run Prettier). | `scripts/format-file.sh web/src/api/client.ts`. |

## T-02: 2026-10-06: APPROVE

**Scope reviewed:** uncommitted working tree on `feat/004-web-contacts-list-create` (base 74a2ccf, on top of T-01's uncommitted work). The T-02 files are:
- `web/package.json` and `package-lock.json`: `react-router` ^8.4.0, which resolves to 8.4.0. The only new lock entries are `react-router`, `cookie-es` and `@remix-run/route-pattern`, as the plan expects.
- `web/src/test/renderWithProviders.tsx`, `web/src/test/msw/requests.ts`, `web/src/App.test.tsx`.
- `web/src/App.tsx`, `main.tsx`, `index.css`, `web/index.html`.
- `web/src/components/AppLayout.tsx` (+ `.module.css`), `web/src/components/NotFoundPage.tsx`.
- `web/src/features/contacts/{ContactsPage,NewContactPage}.tsx`.
- Deleted: `App.css`, `assets/*`, `public/icons.svg`.

**Checks:**
- `FRONTEND_TEST_CMD`: pass, 39/39 in 3 files. That is T-01's 30 plus 9 new tests; none were removed.
- `LINT_CMD`: pass (dotnet format and oxlint both clean).
- `TYPECHECK_CMD`: pass.
- Web build (`BUILD_CMD`): pass.
- `npx prettier --check` on the changed files: clean.
- `npm audit --omit=dev`: 0 vulnerabilities.
- Grep: `fetch(` appears only in `api/client.ts`, and there is no `console.` in `web/src`.

**AC coverage:**
| AC / NFR | Test(s) in `web/src/App.test.tsx` | Adequate? |
|---|---|---|
| AC-001 | `redirects / to the contacts list page` (heading + `getLocation().pathname`) | yes (`index_no_redirect` mutant fails 1) |
| AC-002 | `shows the app header with a Contacts nav link on %s` (3 routes); `leads to the list page when the Contacts nav link is clicked` | mostly; see finding 8 |
| AC-003 | `shows Page not found ... and sends no request on %s` (`/nope`, `/contacts/123/whatever`) | yes |
| AC-004 | `shows the create page when /contacts/new is opened directly` | yes (`no_new_route` fails 2, `contacts/*` fails 1) |
| NFR-004 | `no longer shows the Vite starter content` + the deleted files + gates above | yes |

**Mutation (scratch copy, 15 mutants on the shell):** 13 were killed:
- a fetch from NotFoundPage, both in an effect and during render;
- the index route rendering the list instead of redirecting;
- the brand moved out of `<header>`;
- the nav left unlabelled;
- `<header>` turned into a `<div>`;
- the nav link pointing to `/contacts/new`;
- `*` moved outside the layout route;
- the not-found link pointing to `/`;
- `contacts/new` removed;
- `<h1>` changed to `<h2>`;
- `contacts/*`;
- a starter logo image injected.

Survivors:
- `nav` moved out of `<header>` (finding 8).
- `<Navigate replace>` changed to a push. This is equivalent with a single-entry `MemoryRouter`: Back from `/` redirects forward again. AC-001 requires only the address change, so it's not a finding.

**Test utility probes (scratch):**
- **`recordRequests`.** The AC-003 "no request" assertion kills the fetch mutants on the `toHaveLength(0)` check itself, not only through `onUnhandledRequest: 'error'`. That still holds with a default `GET /api/contacts` handler installed (which later tasks add) and with a fetch delayed by `setTimeout(0)`.
  - `url` keeps the query string (`?page=2&search=a%20b`).
  - A POST `body` is parsed by the time `apiPost` resolves.
  - The listener count is 1 during the test and 0 in the next test, so `onTestFinished` cleanup works. That's equivalent to the plan's "removed in afterEach".
- **`getLocation`/`goBack`.**
  - The initial location is correct right after render.
  - The location is updated after a link click.
  - `search` and `state` are captured after `navigate(url, { state })`.
  - `goBack()` restores both the previous entry and its UI.
  - Back past the first entry is a silent no-op (MemoryRouter behaviour).
- **`rerender`** from the returned result throws `useRoutes() may be used only in the context of a <Router>` (finding 9).

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 8 | Should-fix | test-writer | web/src/App.test.tsx:22 | AC-002 says the app *header* contains the "Contacts" navigation link. The test finds `navigation` named `Main` anywhere on the page, so moving `<nav>` out of `<header>` passes 9/9. The brand is correctly scoped with `within(banner)`. | `within(banner).getByRole('navigation', { name: 'Main' })`. That kills the `nav_outside_header` mutant. |
| 9 | Should-fix | test-writer | web/src/test/renderWithProviders.tsx:43-53 | The providers and the probe wrap `ui` inline, but the helper returns `...result`, which includes `rerender`. `rerender(<App />)` re-renders without `MemoryRouter`/`QueryClientProvider` and throws. This shared utility is reused by every later web task and by specs 005/006. | Pass the providers and `<Probe />` through RTL's `wrapper` option (`render(ui, { wrapper })`), so `rerender` keeps them. Alternatively, leave `rerender` out of the returned type. |
| 10 | Nit | implementer | web/src/main.tsx:10 | `document.getElementById('root')!` has no comment. Conventions say no `!` without one. It was carried over from the starter, but T-02 rewrote this file. | Add a one-line comment (`index.html` always has `#root`), or throw explicitly if it's missing. |

**Notes:**
- **Plan conformance.** The routes match plan section "Routes" exactly: the layout route, index `<Navigate to="/contacts" replace />`, `contacts`, `contacts/new`, `*`. `main.tsx` nests StrictMode → QueryClientProvider → BrowserRouter → App, as planned. `AppLayout` provides `<header>` with a non-heading "MicroCRM", `<nav aria-label="Main">` and `<main><Outlet/>`. NotFound has an h1 "Page not found" and a "Go to contacts" link to `/contacts`. The `renderWithProviders` signature and `RecordedRequest` match the plan.
- **Minimality.** The page stubs are heading-only, and the list stub doesn't fetch. There are no hooks, API types, search, or paging. `index.css` is minimal, with no dark mode.
- **Accessibility of the shell.** It has banner, navigation (labelled) and main landmarks, `<html lang="en">`, and the title `MicroCRM`. The brand is not a heading, so each page's h1 is its only top-level heading. `aria-current` on the nav link isn't required by any AC (optional `NavLink` later).
- **Starter removal.** No counter, starter images, "Get started" text, or starter links remain in `web/src` or `index.html`. `public/favicon.svg` is still the Vite logo. The plan explicitly allows it ("may stay"), so it's not a finding; see follow-ups.
- **Process.** The pre-RED `chore(004)` dependency commit wasn't made separately, because `COMMIT_PER_TASK=0` and everything is uncommitted. The orchestrator should commit the dependency separately if it wants the planned history.

## T-03: 2026-10-06: APPROVE

**Scope reviewed:** uncommitted working tree.
- Tests: `web/src/test/msw/{fixtures,handlers}.ts`, `features/contacts/{contactName.test.ts,ContactsPage.test.tsx}`.
- Production: `api/types.ts`, `features/contacts/{contactName.ts,useContacts.ts,ContactsTable.tsx,ContactsPage.tsx}`.
- T-02 follow-ups: `App.test.tsx`, `test/renderWithProviders.tsx`, the new `test/renderWithProviders.test.tsx`, `main.tsx`.
- No new dependency: the `package.json` diff is still only T-02's `react-router`.

**Checks:**
- `FRONTEND_TEST_CMD`: pass, 51/51 in 6 files.
- `LINT_CMD`: pass (dotnet format and oxlint).
- `TYPECHECK_CMD`: pass.
- Web build: pass.
- `prettier --check src`: clean.
- Grep: `fetch(` appears only in `api/client.ts`, and there is no `console.`.

**T-02 follow-ups:** all closed.
- 8: the nav is now queried `within(banner)`; the nav-outside-header mutant fails 3.
- 9: the helper now uses `render(ui, { wrapper })`; reverting it to the inline form fails the new `renderWithProviders.test.tsx`.
- 10: `main.tsx:10` now has the comment.

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-005 | `ContactsPage.test.tsx` `requests page 1 with page size 20 and no search term` (exactly one GET; checks `page`, `pageSize`, and no `search`) | yes |
| AC-006 | `shows column headers ... in that order`; `shows one row per contact in the order returned`; `shows name, email, company and phone as returned`; `shows empty cells and a first-name-only Name for null values`; `contactName.test.ts` (2) | yes |
| AC-007 | `shows the total count from the API, plural` (45 with 2 items); `shows "1 contact"` | yes |
| AC-012 | `shows a New contact link to the create page`; `leads to the create page when New contact is clicked` | partly; see finding 11 |

**Mutation (scratch copy, 24 mutants):** 17 were killed:
- the count taken from `items.length`, or always plural;
- sorting by first name, by last name, or reversing the rows;
- swapping the Company and Phone cells or headers;
- showing `—` for null;
- removing the caption (fails 5);
- using `td` for the headers;
- a trailing space or double space in the name;
- dropping `pageSize`, a page size of 25, `page - 1`;
- `search=''` always sent, and a `search: ''` param.

Survivors:
- The "New contact" link rendered only once data has arrived (finding 11).
- Four equivalent mutants, which are not findings:
  - `totalCount <= 1`: only 0 differs, and the T-04 empty state replaces the count at 0.
  - `!c.lastName` instead of `=== null`: the API trims blanks to null.
  - Removing `scope="col"`: a `th` in `thead` is a columnheader either way.
  - My `refetchOnMount` mutant was malformed and can be ignored.
- **Not pinned yet** (both are plan-prescribed):
  - **The `signal` pass-through.** Nothing at this level can see it. AC-020 holds through separate cache keys, not through aborting.
  - **The query key shape `['contacts', params]`.** A key that drops `search` will fail at T-05 (AC-013/AC-019), and a key outside the `['contacts']` prefix will fail at T-12 (AC-046 `removeQueries`). Check both mutants again at those tasks.

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 11 | Should-fix | test-writer (can land with T-04's loading/error tests) | web/src/features/contacts/ContactsPage.test.tsx:115-131 | AC-012 says the "New contact" link shows whenever the list page is shown. Both AC-012 tests use a list that has already loaded, so moving the `Link` inside `{data && ...}` passes 20/20. Plan rendering rule 0 also needs the link when there's no request at all (AC-049). | Assert the `New contact` link while the list request is held open with `deferred()`, and after a 500 (an AC-012 row in the T-04 loading and error tests is enough). |

**Notes:**
- **Plan conformance.**
  - `Contact`, `PagedResponse<T>` and `CreateContactRequest` match the plan DTOs.
  - `CONTACTS_PAGE_SIZE`, `ContactListParams` and `contactKeys` match the plan.
  - `contactDisplayName` matches the plan.
  - The table is named by `<caption>Contacts</caption>` and has `th scope="col"` headers.
  - The request is `GET /api/contacts?page=1&pageSize=20`, built through `apiGet` with TanStack's `signal`.
  - `useContactsList` doesn't take the `{ enabled }` option yet. That's correct minimality, because AC-049 owns it.
- **Minimality.** No loading/error/empty states, search, paging or address params yet. The interim `0 contacts` plus an empty table for an empty result is a placeholder that T-04 replaces and tests, so it isn't drift.
- **Test utilities.**
  - `aContact` uses a module-level counter, so ids are unique within each test file.
  - The default handlers match the plan: GET echoes page and pageSize with an empty page, and POST returns a 201 echo.
  - `renderWithProviders.test.tsx` has no AC ID. That's acceptable for a utility test.

## T-04: 2026-10-06: APPROVE

**Scope reviewed:** uncommitted working tree.
- Tests: `web/src/test/deferred.ts` (new) and `web/src/features/contacts/ContactsPage.test.tsx` (+5 tests).
- Production: `web/src/features/contacts/ContactsPage.tsx` and `ContactsPage.module.css` (new).

**Checks:**
- `FRONTEND_TEST_CMD`: pass, 56/56 in 6 files.
- `LINT_CMD`: pass (dotnet format and oxlint).
- `TYPECHECK_CMD`: pass.
- Web build: pass.
- `prettier --check src`: clean.
- Grep: `fetch(` appears only in `api/client.ts` (the `refetch()` hit is a false positive), and there is no `console.`.
- No dependency changes.

**T-03 finding 11:** closed. Moving the `New contact` link into the results branch now fails 4 tests: the AC-008 loading test, the AC-009 500 test, and the two AC-012 tests.

**AC coverage:**
| AC | Test(s) in `ContactsPage.test.tsx` | Adequate? |
|---|---|---|
| AC-008 | `shows a loading status and no alert, empty message or table while the request is held` | yes, for the no-data case; see finding 12 |
| AC-009 | `shows an alert with Try again ... after a 500`; `... after a network error` | yes |
| AC-010 | `repeats the same request on Try again, shows loading, then the results` (2 GETs with the same query; loading shown and the alert gone while held; the table replaces the error) | yes, for the no-data case; see finding 12 |
| AC-011 | `shows "No contacts yet." with an Add a contact link, no table or paging, when empty` | yes |
| AC-012 (loading/error) | AC-012 assertions inside the AC-008 and AC-009 (500) tests | yes |

**Mutation (scratch copy, 20 mutants):** 13 were killed:
- link in results only (fails 4);
- loading = `!data` (fails 3);
- `role="status"` removed;
- `role="alert"` removed;
- Try again doing nothing;
- the empty-state link removed, or pointing to `/contacts`;
- a table added to the empty state;
- the empty message added to loading;
- an alert added to loading (fails 4);
- the empty message added to the error state.

Survivors, all equivalent under today's tests:
- loading = `isFetching` alone;
- loading = `isPending`;
- loading = `!data && !isError`;
- the error branch moved before loading (with or without `!isFetching`);
- empty decided by `items.length === 0`, which T-08's AC-028 must kill.

My Try-again-outside-the-alert mutant was a no-op, so it wasn't run. That case is pinned anyway by `within(alert).getByRole('button', { name: 'Try again' })` in the 500 test.

**On the coordinator's question (`isFetching && !data`):**
- **Matches the plan.** It is a literal translation of plan rendering rule 1: "no data yet for the current key, and a request is in flight". It's checked before the error branch, which is what tasks.md's "loading checks `isFetching` before error" asks for.
- **Why the order survives (probed).** In TanStack Query v5, a refetch of an errored query *with no data* resets the state to `status: 'pending'` and `error: null` while fetching. The plan's risk note ("TanStack keeps `status === 'error'` while refetching") is therefore only true when the query already has data. That is why every order mutant survives.
- **Later tasks:**
  - **Search and page changes (T-05/T-07) and AC-020.** Each new key starts with no data and no `placeholderData`, so it shows loading. Each response lands in its own key, so a late one can't replace the current view.
  - **AC-046 (T-12).** `removeQueries` leaves no data, so the list shows loading and then fetches.
  - **AC-049 (T-06).** A disabled query is `pending` and idle, so `isFetching && !data` is false and the state slot renders nothing. That is rule 0 by construction, whereas `isPending` would wrongly show loading. This is the strongest reason to keep the implementer's form, and AC-049 will kill the `isPending` mutant.
  - **AC-026 Back to a cached key (T-07).** The cached table shows during the background refetch, with no loading flash. That's sensible, and tests there should expect the table.
- **The one weak spot** is a query that has data *and* is in the error state (finding 12).

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 12 | Should-fix | test-writer, then implementer | web/src/features/contacts/ContactsPage.tsx:13-17 | Scenario: a key has data and a background refetch fails (window refocus, or remount of a stale key; production uses 3 retries). The query then has `status: 'error'` with data, and the page correctly shows the alert (AC-009). But when the owner clicks "Try again", the alert stays and no loading indicator shows while the request is in flight, because `!data` is false. Probed: `status: 'error'`, `fetchStatus: 'fetching'`, alert shown, status absent. AC-008 says that while the request hasn't completed, the page shows loading and not the error. The plan's own rule 2 ("`isError` and not fetching") would show the stale table instead, which also misses AC-008. | Test-writer: load a page, make a refetch fail (for example `focusManager.setFocused(false/true)`, with the handler returning `problemJson(500)`), hold the Try again response with `deferred()`, and assert `status` `Loading contacts…` with no `alert` [AC-008/AC-010]. Implementer: loading = `isFetching && (!data \|\| isError)`, still checked before the error branch. That keeps the no-data behaviour and the AC-049 idle case unchanged. |

**Notes:**
- `deferred.ts` matches the plan (`{ promise, resolve }`). Its one `!` has the comment the conventions require.
- **Minimality.** No search, paging, "empty page" (T-08) or no-match message yet. The CSS module holds a single margin rule.
- **Accessibility.** The loading `role="status"` and error `role="alert"` regions are mounted together with their text. Some screen readers announce a live region reliably only when its content changes after it exists. The spec only asks for the roles, so this is noted for FINAL, not raised as a finding.
- **Plan drift (for /document, planner).** The plan's "Error refetch looks like an error" risk should say that it only applies when the query already has data.

## T-05: 2026-10-06: APPROVE

**Scope reviewed:** the uncommitted working tree on top of 7ed0082 (T-01 to T-04 are committed in 16c29a9).
- Test: `web/src/features/contacts/ContactsPage.search.test.tsx` (8 tests).
- Production: `features/contacts/listParams.ts` (new), `ContactSearchForm.tsx` (new), `ContactsPage.tsx`.
- No dependency changes.

**Checks:**
- `FRONTEND_TEST_CMD`: pass, 65/65 in 7 files.
- `LINT_CMD`: pass (dotnet format and oxlint).
- `TYPECHECK_CMD`: pass.
- Web build: pass.
- `prettier --check src`: clean.
- Grep: `fetch(` appears only in `api/client.ts`, and there is no `console.`.

**AC coverage:**
| AC | Test(s) in `ContactsPage.search.test.tsx` | Adequate? |
|---|---|---|
| AC-013 | `submits a trimmed term with Enter ...`; `... with the Search button`; `shows different rows for different terms`; `sends no request while typing without submitting` | yes |
| AC-014 | `shows page 1 of the new results when submitting from another page` (starts at `?page=3`) | yes |
| AC-015 | `requests no search param at page 1 for a whitespace-only term` (starts at `?search=ada&page=2`) | yes |
| AC-019 | `shows the address term in the box and requests the list filtered by it` (the first GET is already filtered) | yes |
| AC-020 | `shows only the latest search when an earlier response arrives late` | yes, for the late response itself; see finding 14 |

**Mutation (scratch copy, 17 mutants, full suite):** 10 were killed:
- **Query key without `search` (carried over from T-03): fails 4.** Key outside the `['contacts']` prefix: fails 1, caught by the new T-04 finding-12 test.
- Form doesn't trim: fails 3.
- Blank submitted as `''`: fails 1.
- Address `search` ignored: fails 7.
- Search keeps the current page: fails 2.
- `toSearchParams` always writes `page`: fails 4.
- `page` ignored: fails 1.
- Box starts empty: fails 1.
- Keyed form: fails 2.

Survivors:
- The reset block in `ContactSearchForm` removed (finding 13).
- `placeholderData: (prev) => prev` (finding 14).
- Interim, owned by later tasks:
  - `setSearchParams(..., { replace: true })` (AC-026, T-07);
  - `toSearchParams` never writing `page` (Next/Previous, T-07);
  - a looser `page || 1` parse (AC-027, T-07);
  - an untrimmed address term (the plan's `listParams.test.ts` "254 with surrounding spaces" row at T-06/T-07).
- `preventDefault` removed. jsdom doesn't implement form submission, so this can't be observed in tests. A real browser would do a native GET submit and reload the page.

**Implementer deviation 1: `seenTerm` "adjust state on prop change" instead of `key`.** Accepted. Probed in scratch:
- **Both forms reset the box on Back:** `?search=bob` → Back gives `?search=ada` with the box showing `ada`.
- **Both forms reset it on the header Contacts link:** `''` with an empty box.
- **The key costs focus.** With `key`, the input remounts on every submit and keyboard focus drops to `body` after Enter. With `seenTerm`, focus stays on the box. That's a real accessibility gain, not just a convenience for the tests.
- **The intent is met:** "the box resets when the address term changes".
- **Planner:** reword plan § Components (`ContactSearchForm`: "keyed by the address term") and the tasks.md T-05 "Done when". Suggested wording: "resets its text (and, from T-06, its error) when the address term changes, by adjusting state during render rather than with a `key`, so focus survives a submit".
- **T-06 caveat.** The same block must also reset the local 254 error and `initialError` (AC-018/AC-049). A key would have done that for free.

**Implementer deviation 2: a minimal `page` parse in `readListParams` at T-05.** Within scope.
- **It's demanded.** The AC-014 test opens `?page=3` and asserts page 3's rows first, and the `page_ignored` mutant fails it.
- **The guard is interim.** The `Number.isInteger && >= 1` guard goes beyond what is demanded (`page || 1` survives). It's an interim placeholder that T-07 replaces with the plan's `/^\d+$/` plus `MAX_PAGE` rule and tests. Under the spec 001 T-13 precedent that isn't drift.
- **T-07 RED is still real.** `Number(' 2')`, `Number('+2')`, `Number('1e3')` and `99999999999` are all accepted today, so those AC-027 rows will fail first. `0`, `-1`, `2.5`, `abc` and absent will be guards.

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 13 | Should-fix | test-writer | web/src/features/contacts/ContactsPage.search.test.tsx | The T-05 "Done when" ("resets when the address changes") isn't pinned. Removing `setTerm(initialTerm)` from the reset block passes 65/65. This is exactly the mechanism deviation 1 replaced, so it should be guarded now and not wait for AC-017/AC-026. | Add a test: submit `ada`, then `bob`, then `goBack()`, and assert that the box shows `ada`. Alternatively, type into the box on `?search=ada` and click the header `Contacts` link, then assert that the box is empty [AC-019]. |
| 14 | Should-fix | test-writer | web/src/features/contacts/ContactsPage.search.test.tsx:171-177 | Adding `placeholderData: (prev) => prev` to `useContactsList` passes every test. The plan rejects it explicitly ("A new key shows the loading state rather than the previous page's table. That makes AC-008 and AC-020 hold by construction"). Nothing asserts what shows while a *new* search is in flight. | In the AC-020 test, after submitting `a` (held), assert `status` `Loading contacts…` and no `table` [AC-008/AC-020]. |
| 15 | Nit | implementer | web/src/features/contacts/ContactSearchForm.tsx:27-30 | The input is labelled by wrapping it in `<label>`. Conventions say `<label htmlFor>`, and T-06 needs an `id` anyway for `aria-describedby="contact-search-error"`. | Use `<label htmlFor="contact-search">` with `<input id="contact-search">` (at T-06 at the latest). |
| 16 | Nit | implementer | web/src/features/contacts/listParams.ts:4 | `readListParams` returns `ContactListParams`. The plan signature returns `ListAddress = { params, searchTooLong }`, and the T-07 unit tests target that shape. | Adopt `ListAddress` at T-06, when `searchTooLong` is demanded. |
| 17 | Nit | test-writer | web/src/features/contacts/ContactsPage.search.test.tsx:48, 65, 112, 132 | `listGets(requests).at(-1)!` uses `!` without a comment, which the conventions require. | Add a comment, or assert the length and then index. |

**Commit:** there are no Blocking findings, so T-05 can be committed. Findings 13 and 14 are test-only and take a couple of lines each. Land them in a short fix cycle before the T-05 commit, or as the first thing in T-06. Either way, they must not slip past T-06.

## T-06: 2026-10-06: APPROVE

**Scope reviewed:** the uncommitted working tree on top of c919731.
- Tests: `web/src/features/contacts/listParams.test.ts` (new, 5 tests) and `ContactsPage.search.test.tsx` (+10).
- Production: `web/src/lib/validationMessages.ts` (new), plus `features/contacts/{listParams.ts,useContacts.ts,ContactSearchForm.tsx,ContactsPage.tsx}`.
- The test-file diff is purely additive (+191, −0), so the Prettier run reformatted no existing tests and none were weakened.

**Checks:**
- `FRONTEND_TEST_CMD`: pass, 82/82 in 8 files.
- `LINT_CMD`: pass (dotnet format and oxlint).
- `TYPECHECK_CMD`: pass.
- Web build: pass.
- `prettier --check src`: clean.
- Grep: `fetch(` appears only in `api/client.ts`, and there is no `console.`.
- No dependency changes.

**Earlier items:**
- **T-05 findings 13 and 14:** closed (verified by the coordinator's mutants). The plan has been reworded.
- **Finding 15 (`htmlFor`):** closed: `<label htmlFor="contact-search">` with `id="contact-search"`.
- **Finding 16 (`ListAddress`):** closed: `readListParams` now returns `{ params, searchTooLong }`.

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-016 | `shows the no-match message and Clear search, and no table, paging or empty message` | yes; see Nit 21 |
| AC-017 | `empties the box and requests the unfiltered page 1 on Clear search` | partly; see finding 19 |
| AC-018 | `shows the 254 message on a submitted term over 254 characters after trimming, and sends nothing`; `sends a submitted term of exactly 254 characters (boundary)`; `removes the 254 message after a valid term is submitted` | partly; see finding 18 |
| AC-049 | `listParams.test.ts` (254, 254 padded, 255, 255 padded, absent); `opening the address with an over-long term` / `... padded with spaces` (no request, no list state, the link is still shown); `submitting a valid term from the over-long address state ...`; `shows the 254 message again, with no request, on Back ...`; `sends a request for an address term of exactly 254 characters (boundary)` | yes |

**Mutation (scratch copy, 22 mutants, full suite):** 16 were killed:
- `isPending` loading (see below);
- `enabled` forced on, or ignored by the hook (fails 4 each);
- `>=` instead of `>` in `readListParams` (fails 4) and in the form (fails 1);
- the too-long check on the untrimmed value, in the address and in the form;
- the form sending anyway;
- the reset block missing `setError(initialError)`, or setting it to `null` (each killed by the Back test);
- the reset block missing `setTerm` (fails 4);
- `aria-invalid` removed (fails 4);
- `aria-describedby` removed (fails 6);
- `initialError` always null (fails 4);
- the no-match branch off (fails 2).

Survivors:
- `setError(null)` removed from a valid submit (finding 18).
- Clear search keeping the current page (finding 19).
- The no-match text using the raw, untrimmed address term (Nit 21).
- The no-match/empty decision by `items.length` (T-08 AC-028 owns it).
- Clear search with `replace` (no AC demands a push for Clear).
- Removing the rule-0 guard `searchTooLong ? null :` (see below).

**Checks the coordinator asked for:**
- **The `isPending` mutant.**
  - *With rule 0 in place:* the AC-049 tests can't see it, because rule 0 renders nothing first. It is still killed, by T-04's `shows loading, not the stale error, when Try again retries after a failed background refetch`.
  - *With rule 0 removed as well:* AC-049 kills it (fails 4). So the comment in the AC-049 test ("a loading condition on isPending would show this") holds only if rule 0 is gone.
  - *Rule 0 on its own:* removing it with the current `isFetching && (!data || isError)` loading condition is equivalent (82/82). A disabled, never-fetched query is idle with no data, so nothing renders anyway.
  - *Verdict:* it's prescribed by the plan (rendering rule 0, the T-06 "Done when") and it guards against a future change to the loading condition. Under the spec 003 T-08 precedent that's a note, not drift.
- **Render-time reset and `initialError` on Back.** Correct. The reset block now sets the term, `seenTerm` and the error from `initialError`.
  - The Back test (valid → Back to the 255-character address) kills both "error not reset" and "error reset to null".
  - Probed: header link from `?search=zed&page=2` after a 255-character submit error gives an empty box and no error.
  - Probed: the same link clicked while already on `/contacts` keeps the typed 255 characters and the error, because the address didn't change. A `key` would behave the same, and no AC covers it, so it's not a finding.
- **The `htmlFor` nit:** closed.
- **`<p className="field-error">`** (Nit 20).

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 18 | Should-fix | test-writer | web/src/features/contacts/ContactsPage.search.test.tsx (AC-018 block) | Removing `setError(null)` from `handleSubmit` passes 82/82. Every "message removed" test submits a term that changes the address, so the render-time reset clears the error instead. When the address *doesn't* change, the stale message stays. Two examples: on `/contacts`, submit 255 characters and then submit a blank box; or on `?search=ada`, submit 255 characters and then submit `ada` again. | Add an AC-018 test: on `/contacts`, submit 255 characters, then clear the box and press Enter. Assert that the message and `aria-invalid` are gone. Optionally, on `?search=ada`, submit 255 characters and then `ada` again. |
| 19 | Should-fix | test-writer | web/src/features/contacts/ContactsPage.search.test.tsx (AC-017 test) | The AC-017 test starts at `?search=zed` (page 1), so a Clear that keeps the current page passes. The no-match state is reachable on page 2 (probed: `?search=zed&page=2` with `totalCount` 0 shows Clear search). AC-017 says "page 1 of the unfiltered list". | Start the AC-017 test at `/contacts?search=zed&page=2`, and assert that the request has `page=1` and the address is `/contacts`. |
| 20 | Nit | implementer | web/src/features/contacts/ContactSearchForm.tsx:51 | `className="field-error"` is a global class with no CSS anywhere (`grep` finds only this line). Conventions use CSS modules, so this is a dead hook, and the error looks the same as body text. | Use a module class (for example a shared `FieldError` style that T-09's form can reuse), or drop the `className` until styling is wanted. |
| 21 | Nit | test-writer | web/src/features/contacts/ContactsPage.search.test.tsx (AC-016 test) | AC-016 says the message shows "the trimmed term". The test types ` zed `, so the form trims it before it reaches the address. Rendering the raw address term passes. | Add a row that opens `/contacts?search=%20zed%20` with 0 results and expects `No contacts match "zed".` |

**Notes:**
- **Plan conformance.**
  - `MAX_SEARCH_LENGTH`, `ListAddress`, `readListParams` and `maxLengthMessage` match the plan, and the submit and address checks share them.
  - The hook takes `{ enabled }` and passes it to `useQuery`.
  - The error id `contact-search-error` is linked through `aria-describedby`, with `aria-invalid="true"`.
  - `initialError` is typed `string | null` and required, where the plan has `initialError?: string`. That's harmless.
- **Minimality.** `validationMessages.ts` holds only `maxLengthMessage`; `REQUIRED` and `INVALID_EMAIL` wait for the form tasks. No paging and no "empty page" state.
- **Accessibility.** The no-match message and Clear search sit in a plain `div`. The spec asks for no live region there.

**Commit:** there are no Blocking findings, so T-06 can be committed. Findings 18 and 19 are test-only, a few lines each. Land them in a short fix cycle, or first thing in T-07; they shouldn't slip past T-07.

## T-07: 2026-10-06: APPROVE

**Scope reviewed:** the uncommitted working tree on top of 489676e.
- Tests: `web/src/features/contacts/listParams.test.ts` (extended) and `ContactsPage.paging.test.tsx` (new).
- Production: `web/src/components/Pagination.tsx` (+ `.module.css`), `features/contacts/listParams.ts`, `ContactsPage.tsx`.
- The only removed line in the existing test files is the widened `import` in `listParams.test.ts`.
- No `staleTime` anywhere in `web/src` (grep is empty).
- No dependency changes.

**Checks:**
- `FRONTEND_TEST_CMD`: pass, 132/132 in 9 files.
- `LINT_CMD`: pass (dotnet format and oxlint).
- `TYPECHECK_CMD`: pass.
- Web build: pass.
- `prettier --check src`: clean.
- Grep: `fetch(` appears only in `api/client.ts`, and there is no `console.`.
- **Stability:** the paging file passed 6/6 runs on its own and 3/3 inside the full suite. The stable ids plus `waitFor` fix for the AC-022 Back test holds without `staleTime`.

**AC coverage:**
| AC | Test(s) | Adequate? |
|---|---|---|
| AC-021 | `shows Previous, Next and "Page 1 of 3" when 45 contacts do not fit on one page` | yes |
| AC-022 | `requests the next page, keeps the search, updates the address and shows page 2 on Next`; `pushes a history entry when paging, so Back returns to the previous page`; `toSearchParams` `writes a page above 1` / `writes both the search and the page` | yes |
| AC-023 | `requests the previous page and keeps the search on Previous`; `leaves out page from the address when Previous returns to page 1` | yes |
| AC-024 | first page (Previous disabled, Next enabled); last page (Next disabled, Previous enabled); middle page (both enabled) | yes |
| AC-025 | 20 contacts → no Pagination; 21 → `Page 1 of 2` (boundary); 1 → no Pagination | yes |
| AC-027 | `listParams.test.ts`: page table (absent, `1`, `2`, `02`, `2147483647` accepted; `0`, `-1`, `2.5`, `abc`, `''`, ` 2`, `2 `, `+2`, `1e3`, `0x10`, `2147483648`, `99999999999` → 1), `MAX_PAGE`, search blank/trim, `toSearchParams`; page tests: 8 invalid address values send `page=1`, `Page 1 of 3` for `?page=1e3`, and a valid `?page=2` is sent | yes |

**T-05 carry-forward mutants (re-run):** all three are now killed.
- `setSearchParams(..., { replace: true })` for paging: fails 1 (the push/Back test).
- `toSearchParams` never writing `page`: fails 5.
- `Number(rawPage) || 1`: fails 11.

**Mutation (scratch copy, 22 mutants, full suite):** 21 were killed:
- **Page parse:** unanchored regex (fails 11); signed regex (fails 2); no `MAX_PAGE` (fails 3); `<` instead of `<=` (fails 1); no `>= 1` (fails 2); clamping instead of falling back to 1 (fails 3).
- **When paging shows:** `>=` instead of `>` (fails 1); `floor` instead of `ceil` (fails 7); deciding by `items.length` (fails 10).
- **Paging actions:** Next/Previous dropping `search` (fails 2); Previous going to 1 (fails 1); Next going +2 (fails 2).
- **Disabled states:** `<` / `>` and never-disabled variants (fail 1 each).
- **Pagination landmark:** unlabelled (fails 10); different text (fails 6).

The one survivor is `page={data.page}` instead of `page={params.page}`. That's equivalent: the API echoes the requested `page` (spec 001 paging), and so does the handler. It's not a finding.

**Findings:** none.

**Notes:**
- **Plan conformance.**
  - The `Pagination` props match the plan (`page`, `totalPages`, `onPageChange`).
  - It's a `nav` named `Pagination` with `Previous`/`Next` buttons using the native `disabled`, and the `Page {p} of {n}` text.
  - Paging shows only when `totalCount > 20`, with `ceil(totalCount / 20)` pages.
  - `readListParams` uses `/^\d+$/` and `1..MAX_PAGE`, exactly as plan § Address params says.
  - Paging pushes through `setSearchParams` and keeps the search.
- **Minimality.** No "This page is empty" state (T-08), and no Back-specific code; AC-026 is T-08's.
- **Accessibility.** `Page X of Y` isn't a live region, so a screen-reader user who presses Next isn't told about the page change, except through the focus staying on the button. The spec doesn't require that, so it's noted for FINAL.

**Commit:** no findings, so T-07 can be committed.

## T-08: 2026-10-06: APPROVE

**Scope reviewed:** the uncommitted working tree on top of f8e47dc.
- Tests: `web/src/features/contacts/ContactsPage.paging.test.tsx` (+1 AC-026 guard, +4 AC-028 tests; additive only).
- Production: `web/src/features/contacts/ContactsPage.tsx` (one new branch, after the two `totalCount === 0` branches).
- No dependency changes.

**Checks:**
- `FRONTEND_TEST_CMD`: pass, 137/137 in 9 files.
- `LINT_CMD`: pass (dotnet format and oxlint).
- `TYPECHECK_CMD`: pass.
- Web build: pass.
- `prettier --check src`: clean.
- Grep: `fetch(` appears only in `api/client.ts`, and there is no `console.`.
- **Stability:** the paging file passed 4/4 runs on its own and 2/2 inside the full suite.

**AC coverage:**
| AC | Test(s) in `ContactsPage.paging.test.tsx` | Adequate? |
|---|---|---|
| AC-026 | `shows page 1 of the search, then the unfiltered page 1, on successive Back` (guard), plus T-07's `pushes a history entry when paging ...` | yes |
| AC-028 | `shows "This page is empty." and a Go to first page button, not the other states`; `requests page 1 and shows the rows when Go to first page is clicked`; `shows "This page is empty." and not the no-match text when a search is set`; `keeps the search when Go to first page is clicked` | yes; see Nit 22 |

**Carry-forward mutants:**
- **`items.length` instead of `totalCount === 0`** (from T-04/T-06): now killed. In the no-match branch it fails 2, in the "No contacts yet." branch 4, and in both together 4.
- **AC-026 Back across search and page:**
  - The search submit using `replace` fails 3.
  - Paging using `replace` fails 2.
  - Removing the search box reset fails 5.
  - The guard checks the rows, the box value and the address after each of two Backs: `?search=ada` page 2 → page 1 → unfiltered.

**Mutation (scratch copy, 12 mutants, full suite):** 9 were killed. Besides the above:
- the empty-page branch removed (fails 4);
- "Go to first page" dropping the search (fails 1);
- "Go to first page" going to `page - 1` (fails 2).

Survivors:
- "Go to first page" using `replace`: no AC asks for a push here. Note only.
- The empty-page state also rendering a `Pagination` nav (Nit 22).
- The empty-page state also rendering the count: the AC says nothing about it. Not a finding.

**Findings:**
| # | Severity | Owner | Location | Issue | Expected fix |
|---|---|---|---|---|---|
| 22 | Nit | test-writer | web/src/features/contacts/ContactsPage.paging.test.tsx (first AC-028 test) | Plan rendering rules say exactly one state shows, with paging only under Results. The AC-011 and AC-016 tests assert there's no `Pagination` nav, but the AC-028 "not the other states" test doesn't, so a Pagination nav added to the empty-page branch survives. | Add `expect(screen.queryByRole('navigation', { name: 'Pagination' })).not.toBeInTheDocument()`. |

**Notes:**
- **Branch order.** The new branch sits after both `totalCount === 0` branches and before Results, so it's effectively `items.length === 0 && totalCount > 0`. That matches plan rules 3 → 4 → 5. "Go to first page" reuses `handlePageChange(1)`, so it pushes, keeps the search and leaves `page` out of the address.
- **Minimality.** Only the branch was added. AC-026 needed no production change, because T-05 and T-07 already push and the T-05 render-time reset restores the box.
- **The list side (AC-005 to AC-028, AC-049) is complete.** Two Should-fix items from T-05 and T-06 are already closed, and no open findings remain on the list side apart from Nit 22.

**Commit:** no Blocking findings, so T-08 can be committed. Nit 22 is optional.

### T-08 re-check after incident (orchestrator, 2026-10-07)
- **Incident:** while proving the Nit 22 guard, the test-writer mutated the real `ContactsPage.tsx` and then ran `git checkout` on it, discarding the approved, uncommitted empty-page branch. The implementer re-applied it; `git diff` shows only the 7-line `data && data.items.length === 0` branch, identical to the approved change.
- **Nit 22:** closed. Verified by the orchestrator in a scratch copy: adding `<Pagination>` to the empty-page branch fails only the first AC-028 test (25/26 pass).
- **Gates:** 137/137 tests, lint, typecheck, build, Prettier clean.
- **Harness gap:** `path-guard.sh` only covers Edit/Write/MultiEdit/NotebookEdit; Bash edits and `git checkout`/`git restore` bypass role write-boundaries. Raised with the owner.
- The reviewer's re-check run stalled before reporting; T-08's earlier APPROVE stands.
