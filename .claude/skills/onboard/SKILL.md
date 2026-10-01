---
name: onboard
description: Adopt this harness in an existing codebase. Map the architecture, detect build/test commands, fill harness.env and docs, and baseline test health.
disable-model-invocation: true
---

# /onboard: bring an existing repo under the harness

1. Delegate to `planner` in ONBOARD mode. It should:
   - fill in `docs/architecture.md` from the actual code,
   - detect and write `TEST_CMD`, `TEST_ONE_CMD`, `LINT_CMD`, `TYPECHECK_CMD`, `FORMAT_CMD`, `BUILD_CMD`, `SOURCE_GLOBS`, `TEST_GLOBS` in `.claude/harness.env`,
   - list untested or high-risk areas.
2. Run `TEST_CMD`, `LINT_CMD` and `TYPECHECK_CMD` yourself and record a **baseline**: pass/fail counts, runtime, known failures. If the suite isn't green, list the failures; the user decides whether to fix them first (recommended) or mark them as known.
3. Propose edits to the **Project**, **Conventions** sections of `CLAUDE.md` based on what was found (existing naming, error handling, module layout, commit style). Apply them only after the user agrees.
4. Recommend a first spec: usually characterization tests for the riskiest untested module before any refactor there.
5. Summarize what was configured and what still needs a human decision.
