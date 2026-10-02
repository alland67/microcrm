# ADR-0003: Case-insensitive text matching, uniqueness, and ordering in SQLite

**Status:** Accepted

> Notes (spec 001 final): `SqliteErrors.IsUniqueConstraintViolation` checks only the extended error code 2067, not the index name. This is correct while `IX_Contacts_Email` is the only unique index; revisit when a second unique index is added.
**Date:** 2026-10-01
**Spec:** specs/001-contacts-api-create-get-list

## Context
Spec 001 requires three case-insensitive behaviors over contact text:
- **Email uniqueness** ignoring case (AC-011), enforced safely under concurrency (AC-016), while contacts with no email never conflict (AC-013) and the email is returned with its original casing (AC-012).
- **Sorting** by last name, then first name, then id, ignoring case, with contacts that have no last name placed last (AC-022, AC-023).
- **Search** as a case-insensitive substring match on first name, last name, or email, where `%`, `_` and `\` in the term match literally (AC-029, AC-033).

Q1 (accepted) limits the guarantee to ASCII letters. The store is SQLite (ADR-0002). An application-level "check, then insert" for uniqueness has a race window, so the database must be the arbiter.

## Options considered
1. **Normalized shadow column** (`NormalizedEmail` = trimmed + lower-cased) with a unique index; sort and search on `lower(...)` expressions.
   - Pros: portable to other providers; .NET lower-casing also covers non-ASCII.
   - Cons: an extra column to keep in sync; sort and search still need `lower()` everywhere, which stops index use and is easy to forget in one query.
2. **`COLLATE NOCASE` on `FirstName`, `LastName`, `Email`** with a unique index on `Email`; sort on the columns directly; search with `LIKE ... ESCAPE '\'`.
   - Pros: one declarative rule in the schema drives uniqueness and ordering consistently. SQLite's `NOCASE` and default `LIKE` both fold exactly ASCII A–Z, which matches Q1. No duplicated data.
   - Cons: SQLite-specific. A provider switch needs a different collation (for example a case-insensitive ICU or `citext` column), and ADR-0002 already lists this as a known switch cost.
3. **Case-insensitive comparisons in application code**, loading rows into memory.
   - Cons: doesn't scale (NFR-003) and can't enforce uniqueness under concurrency. Rejected.

## Decision
We choose **option 2**.
- `FirstName`, `LastName`, and `Email` columns are declared with collation `NOCASE`.
- A **unique index on `Email`** enforces uniqueness at the database. SQLite unique indexes allow many `NULL`s, so contacts without an email never conflict. Emails are trimmed before storage, and empty values become `NULL` (spec Definitions), so "ignoring surrounding whitespace" holds too.
- The API does **not** pre-check for an existing email. It inserts the row and maps the unique-constraint failure (`SqliteException` with extended code 2067, `SQLITE_CONSTRAINT_UNIQUE`, on the `Contacts.Email` index) to `409 Conflict` ProblemDetails. The sequential duplicate (AC-011) and the concurrent duplicate (AC-016) therefore go through the same code path.
- **Ordering:** `ORDER BY (LastName IS NULL), LastName, FirstName, Id`. Name columns sort with their declared `NOCASE` collation. `Id` is stored as text by EF Core and compared with `BINARY` collation (ordinal on the canonical GUID string).
- **Search:** each field is matched with `LIKE @pattern ESCAPE '\'`, where `pattern = '%' + escape(term) + '%'` and `escape` replaces `\` with `\\`, `%` with `\%`, and `_` with `\_` (backslash first). SQLite's built-in `LIKE` is ASCII case-insensitive by default. We don't rely on `string.Contains`, because the SQLite provider may translate it to `instr()`, which is case-sensitive.

## Consequences
- Case-insensitivity is guaranteed for ASCII only. `É` and `é` are treated as different letters in uniqueness, search, and sort. This is documented behavior per Q1.
- Moving to another database provider means revisiting the collation, unique index, and `LIKE` escaping. Integration tests cover all three, so a switch would show up as test failures.
- Leading-wildcard `LIKE` can't use an index, so search is a table scan. That's fine at the NFR-003 scale (10,000 rows). Revisit with FTS5 if volumes grow.
- Changing a column's collation later requires a SQLite table rebuild, which EF Core migrations generate automatically.
