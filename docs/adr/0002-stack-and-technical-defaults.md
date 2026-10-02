# ADR-0002: Stack and technical defaults for MicroCRM

**Status:** Accepted
**Date:** 2026-10-01

## Context
MicroCRM is a new, small, single-user app for contacts and to-dos. The owner chose ASP.NET Core on .NET 10 with REST APIs for the backend and React + TypeScript for the frontend. The harness needs concrete defaults so agents don't make ad-hoc choices.

## Decision

| Area | Choice | Main alternative considered |
|---|---|---|
| Backend runtime | .NET 10 (LTS), SDK pinned via `global.json` | — (owner's choice) |
| API style | **Minimal APIs** with `MapGroup` per feature, `TypedResults` | Controllers: more ceremony; no built-in minimal-API validation parity |
| Validation | DataAnnotations + built-in `AddValidation()` (.NET 10) | FluentValidation: extra dependency, not needed at this size |
| Errors | RFC 9457 ProblemDetails | Custom error envelope |
| Persistence | **EF Core + SQLite** | PostgreSQL: heavier local setup; easy to switch later via EF provider |
| API docs | Built-in `Microsoft.AspNetCore.OpenApi` at `/openapi/v1.json` | Swashbuckle |
| Backend tests | **xUnit v3**, `WebApplicationFactory`, SQLite in-memory, plain `Assert` | NUnit/MSTest; Testcontainers (overkill for SQLite) |
| Frontend build | **Vite** (react-ts template) | Next.js: SSR not needed |
| Server state | **TanStack Query** | Hand-rolled `useEffect` fetching; Redux Toolkit Query |
| Frontend tests | **Vitest + React Testing Library + user-event + MSW** | Jest: slower, extra config with Vite |
| Formatting / lint | `dotnet format`; ESLint (template) + Prettier | — |
| Solution format | `.slnx` (.NET 10 default) | legacy `.sln` |
| Auth | **None in v1** | ASP.NET Core Identity / external IdP: when multi-user or deployed |

## Consequences
- Agents follow `docs/conventions.md`, which encodes these choices.
- Adding any library not listed here requires a plan entry and, if significant, a new ADR.
- Switching SQLite → PostgreSQL later is a provider + connection change plus a migration re-baseline; the tests' in-memory SQLite approach would then need revisiting.
