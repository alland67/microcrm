# ADR-0006: Validation message style

**Status:** Accepted (2026-10-03, with the spec 002 plan)
**Date:** 2026-10-02
**Spec:** specs/002-contacts-api-update-delete

## Context
Validation ProblemDetails responses carry an `errors` dictionary keyed by camelCase field or parameter name (ADR-0004). Spec 001 left the message wording unspecified, and the messages it shipped use three styles:
- `"First name is required."` (names the field in prose),
- `"'page' must be an integer between 1 and 2147483647."` (quotes the parameter, lowercase start),
- `"Must be 100 characters or fewer."` / `"Must be a valid email address."` (sentence case, no field name).

The web UI (specs 004 and 005) will show each message next to its field, where a field name in the message is redundant ("First name: First name is required."). Spec 002 (Q4, resolved 2026-10-02) picks one style for every contacts endpoint and asks for it to be recorded in `docs/conventions.md`, which says changes to that file go through an ADR.

## Options considered
1. **Sentence case, no field name**: `"Required."`, `"Must be 100 characters or fewer."`.
   - Pros: reads well next to a field label; the `errors` key already identifies the field; messages can be shared across fields (one source of rules).
   - Cons: a message read on its own (for example in a log) doesn't say which field it's about. The key is always next to it in the response, so this doesn't matter in practice.
2. **Quoted field-name prefix**: `"'firstName' is required."`.
   - Pros: self-describing.
   - Cons: shows camelCase identifiers to end users; the UI would have to strip them.
3. **Leave as is.**
   - Cons: inconsistent; the UI can't show messages verbatim.

## Decision
We choose **option 1**. Every message in a 400 validation `errors` entry:
- is a sentence that starts with an uppercase letter and ends with a period;
- doesn't name or quote the field or parameter it belongs to (no camelCase key, no `'key'`/`"key"`, no prose subject such as "First name is ...");
- may use a word that describes the expected *format* even if it happens to equal the key, for example `"Must be a valid email address."` under the `email` key (listed as compliant in spec 002).

Canonical messages (one per rule, shared by every field or parameter with that rule):

| Rule | Message |
|---|---|
| Required text missing or blank | `Required.` |
| Text longer than its maximum after trimming | `Must be {max} characters or fewer.` |
| Email not valid | `Must be a valid email address.` |
| Integer query parameter invalid or out of range | `Must be an integer between {min} and {max}.` |

Tests assert the exact canonical message for each rule, plus the generic style checks above.

## Consequences
- Spec 002 aligns the spec 001 messages (create `firstName`, list `page`/`pageSize`/`search`). Status codes and `errors` keys don't change.
- The documenter records the rule in `docs/conventions.md` (Errors row) when spec 002 is documented. The implementer can't edit that file.
- Later specs (to-dos, 003) use the same canonical messages for the same rules and add new ones in the same style.
- The web client can display `errors[field]` verbatim under the field.
