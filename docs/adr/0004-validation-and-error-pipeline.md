# ADR-0004: Request validation and the ProblemDetails error pipeline

**Status:** Accepted
**Date:** 2026-10-01
**Spec:** specs/001-contacts-api-create-get-list

## Context
`docs/conventions.md` says to validate with DataAnnotations plus the built-in `AddValidation()`, and to return all errors as RFC 9457 ProblemDetails. Spec 001 adds requirements that the defaults don't meet:
- Lengths are measured **after trimming**, and whitespace-only optional fields become `null` (AC-005, AC-006, AC-008, AC-009). `[MaxLength]` runs on the raw value before the handler sees it, so it would reject `"  " + 100 chars + "  "`, which the spec says to accept.
- All field errors must arrive together in one 400 response (AC-014).
- Invalid `page`/`pageSize` values, **including non-integers**, must produce a 400 whose `errors` names the parameter (AC-028). If the parameters are bound as `int`, minimal API binding fails first. That gives a 400 with no `errors` dictionary, and in Development it throws (`RouteHandlerOptions.ThrowOnBadRequest` defaults to `true` there).
- Non-GUID ids fail the `{id:guid}` route constraint. No endpoint matches, and ASP.NET Core returns an **empty** 404 body, which breaks AC-037.
- Unhandled exceptions must return a 500 ProblemDetails that leaks nothing (AC-038). In Development, ASP.NET Core adds the developer exception page automatically, and that page includes exception details. The integration tests run in Development because the OpenAPI document is only served there.

## Options considered
1. **DataAnnotations + `AddValidation()` as-is**, with query parameters bound as `int?`.
   - Cons: fails AC-009 at the trimmed boundary, can't name non-integer query parameters (AC-028), and depends on the environment for binding errors. Rejected.
2. **Custom trim-aware `ValidationAttribute`s plus `IValidatableObject`.**
   - Pros: stays in the DataAnnotations family.
   - Cons: trimming logic is split between attributes and the handler. `IValidatableObject` only runs after attribute validation passes, which risks partial error lists (AC-014). Still doesn't help with query parsing.
3. **Explicit, pure validation functions called from the handler**: one normalizes and validates a create request; one parses list query parameters from strings. Combine with an error pipeline that behaves the same in every environment.
   - Pros: deterministic, unit-testable without a host, and all errors are collected in one pass. Behavior doesn't change between environments.
   - Cons: deviates from the conventions text, and handler code is a little more explicit.

## Decision
We choose **option 3**, scoped to the contacts endpoints in spec 001 (later specs may reuse the same pattern):
- **Create body:** every property on the request record is a nullable `string`, so a missing `firstName` reaches validation instead of failing binding. A pure function trims every field, maps empty or whitespace-only optional fields to `null`, and checks required/max-length/email rules **after trimming**. It returns either normalized values or an `errors` dictionary keyed by camelCase field name. The handler returns `TypedResults.ValidationProblem(errors)`. `AddValidation()` is not used for these endpoints.
- **List query:** `page`, `pageSize`, and `search` are bound as `string?` and parsed by a pure function. Bad values become `ValidationProblem` entries keyed `page`, `pageSize`, or `search`.
- **Body binding failures** (malformed JSON, empty body, wrong JSON types): set `RouteHandlerOptions.ThrowOnBadRequest = false` in **all** environments. The framework then sets a 400 without a body, and the status-code-pages middleware turns it into ProblemDetails. AC-015 doesn't require an `errors` dictionary, so none is added.
- **Pipeline** (Program.cs, in this order, in all environments): `AddProblemDetails()`; `UseExceptionHandler()` (no path; it writes ProblemDetails through `IProblemDetailsService` and never includes exception details); `UseStatusCodePages()` (turns any empty 4xx/5xx, such as an unmatched route, a 404 from `TypedResults.NotFound()`, a 400 from a binding failure, or a 415, into `application/problem+json`). Because `UseExceptionHandler` is registered explicitly inside the automatic developer exception page, it handles exceptions first, even in Development.
- **409 Conflict** uses `TypedResults.Problem(statusCode: 409, ...)` so the content type is `application/problem+json`. `TypedResults.Conflict(obj)` would write `application/json`. OpenAPI metadata is declared with `.ProducesProblem(409)`.

## Consequences
- `docs/conventions.md` ("Validation: DataAnnotations on request records + AddValidation()") no longer matches spec 001. When spec 001 is documented, the documenter should amend that line to describe this pattern and reference this ADR.
- Validation rules live in plain functions with unit tests. The integration tests prove they're wired correctly.
- Default ProblemDetails `type`/`title` values come from ASP.NET Core's built-in status mapping, so they stay consistent without custom helpers.
- All error behavior is the same in Development and Production, so tests running in Development prove the production behavior.
