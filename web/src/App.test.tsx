import { screen, within } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { App } from './App'
import { recordRequests } from './test/msw/requests'
import { renderWithProviders } from './test/renderWithProviders'

describe('App shell and routes', () => {
  it('redirects / to the contacts list page [AC-001]', async () => {
    const { getLocation } = renderWithProviders(<App />, { route: '/' })

    expect(await screen.findByRole('heading', { level: 1, name: 'Contacts' })).toBeInTheDocument()
    expect(getLocation().pathname).toBe('/contacts')
  })

  it.each(['/contacts', '/contacts/new', '/nope'])(
    'shows the app header with a Contacts nav link on %s [AC-002]',
    async (route) => {
      renderWithProviders(<App />, { route })

      const banner = await screen.findByRole('banner')
      expect(within(banner).getByText('MicroCRM')).toBeInTheDocument()
      const nav = within(banner).getByRole('navigation', { name: 'Main' })
      expect(within(nav).getByRole('link', { name: 'Contacts' })).toHaveAttribute(
        'href',
        '/contacts',
      )
    },
  )

  it('leads to the list page when the Contacts nav link is clicked [AC-002]', async () => {
    const { user, getLocation } = renderWithProviders(<App />, { route: '/contacts/new' })

    expect(
      await screen.findByRole('heading', { level: 1, name: 'New contact' }),
    ).toBeInTheDocument()
    await user.click(
      within(screen.getByRole('navigation', { name: 'Main' })).getByRole('link', {
        name: 'Contacts',
      }),
    )

    expect(await screen.findByRole('heading', { level: 1, name: 'Contacts' })).toBeInTheDocument()
    expect(getLocation().pathname).toBe('/contacts')
  })

  it.each(['/nope', '/contacts/123/whatever'])(
    'shows Page not found with a link back to the list and sends no request on %s [AC-003]',
    async (route) => {
      const requests = recordRequests()
      const { user, getLocation } = renderWithProviders(<App />, { route })

      expect(
        await screen.findByRole('heading', { level: 1, name: 'Page not found' }),
      ).toBeInTheDocument()
      const link = screen.getByRole('link', { name: 'Go to contacts' })
      expect(link).toHaveAttribute('href', '/contacts')
      expect(requests).toHaveLength(0)

      await user.click(link)
      expect(getLocation().pathname).toBe('/contacts')
    },
  )

  it('shows the create page when /contacts/new is opened directly [AC-004]', async () => {
    renderWithProviders(<App />, { route: '/contacts/new' })

    expect(
      await screen.findByRole('heading', { level: 1, name: 'New contact' }),
    ).toBeInTheDocument()
  })

  it('no longer shows the Vite starter content [NFR-004]', async () => {
    renderWithProviders(<App />, { route: '/contacts' })

    await screen.findByRole('heading', { level: 1, name: 'Contacts' })
    expect(screen.queryByText(/count is/i)).not.toBeInTheDocument()
    expect(screen.queryByRole('img')).not.toBeInTheDocument()
    expect(screen.queryByText(/Get started/i)).not.toBeInTheDocument()
  })
})
