import { HttpResponse } from 'msw'

const PROBLEM_JSON = 'application/problem+json'

/** A response with Content-Type application/problem+json. */
export function problemJson(status: number, body: Record<string, unknown> = {}) {
  return HttpResponse.json(
    { status, title: `Problem ${status}`, ...body },
    { status, headers: { 'Content-Type': PROBLEM_JSON } },
  )
}

/** 400 with an `errors` dictionary (field-validation problem). */
export function validationProblem(errors: Record<string, string[]>) {
  return problemJson(400, { title: 'One or more validation errors occurred.', errors })
}

/** 409 conflict problem. */
export function conflictProblem(title = 'Conflict') {
  return problemJson(409, { title })
}
