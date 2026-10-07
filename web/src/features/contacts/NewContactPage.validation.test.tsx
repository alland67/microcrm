import { screen, waitFor } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { App } from '../../App'
import { recordRequests } from '../../test/msw/requests'
import { renderWithProviders } from '../../test/renderWithProviders'

const REQUIRED = 'Required.'
const INVALID_EMAIL = 'Must be a valid email address.'
const tooLong = (max: number) => `Must be ${max} characters or fewer.`

const FIELDS = [
  { label: 'First name', max: 100 },
  { label: 'Last name', max: 100 },
  { label: 'Email', max: 254 },
  { label: 'Phone', max: 50 },
  { label: 'Company', max: 200 },
  { label: 'Notes', max: 4000 },
] as const

type Label = (typeof FIELDS)[number]['label']

function field(label: Label) {
  return screen.getByRole('textbox', { name: label })
}

function posts(requests: ReturnType<typeof recordRequests>) {
  return requests.filter((r) => r.method === 'POST')
}

function renderCreatePage() {
  const requests = recordRequests()
  const view = renderWithProviders(<App />, { route: '/contacts/new' })
  return { ...view, requests }
}

async function fill(
  user: ReturnType<typeof renderCreatePage>['user'],
  label: Label,
  value: string,
) {
  await user.click(field(label))
  await user.paste(value)
}

async function save(user: ReturnType<typeof renderCreatePage>['user']) {
  await user.click(screen.getByRole('button', { name: 'Save' }))
}

/** Pastes a value of the given length that is valid for the field apart from its length. */
function longValue(label: Label, length: number) {
  return label === 'Email' ? `a@${'x'.repeat(length - 2)}` : 'x'.repeat(length)
}

describe('Create contact page: the form', () => {
  it('shows the New contact heading, six labelled fields, Save and Cancel [AC-029]', () => {
    renderCreatePage()

    expect(screen.getByRole('heading', { level: 1, name: 'New contact' })).toBeInTheDocument()
    for (const { label } of FIELDS) {
      expect(field(label)).toBeInTheDocument()
    }
    expect(screen.getByRole('button', { name: 'Save' })).toBeInTheDocument()
    const cancel = screen.getByRole('link', { name: 'Cancel' })
    expect(cancel).toHaveAttribute('href', '/contacts')
  })

  it('marks First name as required and the other fields as optional [AC-029]', () => {
    renderCreatePage()

    expect(field('First name')).toBeRequired()
    for (const { label } of FIELDS.filter((f) => f.label !== 'First name')) {
      expect(field(label)).not.toBeRequired()
    }
  })

  it('renders Notes as a multi-line textarea and the other fields as single-line inputs [AC-029]', () => {
    renderCreatePage()

    expect(field('Notes').tagName).toBe('TEXTAREA')
    for (const { label } of FIELDS.filter((f) => f.label !== 'Notes')) {
      expect(field(label).tagName).toBe('INPUT')
    }
  })

  it('shows a visible aria-hidden * marker on First name only, keeping its accessible name [AC-029]', () => {
    renderCreatePage()

    const markers = (label: Label) =>
      Array.from(
        (field(label) as HTMLInputElement | HTMLTextAreaElement).labels?.[0]?.querySelectorAll(
          '[aria-hidden="true"]',
        ) ?? [],
      )
    const firstNameMarkers = markers('First name')
    expect(firstNameMarkers).toHaveLength(1)
    expect(firstNameMarkers[0]).toHaveTextContent('*')
    expect(firstNameMarkers[0]).toBeVisible()
    for (const { label } of FIELDS.filter((f) => f.label !== 'First name')) {
      expect(markers(label)).toHaveLength(0)
    }
  })

  it('turns off native validation on the form with novalidate [AC-029]', () => {
    const { container } = renderCreatePage()

    expect(container.querySelector('form')).toHaveAttribute('novalidate')
  })

  it('starts with every field empty and no errors [AC-029]', () => {
    renderCreatePage()

    for (const { label } of FIELDS) {
      expect(field(label)).toHaveValue('')
      expect(field(label)).not.toHaveAttribute('aria-invalid', 'true')
    }
    expect(screen.queryByText(REQUIRED)).not.toBeInTheDocument()
  })

  it('does not use native validation attributes that would block the spec messages [AC-031]', () => {
    renderCreatePage()

    for (const { label } of FIELDS) {
      expect(field(label)).not.toHaveAttribute('maxlength')
      expect(field(label)).not.toHaveAttribute('required')
    }
  })
})

describe('Create contact page: First name required', () => {
  it('shows Required. as First name description and sends nothing when it is empty [AC-030]', async () => {
    const { user, requests } = renderCreatePage()

    await save(user)

    expect(await screen.findByText(REQUIRED)).toBeInTheDocument()
    expect(field('First name')).toHaveAccessibleDescription(REQUIRED)
    expect(posts(requests)).toHaveLength(0)
  })

  it('shows Required. and sends nothing when First name is only spaces [AC-030]', async () => {
    const { user, requests } = renderCreatePage()
    await user.type(field('First name'), '   ')

    await save(user)

    expect(await screen.findByText(REQUIRED)).toBeInTheDocument()
    expect(field('First name')).toHaveAccessibleDescription(REQUIRED)
    expect(posts(requests)).toHaveLength(0)
  })
})

describe('Create contact page: maximum lengths', () => {
  it.each(FIELDS)(
    'shows the $max-character message under $label only, and sends nothing [AC-031]',
    async ({ label, max }) => {
      const { user, requests } = renderCreatePage()
      if (label !== 'First name') await fill(user, 'First name', 'Ada')
      await fill(user, label, longValue(label, max + 1))

      await save(user)

      expect(await screen.findByText(tooLong(max))).toBeInTheDocument()
      expect(screen.getAllByText(/characters or fewer\./)).toHaveLength(1)
      expect(field(label)).toHaveAccessibleDescription(tooLong(max))
      for (const other of FIELDS.filter((f) => f.label !== label)) {
        expect(field(other.label)).not.toHaveAttribute('aria-invalid', 'true')
      }
      expect(posts(requests)).toHaveLength(0)
    },
  )

  it('counts the trimmed value, so max + 1 characters with surrounding spaces still fails [AC-031]', async () => {
    const { user, requests } = renderCreatePage()
    await fill(user, 'First name', `  ${'x'.repeat(101)}  `)

    await save(user)

    expect(await screen.findByText(tooLong(100))).toBeInTheDocument()
    expect(posts(requests)).toHaveLength(0)
  })
})

describe('Create contact page: email', () => {
  it('shows the invalid-email message for ada@ and sends nothing [AC-032]', async () => {
    const { user, requests } = renderCreatePage()
    await fill(user, 'First name', 'Ada')
    await fill(user, 'Email', 'ada@')

    await save(user)

    expect(await screen.findByText(INVALID_EMAIL)).toBeInTheDocument()
    expect(field('Email')).toHaveAccessibleDescription(INVALID_EMAIL)
    expect(posts(requests)).toHaveLength(0)
  })

  it('shows only the invalid-email message for an over-long invalid email [AC-032]', async () => {
    const { user, requests } = renderCreatePage()
    await fill(user, 'First name', 'Ada')
    await fill(user, 'Email', 'x'.repeat(300))

    await save(user)

    expect(await screen.findByText(INVALID_EMAIL)).toBeInTheDocument()
    expect(screen.queryByText(tooLong(254))).not.toBeInTheDocument()
    expect(field('Email')).toHaveAccessibleDescription(INVALID_EMAIL)
    expect(posts(requests)).toHaveLength(0)
  })
})

describe('Create contact page: several errors and accessibility', () => {
  it('shows an error under every invalid field at once [AC-033]', async () => {
    const { user, requests } = renderCreatePage()
    await fill(user, 'Last name', 'x'.repeat(101))
    await fill(user, 'Email', 'x@')

    await save(user)

    expect(await screen.findByText(REQUIRED)).toBeInTheDocument()
    expect(screen.getByText(tooLong(100))).toBeInTheDocument()
    expect(screen.getByText(INVALID_EMAIL)).toBeInTheDocument()
    expect(field('First name')).toHaveAccessibleDescription(REQUIRED)
    expect(field('Last name')).toHaveAccessibleDescription(tooLong(100))
    expect(field('Email')).toHaveAccessibleDescription(INVALID_EMAIL)
    expect(posts(requests)).toHaveLength(0)
  })

  it('marks each errored field aria-invalid and leaves the valid ones unmarked [AC-035]', async () => {
    const { user } = renderCreatePage()
    await fill(user, 'Last name', 'x'.repeat(101))
    await fill(user, 'Email', 'x@')

    await save(user)

    await screen.findByText(REQUIRED)
    expect(field('First name')).toHaveAttribute('aria-invalid', 'true')
    expect(field('Last name')).toHaveAttribute('aria-invalid', 'true')
    expect(field('Email')).toHaveAttribute('aria-invalid', 'true')
    for (const label of ['Phone', 'Company', 'Notes'] as const) {
      expect(field(label)).not.toHaveAttribute('aria-invalid', 'true')
      expect(field(label)).toHaveAccessibleDescription('')
    }
  })

  it('keeps the label as the accessible name when a field has an error [AC-035]', async () => {
    const { user } = renderCreatePage()

    await save(user)

    await screen.findByText(REQUIRED)
    expect(screen.getByRole('textbox', { name: 'First name' })).toBeInTheDocument()
  })
})

describe('Create contact page: Cancel', () => {
  it('goes to the contacts list without a create request, with input typed [AC-047]', async () => {
    const { user, requests, getLocation } = renderCreatePage()
    for (const { label } of FIELDS) {
      await user.type(field(label), label === 'Email' ? 'ada@example.com' : 'abc')
    }

    await user.click(screen.getByRole('link', { name: 'Cancel' }))

    expect(await screen.findByRole('heading', { level: 1, name: 'Contacts' })).toBeInTheDocument()
    expect(getLocation().pathname).toBe('/contacts')
    expect(posts(requests)).toHaveLength(0)
  })

  it('goes to the contacts list without a create request, from an errored form [AC-047]', async () => {
    const { user, requests, getLocation } = renderCreatePage()
    await fill(user, 'Email', 'x@')
    await save(user)
    await screen.findByText(REQUIRED)

    await user.click(screen.getByRole('link', { name: 'Cancel' }))

    await waitFor(() => {
      expect(getLocation().pathname).toBe('/contacts')
    })
    expect(posts(requests)).toHaveLength(0)
  })

  it('goes to the contacts list without a create request, from an untouched form [AC-047]', async () => {
    const { user, requests, getLocation } = renderCreatePage()

    await user.click(screen.getByRole('link', { name: 'Cancel' }))

    await waitFor(() => {
      expect(getLocation().pathname).toBe('/contacts')
    })
    expect(posts(requests)).toHaveLength(0)
  })

  it('shows an empty form with no errors when the page is opened again after cancelling [AC-048]', async () => {
    const { user } = renderCreatePage()
    await fill(user, 'Last name', 'Lovelace')
    await fill(user, 'Email', 'x@')
    await save(user)
    await screen.findByText(REQUIRED)
    await user.click(screen.getByRole('link', { name: 'Cancel' }))

    await user.click(await screen.findByRole('link', { name: 'New contact' }))

    expect(
      await screen.findByRole('heading', { level: 1, name: 'New contact' }),
    ).toBeInTheDocument()
    for (const { label } of FIELDS) {
      expect(field(label)).toHaveValue('')
      expect(field(label)).not.toHaveAttribute('aria-invalid', 'true')
    }
    expect(screen.queryByText(REQUIRED)).not.toBeInTheDocument()
    expect(screen.queryByText(INVALID_EMAIL)).not.toBeInTheDocument()
    expect(screen.queryByText(/characters or fewer\./)).not.toBeInTheDocument()
  })
})
