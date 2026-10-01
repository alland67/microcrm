---
name: spec
description: Start a new feature by drafting a spec with the planner subagent. Use when the user describes a new feature, change, or idea to build.
argument-hint: <feature idea or problem statement>
disable-model-invocation: true
---

# /spec: draft a feature specification

Request: **$ARGUMENTS**

1. **Clarify first (you, not the subagent).** Subagents can't ask the user questions. If the request leaves the *who*, *what problem*, *success criteria*, or *scope boundaries* unclear, ask the user up to 5 focused questions now and wait. Skip this if it's already clear.
2. **Allocate an ID.** List `specs/` and take the next 3-digit number (`001`, `002`, …). Create a short kebab-case slug. Folder: `specs/NNN-slug/`.
3. **Delegate to the `planner` subagent** in SPEC mode. Pass: the folder path, the user's request verbatim, all clarification answers, and pointers to any related existing specs or code areas.
4. **Present the result** to the user: a brief summary, the list of acceptance criteria (IDs + one line each), out-of-scope items, and every open question from the planner's report.
5. **Iterate.** Relay the user's answers/edits back to the planner (resume the same subagent if possible) until there are no open questions.
6. **Approval gate.** Ask the user to approve. On explicit approval, set `**Status:** Approved` and the approval date in `spec.md`, then suggest `/plan NNN`. Never approve on the user's behalf.
