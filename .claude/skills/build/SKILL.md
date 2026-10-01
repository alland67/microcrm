---
name: build
description: Execute an approved spec's tasks with strict red-green-refactor TDD, orchestrating the test-writer, implementer and reviewer subagents.
argument-hint: <spec-id> [task-id]
disable-model-invocation: true
---

# /build: TDD execution loop

Arguments: **$ARGUMENTS** (spec ID, optionally a single task ID such as `T-03`)

You are the **orchestrator**. Delegate the work; don't write tests or production code yourself.

## Preconditions (stop and tell the user if any fail)
- `specs/<id>-*/spec.md` Status is `Approved` and `plan.md` + `tasks.md` exist.
- `TEST_CMD` in `.claude/harness.env` is set, and the suite is **green** before you start (run it). If it's red, stop: pre-existing failures must be fixed or explicitly acknowledged first.
- Working tree state is known (`git status`). Suggest a feature branch `feat/<id>-<slug>` if on the default branch.

## Per task (in dependency order; only the given task if one was passed)
Mark the task `[~]` (in progress) in `tasks.md`, then:

1. **RED: `test-writer`.** Pass: spec folder, task ID, AC IDs.
   Then verify yourself: run the new tests. They must **fail**, and fail for the **expected reason** (missing behavior / assertion), not a test bug.
   If they pass immediately or fail for the wrong reason, send it back to the test-writer once; if still wrong, stop and ask the user.
2. **GREEN + REFACTOR: `implementer`.** Pass: spec folder, task ID, the failing test names.
   Then verify yourself: full `TEST_CMD` is green; `LINT_CMD`/`TYPECHECK_CMD` pass if configured.
3. **REVIEW: `reviewer`.** Pass: spec folder, task ID.
   - `APPROVE` → go to step 4.
   - `CHANGES_REQUESTED` → route each Blocking finding by owner (`test-writer` for test gaps, which must go RED→GREEN again; `implementer` for code), then re-review.
   - **Max 2 fix cycles per task.** After that, stop and escalate to the user with the outstanding findings.
4. **Close the task.** Mark it `[x]` in `tasks.md` with a one-line note. Propose a Conventional Commit message (`feat(<id>): <summary> [T-XX]`). Commit only if `COMMIT_PER_TASK=1` in harness.env or the user asked.

If any subagent reports `BLOCKED`, stop the loop and put its question to the user verbatim with your recommendation.

## After the last task
1. Run the full suite, lint, and typecheck.
2. `reviewer` with `FINAL`: whole-spec conformance and AC→test traceability.
3. On approval, run the `/document` workflow for this spec (delegate to `documenter`).
4. Set `spec.md` Status to `Done`. Summarize for the user: tasks completed, tests added (count per AC), review findings resolved, follow-ups/tech debt reported by agents, and suggested PR title/description.
