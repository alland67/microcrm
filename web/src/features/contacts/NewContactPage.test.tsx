import { fireEvent, screen, waitFor } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { describe, expect, it } from 'vitest'
import { App } from '../../App'
import { deferred } from '../../test/deferred'
import { recordRequests } from '../../test/msw/requests'
import { server } from '../../test/msw/server'
import { renderWithProviders } from '../../test/renderWithProviders'

const LABELS = ['First name', 'Last name', 'Email', 'Phone', 'Company', 'Notes'] as const
type Label = (typeof LABELS)[number]

function field(label: Label) {
  return screen.getByRole('textbox', { name: label })
}

function renderCreatePage() {
  const requests = recordRequests()
  const view = renderWithProviders(<App />, { route: '/contacts/new' })
  return { ...view, requests }
}

type Harness = ReturnType<typeof renderCreatePage>

function posts(requests: Harness['requests']) {
  return requests.filter((r) => r.method === 'POST')
}

async function fill(user: Harness['user'], label: Label, value: string) {
  await user.click(field(label))
  await user.paste(value)
}

async function save(user: Harness['user']) {
  await user.click(screen.getByRole('button', { name: 'Save' }))
}

/** Holds every create response open until the test calls `release`. */
function holdCreate() {
  const gate = deferred()
  server.use(
    http.post('/api/contacts', async ({ request }) => {
      const body = (await request.json()) as Record<string, unknown>
      await gate.promise
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
  )
  return { release: () => gate.resolve() }
}

/** Gives a stray extra request time to show up before the test counts them. */
async function settle() {
  await new Promise((resolve) => setTimeout(resolve, 30))
}

describe('Create contact page: sending a valid form', () => {
  it('sends one POST with the trimmed values of all six fields [AC-038]', async () => {
    const { user, requests } = renderCreatePage()
    await fill(user, 'First name', '  Ada ')
    await fill(user, 'Last name', ' Lovelace ')
    await fill(user, 'Email', ' ada@example.com ')
    await fill(user, 'Phone', ' 555-0100 ')
    await fill(user, 'Company', ' Analytical Engines ')
    await fill(user, 'Notes', ' First programmer. ')

    await save(user)

    await waitFor(() => {
      expect(posts(requests)).toHaveLength(1)
      expect(posts(requests)[0].body).toBeDefined()
    })
    const [post] = posts(requests)
    expect(post.url.pathname).toBe('/api/contacts')
    expect(post.body).toEqual({
      firstName: 'Ada',
      lastName: 'Lovelace',
      email: 'ada@example.com',
      phone: '555-0100',
      company: 'Analytical Engines',
      notes: 'First programmer.',
    })
    await settle()
    expect(posts(requests)).toHaveLength(1)
  })

  it('sends the other five fields as null and shows no error when only First name is filled [AC-034]', async () => {
    const { user, requests } = renderCreatePage()
    await fill(user, 'First name', 'Ada')

    await save(user)

    await waitFor(() => {
      expect(posts(requests)[0]?.body).toBeDefined()
    })
    expect(posts(requests)).toHaveLength(1)
    expect(posts(requests)[0].body).toEqual({
      firstName: 'Ada',
      lastName: null,
      email: null,
      phone: null,
      company: null,
      notes: null,
    })
    expect(
      screen.queryByText(/Required\.|characters or fewer\.|valid email/),
    ).not.toBeInTheDocument()
  })

  it('sends one POST with no error when every field is at exactly its maximum length [AC-034]', async () => {
    const { user, requests } = renderCreatePage()
    const values: Record<Label, string> = {
      'First name': 'f'.repeat(100),
      'Last name': 'l'.repeat(100),
      Email: `a@${'x'.repeat(252)}`,
      Phone: 'p'.repeat(50),
      Company: 'c'.repeat(200),
      Notes: 'n'.repeat(4000),
    }
    for (const label of LABELS) await fill(user, label, values[label])

    await save(user)

    await waitFor(() => {
      expect(posts(requests)[0]?.body).toBeDefined()
    })
    expect(posts(requests)).toHaveLength(1)
    expect(posts(requests)[0].body).toEqual({
      firstName: values['First name'],
      lastName: values['Last name'],
      email: values.Email,
      phone: values.Phone,
      company: values.Company,
      notes: values.Notes,
    })
    expect(
      screen.queryByText(/Required\.|characters or fewer\.|valid email/),
    ).not.toBeInTheDocument()
  })

  it('sends nothing when the form is invalid, even with the other fields filled [AC-038]', async () => {
    const { user, requests } = renderCreatePage()
    await fill(user, 'Last name', 'Lovelace')
    await fill(user, 'Email', 'ada@example.com')

    await save(user)

    expect(await screen.findByText('Required.')).toBeInTheDocument()
    await settle()
    expect(posts(requests)).toHaveLength(0)
  })

  it('sends nothing when only an optional field is invalid [AC-038]', async () => {
    const { user, requests } = renderCreatePage()
    await fill(user, 'First name', 'Ada')
    await fill(user, 'Email', 'ada@')

    await save(user)

    expect(await screen.findByText('Must be a valid email address.')).toBeInTheDocument()
    await settle()
    expect(posts(requests)).toHaveLength(0)
  })
})

describe('Create contact page: saving state', () => {
  it('shows a disabled Saving… button while the request is in progress, then Save is replaced only by it [AC-039]', async () => {
    const { release } = holdCreate()
    const { user, requests } = renderCreatePage()
    await fill(user, 'First name', 'Ada')
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()

    await save(user)

    const saving = await screen.findByRole('button', { name: 'Saving…' })
    expect(saving).toBeDisabled()
    expect(screen.queryByRole('button', { name: 'Save' })).not.toBeInTheDocument()
    expect(posts(requests)).toHaveLength(1)
    release()
  })

  it('sends no second POST on a second click of the Saving… button [AC-039]', async () => {
    const { release } = holdCreate()
    const { user, requests } = renderCreatePage()
    await fill(user, 'First name', 'Ada')
    await save(user)
    const saving = await screen.findByRole('button', { name: 'Saving…' })

    await user.click(saving)
    await user.dblClick(saving)
    await settle()

    expect(posts(requests)).toHaveLength(1)
    release()
    await settle()
    expect(posts(requests)).toHaveLength(1)
  })

  it('sends no second POST when Enter is pressed in First name [AC-039]', async () => {
    const { release } = holdCreate()
    const { user, requests } = renderCreatePage()
    await fill(user, 'First name', 'Ada')
    await save(user)
    await screen.findByRole('button', { name: 'Saving…' })

    await user.click(field('First name'))
    await user.keyboard('{Enter}')
    await settle()

    expect(posts(requests)).toHaveLength(1)
    release()
    await settle()
    expect(posts(requests)).toHaveLength(1)
  })

  it('ignores a form submit event that reaches the handler while saving [AC-039]', async () => {
    const { release } = holdCreate()
    const { user, requests, container } = renderCreatePage()
    await fill(user, 'First name', 'Ada')
    await save(user)
    await screen.findByRole('button', { name: 'Saving…' })
    const form = container.querySelector('form')
    expect(form).not.toBeNull()

    if (form) fireEvent.submit(form)
    await settle()

    expect(posts(requests)).toHaveLength(1)
    release()
    await settle()
    expect(posts(requests)).toHaveLength(1)
  })
})
