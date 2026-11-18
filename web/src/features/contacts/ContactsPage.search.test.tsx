import { screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { App } from '../../App'
import { deferred } from '../../test/deferred'
import { aContact, contactsPage } from '../../test/msw/fixtures'
import { recordRequests } from '../../test/msw/requests'
import { server } from '../../test/msw/server'
import { renderWithProviders } from '../../test/renderWithProviders'

/** One contact whose first name is the given label, so each result set is distinguishable. */
function rowsFor(label: string) {
  return contactsPage([
    aContact({ firstName: label, lastName: 'Row', email: `${label}@example.com` }),
  ])
}

/** Answers the list by looking up `search` (or `page:N` when there is no search) in `byKey`. */
function listBy(byKey: Record<string, string>) {
  server.use(
    http.get('/api/contacts', ({ request }) => {
      const params = new URL(request.url).searchParams
      const key = params.get('search') ?? `page:${params.get('page')}`
      const label = byKey[key]
      return HttpResponse.json(
        label
          ? { ...rowsFor(label), page: Number(params.get('page')) }
          : contactsPage([], { page: Number(params.get('page')) }),
      )
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

describe('Contacts list page: search', () => {
  it('submits a trimmed term with Enter: page 1, address, and returned rows [AC-013]', async () => {
    const requests = recordRequests()
    listBy({ ada: 'Adaresult' })
    const { user, getLocation } = renderWithProviders(<App />, { route: '/contacts' })
    const box = await screen.findByRole('searchbox', { name: 'Search contacts' })

    await user.type(box, '  ada  {Enter}')

    expect(await screen.findByText(/Adaresult Row/)).toBeInTheDocument()
    const last = lastListGet(requests)
    expect(last.url.searchParams.get('search')).toBe('ada')
    expect(last.url.searchParams.get('page')).toBe('1')
    expect(getLocation().pathname).toBe('/contacts')
    expect(getLocation().search).toBe('?search=ada')
  })

  it('submits a trimmed term with the Search button [AC-013]', async () => {
    const requests = recordRequests()
    listBy({ ada: 'Adaresult' })
    const { user, getLocation } = renderWithProviders(<App />, { route: '/contacts' })
    const box = await screen.findByRole('searchbox', { name: 'Search contacts' })

    await user.type(box, '  ada  ')
    await user.click(screen.getByRole('button', { name: 'Search' }))

    expect(await screen.findByText(/Adaresult Row/)).toBeInTheDocument()
    const last = lastListGet(requests)
    expect(last.url.searchParams.get('search')).toBe('ada')
    expect(last.url.searchParams.get('page')).toBe('1')
    expect(getLocation().search).toBe('?search=ada')
  })

  it('shows different rows for different terms (query key includes search) [AC-013]', async () => {
    listBy({ ada: 'Adaresult', grace: 'Graceresult' })
    const { user } = renderWithProviders(<App />, { route: '/contacts' })
    const box = await screen.findByRole('searchbox', { name: 'Search contacts' })

    await user.type(box, 'ada{Enter}')
    expect(await screen.findByText(/Adaresult Row/)).toBeInTheDocument()
    await user.clear(box)
    await user.type(box, 'grace{Enter}')

    expect(await screen.findByText(/Graceresult Row/)).toBeInTheDocument()
    expect(screen.queryByText(/Adaresult Row/)).not.toBeInTheDocument()
  })

  it('sends no request while typing without submitting [AC-013]', async () => {
    const requests = recordRequests()
    listBy({})
    const { user, getLocation } = renderWithProviders(<App />, { route: '/contacts' })
    const box = await screen.findByRole('searchbox', { name: 'Search contacts' })
    await screen.findByText('No contacts yet.', { exact: false })
    const before = listGets(requests).length

    await user.type(box, 'ada')
    await new Promise((r) => setTimeout(r, 50))

    expect(box).toHaveValue('ada')
    expect(listGets(requests)).toHaveLength(before)
    expect(getLocation().search).toBe('')
  })

  it('shows page 1 of the new results when submitting from another page [AC-014]', async () => {
    const requests = recordRequests()
    listBy({ 'page:3': 'Pagethree', ada: 'Adaresult' })
    const { user, getLocation } = renderWithProviders(<App />, { route: '/contacts?page=3' })
    expect(await screen.findByText(/Pagethree Row/)).toBeInTheDocument()
    const box = screen.getByRole('searchbox', { name: 'Search contacts' })

    await user.type(box, 'ada{Enter}')

    expect(await screen.findByText(/Adaresult Row/)).toBeInTheDocument()
    expect(screen.queryByText(/Pagethree Row/)).not.toBeInTheDocument()
    const last = lastListGet(requests)
    expect(last.url.searchParams.get('page')).toBe('1')
    expect(last.url.searchParams.get('search')).toBe('ada')
    expect(getLocation().search).toBe('?search=ada')
    expect(getLocation().search).not.toContain('page')
  })

  it('requests no search param at page 1 for a whitespace-only term [AC-015]', async () => {
    const requests = recordRequests()
    listBy({ ada: 'Adaresult', 'page:1': 'Unfiltered' })
    const { user, getLocation } = renderWithProviders(<App />, {
      route: '/contacts?search=ada&page=2',
    })
    const box = await screen.findByRole('searchbox', { name: 'Search contacts' })
    expect(await screen.findByText(/Adaresult Row/)).toBeInTheDocument()

    await user.clear(box)
    await user.type(box, '   {Enter}')

    expect(await screen.findByText(/Unfiltered Row/)).toBeInTheDocument()
    const last = lastListGet(requests)
    expect(last.url.searchParams.get('page')).toBe('1')
    expect(last.url.searchParams.has('search')).toBe(false)
    expect(getLocation().pathname).toBe('/contacts')
    expect(getLocation().search).toBe('')
  })

  it('shows the address term in the box and requests the list filtered by it [AC-019]', async () => {
    const requests = recordRequests()
    listBy({ lovelace: 'Lovelaceresult', 'page:1': 'Unfiltered' })

    renderWithProviders(<App />, { route: '/contacts?search=lovelace' })

    expect(await screen.findByText(/Lovelaceresult Row/)).toBeInTheDocument()
    expect(screen.queryByText(/Unfiltered Row/)).not.toBeInTheDocument()
    expect(screen.getByRole('searchbox', { name: 'Search contacts' })).toHaveValue('lovelace')
    const gets = listGets(requests)
    expect(gets[0].url.searchParams.get('search')).toBe('lovelace')
    expect(gets[0].url.searchParams.get('page')).toBe('1')
  })

  it('shows only the latest search when an earlier response arrives late [AC-020]', async () => {
    const requests = recordRequests()
    const gateA = deferred()
    server.use(
      http.get('/api/contacts', async ({ request }) => {
        const params = new URL(request.url).searchParams
        const search = params.get('search')
        if (search === 'a') {
          await gateA.promise
          return HttpResponse.json(rowsFor('Aresult'))
        }
        if (search === 'b') return HttpResponse.json(rowsFor('Bresult'))
        return HttpResponse.json(contactsPage([]))
      }),
    )
    const { user } = renderWithProviders(<App />, { route: '/contacts' })
    const box = await screen.findByRole('searchbox', { name: 'Search contacts' })

    await user.type(box, 'a{Enter}')
    await waitFor(() =>
      expect(listGets(requests).some((r) => r.url.searchParams.get('search') === 'a')).toBe(true),
    )
    // While `a` is in flight the page shows loading, not the previous list's table.
    expect(await screen.findByRole('status')).toHaveTextContent('Loading contacts…')
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    await user.clear(box)
    await user.type(box, 'b{Enter}')
    expect(await screen.findByText(/Bresult Row/)).toBeInTheDocument()

    gateA.resolve()
    await new Promise((r) => setTimeout(r, 100))

    expect(screen.queryByText(/Aresult Row/)).not.toBeInTheDocument()
    expect(screen.getByText(/Bresult Row/)).toBeInTheDocument()
    expect(screen.getByRole('searchbox', { name: 'Search contacts' })).toHaveValue('b')
  })

  it('resets the box to the address term on Back [AC-019]', async () => {
    listBy({ ada: 'Adaresult', bob: 'Bobresult' })
    const { user, goBack, getLocation } = renderWithProviders(<App />, { route: '/contacts' })
    const box = await screen.findByRole('searchbox', { name: 'Search contacts' })
    await user.type(box, 'ada{Enter}')
    expect(await screen.findByText(/Adaresult Row/)).toBeInTheDocument()
    await user.clear(box)
    await user.type(box, 'bob{Enter}')
    expect(await screen.findByText(/Bobresult Row/)).toBeInTheDocument()

    await goBack()

    expect(getLocation().search).toBe('?search=ada')
    // The cached rows may be replaced by a background refetch (fresh ids), so retry the lookup.
    await waitFor(() => expect(screen.getByText(/Adaresult Row/)).toBeInTheDocument())
    expect(screen.queryByText(/Bobresult Row/)).not.toBeInTheDocument()
    expect(screen.getByRole('searchbox', { name: 'Search contacts' })).toHaveValue('ada')
  })

  it('empties unsubmitted box text when the header Contacts link clears the address [AC-019]', async () => {
    listBy({ ada: 'Adaresult' })
    const { user, getLocation } = renderWithProviders(<App />, { route: '/contacts?search=ada' })
    const box = await screen.findByRole('searchbox', { name: 'Search contacts' })
    expect(await screen.findByText(/Adaresult Row/)).toBeInTheDocument()
    expect(box).toHaveValue('ada')
    await user.type(box, 'xyz')

    await user.click(
      within(screen.getByRole('navigation', { name: 'Main' })).getByRole('link', {
        name: 'Contacts',
      }),
    )

    await waitFor(() => expect(getLocation().search).toBe(''))
    expect(screen.getByRole('searchbox', { name: 'Search contacts' })).toHaveValue('')
  })
  describe('no match, clear, and too-long terms', () => {
    const TOO_LONG = 'Must be 254 characters or fewer.'
    const long = (n: number) => 'x'.repeat(n)

    it('shows the no-match message and Clear search, and no table, paging or empty message [AC-016]', async () => {
      listBy({})
      const { user } = renderWithProviders(<App />, { route: '/contacts' })
      const box = await screen.findByRole('searchbox', { name: 'Search contacts' })

      await user.type(box, ' zed {Enter}')

      expect(await screen.findByText('No contacts match "zed".')).toBeInTheDocument()
      expect(screen.getByRole('button', { name: 'Clear search' })).toBeInTheDocument()
      expect(screen.queryByRole('table')).not.toBeInTheDocument()
      expect(screen.queryByRole('navigation', { name: 'Pagination' })).not.toBeInTheDocument()
      expect(screen.queryByText('No contacts yet.', { exact: false })).not.toBeInTheDocument()
    })

    it('empties the box and requests the unfiltered page 1 on Clear search [AC-017]', async () => {
      const requests = recordRequests()
      listBy({ 'page:1': 'Unfiltered' })
      const { user, getLocation } = renderWithProviders(<App />, {
        route: '/contacts?search=zed&page=2',
      })
      await user.click(await screen.findByRole('button', { name: 'Clear search' }))

      expect(await screen.findByText(/Unfiltered Row/)).toBeInTheDocument()
      expect(screen.getByRole('searchbox', { name: 'Search contacts' })).toHaveValue('')
      expect(getLocation().pathname).toBe('/contacts')
      expect(getLocation().search).toBe('')
      const last = lastListGet(requests)
      expect(last.url.searchParams.get('page')).toBe('1')
      expect(last.url.searchParams.has('search')).toBe(false)
    })

    it('shows the 254 message on a submitted term over 254 characters after trimming, and sends nothing [AC-018]', async () => {
      const requests = recordRequests()
      listBy({})
      const { user, getLocation } = renderWithProviders(<App />, { route: '/contacts' })
      const box = await screen.findByRole('searchbox', { name: 'Search contacts' })
      await screen.findByText('No contacts yet.', { exact: false })
      const before = listGets(requests).length
      const term = long(255)

      await user.click(box)
      await user.paste(`  ${term}  `)
      await user.click(screen.getByRole('button', { name: 'Search' }))

      await waitFor(() => expect(box).toHaveAccessibleDescription(TOO_LONG))
      expect(box).toHaveAttribute('aria-invalid', 'true')
      await new Promise((r) => setTimeout(r, 50))
      expect(listGets(requests)).toHaveLength(before)
      expect(listGets(requests).some((r) => r.url.searchParams.get('search') === term)).toBe(false)
      expect(getLocation().search).toBe('')
    })

    it('sends a submitted term of exactly 254 characters (boundary) [AC-018]', async () => {
      const requests = recordRequests()
      listBy({})
      const { user, getLocation } = renderWithProviders(<App />, { route: '/contacts' })
      const box = await screen.findByRole('searchbox', { name: 'Search contacts' })
      const term = long(254)

      await user.click(box)
      await user.paste(` ${term} `)
      await user.click(screen.getByRole('button', { name: 'Search' }))

      await waitFor(() =>
        expect(listGets(requests).some((r) => r.url.searchParams.get('search') === term)).toBe(
          true,
        ),
      )
      expect(getLocation().search).toBe(`?search=${term}`)
      expect(box).not.toHaveAttribute('aria-invalid', 'true')
      expect(screen.queryByText(TOO_LONG)).not.toBeInTheDocument()
    })

    it('removes the 254 message after a valid term is submitted [AC-018]', async () => {
      listBy({ ada: 'Adaresult' })
      const { user } = renderWithProviders(<App />, { route: '/contacts' })
      const box = await screen.findByRole('searchbox', { name: 'Search contacts' })
      await user.click(box)
      await user.paste(long(255))
      await user.click(screen.getByRole('button', { name: 'Search' }))
      await waitFor(() => expect(box).toHaveAccessibleDescription(TOO_LONG))

      await user.clear(box)
      await user.type(box, 'ada{Enter}')

      expect(await screen.findByText(/Adaresult Row/)).toBeInTheDocument()
      expect(screen.queryByText(TOO_LONG)).not.toBeInTheDocument()
      expect(screen.getByRole('searchbox', { name: 'Search contacts' })).not.toHaveAttribute(
        'aria-invalid',
        'true',
      )
    })

    it('shows the no-match message with the trimmed term for a padded address term [AC-016]', async () => {
      listBy({})

      renderWithProviders(<App />, { route: '/contacts?search=%20zed%20' })

      expect(await screen.findByText('No contacts match "zed".')).toBeInTheDocument()
    })

    it('removes the 254 message when the box is cleared and submitted [AC-018]', async () => {
      listBy({})
      const { user } = renderWithProviders(<App />, { route: '/contacts' })
      const box = await screen.findByRole('searchbox', { name: 'Search contacts' })
      await user.click(box)
      await user.paste(long(255))
      await user.click(screen.getByRole('button', { name: 'Search' }))
      await waitFor(() => expect(box).toHaveAccessibleDescription(TOO_LONG))

      await user.clear(box)
      await user.type(box, '{Enter}')

      await waitFor(() => expect(box).not.toHaveAttribute('aria-invalid', 'true'))
      expect(screen.queryByText(TOO_LONG)).not.toBeInTheDocument()
    })

    it('removes the 254 message when the unchanged address term is submitted again [AC-018]', async () => {
      listBy({ ada: 'Adaresult' })
      const { user, getLocation } = renderWithProviders(<App />, {
        route: '/contacts?search=ada',
      })
      const box = await screen.findByRole('searchbox', { name: 'Search contacts' })
      expect(await screen.findByText(/Adaresult Row/)).toBeInTheDocument()
      await user.clear(box)
      await user.paste(long(255))
      await user.click(screen.getByRole('button', { name: 'Search' }))
      await waitFor(() => expect(box).toHaveAccessibleDescription(TOO_LONG))

      await user.clear(box)
      await user.type(box, 'ada{Enter}')

      await waitFor(() => expect(box).not.toHaveAttribute('aria-invalid', 'true'))
      expect(screen.queryByText(TOO_LONG)).not.toBeInTheDocument()
      expect(getLocation().search).toBe('?search=ada')
    })

    describe.each([
      ['an over-long term', (t: string) => t],
      ['an over-long term padded with spaces', (t: string) => `%20%20${t}%20%20`],
    ])('opening the address with %s', (_name, encode) => {
      it('shows the term with the 254 message and none of the list states, and sends no request [AC-049]', async () => {
        const requests = recordRequests()
        listBy({})
        const term = long(255)

        renderWithProviders(<App />, { route: `/contacts?search=${encode(term)}` })

        const box = await screen.findByRole('searchbox', { name: 'Search contacts' })
        expect(box).toHaveValue(term)
        expect(box).toHaveAccessibleDescription(TOO_LONG)
        expect(box).toHaveAttribute('aria-invalid', 'true')
        expect(screen.getByRole('link', { name: 'New contact' })).toHaveAttribute(
          'href',
          '/contacts/new',
        )
        await new Promise((r) => setTimeout(r, 100))
        expect(requests).toHaveLength(0)
        // A disabled query is pending but idle: a loading condition on isPending would show this.
        expect(screen.queryByRole('status')).not.toBeInTheDocument()
        expect(screen.queryByText('Loading contacts…')).not.toBeInTheDocument()
        expect(screen.queryByRole('alert')).not.toBeInTheDocument()
        expect(screen.queryByText('No contacts yet.', { exact: false })).not.toBeInTheDocument()
        expect(screen.queryByText(/No contacts match/)).not.toBeInTheDocument()
        expect(screen.queryByRole('table')).not.toBeInTheDocument()
        expect(screen.queryByRole('navigation', { name: 'Pagination' })).not.toBeInTheDocument()
      })
    })

    it('submitting a valid term from the over-long address state sends one request and removes the message [AC-049]', async () => {
      const requests = recordRequests()
      listBy({ ada: 'Adaresult' })
      const { user, getLocation } = renderWithProviders(<App />, {
        route: `/contacts?search=${long(255)}`,
      })
      const box = await screen.findByRole('searchbox', { name: 'Search contacts' })
      await waitFor(() => expect(box).toHaveAccessibleDescription(TOO_LONG))

      await user.clear(box)
      await user.type(box, 'ada{Enter}')

      expect(await screen.findByText(/Adaresult Row/)).toBeInTheDocument()
      expect(screen.queryByText(TOO_LONG)).not.toBeInTheDocument()
      expect(box).not.toHaveAttribute('aria-invalid', 'true')
      expect(getLocation().search).toBe('?search=ada')
      expect(listGets(requests)).toHaveLength(1)
      const only = listGets(requests)[0]
      expect(only.url.searchParams.get('search')).toBe('ada')
      expect(only.url.searchParams.get('page')).toBe('1')
    })

    it('shows the 254 message again, with no request, on Back to the over-long address [AC-049]', async () => {
      const requests = recordRequests()
      listBy({ ada: 'Adaresult' })
      const term = long(255)
      const { user, goBack, getLocation } = renderWithProviders(<App />, {
        route: `/contacts?search=${term}`,
      })
      const box = await screen.findByRole('searchbox', { name: 'Search contacts' })
      await waitFor(() => expect(box).toHaveAccessibleDescription(TOO_LONG))
      await user.clear(box)
      await user.type(box, 'ada{Enter}')
      expect(await screen.findByText(/Adaresult Row/)).toBeInTheDocument()
      expect(screen.queryByText(TOO_LONG)).not.toBeInTheDocument()
      const before = requests.length

      await goBack()

      expect(getLocation().search).toBe(`?search=${term}`)
      const again = screen.getByRole('searchbox', { name: 'Search contacts' })
      await waitFor(() => expect(again).toHaveAccessibleDescription(TOO_LONG))
      expect(again).toHaveValue(term)
      expect(again).toHaveAttribute('aria-invalid', 'true')
      await new Promise((r) => setTimeout(r, 100))
      expect(requests).toHaveLength(before)
      expect(screen.queryByRole('table')).not.toBeInTheDocument()
      expect(screen.queryByRole('status')).not.toBeInTheDocument()
    })

    it('sends a request for an address term of exactly 254 characters (boundary) [AC-049]', async () => {
      const requests = recordRequests()
      listBy({})
      const term = long(254)

      renderWithProviders(<App />, { route: `/contacts?search=${term}` })

      await waitFor(() => expect(listGets(requests)).toHaveLength(1))
      expect(listGets(requests)[0].url.searchParams.get('search')).toBe(term)
      const box = screen.getByRole('searchbox', { name: 'Search contacts' })
      expect(box).not.toHaveAttribute('aria-invalid', 'true')
      expect(screen.queryByText(TOO_LONG)).not.toBeInTheDocument()
    })
  })
})
