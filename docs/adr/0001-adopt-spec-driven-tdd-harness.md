# ADR-0001: Adopt a spec-driven, test-driven multi-agent workflow

**Status:** Accepted
**Date:** YYYY-MM-DD

## Context
We develop with Claude Code. Single-session agent development tends to drift from intent, skip tests, and grade its own work. We want repeatable quality on both new and existing codebases.

## Decision
All behavior changes follow Spec → Plan → Tasks → Red/Green/Refactor → Review → Docs, using dedicated subagents (planner, test-writer, implementer, reviewer, documenter) with enforced write boundaries, and human approval gates on specs and plans. Rules live in `docs/constitution.md`.

## Consequences
- Easier: traceability from requirement to test, independent review, predictable task size, resumable work.
- Harder: more ceremony for small changes (mitigated: trivial chores may skip the spec, never the tests), more tokens per feature.
