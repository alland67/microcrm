# Engineering Constitution

These are the non-negotiables for every change, human- or agent-authored. Agents treat violations as **Blocking**. Change this file only through an ADR.

## 1. Specs before code
- Behavior changes start from a spec in `specs/` with testable acceptance criteria (ACs). The spec says *what* and *why*; the plan says *how*.
- No `/build` on a spec that isn't `Approved` by a human.
- If implementation reveals the spec is wrong or incomplete, stop and update the spec (with approval) instead of coding around it.

## 2. Tests before code (red → green → refactor)
- No production code is written without a failing test that demands it.
- A new test must fail first, and for the right reason, before the code that satisfies it is written.
- Tests are never deleted, skipped, or weakened to make a change pass. Changing a test means the spec changed.
- Every AC is traced to at least one test; test names or comments carry the AC ID.
- The full suite is green at the end of every task.

## 3. Separation of duties
- The agent that writes tests doesn't write the production code for them, and vice versa.
- The reviewer is independent and read-only on code. Humans approve specs and plans.

## 4. Small, reversible steps
- Tasks are sized to one red-green-refactor cycle. Each leaves the codebase working.
- Commits are small and use Conventional Commits referencing the spec ID.

## 5. Quality floor
- Lint and typecheck (if configured) pass on every task.
- No secrets in code, logs, or tests. Validate all external input. Handle errors explicitly; never swallow them.
- New dependencies require a plan/ADR entry with a reason.

## 6. Docs describe reality
- Docs, CHANGELOG, and ADRs are updated as part of finishing a spec, and they're verified against the code.

<!-- Add project-specific principles below (e.g. performance budgets, accessibility level, API compatibility policy). -->
