import { fireEvent, screen, waitFor, within } from '@testing-library/react'
import { http, HttpResponse } from 'msw'
import { useEffect } from 'react'
import { useNavigate } from 'react-router'
import { describe, expect, it, onTestFinished, vi } from 'vitest'
import { App } from '../../App'
import { deferred } from '../../test/deferred'
import { aContact, contactsPage } from '../../test/msw/fixtures'
import { recordRequests } from '../../test/msw/requests'
import { conflictProblem, validationProblem } from '../../test/msw/problems'
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

function renderAt(route: string, ui = <App />) {
  const requests = recordRequests()
  const view = renderWithProviders(ui, { route })
  return { ...view, requests }
}

function listGets(requests: Harness['requests']) {
  return requests.filter((r) => r.method === 'GET' && r.url.pathname === '/api/contacts')
}

/** The confirmation text, which must sit inside an element with role `status`. */
async function findConfirmation(text: string) {
  const element = await screen.findByText(text)
  expect(element.closest('[role="status"]')).not.toBeNull()
}

async function openNewAndSave(user: Harness['user'], firstName: string, lastName?: string) {
  await user.click(await screen.findByRole('link', { name: 'New contact' }))
  await fill(user, 'First name', firstName)
  if (lastName) await fill(user, 'Last name', lastName)
  await save(user)
}

describe('Create contact page: success goes back to the list', () => {
  it('returns to /contacts with no search or page and confirms the new contact by full name [AC-045]', async () => {
    const { user, getLocation } = renderAt('/contacts?search=zed&page=2')

    await openNewAndSave(user, 'Ada', 'Lovelace')

    await findConfirmation('Contact "Ada Lovelace" created.')
    expect(getLocation().pathname).toBe('/contacts')
    expect(getLocation().search).toBe('')
    expect(screen.getByRole('heading', { level: 1, name: 'Contacts' })).toBeInTheDocument()
  })

  it('names a first-name-only contact by first name alone [AC-045]', async () => {
    const { user } = renderAt('/contacts')

    await openNewAndSave(user, 'Cher')

    await findConfirmation('Contact "Cher" created.')
  })

  it('shows no confirmation on the list before any create [AC-045]', async () => {
    renderAt('/contacts')

    await screen.findByRole('heading', { level: 1, name: 'Contacts' })
    expect(screen.queryByText(/created\./)).not.toBeInTheDocument()
  })

  it('stays on the create page and shows no confirmation while the POST is held [AC-045]', async () => {
    const { release } = holdCreate()
    const { user, getLocation } = renderAt('/contacts')

    await openNewAndSave(user, 'Ada', 'Lovelace')
    await screen.findByRole('button', { name: 'Saving…' })

    expect(getLocation().pathname).toBe('/contacts/new')
    expect(screen.queryByText(/created\./)).not.toBeInTheDocument()
    release()
    expect(await screen.findByText('Contact "Ada Lovelace" created.')).toBeInTheDocument()
  })
})

describe('Create contact page: the list is fetched again after a create', () => {
  it('shows the loading state, not a stale table, while the second GET is held, then the new row [AC-046]', async () => {
    const second = deferred()
    let calls = 0
    const babbage = aContact({ firstName: 'Charles', lastName: 'Babbage' })
    const lovelace = aContact({ firstName: 'Ada', lastName: 'Lovelace' })
    server.use(
      http.get('/api/contacts', async () => {
        calls += 1
        if (calls === 1) return HttpResponse.json(contactsPage([babbage]))
        await second.promise
        return HttpResponse.json(contactsPage([babbage, lovelace]))
      }),
    )
    const { user, requests } = renderAt('/contacts')
    expect(await screen.findByText(/Babbage/)).toBeInTheDocument()

    await openNewAndSave(user, 'Ada', 'Lovelace')

    expect(await screen.findByText('Contact "Ada Lovelace" created.')).toBeInTheDocument()
    expect(await screen.findByText('Loading contacts…')).toBeInTheDocument()
    expect(screen.queryByRole('table')).not.toBeInTheDocument()
    expect(screen.queryByText(/Babbage/)).not.toBeInTheDocument()

    second.resolve()

    expect(await screen.findByText(/Lovelace/, { selector: 'td' })).toBeInTheDocument()
    expect(screen.getByText(/Babbage/)).toBeInTheDocument()
    expect(listGets(requests)).toHaveLength(2)
  })
})

/** Navigates to /contacts carrying arbitrary history state, which `route` cannot express. */
function GoToListWithState({ state }: { state: unknown }) {
  const navigate = useNavigate()
  useEffect(() => {
    navigate('/contacts', { state, replace: true })
  }, [navigate, state])
  return null
}

describe('Contacts list: the confirmation state is read defensively', () => {
  it('shows the confirmation for well-formed state (proves the harness) [AC-045]', async () => {
    renderAt(
      '/contacts',
      <>
        <GoToListWithState state={{ createdContactName: 'Ada Lovelace' }} />
        <App />
      </>,
    )

    expect(await screen.findByText('Contact "Ada Lovelace" created.')).toBeInTheDocument()
  })

  it.each([
    ['a string', 'Contact created'],
    ['a number', 42],
    ['null', null],
    ['an empty object', {}],
    ['an array', ['Ada']],
    ['a non-string name', { createdContactName: 5 }],
    ['a null name', { createdContactName: null }],
    ['an object name', { createdContactName: { first: 'Ada' } }],
  ])(
    'shows no confirmation and still renders the list for %s as state [AC-045]',
    async (_label, state) => {
      renderAt(
        '/contacts',
        <>
          <GoToListWithState state={state} />
          <App />
        </>,
      )

      expect(await screen.findByText('No contacts yet.', { exact: false })).toBeInTheDocument()
      expect(screen.getByRole('heading', { level: 1, name: 'Contacts' })).toBeInTheDocument()
      expect(screen.queryByText(/created\./)).not.toBeInTheDocument()
    },
  )
})

const LAST_LONG = 'Must be 100 characters or fewer.'
const INVALID_EMAIL = 'Must be a valid email address.'
const DUPLICATE = 'A contact with this email already exists.'

/** Fills every field with client-valid values, so only the server can object. */
async function fillAllValid(user: Harness['user']) {
  await fill(user, 'First name', 'Ada')
  await fill(user, 'Last name', 'Lovelace')
  await fill(user, 'Email', 'ada@example.com')
  await fill(user, 'Phone', '555-0100')
  await fill(user, 'Company', 'Analytical Engines')
  await fill(user, 'Notes', 'First programmer.')
}

function expectAllValuesKept() {
  expect(field('First name')).toHaveValue('Ada')
  expect(field('Last name')).toHaveValue('Lovelace')
  expect(field('Email')).toHaveValue('ada@example.com')
  expect(field('Phone')).toHaveValue('555-0100')
  expect(field('Company')).toHaveValue('Analytical Engines')
  expect(field('Notes')).toHaveValue('First programmer.')
}

describe('Create contact page: server validation errors', () => {
  it('shows each 400 message verbatim under its field and keeps everything typed [AC-040]', async () => {
    server.use(
      http.post('/api/contacts', () =>
        validationProblem({ lastName: [LAST_LONG], email: [INVALID_EMAIL] }),
      ),
    )
    const { user, requests, getLocation } = renderCreatePage()
    await fillAllValid(user)

    await save(user)

    expect(await screen.findByText(LAST_LONG)).toBeInTheDocument()
    expect(field('Last name')).toHaveAccessibleDescription(LAST_LONG)
    expect(field('Last name')).toHaveAttribute('aria-invalid', 'true')
    expect(field('Email')).toHaveAccessibleDescription(INVALID_EMAIL)
    expect(field('Email')).toHaveAttribute('aria-invalid', 'true')
    for (const label of ['First name', 'Phone', 'Company', 'Notes'] as const) {
      expect(field(label)).not.toHaveAttribute('aria-invalid', 'true')
    }
    expectAllValuesKept()
    expect(screen.getByRole('heading', { level: 1, name: 'New contact' })).toBeInTheDocument()
    expect(getLocation().pathname).toBe('/contacts/new')
    expect(posts(requests)).toHaveLength(1)
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('re-enables Save after a 400 so the owner can try again [AC-040]', async () => {
    server.use(http.post('/api/contacts', () => validationProblem({ lastName: [LAST_LONG] })))
    const { user } = renderCreatePage()
    await fillAllValid(user)

    await save(user)

    await screen.findByText(LAST_LONG)
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()
  })

  it('shows a message for an unknown key in an alert and keeps the typed values [AC-041]', async () => {
    server.use(
      http.post('/api/contacts', () => validationProblem({ nickname: ['Something odd.'] })),
    )
    const { user, getLocation } = renderCreatePage()
    await fillAllValid(user)

    await save(user)

    expect(await screen.findByRole('alert')).toHaveTextContent('Something odd.')
    expectAllValuesKept()
    expect(getLocation().pathname).toBe('/contacts/new')
    for (const label of LABELS) {
      expect(field(label)).not.toHaveAttribute('aria-invalid', 'true')
    }
  })

  it('shows a known-key message under its field and an unknown-key message in an alert at once [AC-040, AC-041]', async () => {
    server.use(
      http.post('/api/contacts', () =>
        validationProblem({
          company: ['Must be 200 characters or fewer.'],
          nickname: ['Something odd.'],
        }),
      ),
    )
    const { user } = renderCreatePage()
    await fillAllValid(user)

    await save(user)

    expect(await screen.findByRole('alert')).toHaveTextContent('Something odd.')
    expect(field('Company')).toHaveAccessibleDescription('Must be 200 characters or fewer.')
    expect(screen.getByRole('alert')).not.toHaveTextContent('200 characters')
  })

  it('replaces the old server errors when a resubmit is rejected differently [AC-040]', async () => {
    let calls = 0
    server.use(
      http.post('/api/contacts', () => {
        calls += 1
        return calls === 1
          ? validationProblem({ lastName: [LAST_LONG] })
          : validationProblem({ company: ['Must be 200 characters or fewer.'] })
      }),
    )
    const { user, requests } = renderCreatePage()
    await fillAllValid(user)
    await save(user)
    await screen.findByText(LAST_LONG)

    await save(user)

    expect(await screen.findByText('Must be 200 characters or fewer.')).toBeInTheDocument()
    expect(screen.queryByText(LAST_LONG)).not.toBeInTheDocument()
    expect(field('Last name')).not.toHaveAttribute('aria-invalid', 'true')
    expect(field('Company')).toHaveAttribute('aria-invalid', 'true')
    expect(posts(requests)).toHaveLength(2)
  })
})

describe('Create contact page: duplicate email', () => {
  it('shows the duplicate message under Email, not the problem title, and keeps everything typed [AC-042]', async () => {
    server.use(http.post('/api/contacts', () => conflictProblem('Conflict!')))
    const { user, getLocation } = renderCreatePage()
    await fillAllValid(user)

    await save(user)

    expect(await screen.findByText(DUPLICATE)).toBeInTheDocument()
    expect(field('Email')).toHaveAccessibleDescription(DUPLICATE)
    expect(field('Email')).toHaveAttribute('aria-invalid', 'true')
    expect(screen.queryByText(/Conflict!/)).not.toBeInTheDocument()
    expectAllValuesKept()
    expect(screen.getByRole('heading', { level: 1, name: 'New contact' })).toBeInTheDocument()
    expect(getLocation().pathname).toBe('/contacts/new')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Save' })).toBeEnabled()
  })
})

describe('Create contact page: focus after a server error', () => {
  it('moves focus to Email after a 409 [AC-036]', async () => {
    server.use(http.post('/api/contacts', () => conflictProblem()))
    const { user } = renderCreatePage()
    await fillAllValid(user)

    await save(user)

    await screen.findByText(DUPLICATE)
    await waitFor(() => {
      expect(field('Email')).toHaveFocus()
    })
  })

  it('moves focus to the first errored field, Last name, after a 400 on lastName and company [AC-036]', async () => {
    server.use(
      http.post('/api/contacts', () =>
        validationProblem({ company: ['Must be 200 characters or fewer.'], lastName: [LAST_LONG] }),
      ),
    )
    const { user } = renderCreatePage()
    await fillAllValid(user)

    await save(user)

    await screen.findByText(LAST_LONG)
    await waitFor(() => {
      expect(field('Last name')).toHaveFocus()
    })
  })

  it('shows the server error on the field already when focus arrives [AC-036]', async () => {
    server.use(http.post('/api/contacts', () => validationProblem({ lastName: [LAST_LONG] })))
    const { user } = renderCreatePage()
    await fillAllValid(user)
    const lastName = field('Last name')
    const atFocus: { invalid: string | null; description: string }[] = []
    lastName.addEventListener('focus', () => {
      const errorId = lastName.getAttribute('aria-describedby')
      atFocus.push({
        invalid: lastName.getAttribute('aria-invalid'),
        description: (errorId && document.getElementById(errorId)?.textContent) || '',
      })
    })

    await save(user)

    await waitFor(() => {
      expect(lastName).toHaveFocus()
    })
    expect(atFocus).toEqual([{ invalid: 'true', description: LAST_LONG }])
  })
})

describe('Create contact page: editing away a server error', () => {
  it('removes the duplicate message once Email is edited to another valid address, without a submit [AC-037]', async () => {
    server.use(http.post('/api/contacts', () => conflictProblem()))
    const { user, requests } = renderCreatePage()
    await fillAllValid(user)
    await save(user)
    await screen.findByText(DUPLICATE)

    await user.clear(field('Email'))
    await user.type(field('Email'), 'grace@example.com')

    expect(screen.queryByText(DUPLICATE)).not.toBeInTheDocument()
    expect(field('Email')).not.toHaveAttribute('aria-invalid', 'true')
    expect(field('Email')).toHaveAccessibleDescription('')
    expect(posts(requests)).toHaveLength(1)
  })

  it('shows the client rule instead of the duplicate message when Email is edited to an invalid value [AC-037]', async () => {
    server.use(http.post('/api/contacts', () => conflictProblem()))
    const { user } = renderCreatePage()
    await fillAllValid(user)
    await save(user)
    await screen.findByText(DUPLICATE)

    await user.type(field('Email'), '@')

    expect(field('Email')).toHaveAccessibleDescription(INVALID_EMAIL)
    expect(screen.queryByText(DUPLICATE)).not.toBeInTheDocument()
  })

  it('removes a server field error when that field is edited, and keeps the others [AC-037]', async () => {
    server.use(
      http.post('/api/contacts', () =>
        validationProblem({ lastName: [LAST_LONG], company: ['Must be 200 characters or fewer.'] }),
      ),
    )
    const { user } = renderCreatePage()
    await fillAllValid(user)
    await save(user)
    await screen.findByText(LAST_LONG)

    await user.type(field('Last name'), 'x')

    expect(screen.queryByText(LAST_LONG)).not.toBeInTheDocument()
    expect(field('Last name')).not.toHaveAttribute('aria-invalid', 'true')
    expect(field('Company')).toHaveAccessibleDescription('Must be 200 characters or fewer.')
  })

  it('keeps a server error on a field the owner has not edited [AC-037]', async () => {
    server.use(http.post('/api/contacts', () => conflictProblem()))
    const { user } = renderCreatePage()
    await fillAllValid(user)
    await save(user)
    await screen.findByText(DUPLICATE)

    await user.type(field('Phone'), '1')

    expect(field('Email')).toHaveAccessibleDescription(DUPLICATE)
  })
})

describe('Create contact page: a client-blocked submit after a server error', () => {
  /** First POST: 400 with a Last name error and an unknown-key error. Later POSTs: `later`. */
  function serverThen(later: () => Response) {
    let calls = 0
    server.use(
      http.post('/api/contacts', () => {
        calls += 1
        return calls === 1
          ? validationProblem({ lastName: ['Server last.'], nickname: ['Odd.'] })
          : later()
      }),
    )
  }

  async function reachClientBlockedSubmit(h: ReturnType<typeof renderCreatePage>) {
    await fillAllValid(h.user)
    await save(h.user)
    await screen.findByText('Server last.')
    expect(await screen.findByRole('alert')).toHaveTextContent('Odd.')
    await fill(h.user, 'Phone', 'x'.repeat(51))
    await save(h.user)
  }

  it('clears the previous attempt server errors and focuses the first field that shows an error [AC-036, AC-040]', async () => {
    serverThen(() => validationProblem({ company: ['Server company.'] }))
    const h = renderCreatePage()

    await reachClientBlockedSubmit(h)

    expect(await screen.findByText('Must be 50 characters or fewer.')).toBeInTheDocument()
    expect(screen.queryByText('Server last.')).not.toBeInTheDocument()
    expect(field('Last name')).not.toHaveAttribute('aria-invalid', 'true')
    expect(field('Last name')).toHaveAccessibleDescription('')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
    expect(screen.queryByText('Odd.')).not.toBeInTheDocument()
    await waitFor(() => {
      expect(field('Phone')).toHaveFocus()
    })
    expect(posts(h.requests)).toHaveLength(1)
  })

  it('shows the new server errors when the fixed form is submitted again and rejected [AC-036, AC-040]', async () => {
    serverThen(() => validationProblem({ company: ['Server company.'], other: ['Fresh.'] }))
    const h = renderCreatePage()
    await reachClientBlockedSubmit(h)
    await screen.findByText('Must be 50 characters or fewer.')

    await h.user.clear(field('Phone'))
    await h.user.type(field('Phone'), '555')
    await save(h.user)

    expect(await screen.findByText('Server company.')).toBeInTheDocument()
    expect(field('Company')).toHaveAccessibleDescription('Server company.')
    expect(await screen.findByRole('alert')).toHaveTextContent('Fresh.')
    expect(screen.queryByText('Server last.')).not.toBeInTheDocument()
    expect(screen.queryByText('Odd.')).not.toBeInTheDocument()
    expect(field('Last name')).not.toHaveAttribute('aria-invalid', 'true')
    expect(posts(h.requests)).toHaveLength(2)
    await waitFor(() => {
      expect(field('Company')).toHaveFocus()
    })
  })
})

describe('Create contact page: repeated unknown-key messages', () => {
  it('renders every message in the alert, even identical ones, without logging to the console [AC-041]', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    onTestFinished(() => {
      consoleError.mockRestore()
    })
    server.use(
      http.post('/api/contacts', () =>
        validationProblem({ nickname: ['Same message.'], alias: ['Same message.'] }),
      ),
    )
    const { user } = renderCreatePage()
    await fillAllValid(user)

    await save(user)

    const alert = await screen.findByRole('alert')
    expect(within(alert).getAllByText('Same message.')).toHaveLength(2)
    expect(consoleError).not.toHaveBeenCalled()
  })
})
