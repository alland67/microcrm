# ADR-0009: Store to-do timestamps as UTC ticks so lists can order by them in SQL

**Status:** Accepted (2026-10-05, with the spec 003 plan)
**Date:** 2026-10-05
**Spec:** specs/003-todos-api

## Context
Spec 003 orders every to-do list by due date (no due date last), then `createdAt` ascending, then `id` ascending (AC-044, Q3), and pages it with `totalCount` (AC-045). NFR-003 asks for fast lists at 10,000 to-dos, so ordering and paging must run in SQL.

EF Core's SQLite provider stores `DateTimeOffset` as `TEXT` and **refuses to translate `ORDER BY` on a `DateTimeOffset` expression**. Verified on 2026-10-05 with EF Core 10.0.12: `NotSupportedException: SQLite does not support expressions of type 'DateTimeOffset' in ORDER BY clauses.` The contacts list never hit this because it orders by name and id only.

Ordering by `id` instead of `createdAt` is not equivalent. A version 7 GUID carries the creation time only to the millisecond, followed by random bits, so two to-dos created within the same millisecond (common under `FakeTimeProvider`, and possible in production) could come out in a different order from `createdAt` + `id`.

## Options considered
1. **Order on the client** (load all matching rows, sort in memory).
   - Cons: loads every matching row per request; fails NFR-003 at scale. Rejected.
2. **Order by `id` only.**
   - Cons: not the specified order (see above). Rejected.
3. **Value converter `DateTimeOffset` → `long` UTC ticks** (`INTEGER` column) for the to-do timestamps.
   - Pros: SQL ordering and comparison are plain integer operations; ordering works with EF Core (verified: generated SQL is `ORDER BY "DueDate" IS NULL, "DueDate", "CreatedAt", "Id"`); full 100 ns precision; values read back with offset `+00:00`, so JSON matches the contacts endpoints (`2026-01-02T03:04:05+00:00`).
   - Cons: raw database inspection shows integers instead of readable text; storage differs from `Contacts`, whose timestamps stay `TEXT`.
4. **Value converter to fixed-width ISO text.**
   - Pros: human-readable.
   - Cons: custom format and parse code on both sides, easy to get subtly wrong (offsets, fraction width); no benefit for the API. Rejected in favor of 3.
5. **EF Core's built-in `DateTimeOffsetToBinaryConverter`.**
   - Pros: built in.
   - Cons: the stored value packs ticks and offset together, so it's opaque in raw SQL and only orders correctly when every value has the same offset. Rejected in favor of 3, which normalizes to UTC explicitly.

## Decision
We choose **option 3** for all three to-do timestamps: `CreatedAt`, `UpdatedAt`, and `CompletedAt`.
- Converter: write `value.UtcTicks`, read `new DateTimeOffset(ticks, TimeSpan.Zero)`. For the nullable `CompletedAt`, `null` stays `NULL`.
- Columns: `CreatedAt INTEGER NOT NULL`, `UpdatedAt INTEGER NOT NULL`, `CompletedAt INTEGER NULL`.
- The CLR type stays `DateTimeOffset`, so handlers, DTOs, JSON, and `TimeProvider` usage are unchanged. All three use one converter, so the table is uniform and later specs can sort or filter on `completedAt`/`updatedAt` without another migration.
- `Contacts` is **not** changed. Changing its storage would need a table rebuild, which ADR-0008 identifies as hazardous now that to-dos reference contacts.

## Consequences
- Tests or scripts that write `Todos` rows directly must write ticks (`DateTimeOffset.UtcTicks`) into these columns. Tests that inspect rows directly read integers.
- If a later spec needs to order contacts by a timestamp, it faces the same EF Core limitation and should reuse this converter. Moving `Contacts` to it requires a table rebuild (see the ADR-0008 migration hazard).
- `docs/architecture.md` (data model) records the storage types during `/document 003`.
