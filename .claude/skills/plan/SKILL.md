---
name: plan
description: Produce the technical plan and TDD task breakdown for an approved spec using the planner subagent.
argument-hint: <spec-id, e.g. 003>
disable-model-invocation: true
---

# /plan: technical plan + task breakdown

Spec: **$ARGUMENTS**

1. Resolve the spec folder (`specs/$ARGUMENTS-*/`). If it doesn't exist, or `spec.md` Status isn't `Approved`, stop and tell the user to finish `/spec` first.
2. Delegate to the `planner` subagent in PLAN mode with the folder path. It writes `plan.md`, `tasks.md`, and any ADRs.
3. Check the result yourself before showing it:
   - Every AC in `spec.md` appears in the traceability table in `tasks.md`.
   - Every task names its ACs, test file(s), dependencies, and a "done when".
   - Tasks are small (one red-green-refactor cycle) and ordered by dependency.
   If something's missing, send it back to the planner once with specifics.
4. Present: the approach in a few sentences, key interface/data changes, risks, ADRs created, and the task list (ID + title + ACs).
5. **Approval gate.** On explicit user approval, set `**Status:** Planned` in `plan.md` and suggest `/build $ARGUMENTS`.
