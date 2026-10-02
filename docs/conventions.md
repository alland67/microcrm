# MicroCRM Conventions

Read before writing or reviewing code. Changes to this file go through an ADR.

## REST API

| Aspect | Convention |
|---|---|
| Base path | `/api/{resource}` with plural nouns: `/api/contacts`, `/api/todos` |
| IDs | `Guid` (create with `Guid.CreateVersion7()`), route constraint `{id:guid}` |
| Create | `POST /api/contacts` → **201 Created** + `Location` header + resource body |
| Read | `GET /api/contacts/{id}` → **200**, or **404** if missing |
| List | `GET /api/contacts?page=1&pageSize=20&search=...` → **200** with `{ items, page, pageSize, totalCount }`; defaults page=1, pageSize=20, max pageSize=100 |
| Update | `PUT /api/contacts/{id}` (full replace) → **200** + updated resource; **404** if missing |
| Partial actions | Explicit sub-resources over PATCH, e.g. `POST /api/todos/{id}/complete` |
| Delete | `DELETE /api/contacts/{id}` → **204**; **404** if missing |
| Nested | `GET /api/contacts/{id}/todos` for a contact's to-dos |
| JSON | camelCase; enums as strings; dates are ISO-8601 UTC (`DateTimeOffset`); omit nothing, use `null` |
| Errors | RFC 9457 **ProblemDetails** (`application/problem+json`). 400 validation (with `errors` dictionary, field-validation errors only), 404 not found, 409 conflict. A 400 for an unreadable body (malformed JSON, empty body, wrong JSON type) is plain ProblemDetails without `errors`. Never leak exception details. |
| Concurrency | Not in v1 unless a spec asks for it |
| Docs | OpenAPI served at `/openapi/v1.json` in Development |

## Backend (`src/MicroCrm.Api/`)

```
src/MicroCrm.Api/
  Program.cs                   composition root only: services, middleware, MapXxxEndpoints()
  Data/                        AppDbContext, entity configurations, Migrations/
  Features/
    Contacts/
      ContactsEndpoints.cs     static MapContactsEndpoints(this IEndpointRouteBuilder) using MapGroup("/api/contacts")
      ContactDtos.cs           request/response records (never expose EF entities)
      Contact.cs               entity + domain rules
    Todos/ ...
  Common/                      paging, ProblemDetails helpers, TimeProvider usage
```

- Minimal APIs with `MapGroup` per feature, `TypedResults` return types (`Results<Ok<T>, NotFound>`), `.WithName()` + `.WithSummary()` for OpenAPI.
- Validation: plain validation functions called from the handler (`ContactInput.Parse`, `ListQuery.Parse`) that trim first, then validate, and return all errors together; the handler returns `TypedResults.ValidationProblem(errors)`. Set `RouteHandlerOptions.ThrowOnBadRequest = false`. Conflicts use `TypedResults.Problem(statusCode: 409, ...)` so the content type is `application/problem+json`. See ADR-0004.
- `async` all the way; accept and pass `CancellationToken`.
- Inject `TimeProvider` for anything time-based (never `DateTime.UtcNow` directly) so tests can control time.
- Nullable reference types on; warnings are errors (see `Directory.Build.props`).
- EF Core: SQLite file DB in dev (`microcrm.db`, gitignored); one migration per spec task that changes the schema. Keep queries in handlers until duplication justifies extraction.
- SQLite `AlterColumn` migrations: EF rebuilds the table from the migration's Designer model, so hand-editing `Up` does not change the rebuild. Change the model or configuration and regenerate the migration instead.

## Backend tests (`tests/MicroCrm.Api.Tests/`)

- **xUnit v3**, plain `Assert` (no assertion library unless an ADR adds one).
- `Integration/`: preferred for API acceptance criteria. Use `WebApplicationFactory<Program>`, call real HTTP endpoints with `HttpClient`, and assert status code, headers, and JSON body. Replace the DB with a **named shared-cache in-memory SQLite database**, one per fixture (test class), with a connection per request (per `DbContext`). See ADR-0005. Shared setup lives in `Integration/Infrastructure/` (e.g. `ApiFactory`).
- `Unit/`: pure domain logic (entity rules, paging math) with no host.
- Naming: `Method_Scenario_Expected_ACnnn`. One behavior per test; Arrange/Act/Assert.
- Pass `TestContext.Current.CancellationToken` to async calls.
- Use `FakeTimeProvider` (Microsoft.Extensions.TimeProvider.Testing) when time matters.

## Frontend (`web/`)

```
web/src/
  api/          client.ts (fetch wrapper, ProblemDetails → typed error), types.ts (DTOs mirroring the API)
  features/
    contacts/   ContactsPage.tsx, ContactForm.tsx, useContacts.ts (TanStack Query hooks), *.test.tsx
    todos/ ...
  components/   shared presentational components
  test/         setup.ts, msw/handlers.ts, msw/server.ts, renderWithProviders.tsx
```

- TypeScript strict; no `any`, no non-null `!` without a comment explaining why.
- Function components + hooks. Server state lives in **TanStack Query** (`useQuery`/`useMutation`, query keys like `['contacts', { page, search }]`). Local UI state lives in `useState`. No global store unless an ADR adds one.
- All HTTP goes through `api/client.ts` using relative `/api/...` URLs (Vite proxies in dev).
- Accessible by default: real `<button>`, `<label htmlFor>`, form errors linked with `aria-describedby`.
- Styling: plain CSS modules until a spec or ADR says otherwise.

## Frontend tests

- **Vitest + React Testing Library + user-event**, co-located as `Component.test.tsx`.
- Query by role, label, or text the way a user would (`getByRole('button', { name: /save/i })`). Never by class or test id unless there's no accessible alternative.
- Mock the network with **MSW** (`web/src/test/msw/`), not by mocking `fetch` or hooks. Each test overrides handlers for its scenario (`server.use(...)`), including error responses shaped as ProblemDetails.
- Render through `renderWithProviders` (fresh `QueryClient` per test with `retry: false`).
- Test names end with the AC ID: `it('lists contacts sorted by name [AC-004]')`.
