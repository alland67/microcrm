import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { server } from '../test/msw/server'
import { conflictProblem, problemJson, validationProblem } from '../test/msw/problems'
import { ApiError, apiGet, apiPost } from './client'

async function rejectionOf(promise: Promise<unknown>): Promise<unknown> {
  try {
    await promise
  } catch (error) {
    return error
  }
  throw new Error('Expected the promise to reject')
}

async function apiErrorOf(promise: Promise<unknown>): Promise<ApiError> {
  const error = await rejectionOf(promise)
  expect(error).toBeInstanceOf(ApiError)
  return error as ApiError
}

describe('apiGet / apiPost', () => {
  it('apiGet returns the parsed JSON body of a 200 from a relative URL [AC-009]', async () => {
    server.use(http.get('/api/things', () => HttpResponse.json({ items: [1, 2], totalCount: 2 })))

    const result = await apiGet<{ items: number[]; totalCount: number }>('/api/things?page=1')

    expect(result).toEqual({ items: [1, 2], totalCount: 2 })
  })

  it('apiGet sends Accept: application/json [NFR-003]', async () => {
    let accept: string | null = null
    server.use(
      http.get('/api/things', ({ request }) => {
        accept = request.headers.get('accept')
        return HttpResponse.json({})
      }),
    )

    await apiGet('/api/things')

    expect(accept).toContain('application/json')
  })

  it('apiPost sends a JSON body with Content-Type and returns the parsed 201 body [AC-040]', async () => {
    let contentType: string | null = null
    let received: unknown = null
    server.use(
      http.post('/api/things', async ({ request }) => {
        contentType = request.headers.get('content-type')
        received = await request.json()
        return HttpResponse.json({ id: 'abc', name: 'Ada' }, { status: 201 })
      }),
    )

    const result = await apiPost<{ id: string; name: string }>('/api/things', { name: 'Ada' })

    expect(contentType).toContain('application/json')
    expect(received).toEqual({ name: 'Ada' })
    expect(result).toEqual({ id: 'abc', name: 'Ada' })
  })

  it('rejects a 400 with an errors dictionary as kind validation, keys unchanged [AC-040]', async () => {
    const errors = {
      firstName: ['Required.'],
      LastName: ['Must be 100 characters or fewer.', 'Another.'],
    }
    server.use(http.post('/api/things', () => validationProblem(errors)))

    const error = await apiErrorOf(apiPost('/api/things', {}))

    expect(error.kind).toBe('validation')
    expect(error.status).toBe(400)
    expect(error.fieldErrors).toEqual(errors)
    expect(error.problem?.status).toBe(400)
  })

  it('rejects a 400 without errors as kind unexpected [AC-043]', async () => {
    server.use(http.post('/api/things', () => problemJson(400, { title: 'Bad request.' })))

    const error = await apiErrorOf(apiPost('/api/things', {}))

    expect(error.kind).toBe('unexpected')
    expect(error.status).toBe(400)
    expect(error.fieldErrors).toEqual({})
  })

  it.each([
    ['a string', 'Required.'],
    ['an array of numbers', { firstName: [1, 2] }],
    ['a non-array value', { firstName: 'Required.' }],
    ['an array', ['Required.']],
    ['an empty object', {}],
    ['a key with an empty array', { firstName: [] }],
  ])('rejects a 400 whose errors is %s as kind unexpected [AC-043]', async (_label, errors) => {
    server.use(http.post('/api/things', () => problemJson(400, { errors })))

    const error = await apiErrorOf(apiPost('/api/things', {}))

    expect(error.kind).toBe('unexpected')
    expect(error.status).toBe(400)
  })

  it.each(['Conflict', 'Duplicate email', 'Conflict!'])(
    'rejects a 409 problem with title "%s" as kind conflict [AC-042]',
    async (title) => {
      server.use(http.post('/api/things', () => conflictProblem(title)))

      const error = await apiErrorOf(apiPost('/api/things', {}))

      expect(error.kind).toBe('conflict')
      expect(error.status).toBe(409)
    },
  )

  it('rejects a 409 with an empty body as kind conflict [AC-042]', async () => {
    server.use(http.post('/api/things', () => new HttpResponse(null, { status: 409 })))

    const error = await apiErrorOf(apiPost('/api/things', {}))

    expect(error.kind).toBe('conflict')
    expect(error.status).toBe(409)
  })

  it('rejects a 409 with a text/plain body as kind conflict [AC-042]', async () => {
    server.use(
      http.post(
        '/api/things',
        () =>
          new HttpResponse('Conflict', { status: 409, headers: { 'Content-Type': 'text/plain' } }),
      ),
    )

    const error = await apiErrorOf(apiPost('/api/things', {}))

    expect(error.kind).toBe('conflict')
    expect(error.status).toBe(409)
  })

  it.each([422, 500, 404])(
    'rejects a %i problem carrying a valid errors dictionary as kind unexpected, only 400 is validation [AC-043]',
    async (status) => {
      server.use(
        http.post('/api/things', () => problemJson(status, { errors: { firstName: ['x'] } })),
      )

      const error = await apiErrorOf(apiPost('/api/things', {}))

      expect(error.kind).toBe('unexpected')
      expect(error.status).toBe(status)
      expect(error.fieldErrors).toEqual({})
    },
  )

  it.each([
    ['an array', ['x']],
    ['a string', 'boom'],
  ])('rejects a 500 whose JSON body is %s with problem null [AC-043]', async (_label, body) => {
    server.use(http.get('/api/things', () => HttpResponse.json(body, { status: 500 })))

    const error = await apiErrorOf(apiGet('/api/things'))

    expect(error.kind).toBe('unexpected')
    expect(error.status).toBe(500)
    expect(error.problem).toBeNull()
  })

  it('rejects a 500 problem as kind unexpected with its status [AC-043]', async () => {
    server.use(http.get('/api/things', () => problemJson(500)))

    const error = await apiErrorOf(apiGet('/api/things'))

    expect(error.kind).toBe('unexpected')
    expect(error.status).toBe(500)
    expect(error.problem?.status).toBe(500)
  })

  it('rejects a 404 problem as kind unexpected with its status [AC-043]', async () => {
    server.use(http.get('/api/things', () => problemJson(404)))

    const error = await apiErrorOf(apiGet('/api/things'))

    expect(error.kind).toBe('unexpected')
    expect(error.status).toBe(404)
  })

  it('rejects a 502 with a text/plain body as kind unexpected with problem null [AC-009]', async () => {
    server.use(
      http.get(
        '/api/things',
        () =>
          new HttpResponse('Bad gateway', {
            status: 502,
            headers: { 'Content-Type': 'text/plain' },
          }),
      ),
    )

    const error = await apiErrorOf(apiGet('/api/things'))

    expect(error.kind).toBe('unexpected')
    expect(error.status).toBe(502)
    expect(error.problem).toBeNull()
  })

  it('rejects a network failure as kind unexpected with status null [AC-009]', async () => {
    server.use(http.get('/api/things', () => HttpResponse.error()))

    const error = await apiErrorOf(apiGet('/api/things'))

    expect(error.kind).toBe('unexpected')
    expect(error.status).toBeNull()
    expect(error.problem).toBeNull()
  })

  it('rejects a 200 with a non-JSON body as kind unexpected [AC-009]', async () => {
    server.use(
      http.get(
        '/api/things',
        () =>
          new HttpResponse('not json', { status: 200, headers: { 'Content-Type': 'text/plain' } }),
      ),
    )

    const error = await apiErrorOf(apiGet('/api/things'))

    expect(error.kind).toBe('unexpected')
    expect(error.status).toBe(200)
  })

  it('rethrows an aborted request as an AbortError, not an ApiError [AC-009]', async () => {
    server.use(http.get('/api/things', () => HttpResponse.json({})))
    const controller = new AbortController()
    controller.abort()

    const error = await rejectionOf(apiGet('/api/things', { signal: controller.signal }))

    expect(error).not.toBeInstanceOf(ApiError)
    expect((error as { name?: string }).name).toBe('AbortError')
  })

  it('keeps request values out of the error message [NFR-005]', async () => {
    server.use(http.post('/api/things', () => conflictProblem('ada@example.com already exists')))

    const error = await apiErrorOf(apiPost('/api/things', { email: 'ada@example.com' }))

    expect(error.message).not.toContain('ada@example.com')
    expect(error.message).not.toContain('already exists')
  })

  it('throws a plain Error for a path that does not start with /api/ [NFR-003]', async () => {
    const getError = await rejectionOf((async () => apiGet('/other/things'))())
    const postError = await rejectionOf((async () => apiPost('https://example.com/api/x', {}))())

    for (const error of [getError, postError]) {
      expect(error).toBeInstanceOf(Error)
      expect(error).not.toBeInstanceOf(ApiError)
    }
  })
})
