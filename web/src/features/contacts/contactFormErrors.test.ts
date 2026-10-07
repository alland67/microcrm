import { describe, expect, it } from 'vitest'
import { ApiError } from '../../api/client'
import {
  DUPLICATE_EMAIL_MESSAGE,
  SAVE_FAILED_MESSAGE,
  toContactFormErrors,
} from './contactFormErrors'

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

describe('toContactFormErrors: unexpected failures', () => {
  const SAVE_FAILED = { fields: {}, form: ["The contact couldn't be saved. Try again."] }

  it('exports the save-failed message text [AC-043]', () => {
    expect(SAVE_FAILED_MESSAGE).toBe("The contact couldn't be saved. Try again.")
  })

  it.each([
    [
      'a 500',
      { kind: 'unexpected' as const, status: 500, problem: { status: 500, title: 'Boom' } },
    ],
    ['a network failure', { kind: 'unexpected' as const, status: null }],
    [
      'a 400 without errors',
      { kind: 'unexpected' as const, status: 400, problem: { status: 400 } },
    ],
  ])('maps an unexpected ApiError (%s) to the form-level message only [AC-043]', (_label, init) => {
    expect(toContactFormErrors(new ApiError(init))).toEqual(SAVE_FAILED)
  })

  it.each([
    ['a plain Error', new Error('boom')],
    ['a string', 'boom'],
    ['null', null],
    ['undefined', undefined],
    ['a plain object', { kind: 'validation' }],
  ])('maps %s that is not an ApiError to the form-level message [AC-043]', (_label, error) => {
    expect(toContactFormErrors(error)).toEqual(SAVE_FAILED)
  })

  it('does not leak the problem title or detail into the message [AC-043]', () => {
    const error = new ApiError({
      kind: 'unexpected',
      status: 500,
      problem: { status: 500, title: 'SECRET-TITLE', detail: 'SECRET-DETAIL' },
    })

    expect(JSON.stringify(toContactFormErrors(error))).not.toMatch(/SECRET/)
  })
})
