---
name: document
description: Update README, docs, CHANGELOG and ADRs for a completed spec using the documenter subagent.
argument-hint: [spec-id]
disable-model-invocation: true
---

# /document

Spec: **$ARGUMENTS** (if empty, document the current diff against the default branch)

1. Delegate to `documenter` with the spec folder path (or "current diff").
2. Show the user the list of files changed and the CHANGELOG entry.
3. List any follow-ups the documenter reported (e.g. wrong docstrings that need the implementer).
