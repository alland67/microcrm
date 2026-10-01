# CLAUDE.md

> Project instructions for Claude Code. Loaded into the main session **and every subagent**, so keep it lean.
> Detail lives in the files referenced below.

@docs/constitution.md

## Project

- **Name:** MicroCRM
- **Purpose:** A small CRM for managing contacts and the to-dos attached to them, exposed through a simple REST API with a React web UI.
- **Stack:** ASP.NET Core (.NET 10) minimal APIs · EF Core + SQLite · xUnit v3 + WebApplicationFactory | React + TypeScript (Vite) · TanStack Query · Vitest + Testing Library + MSW. Decisions: `docs/adr/0002-stack-and-technical-defaults.md`.
- **Layout:** `src/MicroCrm.Api/` (API) · `tests/MicroCrm.Api.Tests/` (backend tests) · `web/` (frontend; tests co-located as `*.test.tsx`, shared test utilities in `web/src/test/`).
- **Architecture:** `docs/architecture.md` · **Backlog:** `docs/roadmap.md`
- **Run locally:** see `DEV_API_CMD` / `DEV_WEB_CMD` in `.claude/harness.env` (Vite proxies `/api` to the API).

## Commands

All build/test/lint commands are defined once in `.claude/harness.env` (hooks read them too).
Always use those commands; never guess an alternative.

## How work flows here

Every change follows **Spec → Plan → Tasks → (Red → Green → Refactor → Review) per task → Docs**.

| Step | Command | Agent | Output |
|---|---|---|---|
| Specify | `/spec <idea>` | planner | `specs/NNN-slug/spec.md` |
| Plan | `/plan NNN` | planner | `plan.md`, `tasks.md` |
| Build | `/build NNN [T-XX]` | test-writer → implementer → reviewer | code + tests, `review.md` |
| Document | `/document NNN` | documenter | docs, CHANGELOG, ADRs |
| Bug | `/bugfix <report>` | all | regression test first |
| Existing repo | `/onboard` | planner | architecture doc, harness.env |
| Progress | `/status` | — | summary |

Human approval gates: after the spec, and after the plan. Do not start `/build` on a spec whose status is not `Approved`.

## Orchestration rules (main session)

- The main session **orchestrates**; subagents do the work. Don't write tests or production code in the main session when a `/build` is running.
- Subagents start with a fresh context and **cannot ask the user questions**. Always pass them: the spec folder path, the task ID, and the acceptance-criteria IDs in scope. Surface any `BLOCKED` report to the user.
- Every subagent ends with the report format in `docs/agent-contracts.md`. Parse the `STATUS` line.
- Trivial chores (typos, dependency bumps, formatting) may skip the spec, but never skip tests for behavior changes.

## Repository map

```
.claude/agents/     subagent definitions (planner, test-writer, implementer, reviewer, documenter)
.claude/skills/     slash-command workflows (/spec, /plan, /build, ...)
.claude/hooks/      guardrail scripts (role write-boundaries, formatting, stop gate)
.claude/harness.env project commands and path globs used by hooks
docs/               constitution, architecture, agent contracts, ADRs, harness guide
specs/              one folder per feature: spec.md, plan.md, tasks.md, review.md
```

## Conventions

- **test-writer, implementer, and reviewer must read `docs/conventions.md` before writing or judging code.** It defines REST shape, error format, folder layout, and test style for both backend and frontend.
- A spec may span backend and frontend, but each task touches one side only (API task, then UI task), so each red-green cycle runs one suite.
- Commits: Conventional Commits (`feat:`, `fix:`, `test:`, `refactor:`, `docs:`, `chore:`), referencing the spec ID, e.g. `feat(003): add rate limiter [T-02]`.
- Test names reference acceptance criteria: C# `CreateContact_WithoutName_Returns400_AC002`; TS `it('shows a validation error when name is empty [AC-002]')`.
