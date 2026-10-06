import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { App } from '../../App'
import { aContact, contactsPage } from '../../test/msw/fixtures'
import { recordRequests } from '../../test/msw/requests'
import { server } from '../../test/msw/server'
import { renderWithProviders } from '../../test/renderWithProviders'

/** Answers every list GET with one row labelled after the requested page, and the given total. */
function listWithTotal(totalCount: number) {
  server.use(
    http.get('/api/contacts', ({ request }) => {
      const page = Number(new URL(request.url).searchParams.get('page'))
      // Stable id per page: a background refetch must not remount the row.
      const id = `00000000-0000-7000-8000-${String(page).padStart(12, '0')}`
      const items = [aContact({ id, firstName: `Pagerow${page}`, lastName: 'Row' })]
      return HttpResponse.json(contactsPage(items, { page, totalCount }))
    }),
  )
}

function listGets(requests: ReturnType<typeof recordRequests>) {
  return requests.filter((r) => r.method === 'GET' && r.url.pathname === '/api/contacts')
}

/** The most recent list GET. Asserts that one was sent, so a missing request fails clearly. */
function lastListGet(requests: ReturnType<typeof recordRequests>) {
  const gets = listGets(requests)
  expect(gets.length).toBeGreaterThan(0)
  return gets[gets.length - 1]
}

function pagination() {
  return screen.getByRole('navigation', { name: 'Pagination' })
}

describe('Contacts list page: paging controls', () => {
  it('shows Previous, Next and "Page 1 of 3" when 45 contacts do not fit on one page [AC-021]', async () => {
    listWithTotal(45)
    renderWithProviders(<App />, { route: '/contacts' })

    await screen.findByText(/Pagerow1 Row/)

    const nav = pagination()
    expect(within(nav).getByRole('button', { name: 'Previous' })).toBeInTheDocument()
    expect(within(nav).getByRole('button', { name: 'Next' })).toBeInTheDocument()
    expect(within(nav).getByText('Page 1 of 3')).toBeInTheDocument()
  })

  it('requests the next page, keeps the search, updates the address and shows page 2 on Next [AC-022]', async () => {
    const requests = recordRequests()
    listWithTotal(45)
    const { user, getLocation } = renderWithProviders(<App />, { route: '/contacts?search=ada' })
    await screen.findByText(/Pagerow1 Row/)

    await user.click(within(pagination()).getByRole('button', { name: 'Next' }))

    expect(await screen.findByText(/Pagerow2 Row/)).toBeInTheDocument()
    expect(screen.queryByText(/Pagerow1 Row/)).not.toBeInTheDocument()
    expect(within(pagination()).getByText('Page 2 of 3')).toBeInTheDocument()
    const last = lastListGet(requests)
    expect(last.url.searchParams.get('page')).toBe('2')
    expect(last.url.searchParams.get('search')).toBe('ada')
    expect(last.url.searchParams.get('pageSize')).toBe('20')
    expect(getLocation().pathname).toBe('/contacts')
    const address = new URLSearchParams(getLocation().search)
    expect(address.get('page')).toBe('2')
    expect(address.get('search')).toBe('ada')
  })

  it('pushes a history entry when paging, so Back returns to the previous page [AC-022]', async () => {
    listWithTotal(45)
    const { user, goBack, getLocation } = renderWithProviders(<App />, { route: '/contacts' })
    await screen.findByText(/Pagerow1 Row/)
    await user.click(within(pagination()).getByRole('button', { name: 'Next' }))
    await screen.findByText(/Pagerow2 Row/)

    await goBack()

    await waitFor(() => expect(screen.getByText(/Pagerow1 Row/)).toBeInTheDocument())
    expect(getLocation().search).toBe('')
  })

  it('requests the previous page and keeps the search on Previous [AC-023]', async () => {
    const requests = recordRequests()
    listWithTotal(45)
    const { user, getLocation } = renderWithProviders(<App />, {
      route: '/contacts?search=ada&page=3',
    })
    await screen.findByText(/Pagerow3 Row/)

    await user.click(within(pagination()).getByRole('button', { name: 'Previous' }))

    expect(await screen.findByText(/Pagerow2 Row/)).toBeInTheDocument()
    expect(within(pagination()).getByText('Page 2 of 3')).toBeInTheDocument()
    const last = lastListGet(requests)
    expect(last.url.searchParams.get('page')).toBe('2')
    expect(last.url.searchParams.get('search')).toBe('ada')
    const address = new URLSearchParams(getLocation().search)
    expect(address.get('page')).toBe('2')
    expect(address.get('search')).toBe('ada')
  })

  it('leaves out page from the address when Previous returns to page 1 [AC-023]', async () => {
    listWithTotal(45)
    const { user, getLocation } = renderWithProviders(<App />, { route: '/contacts?page=2' })
    await screen.findByText(/Pagerow2 Row/)

    await user.click(within(pagination()).getByRole('button', { name: 'Previous' }))

    expect(await screen.findByText(/Pagerow1 Row/)).toBeInTheDocument()
    expect(getLocation().search).toBe('')
  })

  it('disables Previous and enables Next on the first page [AC-024]', async () => {
    listWithTotal(45)
    renderWithProviders(<App />, { route: '/contacts' })
    await screen.findByText(/Pagerow1 Row/)

    const nav = pagination()
    expect(within(nav).getByRole('button', { name: 'Previous' })).toBeDisabled()
    expect(within(nav).getByRole('button', { name: 'Next' })).toBeEnabled()
  })

  it('disables Next and enables Previous on the last page [AC-024]', async () => {
    listWithTotal(45)
    renderWithProviders(<App />, { route: '/contacts?page=3' })
    await screen.findByText(/Pagerow3 Row/)

    const nav = pagination()
    expect(within(nav).getByText('Page 3 of 3')).toBeInTheDocument()
    expect(within(nav).getByRole('button', { name: 'Next' })).toBeDisabled()
    expect(within(nav).getByRole('button', { name: 'Previous' })).toBeEnabled()
  })

  it('enables both buttons on a middle page [AC-024]', async () => {
    listWithTotal(45)
    renderWithProviders(<App />, { route: '/contacts?page=2' })
    await screen.findByText(/Pagerow2 Row/)

    const nav = pagination()
    expect(within(nav).getByRole('button', { name: 'Previous' })).toBeEnabled()
    expect(within(nav).getByRole('button', { name: 'Next' })).toBeEnabled()
  })

  it('shows no paging controls when 20 contacts fit on one page [AC-025]', async () => {
    listWithTotal(20)
    renderWithProviders(<App />, { route: '/contacts' })

    await screen.findByText(/Pagerow1 Row/)

    expect(screen.getByText('20 contacts')).toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Pagination' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Next' })).not.toBeInTheDocument()
  })

  it('shows "Page 1 of 2" when 21 contacts spill onto a second page (boundary) [AC-025]', async () => {
    listWithTotal(21)
    renderWithProviders(<App />, { route: '/contacts' })

    await screen.findByText(/Pagerow1 Row/)

    expect(within(pagination()).getByText('Page 1 of 2')).toBeInTheDocument()
  })

  it('shows no paging controls for a single contact [AC-025]', async () => {
    listWithTotal(1)
    renderWithProviders(<App />, { route: '/contacts' })

    await screen.findByText(/Pagerow1 Row/)

    expect(screen.queryByRole('navigation', { name: 'Pagination' })).not.toBeInTheDocument()
  })
})

describe('Contacts list page: page from the address', () => {
  it.each(['0', 'abc', '-1', '2.5', '99999999999', ' 2', '+2', '1e3'])(
    'requests page 1 when the address has page=%j [AC-027]',
    async (value) => {
      const requests = recordRequests()
      listWithTotal(45)
      renderWithProviders(<App />, { route: `/contacts?page=${encodeURIComponent(value)}` })

      await screen.findByText(/Pagerow1 Row/)

      const gets = listGets(requests)
      expect(gets).toHaveLength(1)
      expect(gets[0].url.searchParams.get('page')).toBe('1')
    },
  )

  it('shows "Page 1 of 3" for an invalid page in the address [AC-027]', async () => {
    listWithTotal(45)
    renderWithProviders(<App />, { route: '/contacts?page=1e3' })

    await screen.findByText(/Pagerow1 Row/)

    expect(within(pagination()).getByText('Page 1 of 3')).toBeInTheDocument()
  })

  it('requests the page from the address when it is a whole number of 1 or more [AC-027]', async () => {
    const requests = recordRequests()
    listWithTotal(45)
    renderWithProviders(<App />, { route: '/contacts?page=2' })

    await screen.findByText(/Pagerow2 Row/)

    expect(lastListGet(requests).url.searchParams.get('page')).toBe('2')
  })
})
