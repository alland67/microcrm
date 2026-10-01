# Agent Contracts

How the main session (orchestrator) and subagents hand work to each other. Subagents start with a fresh context and can't ask the user questions, so everything they need must be in the delegation, and everything the orchestrator needs must be in the report.

## Roles and boundaries

| Agent | Owns | May write | Must not |
|---|---|---|---|
| planner | spec, plan, tasks, ADRs, onboarding | `specs/`, `docs/adr/`, `docs/architecture.md`, `.claude/harness.env` | write code or tests; approve specs |
| test-writer | RED step | files matching `TEST_GLOBS` | write production code; weaken tests |
| implementer | GREEN + REFACTOR | everything except tests, specs, constitution | edit tests; add dependencies unasked |
| reviewer | independent quality gate | `specs/*/review.md` (+ its memory) | modify code or tests |
| documenter | docs reflect reality | `docs/`, `*.md`, CHANGELOG, README | modify source code |

Boundaries are enforced by `.claude/hooks/path-guard.sh` using globs in `.claude/harness.env`. (File-writing via Bash isn't covered by the hook; agents are instructed not to do it, and the reviewer checks diffs.)

## Delegation message (orchestrator → subagent)
Always include:
- **Spec folder:** `specs/NNN-slug/`
- **Mode / scope:** e.g. `SPEC`, `PLAN`, task `T-03`, `FINAL`
- **AC IDs in scope** (for test-writer/implementer/reviewer)
- **Context the agent can't infer:** user decisions, answers to open questions, failing test names, reviewer findings being addressed
- **Definition of done** for this invocation

## Report (subagent → orchestrator)
Every subagent ends its final message with exactly this block:

```
## REPORT
STATUS: DONE | BLOCKED | APPROVE | CHANGES_REQUESTED
SCOPE: <spec id> <task id | mode>
FILES CHANGED:
- path/to/file (created|modified): one-line purpose
EVIDENCE:
- <command run> -> <result summary, e.g. "12 passed, 2 failed (expected: AC-004, AC-005)">
AC COVERAGE: AC-001 -> test_name[, ...]    (test-writer, reviewer)
FINDINGS: (reviewer only) [Blocking|Should-fix|Nit] [owner] file:line: issue -> fix
OPEN QUESTIONS / BLOCKERS:
- <question for the human, with options and a recommendation>
FOLLOW-UPS:
- <out-of-scope issues noticed, tech debt, doc drift>
```

- `DONE`: the invocation's definition of done is met, with evidence.
- `BLOCKED`: can't proceed without a human decision or out-of-boundary change. Explain exactly what and why.
- `APPROVE` / `CHANGES_REQUESTED`: reviewer verdicts only.

## Status values
- `spec.md`: `Draft` → `Approved` → `In Progress` → `Done` (or `Superseded`)
- `plan.md`: `Draft` → `Planned`
- `tasks.md` items: `[ ]` todo, `[~]` in progress, `[x]` done
