---
name: review
description: Run an independent review with the reviewer subagent on a spec, a task, or the current uncommitted diff.
argument-hint: [spec-id] [task-id | FINAL]
disable-model-invocation: true
---

# /review

Arguments: **$ARGUMENTS**

- With a spec ID (and optional task ID or `FINAL`): delegate to `reviewer` with the spec folder and scope.
- With no arguments: delegate to `reviewer` to review the current diff (`git diff` + staged) against the constitution and architecture; there's no spec to check conformance against, so ask it to flag that as a finding if the change alters behavior.

Present the verdict, then Blocking findings (with owner), then Should-fix. Offer to route Blocking findings to the right agent. Don't fix anything automatically unless the user says so.
