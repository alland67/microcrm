export type ProblemDetails = {
  type?: string
  title?: string
  status?: number
  detail?: string
  instance?: string
  errors?: Record<string, string[]>
}

export type ApiErrorKind = 'validation' | 'conflict' | 'unexpected'

type FieldErrors = Readonly<Record<string, readonly string[]>>

type ApiErrorInit = {
  kind: ApiErrorKind
  status: number | null
  problem?: ProblemDetails | null
  fieldErrors?: FieldErrors
}

export class ApiError extends Error {
  readonly kind: ApiErrorKind
  readonly status: number | null
  readonly problem: ProblemDetails | null
  readonly fieldErrors: FieldErrors

  constructor(init: ApiErrorInit) {
    // Fixed per kind: never includes request or response values (NFR-005).
    super(
      init.status === null ? 'Request failed (no response).' : `Request failed (${init.status}).`,
    )
    this.name = 'ApiError'
    this.kind = init.kind
    this.status = init.status
    this.problem = init.problem ?? null
    this.fieldErrors = init.fieldErrors ?? {}
  }
}

type RequestOptions = { signal?: AbortSignal }

export function apiGet<T>(path: string, options?: RequestOptions): Promise<T> {
  return request<T>('GET', path, undefined, options)
}

export function apiPost<TResponse>(
  path: string,
  body: unknown,
  options?: RequestOptions,
): Promise<TResponse> {
  return request<TResponse>('POST', path, body, options)
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function isFieldErrors(value: unknown): value is Record<string, string[]> {
  if (!isObject(value)) return false
  const lists = Object.values(value)
  return (
    lists.length > 0 &&
    lists.every(
      (messages) =>
        Array.isArray(messages) &&
        messages.length > 0 &&
        messages.every((m) => typeof m === 'string'),
    )
  )
}

async function readJson(response: Response): Promise<unknown> {
  try {
    return await response.json()
  } catch {
    return undefined
  }
}

async function toApiError(response: Response): Promise<ApiError> {
  const parsed = await readJson(response)
  const problem = isObject(parsed) ? (parsed as ProblemDetails) : null
  const status = response.status

  if (status === 409) {
    return new ApiError({ kind: 'conflict', status, problem })
  }
  if (status === 400 && problem && isFieldErrors(problem.errors)) {
    return new ApiError({ kind: 'validation', status, problem, fieldErrors: problem.errors })
  }
  return new ApiError({ kind: 'unexpected', status, problem })
}

async function request<T>(
  method: 'GET' | 'POST',
  path: string,
  body: unknown,
  options?: RequestOptions,
): Promise<T> {
  if (!path.startsWith('/api/')) {
    throw new Error('API paths must start with /api/.')
  }

  const headers: Record<string, string> = { Accept: 'application/json' }
  const init: RequestInit = { method, headers, signal: options?.signal }
  if (method === 'POST') {
    headers['Content-Type'] = 'application/json'
    init.body = JSON.stringify(body)
  }

  let response: Response
  try {
    response = await fetch(path, init)
  } catch (error) {
    if (error instanceof Error && error.name === 'AbortError') throw error
    throw new ApiError({ kind: 'unexpected', status: null })
  }

  if (!response.ok) {
    throw await toApiError(response)
  }

  try {
    return (await response.json()) as T
  } catch {
    throw new ApiError({ kind: 'unexpected', status: response.status })
  }
}
