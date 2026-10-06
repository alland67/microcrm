import { http, HttpResponse, type RequestHandler } from 'msw'

// Default handlers. Tests override per scenario with server.use(...).
export const handlers: RequestHandler[] = [
  // An empty page that echoes the requested page and pageSize.
  http.get('/api/contacts', ({ request }) => {
    const url = new URL(request.url)
    const page = Number(url.searchParams.get('page') ?? '1')
    const pageSize = Number(url.searchParams.get('pageSize') ?? '20')
    return HttpResponse.json({ items: [], page, pageSize, totalCount: 0 })
  }),
  // 201 echoing the posted body plus an id and timestamps.
  http.post('/api/contacts', async ({ request }) => {
    const body = (await request.json()) as Record<string, unknown>
    return HttpResponse.json(
      {
        ...body,
        id: '00000000-0000-7000-8000-00000000ffff',
        createdAt: '2026-01-01T09:00:00+00:00',
        updatedAt: '2026-01-01T09:00:00+00:00',
      },
      { status: 201 },
    )
  }),
]
