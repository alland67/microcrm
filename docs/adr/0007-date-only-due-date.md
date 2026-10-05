# ADR-0007: Date-only `dueDate` (`DateOnly`, `YYYY-MM-DD`)

**Status:** Accepted (2026-10-05, with the spec 003 plan)
**Date:** 2026-10-05
**Spec:** specs/003-todos-api

## Context
`docs/conventions.md` says: "dates are ISO-8601 UTC (`DateTimeOffset`)". Spec 003 adds a to-do `dueDate` that is a **calendar day**, not an instant (spec Definitions, "Due date"; Q2 and Q9 resolved 2026-10-05):
- It is sent and returned as a string in exactly the form `YYYY-MM-DD`, must be a real calendar date, and may be any date from `0001-01-01` to `9999-12-31`, past dates included (AC-005, AC-012).
- A to-do is overdue when it is open and its due date is before *today*, where today is the UTC calendar date of the server clock (AC-049, AC-050).
- An invalid value must produce a 400 **validation** ProblemDetails with a `dueDate` entry `Must be a valid date in YYYY-MM-DD format.` (AC-012), not a body-binding 400 without `errors`.

Storing a due date as a `DateTimeOffset` (for example midnight UTC) would invent a time and a zone the user never gave, would show up in JSON as `2026-10-05T00:00:00+00:00`, and would make "due today" depend on the reader's time zone.

## Options considered
1. **`DateTimeOffset` at midnight UTC** (follow the convention literally).
   - Pros: no exception to the convention.
   - Cons: the JSON shape contradicts the spec (`YYYY-MM-DD` exactly); invented time part; risk of off-by-one-day bugs in the web client when it converts to local time. Rejected.
2. **`DateOnly` in the model, bound as `DateOnly?` in the request record.**
   - Pros: least code.
   - Cons: a malformed value fails JSON binding, which gives a 400 **without** an `errors` dictionary (ADR-0004), so AC-012 can't be met. System.Text.Json's `DateOnly` reader is also more lenient about formats than the spec allows. Rejected.
3. **`DateOnly` in the entity and response, `string?` in the request, parsed by the pure validation function.**
   - Pros: matches the spec's JSON shape exactly (System.Text.Json writes `DateOnly` as `yyyy-MM-dd`); validation errors join the other field errors in one 400 (ADR-0004 pattern); EF Core's SQLite provider stores `DateOnly` as `TEXT` `'yyyy-MM-dd'`, which sorts and compares correctly as text for 4-digit years, so ordering (AC-044) and the overdue range (AC-049) run in SQL.
   - Cons: an explicit exception to the conventions date rule.

## Decision
We choose **option 3**.
- **Request:** `dueDate` is a `string?`. It is trimmed (spec Definitions: every text value is trimmed). Missing, `null`, empty, or whitespace-only means `null` (AC-006). Otherwise it must satisfy `DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)`. That call was checked on .NET 10 and rejects `2026-10-5`, `226-10-05`, `02026-10-05`, `2026-02-30`, full-width digits, and any time or zone suffix, and accepts `0001-01-01` and `9999-12-31`. On failure the error is `dueDate: ["Must be a valid date in YYYY-MM-DD format."]`.
- **Model and storage:** `DateOnly?` on the entity, stored by EF Core as nullable `TEXT` in `yyyy-MM-dd` form.
- **Response:** `DateOnly?`, serialized by System.Text.Json as `"yyyy-MM-dd"` or `null`.
- **Today** for the overdue rule is `DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime)`, computed per request from the injected `TimeProvider` (never `DateTime.UtcNow`), so tests control it with `FakeTimeProvider`.
- **Rule of thumb for later specs:** a value that names a day (due date, birthday) is a `DateOnly` sent as `YYYY-MM-DD`; a value that names an instant (created, updated, completed) stays a UTC `DateTimeOffset`.

## Consequences
- `docs/conventions.md` (REST table, JSON row) must be amended to: "instants are ISO-8601 UTC `DateTimeOffset`; calendar days are `DateOnly` sent as `YYYY-MM-DD` (ADR-0007)". The documenter makes this change in `/document 003` (the implementer can't edit that file).
- The web client (specs 005, 006) must treat `dueDate` as a plain date string and must not pass it through `new Date(...)`, which would parse it as UTC midnight and shift it a day for users west of UTC.
- Overdue uses the UTC date (Q2). For a user west of UTC a to-do turns overdue in the evening of its due date. This is an accepted trade-off in the spec.
- Text ordering of `DueDate` relies on 4-digit years, which the parse rule guarantees.
