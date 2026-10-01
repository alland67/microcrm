---
name: bugfix
description: Fix a bug the TDD way. Reproduce it with a failing regression test first, then fix, review, and document.
argument-hint: <bug description, issue link, or error message>
disable-model-invocation: true
---

# /bugfix: regression-test-first bug fix

Bug: **$ARGUMENTS**

1. **Clarify.** If expected vs. actual behavior or reproduction steps are unclear, ask the user (subagents can't).
2. **Mini-spec.** Create `specs/NNN-fix-<slug>/spec.md` from the template with Type `Bug`: expected behavior as ACs (EARS), actual behavior, repro steps, suspected area. Status `Approved` (bug specs don't need a separate approval unless the fix changes intended behavior; if it does, ask the user). Create a one-task `tasks.md`.
3. **RED: `test-writer`.** Write a regression test that reproduces the bug. It must fail with the bug's symptom. If you can't reproduce it, stop and report what was tried.
4. **GREEN: `implementer`.** Fix the root cause (not the symptom), full suite green.
5. **REVIEW: `reviewer`.** Also ask it to check for the same bug pattern elsewhere; report other occurrences as follow-ups, don't fix them in this change.
6. **DOCUMENT: `documenter`.** CHANGELOG entry under **Fixed**.
7. Summarize root cause, fix, regression test name, and follow-ups. Propose commit `fix(<id>): <summary>`.
