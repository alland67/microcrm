import { describe, expect, it } from 'vitest'
import { ApiError } from '../../api/client'
import { DUPLICATE_EMAIL_MESSAGE, toContactFormErrors } from './contactFormErrors'

function validation(errors: Record<string, string[]>) {
  return new ApiError({
    kind: 'validation',
    status: 400,
    problem: { status: 400, errors },
    fieldErrors: errors,
  })
}

describe('toContactFormErrors: validation', () => {
  it('puts each known key under its field [AC-040]', () => {
    const result = toContactFormErrors(
      validation({
        firstName: ['Required.'],
        lastName: ['Must be 100 characters or fewer.'],
        email: ['Must be a valid email address.'],
        phone: ['Must be 50 characters or fewer.'],
        company: ['Must be 200 characters or fewer.'],
        notes: ['Must be 4000 characters or fewer.'],
      }),
    )

    expect(result).toEqual({
      fields: {
        firstName: 'Required.',
        lastName: 'Must be 100 characters or fewer.',
        email: 'Must be a valid email address.',
        phone: 'Must be 50 characters or fewer.',
        company: 'Must be 200 characters or fewer.',
        notes: 'Must be 4000 characters or fewer.',
      },
      form: [],
    })
  })

  it('joins two messages for one key with a single space [AC-040]', () => {
    const result = toContactFormErrors(validation({ email: ['First problem.', 'Second problem.'] }))

    expect(result.fields).toEqual({ email: 'First problem. Second problem.' })
    expect(result.form).toEqual([])
  })

  it('sends every message of an unknown key to the form level, verbatim [AC-041]', () => {
    const result = toContactFormErrors(validation({ nickname: ['Something odd.', 'And another.'] }))

    expect(result.fields).toEqual({})
    expect(result.form).toEqual(['Something odd.', 'And another.'])
  })

  it('splits known and unknown keys between fields and form [AC-040, AC-041]', () => {
    const result = toContactFormErrors(
      validation({ lastName: ['Must be 100 characters or fewer.'], nickname: ['Something odd.'] }),
    )

    expect(result).toEqual({
      fields: { lastName: 'Must be 100 characters or fewer.' },
      form: ['Something odd.'],
    })
  })

  it('does not match keys case-insensitively or by prefix [AC-041]', () => {
    const result = toContactFormErrors(validation({ FirstName: ['Nope.'], first: ['Nope too.'] }))

    expect(result.fields).toEqual({})
    expect(result.form).toEqual(['Nope.', 'Nope too.'])
  })
})

describe('toContactFormErrors: conflict', () => {
  it('shows the duplicate-email message under Email and ignores the problem title [AC-042]', () => {
    const error = new ApiError({
      kind: 'conflict',
      status: 409,
      problem: { status: 409, title: 'Conflict!' },
    })

    const result = toContactFormErrors(error)

    expect(result).toEqual({
      fields: { email: 'A contact with this email already exists.' },
      form: [],
    })
    expect(JSON.stringify(result)).not.toContain('Conflict!')
  })

  it('exports the duplicate-email message text [AC-042]', () => {
    expect(DUPLICATE_EMAIL_MESSAGE).toBe('A contact with this email already exists.')
  })
})
