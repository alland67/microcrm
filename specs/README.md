# Specs

One folder per feature or bug: `specs/NNN-short-slug/`, numbered sequentially.

| File | Written by | Purpose |
|---|---|---|
| `spec.md` | planner (`/spec`), approved by a human | What & why, with EARS acceptance criteria `AC-###` |
| `plan.md` | planner (`/plan`) | How: design, interfaces, test strategy, risks |
| `tasks.md` | planner (`/plan`) | Ordered TDD tasks `T-##` mapped to ACs |
| `review.md` | reviewer (`/build`, `/review`) | Findings and verdicts per task and final |

Templates are in `_templates/`. Specs are never deleted; outdated ones are marked `Superseded` with a link to the replacement.
