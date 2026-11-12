# ADR-0010: Client-side routing with React Router (declarative mode)

**Status:** Accepted (2026-10-06, with the spec 004 plan)
**Date:** 2026-10-06
**Spec:** specs/004-web-contacts-list-create

## Context
Spec 004 is the first web spec. It needs:
- addresses for the contacts list (`/contacts`) and create page (`/contacts/new`), with `/` redirecting to `/contacts` (AC-001, AC-004);
- the list's search term and page held in the query string, so reload, sharing, and the browser's Back button restore the view (Q1; AC-019, AC-026, AC-027);
- a "Page not found" page for any other address (AC-003);
- navigation after a successful create, carrying a one-off confirmation (AC-045), and on Cancel (AC-047);
- component tests that start at any address and can go Back, without a real browser (NFR-001).

Specs 005 (contact detail with an id in the path) and 006 (to-dos page with filters in the query string) need the same things plus path parameters. The web app has no router today, and ADR-0002 does not list one. Constitution §5 and ADR-0002 require a plan entry and an ADR for a new dependency. The owner chose React Router in declarative mode on 2026-10-06 (spec 004 Q9).

## Options considered
1. **React Router (`react-router` 8.x), declarative mode only** (`<BrowserRouter>`, `<Routes>`/`<Route>`, `<Navigate>`, `<Link>`, `useSearchParams`, `useNavigate`, `useLocation`; `<MemoryRouter initialEntries>` in tests).
   - Pros: the most widely used React router, so it's well documented and familiar to contributors and agents. Declarative mode is a small API surface. It has no loaders, actions, or framework build plugin, so it doesn't compete with TanStack Query for server state. `useSearchParams` covers the address-held list state directly. `MemoryRouter` makes route tests trivial. Peer range `react >= 19.2.7` matches the app (React 19.2.8). The package is one dependency with two small transitives (`cookie-es`, `@remix-run/route-pattern`).
   - Cons: the library also has "data" and "framework" modes we won't use. Docs and examples mix the modes, so agents must stay in declarative mode (recorded in conventions by the documenter). Search params are untyped strings, so we parse and validate them ourselves (wanted anyway for AC-027). Major versions arrive often (v7 to v8 within a year); upgrades are deliberate, not automatic. Engines require Node >= 22.22 (local Node is 24.6; there is no CI yet).
2. **TanStack Router.**
   - Pros: fully type-safe routes and search params with built-in validation; same vendor as TanStack Query.
   - Cons: more concepts (route tree, generated route types or code-based trees, search-param schemas). It works best with its Vite plugin and code generation, which adds tooling to a two-page app. Less familiar. Over-built for three routes now and two more later.
3. **wouter.**
   - Pros: tiny (a few kB), hook-based, simple API, has a memory location for tests.
   - Cons: fewer conventions and docs. Search-param handling is thinner (`useSearch` returns a string; updating it means building URLs by hand). A smaller community and less certain upkeep. We'd likely outgrow it with nested layouts and path params in specs 005 and 006.
4. **Hand-rolled History API** (`history.pushState`, `popstate`, a small `useLocation` hook, a route switch).
   - Pros: no dependency.
   - Cons: we'd own and test path matching, link click interception (modifier keys, `target`), Back/Forward, the redirect, search-param updates, and a memory history for tests. That's real code with edge cases, and every later web spec would extend it. Easy to get subtly wrong (focus, scroll, `popstate` timing).

## Decision
We will use **`react-router` (^8.4.0) in declarative mode only**:
- `main.tsx` wraps the app in `<BrowserRouter>`. Routes are declared with `<Routes>`/`<Route>` in one place (`App.tsx`), with a layout route for the app shell and a `*` route for "Page not found".
- List state lives in the query string through `useSearchParams`; pages navigate with `<Link>`, `<Navigate replace>`, and `useNavigate`.
- Tests render through `renderWithProviders`, which wraps the UI in `<MemoryRouter initialEntries={[route]}>`.
- **Not used:** data mode (`createBrowserRouter`, `RouterProvider`, loaders, actions, `useFetcher`), framework mode (`@react-router/dev`, the Vite plugin, file routes), and `react-router-dom` (merged into `react-router` since v7). Server state stays in TanStack Query (ADR-0002).

## Consequences
- `web/package.json` gains `react-router` as a runtime dependency. It's added as a chore at the start of spec 004 T-02, before the RED step that needs it.
- ADR-0002's stack table gains a "Client routing" row pointing here, and `docs/conventions.md` (Frontend) records declarative mode only, plus the `renderWithProviders` `route` option. The documenter makes both changes when spec 004 is documented.
- Production hosting must serve `index.html` for unknown paths (deep links such as `/contacts/new`). The Vite dev server already does. Deployment is not decided (roadmap), so this is a deployment follow-up, not a spec 004 concern.
- If we later want typed search params or route-level data loading, revisit with a new ADR (TanStack Router is the main alternative). Moving out of declarative mode inside React Router would also need an ADR, because it overlaps with TanStack Query.
