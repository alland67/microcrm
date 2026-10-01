---
name: status
description: Summarize the state of all specs, their tasks, and outstanding review findings.
disable-model-invocation: true
---

# /status

For each `specs/NNN-*/` folder, read `spec.md` (Status), `tasks.md` (count `[x]`, `[~]`, `[ ]`), and `review.md` (latest verdict, open Blocking findings).
Show one compact table: ID, title, status, tasks done/total, latest review verdict, next action.
Then list anything blocked or awaiting human approval. Also report whether `TEST_CMD` is configured and, if it's quick, whether the suite is currently green.
