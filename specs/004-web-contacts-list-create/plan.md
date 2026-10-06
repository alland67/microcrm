# 004: Web: contacts list and create form: Technical plan

**Status:** Planned
**Spec:** ./spec.md (amended 2026-10-06: AC-049, Q-A option (b))
**ADRs:** ADR-0010 (client-side routing with React Router, declarative mode), Accepted 2026-10-06. Builds on ADR-0002 (stack), ADR-0004 (ProblemDetails pipeline: 400 with or without `errors`, 409 as problem+json), and ADR-0006 (canonical messages, shown verbatim).

## Approach
This is the first web spec. It replaces the Vite starter with a small app built from four layers. Later web specs (005, 006) reuse every layer.

1. **One API client** (`api/client.ts`). It wraps `fetch` for relative `/api/...` URLs, parses JSON, and turns every non-2xx or unreadable response, plus network failures, into one typed `ApiError`. Each error has a `kind` (`validation`, `conflict`, or `unexpected`), the parsed ProblemDetails, and the field errors. Components never call `fetch` (NFR-003). This is the only place that knows the ADR-0004 error shapes.
2. **Query hooks per feature** (`features/contacts/useContacts.ts`), built on TanStack Query, with the query key `['contacts', { page, search }]` (conventions). The list hook takes already-validated params. The create hook drops cached contact lists on success, so returning to the list always refetches (AC-046).
3. **Routing with React Router in declarative mode** (ADR-0010). One `<Routes>` lives in `App.tsx`, inside a layout route that renders the header and nav (AC-002). `/` redirects to `/contacts` with `<Navigate replace>` (AC-001), and `*` renders "Page not found" (AC-003). The list page's search term and page live in the query string (`useSearchParams`). Each search or page change *pushes* a history entry, so Back restores the previous view (AC-026). After a create, `navigate('/contacts', { state })` carries a one-off confirmation (AC-045).
4. **Pages and components** under `features/contacts/`. Shared presentational pieces (layout, not-found, pagination) live in `components/`. The pure helpers have unit tests: address-param parsing, client-side form rules, mapping a server error to form errors, and the display name. Pages and components are covered by component tests.

**Server state vs. local state.** List data and the create mutation are TanStack Query state. The search box text, the form values, the client-side errors, and "which server errors the owner has since edited away" are local `useState`. There is no global store.

**List rendering rules.** Exactly one of these states shows, in this order:
0. **Search too long in the address** (AC-049): the address term is longer than 254 characters after trimming. The query is disabled, so no request is sent. Only the heading, the "New contact" link, and the search form show. The box holds the term and carries the 254 message. A disabled query sits at `status: 'pending'`, `fetchStatus: 'idle'`, so this check must come before Loading, otherwise a loading indicator with no request would show.
1. **Loading**: no data yet for the current key, and a request is in flight. This includes a "Try again" refetch after an error: TanStack keeps `status === 'error'` while refetching, so the page must check `isFetching` first (AC-008, AC-010).
2. **Error**: `isError` and not fetching (AC-009).
3. **Empty**: `items` is empty and `totalCount === 0`. The message depends on whether a search is active (AC-011, AC-016).
4. **Empty page**: `items` is empty and `totalCount > 0` (AC-028).
5. **Results**: the count, the table, and the paging controls, which only show when `totalCount > 20` (AC-021, AC-025).

`placeholderData`/`keepPreviousData` is **not** used. A new key shows the loading state rather than the previous page's table. That makes AC-008 and AC-020 hold by construction: each key's response lands in its own cache entry, and only the current key's entry is shown.

**Form behavior.** The form has `noValidate`, and inputs don't use the `maxLength` or `required` attributes. Native validation would block the spec's own messages, and `maxLength` would make AC-031 impossible to trigger. First name is marked required with `aria-required="true"` plus a visible marker that's `aria-hidden`, so the label's accessible name stays "First name". Fields validate on submit. After the first failed submit (client or server), each field revalidates as it changes (Q6, AC-037). Editing a field also hides that field's server error from the last attempt, and its client rules then decide what shows. Submitting clears all server errors from the previous attempt. Because they're derived from the mutation's `error`, `mutate()` resets them when the new attempt starts (AC-044). After any blocked submit, focus moves to the first field in form order that shows an error. For server errors this happens in an effect once the error arrives (AC-036). A submit while the mutation is pending is ignored in the handler, as well as by disabling "Save" (AC-039; covers Enter in a field).

**What the form sends.** The six fields go out trimmed, with blank optional fields sent as `null`. `firstName` is the trimmed string. This mirrors the API's own normalization (ADR-0004), so the client validates exactly what the server will validate. AC-038 tests assert this body.

## Components touched
All paths are under `web/`. No API or backend change.

| Component / module | Change | Notes |
|---|---|---|
| `package.json`, `package-lock.json` | modified | + `react-router` ^8.4.0 (ADR-0010), added as a chore at the start of T-02 |
| `index.html` | modified | `<title>MicroCRM</title>` |
| `src/main.tsx` | modified | `<StrictMode>` → `<QueryClientProvider client={new QueryClient()}>` → `<BrowserRouter>` → `<App />` |
| `src/App.tsx` | replaced | Exports `App`: the `<Routes>` tree (layout route; index → `<Navigate to="/contacts" replace />`; `contacts`; `contacts/new`; `*`). Starter content removed |
| `src/App.css`, `src/assets/*`, `public/icons.svg` | deleted | Starter-only (NFR-004). `public/favicon.svg` may stay |
| `src/index.css` | replaced | Minimal light global styles (no starter rules, no dark mode; spec non-goal) |
| `src/api/client.ts` | new | `apiGet`, `apiPost`, `ApiError`, `ProblemDetails` |
| `src/api/types.ts` | new | `Contact`, `PagedResponse<T>`, `CreateContactRequest` |
| `src/lib/validationMessages.ts` | new | ADR-0006 canonical messages shared by search and form |
| `src/components/AppLayout.tsx` (+ `.module.css`) | new | `<header>` with "MicroCRM", `<nav aria-label="Main">` with the "Contacts" link, `<main>` with `<Outlet />` |
| `src/components/NotFoundPage.tsx` | new | "Page not found" + link to `/contacts` |
| `src/components/Pagination.tsx` (+ `.module.css`) | new | Previous/Next + "Page X of Y"; presentational, reused by spec 006 |
| `src/features/contacts/ContactsPage.tsx` (+ `.module.css`) | new | List page: address params, states, search form, table, pagination, confirmation |
| `src/features/contacts/ContactsTable.tsx` | new | Presentational table |
| `src/features/contacts/ContactSearchForm.tsx` | new | Search box + client-side 254 check |
| `src/features/contacts/listParams.ts` | new | Pure: address ↔ `ContactListParams` |
| `src/features/contacts/contactName.ts` | new | Pure: display name (Definitions) |
| `src/features/contacts/useContacts.ts` | new | `useContactsList`, `useCreateContact`, `contactKeys`, `CONTACTS_PAGE_SIZE` |
| `src/features/contacts/NewContactPage.tsx` | new | Create page: heading, mutation, success navigation |
| `src/features/contacts/ContactForm.tsx` (+ `.module.css`) | new | Six fields, client and server errors, focus, Save/Cancel. Reused for edit in spec 005 |
| `src/features/contacts/contactFormRules.ts` | new | Pure: `validateContactForm`, `toCreateContactRequest` |
| `src/features/contacts/contactFormErrors.ts` | new | Pure: `ApiError` → `{ fields, form }` for the form |
| `src/test/setup.ts` | modified (test-writer) | Start/reset/close the MSW server; `onUnhandledRequest: 'error'` |
| `src/test/msw/*`, `src/test/renderWithProviders.tsx`, `src/test/deferred.ts` | new (test-writer) | See Test strategy |
| `src/**/*.test.ts(x)` | new (test-writer) | See Test strategy. `src/test/smoke.test.tsx` stays |
| `docs/adr/0010-*.md` | new (planner) | Proposed |

## Interfaces & data
These signatures are what the tests target. The implementer may add private helpers. Public names should stay as written unless the test-writer and implementer agree on a change in the task's review.

TypeScript constraints from `tsconfig.app.json`: TS 6 is `strict` by default, plus `erasableSyntaxOnly` and `verbatimModuleSyntax`. So: no `enum`, no constructor parameter properties (declare fields explicitly), and `import type` for type-only imports.

### API client (`src/api/client.ts`)
```ts
export type ProblemDetails = {
  type?: string; title?: string; status?: number; detail?: string; instance?: string
  errors?: Record<string, string[]>
}

export type ApiErrorKind = 'validation' | 'conflict' | 'unexpected'   // spec 005 adds 'not-found'

export class ApiError extends Error {
  readonly kind: ApiErrorKind
  readonly status: number | null              // null = no response (network failure)
  readonly problem: ProblemDetails | null     // parsed body when it was a JSON object, else null
  readonly fieldErrors: Readonly<Record<string, readonly string[]>>  // non-empty only for 'validation'
}

export function apiGet<T>(path: string, options?: { signal?: AbortSignal }): Promise<T>
export function apiPost<TResponse>(path: string, body: unknown, options?: { signal?: AbortSignal }): Promise<TResponse>
```
How responses are classified:
- **2xx:** the body is parsed as JSON and returned. If the JSON can't be parsed, it's `unexpected` with that status.
- **400** whose body is an object with an `errors` object, where every value is an array of strings: `validation`, with `fieldErrors = errors` and the keys exactly as sent.
- **409:** `conflict`, whatever the body says. Q5: the UI never shows the 409 `title`.
- **Anything else** (400 without a usable `errors`, 404, 405, 415, 5xx, a non-JSON error body): `unexpected`.
- **`fetch` rejects** with anything other than an `AbortError`: `unexpected`, with `status: null`. An `AbortError` is re-thrown unchanged, so TanStack Query's cancellation still works.

Request details:
- `apiPost` sends `Content-Type: application/json` and `JSON.stringify(body)`. Both functions send `Accept: application/json`.
- `path` must start with `/api/`. A call that doesn't is a programming error and throws a plain `Error`.

NFR-005: the `ApiError` message is fixed per kind (for example `"Request failed (409)."`) and never includes request or response values. The client never calls `console.*`.

### DTOs (`src/api/types.ts`)
These mirror `ContactResponse`, `PagedResponse<T>`, and `CreateContactRequest` in `src/MicroCrm.Api/` (camelCase, `null` never omitted).
```ts
export type Contact = {
  id: string; firstName: string; lastName: string | null; email: string | null
  phone: string | null; company: string | null; notes: string | null
  createdAt: string; updatedAt: string   // ISO-8601 UTC; not displayed in this spec
}
export type PagedResponse<T> = { items: T[]; page: number; pageSize: number; totalCount: number }
export type CreateContactRequest = {
  firstName: string; lastName: string | null; email: string | null
  phone: string | null; company: string | null; notes: string | null
}
```

### HTTP calls made by the web app (API unchanged; specs 001/002)
```text
GET  /api/contacts?page={n}&pageSize=20[&search={trimmed term}]
     page and pageSize are ALWAYS sent (AC-005); search only when non-blank (AC-015)
     200 { items: Contact[], page, pageSize, totalCount }; 400 validation (not expected; shown as AC-009 error)
POST /api/contacts   body: CreateContactRequest (trimmed; blank optional fields null)
     201 Contact (+ Location) | 400 validation { errors: { firstName|lastName|email|phone|company|notes|<other>: string[] } }
     | 400 without errors (unexpected) | 409 problem+json (duplicate email) | 5xx (unexpected)
```

### Contacts hooks (`src/features/contacts/useContacts.ts`)
```ts
export const CONTACTS_PAGE_SIZE = 20
export type ContactListParams = { page: number; search: string | null }   // already validated
export const contactKeys = {
  all: ['contacts'] as const,
  list: (params: ContactListParams) => ['contacts', params] as const,
}
export function useContactsList(
  params: ContactListParams,
  options?: { enabled?: boolean },   // false when the address search is too long (AC-049)
): UseQueryResult<PagedResponse<Contact>, Error>
export function useCreateContact(): UseMutationResult<Contact, Error, CreateContactRequest>
```
- `useContactsList` passes TanStack's `signal` to `apiGet`.
- `useCreateContact`'s `onSuccess` calls `queryClient.removeQueries({ queryKey: contactKeys.all })`, and the page navigates after that. We use `removeQueries`, not `invalidateQueries`: with no cache entry, the list mounts in the loading state and fetches, so a list cached before the create is never shown (AC-046).

### Address params (`src/features/contacts/listParams.ts`)
```ts
export const MAX_PAGE = 2147483647            // API int range (ListQuery)
export const MAX_SEARCH_LENGTH = 254          // API ListQuery.MaxSearchLength
export type ListAddress = { params: ContactListParams; searchTooLong: boolean }
export function readListParams(searchParams: URLSearchParams): ListAddress
export function toSearchParams(params: ContactListParams): URLSearchParams
```
`readListParams` rules:
- `page` counts only if it matches `/^\d+$/` and its value is between 1 and `MAX_PAGE`. Anything else (absent, `0`, `-1`, `2.5`, `abc`, ` 2`, `+2`, too large) becomes 1 (AC-027).
- `search` is trimmed. Empty or absent becomes `null`.
- `searchTooLong` is `true` when the trimmed `search` is longer than `MAX_SEARCH_LENGTH` (AC-049). `params.search` still holds the trimmed term, so the box can show it, but the page passes `enabled: false` to `useContactsList`.

`toSearchParams` leaves out `page` when it's 1 and leaves out `search` when it's null, so `/contacts` is the canonical page 1.

**Over-long search in the address (AC-049, Q-A (b)).**
- The page renders the search form with `initialTerm` set to the term and `initialError` set to `maxLengthMessage(254)`. It sends no request and shows no list state (rendering rule 0).
- Submitting a valid term, or Clear search, replaces the address and the normal flow resumes.
- Both the submit check (AC-018) and the address check (AC-049) use `MAX_SEARCH_LENGTH` and `maxLengthMessage`, so they can't diverge.

### Display name (`src/features/contacts/contactName.ts`)
```ts
export function contactDisplayName(c: Pick<Contact, 'firstName' | 'lastName'>): string
// "First Last", or "First" when lastName is null
```

### Form rules (`src/features/contacts/contactFormRules.ts`)
```ts
export type ContactFormField = 'firstName' | 'lastName' | 'email' | 'phone' | 'company' | 'notes'
export const CONTACT_FORM_FIELDS: readonly ContactFormField[]   // form order: firstName, lastName, email, phone, company, notes
export type ContactFormValues = Record<ContactFormField, string>
export type ContactFieldErrors = Partial<Record<ContactFormField, string>>
export const CONTACT_FIELD_MAX: Record<ContactFormField, number>   // 100, 100, 254, 50, 200, 4000
export function validateContactForm(values: ContactFormValues): ContactFieldErrors
export function validateContactField(field: ContactFormField, value: string): string | undefined
export function toCreateContactRequest(values: ContactFormValues): CreateContactRequest
export function isValidEmail(trimmed: string): boolean   // exactly one '@', ≥1 char each side, no /\s/
```
These rules mirror `ContactInput.Parse`. Each value is trimmed first, then:
- `firstName`: blank gives `Required.`, otherwise max length.
- Other fields: max length when not blank.
- Email: a non-blank email that fails `isValidEmail` gives `Must be a valid email address.`, **replacing** a max-length error. The server overwrites the same way.

Lengths are counted in UTF-16 code units (`string.length`), the same as .NET.

### Messages (`src/lib/validationMessages.ts`)
```ts
export const REQUIRED = 'Required.'
export const INVALID_EMAIL = 'Must be a valid email address.'
export function maxLengthMessage(max: number): string   // `Must be ${max} characters or fewer.`
```

### Server error → form errors (`src/features/contacts/contactFormErrors.ts`)
```ts
export type ContactFormServerErrors = { fields: ContactFieldErrors; form: string[] }
export const DUPLICATE_EMAIL_MESSAGE = 'A contact with this email already exists.'
export const SAVE_FAILED_MESSAGE = "The contact couldn't be saved. Try again."
export function toContactFormErrors(error: unknown): ContactFormServerErrors
```
- `validation`: each known key's messages are joined with a space and go under that field. Every message from an unknown key goes to `form`, verbatim (AC-040, AC-041).
- `conflict`: `{ fields: { email: DUPLICATE_EMAIL_MESSAGE }, form: [] }` (AC-042).
- `unexpected`, or any non-`ApiError`: `{ fields: {}, form: [SAVE_FAILED_MESSAGE] }` (AC-043).

### Components (props)
```ts
// src/components/Pagination.tsx: rendered by the page only when totalPages > 1
type PaginationProps = { page: number; totalPages: number; onPageChange: (page: number) => void }

// src/features/contacts/ContactSearchForm.tsx: keyed by the address term, so Back/Clear reset the box
type ContactSearchFormProps = {
  initialTerm: string
  initialError?: string          // set when the address term is too long (AC-049)
  onSearch: (term: string | null) => void
}

// src/features/contacts/ContactForm.tsx: reused by spec 005's edit page
type ContactFormProps = {
  isSaving: boolean
  serverErrors: ContactFormServerErrors | null   // from the latest attempt; null while pending or before any attempt
  onSubmit: (values: ContactFormValues) => void  // only called when client validation passes
  cancelTo: string                                // Cancel link target
}
```

### Routes (`src/App.tsx`)
```text
<Route element={<AppLayout />}>
  index                 -> <Navigate to="/contacts" replace />
  contacts              -> <ContactsPage />
  contacts/new          -> <NewContactPage />
  *                     -> <NotFoundPage />
```
The success navigation uses `navigate('/contacts', { state: { createdContactName: string } })`. `ContactsPage` reads it from `useLocation().state` through a type guard, because the state is `unknown`.

### Exact UI text and roles (tests query these; NFR-002)
| Where | Element | Text / accessible name |
|---|---|---|
| Header (every page) | `banner` landmark | contains text `MicroCRM` (not a heading) |
| | `navigation` named `Main` | link `Contacts` → `/contacts` |
| Not found | `heading` level 1 | `Page not found` |
| | link | `Go to contacts` → `/contacts` |
| List | `heading` level 1 | `Contacts` |
| | link | `New contact` → `/contacts/new` |
| | `search` landmark (`<form role="search">`) | textbox (`type="search"`, role `searchbox`) labelled `Search contacts`; button `Search` |
| | search error | `Must be 254 characters or fewer.` (id `contact-search-error`, via `aria-describedby`; box `aria-invalid="true"`). Same text and linking whether it comes from a submit (AC-018) or from the address (AC-049) |
| | `status` | `Loading contacts…` (U+2026) |
| | `alert` | `Couldn't load contacts.` + button `Try again` |
| | text | `{n} contacts` / `1 contact` (only with results) |
| | `table` named `Contacts` (via `<caption>`) | `columnheader`s `Name`, `Email`, `Company`, `Phone`; one `row` per contact |
| | empty | `No contacts yet.` + link `Add a contact` → `/contacts/new` |
| | no match | `No contacts match "{term}".` + button `Clear search` |
| | empty page | `This page is empty.` + button `Go to first page` |
| | `navigation` named `Pagination` | buttons `Previous`, `Next` (`disabled` at the ends); text `Page {p} of {n}` |
| | `status` | `Contact "{name}" created.` (after a create) |
| Create | `heading` level 1 | `New contact` |
| | textboxes labelled | `First name` (`aria-required="true"`), `Last name`, `Email`, `Phone`, `Company`, `Notes` (textarea) |
| | button | `Save`; while pending `Saving…` (U+2026), `disabled` |
| | link | `Cancel` → `/contacts` |
| | field error | text under the field, id `{field}-error`, linked by `aria-describedby`, field `aria-invalid="true"` |
| | `alert` (form level) | unknown-key messages verbatim, or `The contact couldn't be saved. Try again.` |

## Test strategy
All web tests use Vitest, Testing Library, user-event, and MSW (`FRONTEND_TEST_CMD`). Each page test renders `<App />` through `renderWithProviders` at a route, so routing, providers, and the real client and hooks are all exercised (NFR-001). HTTP is mocked only with MSW. There's no mocking of `fetch`, hooks, or modules. Error responses use `application/problem+json`.

**Shared test utilities** (test-writer owns `web/src/test/*`):
- **`test/setup.ts`**: `beforeAll(server.listen({ onUnhandledRequest: 'error' }))`, `afterEach(server.resetHandlers())`, `afterAll(server.close())`, plus the existing `cleanup`. An unexpected API call fails the test, which backs AC-003 and the "SHALL NOT send" ACs.
- **`test/msw/server.ts`**: `setupServer(...handlers)`.
- **`test/msw/handlers.ts`**: the defaults. `GET /api/contacts` returns an empty page that echoes `page`/`pageSize`. `POST /api/contacts` returns 201 with the posted body plus an `id` and timestamps. Tests override them with `server.use(...)`.
- **`test/msw/fixtures.ts`**:
  - `aContact(overrides?)`: a unique `id` and realistic values.
  - `contactsPage(items, { page = 1, pageSize = 20, totalCount = items.length })`.
- **`test/msw/problems.ts`**:
  - `problemJson(status, body?)`: an `HttpResponse` with `Content-Type: application/problem+json`.
  - `validationProblem(errors)`: 400 with the `errors` dictionary.
  - `conflictProblem(title?)`: 409.
- **`test/msw/requests.ts`**: `recordRequests()` returns a live array of `{ method, url: URL, body?: unknown }`, built from `server.events.on('request:start')`, with the listener removed in `afterEach`. Tests use it to assert that a request was or wasn't sent and what its query and body were.
- **`test/deferred.ts`**: `deferred<T>()` returns `{ promise, resolve }`. A handler awaits it to hold a response open (loading and saving states, late responses).
- **`test/renderWithProviders.tsx`**:
  ```ts
  renderWithProviders(ui: ReactElement, options?: { route?: string; queryClient?: QueryClient })
    : RenderResult & { user: UserEvent; queryClient: QueryClient;
                       getLocation(): { pathname: string; search: string; state: unknown };
                       goBack(): Promise<void> }
  ```
  - It creates a fresh `QueryClient` with `retry: false` (conventions).
  - It wraps `ui` in `<QueryClientProvider>` and `<MemoryRouter initialEntries={[route ?? '/']}>`.
  - A small test-only probe component captures `useLocation()` and `useNavigate()`, so tests can read the current address (AC-001, AC-013, AC-045) and go Back with `navigate(-1)` inside `act` (AC-026).
  - `user` comes from `userEvent.setup()`.

**Techniques:**
- Long values (255, 4000+ characters): use `await user.click(field); await user.paste(value)`, not `user.type`, which is too slow.
- Loading and saving states (AC-008, AC-039, AC-044, AC-046) and late responses (AC-020): hold handlers open with `deferred()`.
- Race in AC-020: the `search=a` handler awaits a gate, while the `search=b` handler answers at once. Release `a` only after `b`'s rows are shown, then assert that `a`'s rows never appear.
- Assert the address with `getLocation()`, and requests with `recordRequests()`. Don't rely on handler side effects.

| AC / NFR | Level | Test file | Notes |
|---|---|---|---|
| (client) | unit (MSW) | `web/src/api/client.test.ts` | Classifies 2xx, 400 ± `errors`, 409, 404, 500, network error, non-JSON bodies, and abort pass-through; message has no values (NFR-005); relative URL (NFR-003) |
| AC-001–AC-004 | component | `web/src/App.test.tsx` | Routes, header on every page, not-found sends no request |
| AC-005–AC-012 | component | `web/src/features/contacts/ContactsPage.test.tsx` | Query params via `recordRequests`; null cells; count singular/plural; `deferred` for loading; 500 problem then success for retry |
| AC-013–AC-020, AC-049 | component | `web/src/features/contacts/ContactsPage.search.test.tsx` | Starts at `/contacts?search=…&page=2` for AC-014/015; AC-020 race with `deferred`; AC-049 opens `/contacts?search=<255 chars>` and asserts zero requests with `recordRequests()` |
| AC-021–AC-028 | component | `web/src/features/contacts/ContactsPage.paging.test.tsx` | `totalCount` 45 (3 pages), 20 (1 page), 21 (2 pages); `goBack()` for AC-026; `?page=0`, `abc`, `-1`, `2.5`, `99999999999` for AC-027 |
| AC-027, AC-049 | unit | `web/src/features/contacts/listParams.test.ts` | Parse table, including `MAX_PAGE` boundaries; `searchTooLong` at 254, 255, and 254 with surrounding spaces |
| AC-029–AC-033, AC-035–AC-037, AC-047, AC-048 | component | `web/src/features/contacts/NewContactPage.validation.test.tsx` | Starts at `/contacts/new` or clicks through from `/contacts` |
| AC-030–AC-034 | unit | `web/src/features/contacts/contactFormRules.test.ts` | Boundary table per field (max, max+1, trimmed max), email cases, trimming and null mapping for the request |
| AC-034, AC-038–AC-046 | component | `web/src/features/contacts/NewContactPage.test.tsx` | POST body via `recordRequests`; `deferred` for saving; 400/409/500/network; list refetch after create |
| AC-040–AC-043 | unit | `web/src/features/contacts/contactFormErrors.test.ts` | Mapping table, including multiple messages for a key and a non-`ApiError` |
| AC-006 (name) | unit | `web/src/features/contacts/contactName.test.ts` | With and without last name |
| NFR-001, NFR-002 | review | all of the above | Reviewer checks queries are by role, label, or text only, and that MSW is used |
| NFR-003, NFR-005 | review + unit | `client.test.ts` | Reviewer greps `web/src` for `fetch(` outside `api/client.ts` and for `console.` |
| NFR-004 | commands | every task's evidence | `LINT_CMD`, `TYPECHECK_CMD`, `FRONTEND_TEST_CMD`, `BUILD_CMD`; starter files deleted in T-02 |
| NFR-006 | manual | FINAL review | Steps in `tasks.md` |

## Dependencies
- **`react-router` ^8.4.0** (runtime). Needed for addresses, address-held list state, Back, and not-found (spec Q9). Declarative mode only. Its transitives are `cookie-es` and `@remix-run/route-pattern`. Peer `react >= 19.2.7` (the app has 19.2.8); engines Node >= 22.22 (local is 24.6). See **ADR-0010**, which compares TanStack Router, wouter, and a hand-rolled History API. Install it with `npm --prefix web install react-router@^8.4.0` as a separate `chore(004)` commit at the start of T-02, so T-02's RED fails for the right reason (missing shell), not a missing module.
- No other new packages. TanStack Query, MSW, Testing Library, user-event, and jest-dom are already installed (ADR-0002). Plain CSS through CSS modules (`*.module.css`) needs no package. Vite supports them, and with `test.css: false` class names are inert in tests, which never query by class anyway.

## Risks & mitigations
- **Client and server rules drift** (lengths, email, whitespace). JS `trim()`/`\s` and .NET `Trim()`/`char.IsWhiteSpace` disagree on a few exotic characters (for example U+0085 and U+FEFF). Mitigations: the API stays the source of truth, AC-040 shows any server field error, and `contactFormRules.test.ts` pins the ASCII and common cases. Lengths are counted in UTF-16 code units on both sides.
- **Email error precedence.** The server reports `Must be a valid email address.` instead of the max-length message when both apply. The client mirrors this. AC-031 tests use a *valid* over-long email (for example `'a'.repeat(250) + '@x.io'`).
- **Error refetch looks like an error.** TanStack keeps `status: 'error'` during a "Try again" refetch. The rendering order (loading checks `isFetching` first) is spelled out above and covered by AC-010 with a held response.
- **Stale list after create.** Handled by `removeQueries` (not `invalidateQueries`) before navigating. AC-046 asserts the loading state, then the new row.
- **The one-off confirmation persists in history state.** A reload or Back to that entry shows it again. That's acceptable for this spec. Any search or paging creates new entries without it.
- **Production retry delay.** `main.tsx` uses TanStack defaults (3 retries with backoff), so a failed list shows its error after about 7 s in the real app. Tests use `retry: false`. Not changed here, because spec 004 has no criterion for it and untested config shouldn't be added. Listed as a follow-up.
- **Native validation interference.** `type="email"` with `noValidate` avoids browser bubbles. jsdom's sanitization only strips the outer whitespace and newlines of an email input, and the app trims anyway.
- **React Router majors move fast** (v7 → v8 in 2026). Pin `^8.4.0`, use only the declarative API (ADR-0010), and upgrade deliberately.
- **Relative URLs in tests.** Verified during planning: Vitest's jsdom environment resolves `fetch('/api/…')` against `http://localhost:3000`. MSW matches relative handler paths. A jsdom `AbortController` signal is accepted, and `HttpResponse.error()` rejects with `TypeError`.

## Rollout
Web only. No API change, no migration, no feature flag. The starter page is replaced outright. Rollback is reverting the spec's commits (and the `react-router` chore commit). Deep-link fallback for production hosting is out of scope (spec non-goal, ADR-0010 consequence).

## Doc drift for /document (not build tasks)
- **ADR-0002:** the stack table says "ESLint (template) + Prettier", but the repo uses **oxlint** (`web/.oxlintrc.json`, `npm run lint` → `oxlint`). Amend it, and add a "Client routing" row → ADR-0010. `docs/conventions.md` has no lint line today. Add one, or leave it to ADR-0002.
- **`web/README.md`** is the Vite starter README. Replace it with MicroCRM web dev/test instructions, or a pointer to the root docs.
- **`docs/architecture.md`:**
  - "Web API client" and "Web features" rows go from planned to built.
  - Add rows for routing (`App.tsx`, ADR-0010), `components/`, `lib/`, and the test utilities.
  - Add a web data-flow note (address → params → query key → client → ProblemDetails → `ApiError`).
  - "Cross-cutting: Validation" names the web rule module.
- **`docs/conventions.md` (Frontend and Frontend tests):**
  - Add `components/`, `lib/`, and the actual contacts file names to the layout.
  - Record declarative-mode React Router and list state in the address.
  - Record the `ApiError` kinds.
  - Document the `renderWithProviders` options (`route`, `getLocation`, `goBack`), `recordRequests`, `deferred`, and `onUnhandledRequest: 'error'`.
  - Advise `user.paste` for long values.
- **`docs/roadmap.md`** row 004 → Done. **CHANGELOG** entry.
- `web/index.html` title (changed in T-02) needs no doc.
