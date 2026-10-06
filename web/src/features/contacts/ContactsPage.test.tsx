import { screen, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { App } from '../../App'
import { deferred } from '../../test/deferred'
import { aContact, contactsPage } from '../../test/msw/fixtures'
import { problemJson } from '../../test/msw/problems'
import { recordRequests } from '../../test/msw/requests'
import { server } from '../../test/msw/server'
import { renderWithProviders } from '../../test/renderWithProviders'

function listReturns(body: ReturnType<typeof contactsPage>) {
  server.use(http.get('/api/contacts', () => HttpResponse.json(body)))
}

describe('Contacts list page: request and table', () => {
  it('requests page 1 with page size 20 and no search term [AC-005]', async () => {
    const requests = recordRequests()
    listReturns(contactsPage([aContact()]))

    renderWithProviders(<App />, { route: '/contacts' })

    expect(await screen.findByRole('table', { name: 'Contacts' })).toBeInTheDocument()
    const gets = requests.filter((r) => r.method === 'GET' && r.url.pathname === '/api/contacts')
    expect(gets).toHaveLength(1)
    expect(gets[0].url.searchParams.get('page')).toBe('1')
    expect(gets[0].url.searchParams.get('pageSize')).toBe('20')
    expect(gets[0].url.searchParams.has('search')).toBe(false)
  })

  it('shows column headers Name, Email, Company, Phone in that order [AC-006]', async () => {
    listReturns(contactsPage([aContact()]))

    renderWithProviders(<App />, { route: '/contacts' })

    const table = await screen.findByRole('table', { name: 'Contacts' })
    const headers = within(table)
      .getAllByRole('columnheader')
      .map((h) => h.textContent)
    expect(headers).toEqual(['Name', 'Email', 'Company', 'Phone'])
  })

  it('shows one row per contact in the order returned, without re-sorting [AC-006]', async () => {
    listReturns(
      contactsPage([
        aContact({ firstName: 'Zed', lastName: 'Zulu', email: 'zed@example.com' }),
        aContact({ firstName: 'Ada', lastName: 'Lovelace', email: 'ada@example.com' }),
        aContact({ firstName: 'Mia', lastName: 'Mills', email: 'mia@example.com' }),
      ]),
    )

    renderWithProviders(<App />, { route: '/contacts' })

    const table = await screen.findByRole('table', { name: 'Contacts' })
    const rows = within(table).getAllByRole('row').slice(1) // skip the header row
    expect(rows).toHaveLength(3)
    const firstCells = rows.map((r) => within(r).getAllByRole('cell')[0].textContent)
    expect(firstCells).toEqual(['Zed Zulu', 'Ada Lovelace', 'Mia Mills'])
  })

  it('shows name, email, company and phone as returned in each cell [AC-006]', async () => {
    listReturns(
      contactsPage([
        aContact({
          firstName: 'Ada',
          lastName: 'Lovelace',
          email: 'ada@example.com',
          company: 'Analytical Engines Ltd',
          phone: '555-0100',
        }),
      ]),
    )

    renderWithProviders(<App />, { route: '/contacts' })

    const table = await screen.findByRole('table', { name: 'Contacts' })
    const row = within(table).getAllByRole('row')[1]
    const cells = within(row)
      .getAllByRole('cell')
      .map((c) => c.textContent)
    expect(cells).toEqual(['Ada Lovelace', 'ada@example.com', 'Analytical Engines Ltd', '555-0100'])
  })

  it('shows empty cells and a first-name-only Name for null values [AC-006]', async () => {
    listReturns(
      contactsPage([
        aContact({ firstName: 'Cher', lastName: null, email: null, company: null, phone: null }),
      ]),
    )

    renderWithProviders(<App />, { route: '/contacts' })

    const table = await screen.findByRole('table', { name: 'Contacts' })
    const row = within(table).getAllByRole('row')[1]
    const cells = within(row)
      .getAllByRole('cell')
      .map((c) => c.textContent)
    expect(cells).toEqual(['Cher', '', '', ''])
  })

  it('shows the total count from the API, plural [AC-007]', async () => {
    listReturns(contactsPage([aContact(), aContact()], { totalCount: 45 }))

    renderWithProviders(<App />, { route: '/contacts' })

    expect(await screen.findByText('45 contacts')).toBeInTheDocument()
  })

  it('shows "1 contact" when the total is one [AC-007]', async () => {
    listReturns(contactsPage([aContact()], { totalCount: 1 }))

    renderWithProviders(<App />, { route: '/contacts' })

    expect(await screen.findByText('1 contact')).toBeInTheDocument()
  })

  it('shows a New contact link to the create page [AC-012]', async () => {
    renderWithProviders(<App />, { route: '/contacts' })

    const link = await screen.findByRole('link', { name: 'New contact' })
    expect(link).toHaveAttribute('href', '/contacts/new')
  })

  it('leads to the create page when New contact is clicked [AC-012]', async () => {
    const { user, getLocation } = renderWithProviders(<App />, { route: '/contacts' })

    await user.click(await screen.findByRole('link', { name: 'New contact' }))

    expect(
      await screen.findByRole('heading', { level: 1, name: 'New contact' }),
    ).toBeInTheDocument()
    expect(getLocation().pathname).toBe('/contacts/new')
  })
})

describe('Contacts list page: loading, error with retry, empty', () => {
  it('shows a loading status and no alert, empty message or table while the request is held [AC-008]', async () => {
    const gate = deferred()
    server.use(
      http.get('/api/contacts', async () => {
        await gate.promise
        return HttpResponse.json(contactsPage([aContact()]))
      }),
    )

    renderWithProviders(<App />, { route: '/contacts' })

    expect(await screen.findByRole('status')).toHaveTextContent('Loading contacts…')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.queryByText('No contacts yet.')).not.toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    // AC-012: the New contact link is shown while the list is still loading
    expect(screen.getByRole('link', { name: 'New contact' })).toHaveAttribute(
      'href',
      '/contacts/new',
    )

    gate.resolve()
    expect(await screen.findByRole('table', { name: 'Contacts' })).toBeInTheDocument()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
  })

  it('shows an alert with Try again, and no table or empty message, after a 500 [AC-009]', async () => {
    server.use(http.get('/api/contacts', () => problemJson(500)))

    renderWithProviders(<App />, { route: '/contacts' })

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent("Couldn't load contacts.")
    expect(within(alert).getByRole('button', { name: 'Try again' })).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    expect(screen.queryByText('No contacts yet.')).not.toBeInTheDocument()
    expect(screen.queryByRole('status')).not.toBeInTheDocument()
    // AC-012: the New contact link is shown after the list failed to load
    expect(screen.getByRole('link', { name: 'New contact' })).toHaveAttribute(
      'href',
      '/contacts/new',
    )
  })

  it('shows an alert with Try again, and no table or empty message, after a network error [AC-009]', async () => {
    server.use(http.get('/api/contacts', () => HttpResponse.error()))

    renderWithProviders(<App />, { route: '/contacts' })

    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent("Couldn't load contacts.")
    expect(screen.getByRole('button', { name: 'Try again' })).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    expect(screen.queryByText('No contacts yet.')).not.toBeInTheDocument()
  })

  it('repeats the same request on Try again, shows loading, then the results [AC-010]', async () => {
    const requests = recordRequests()
    const gate = deferred()
    let calls = 0
    server.use(
      http.get('/api/contacts', async () => {
        calls += 1
        if (calls === 1) return problemJson(500)
        await gate.promise
        return HttpResponse.json(
          contactsPage([aContact({ firstName: 'Grace', lastName: 'Hopper' })]),
        )
      }),
    )

    const { user } = renderWithProviders(<App />, { route: '/contacts' })
    await user.click(await screen.findByRole('button', { name: 'Try again' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Loading contacts…')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()

    gate.resolve()
    const table = await screen.findByRole('table', { name: 'Contacts' })
    expect(within(table).getByText('Grace Hopper')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()

    const gets = requests.filter((r) => r.method === 'GET' && r.url.pathname === '/api/contacts')
    expect(gets).toHaveLength(2)
    expect(gets[1].url.search).toBe(gets[0].url.search)
    expect(gets[1].url.searchParams.get('page')).toBe('1')
    expect(gets[1].url.searchParams.get('pageSize')).toBe('20')
    expect(gets[1].url.searchParams.has('search')).toBe(false)
  })

  it('shows loading, not the stale error, when Try again retries after a failed background refetch [AC-008][AC-010]', async () => {
    const gate = deferred()
    let calls = 0
    server.use(
      http.get('/api/contacts', async () => {
        calls += 1
        if (calls === 1) return HttpResponse.json(contactsPage([aContact()]))
        if (calls === 2) return problemJson(500)
        await gate.promise
        return HttpResponse.json(
          contactsPage([aContact({ firstName: 'Grace', lastName: 'Hopper' })]),
        )
      }),
    )

    const { user, queryClient } = renderWithProviders(<App />, { route: '/contacts' })
    expect(await screen.findByRole('table', { name: 'Contacts' })).toBeInTheDocument()

    // A background refetch of the already-loaded list fails.
    await queryClient.invalidateQueries({ queryKey: ['contacts'] })
    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent("Couldn't load contacts.")

    await user.click(within(alert).getByRole('button', { name: 'Try again' }))

    expect(await screen.findByRole('status')).toHaveTextContent('Loading contacts…')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()

    gate.resolve()
    const table = await screen.findByRole('table', { name: 'Contacts' })
    expect(within(table).getByText('Grace Hopper')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('shows "No contacts yet." with an Add a contact link, no table or paging, when empty [AC-011]', async () => {
    listReturns(contactsPage([], { totalCount: 0 }))

    renderWithProviders(<App />, { route: '/contacts' })

    expect(await screen.findByText('No contacts yet.')).toBeInTheDocument()
    expect(screen.getByRole('link', { name: 'Add a contact' })).toHaveAttribute(
      'href',
      '/contacts/new',
    )
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    expect(screen.queryByRole('navigation', { name: 'Pagination' })).not.toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})
