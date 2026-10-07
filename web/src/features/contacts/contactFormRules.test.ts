import { describe, expect, it } from 'vitest'
import {
  CONTACT_FIELD_MAX,
  CONTACT_FORM_FIELDS,
  isValidEmail,
  toCreateContactRequest,
  validateContactField,
  validateContactForm,
  type ContactFormField,
  type ContactFormValues,
} from './contactFormRules'

const REQUIRED = 'Required.'
const INVALID_EMAIL = 'Must be a valid email address.'
const tooLong = (max: number) => `Must be ${max} characters or fewer.`

function blankForm(overrides: Partial<ContactFormValues> = {}): ContactFormValues {
  return {
    firstName: 'Ada',
    lastName: '',
    email: '',
    phone: '',
    company: '',
    notes: '',
    ...overrides,
  }
}

/** A string of exactly `length` characters that is a valid email. */
function emailOfLength(length: number): string {
  return `a@${'x'.repeat(length - 2)}`
}

/** A value of exactly `length` characters that is valid for the field. */
function validValue(field: ContactFormField, length: number): string {
  return field === 'email' ? emailOfLength(length) : 'x'.repeat(length)
}

describe('contact form rules: constants', () => {
  it('lists the fields in form order [AC-029]', () => {
    expect(CONTACT_FORM_FIELDS).toEqual([
      'firstName',
      'lastName',
      'email',
      'phone',
      'company',
      'notes',
    ])
  })

  it('mirrors the API maximum lengths [AC-031]', () => {
    expect(CONTACT_FIELD_MAX).toEqual({
      firstName: 100,
      lastName: 100,
      email: 254,
      phone: 50,
      company: 200,
      notes: 4000,
    })
  })
})

describe('validateContactForm: First name required', () => {
  it.each([
    ['empty', ''],
    ['spaces only', '   '],
    ['whitespace characters only', ' \t\n '],
  ])('gives Required. when First name is %s [AC-030]', (_label, firstName) => {
    expect(validateContactForm(blankForm({ firstName }))).toEqual({ firstName: REQUIRED })
  })

  it('has no errors when only First name is filled and every optional field is blank [AC-034]', () => {
    expect(validateContactForm(blankForm())).toEqual({})
  })

  it('treats whitespace-only optional fields as blank [AC-034]', () => {
    const values = blankForm({
      lastName: '  ',
      email: '   ',
      phone: ' ',
      company: '  ',
      notes: '\n',
    })
    expect(validateContactForm(values)).toEqual({})
  })
})

describe('validateContactForm: maximum lengths', () => {
  const fields = Object.keys(CONTACT_FIELD_MAX) as ContactFormField[]

  it.each(fields)('accepts %s at exactly its maximum [AC-034]', (field) => {
    const values = blankForm({ [field]: validValue(field, CONTACT_FIELD_MAX[field]) })
    expect(validateContactForm(values)).toEqual({})
  })

  it.each(fields)('rejects %s at maximum + 1 [AC-031]', (field) => {
    const max = CONTACT_FIELD_MAX[field]
    // Email is length-checked only when it is otherwise valid, so use a valid shape.
    const values = blankForm({ [field]: validValue(field, max + 1) })
    expect(validateContactForm(values)).toEqual({ [field]: tooLong(max) })
  })

  it.each(fields)(
    'accepts %s at its maximum with surrounding spaces (trimmed first) [AC-031]',
    (field) => {
      const max = CONTACT_FIELD_MAX[field]
      const values = blankForm({ [field]: `  ${validValue(field, max)}  ` })
      expect(validateContactForm(values)).toEqual({})
    },
  )

  it('counts a padded First name after trimming, so max + 1 plus spaces still fails [AC-031]', () => {
    const values = blankForm({ firstName: ` ${'x'.repeat(101)} ` })
    expect(validateContactForm(values)).toEqual({ firstName: tooLong(100) })
  })
})

describe('validateContactForm: email', () => {
  it.each([
    ['a@b', false],
    ['@b', true],
    ['a@', true],
    ['a@@b', true],
    ['a@b@c', true],
    ['a b@c', true],
    ['a@b c', true],
    ['a\t@b', true],
  ])('email %j: invalid is %s [AC-032]', (email, invalid) => {
    const errors = validateContactForm(blankForm({ email }))
    if (invalid) {
      expect(errors).toEqual({ email: INVALID_EMAIL })
    } else {
      expect(errors).toEqual({})
    }
  })

  it('accepts a valid email with surrounding spaces [AC-032]', () => {
    expect(validateContactForm(blankForm({ email: '  ada@example.com  ' }))).toEqual({})
  })

  it('treats a blank email as valid [AC-034]', () => {
    expect(validateContactForm(blankForm({ email: '   ' }))).toEqual({})
  })

  it('gives only the invalid-email message for an over-long invalid email [AC-032]', () => {
    const email = 'x'.repeat(300) // no @, and over 254
    expect(validateContactForm(blankForm({ email }))).toEqual({ email: INVALID_EMAIL })
  })

  it('gives only the invalid-email message for an over-long email with two @ signs [AC-032]', () => {
    const email = `a@@${'x'.repeat(300)}`
    expect(validateContactForm(blankForm({ email }))).toEqual({ email: INVALID_EMAIL })
  })
})

describe('validateContactForm: several errors', () => {
  it('reports every invalid field at once [AC-033]', () => {
    const values = blankForm({
      firstName: '',
      lastName: 'x'.repeat(101),
      email: 'x@',
      phone: 'x'.repeat(51),
      company: 'x'.repeat(201),
      notes: 'x'.repeat(4001),
    })
    expect(validateContactForm(values)).toEqual({
      firstName: REQUIRED,
      lastName: tooLong(100),
      email: INVALID_EMAIL,
      phone: tooLong(50),
      company: tooLong(200),
      notes: tooLong(4000),
    })
  })
})

describe('validateContactField', () => {
  it('gives Required. for a blank First name [AC-030]', () => {
    expect(validateContactField('firstName', '  ')).toBe(REQUIRED)
  })

  it('gives undefined for a blank optional field [AC-034]', () => {
    expect(validateContactField('lastName', '')).toBeUndefined()
    expect(validateContactField('notes', '   ')).toBeUndefined()
  })

  it('gives the maximum-length message for one field only [AC-031]', () => {
    expect(validateContactField('phone', 'x'.repeat(51))).toBe(tooLong(50))
    expect(validateContactField('phone', 'x'.repeat(50))).toBeUndefined()
  })

  it('gives the invalid-email message, not the length message, for a long invalid email [AC-032]', () => {
    expect(validateContactField('email', 'x'.repeat(300))).toBe(INVALID_EMAIL)
    expect(validateContactField('email', 'x@')).toBe(INVALID_EMAIL)
  })

  it('gives the length message for a long but valid-shaped email [AC-031]', () => {
    expect(validateContactField('email', emailOfLength(255))).toBe(tooLong(254))
  })

  it('agrees with validateContactForm for the same value [AC-031]', () => {
    const values = blankForm({ company: 'x'.repeat(201) })
    expect(validateContactField('company', values.company)).toBe(
      validateContactForm(values).company,
    )
  })
})

describe('isValidEmail', () => {
  it.each(['a@b', 'ada@example.com', 'a.b+c@d.e'])('accepts %j [AC-032]', (email) => {
    expect(isValidEmail(email)).toBe(true)
  })

  it.each(['', 'ab', '@b', 'a@', '@', 'a@@b', 'a@b@c', 'a b@c', 'a@b c', 'a@b\t'])(
    'rejects %j [AC-032]',
    (email) => {
      expect(isValidEmail(email)).toBe(false)
    },
  )
})

describe('toCreateContactRequest', () => {
  it('trims every field [AC-038]', () => {
    const request = toCreateContactRequest({
      firstName: '  Ada ',
      lastName: ' Lovelace  ',
      email: '  ada@example.com ',
      phone: ' 555-0100 ',
      company: '  Analytical Engines ',
      notes: '  First programmer.\n ',
    })

    expect(request).toEqual({
      firstName: 'Ada',
      lastName: 'Lovelace',
      email: 'ada@example.com',
      phone: '555-0100',
      company: 'Analytical Engines',
      notes: 'First programmer.',
    })
  })

  it('maps blank optional fields to null, including whitespace-only ones [AC-038]', () => {
    const request = toCreateContactRequest(
      blankForm({ lastName: '', email: '   ', phone: ' \t', notes: '\n' }),
    )

    expect(request).toEqual({
      firstName: 'Ada',
      lastName: null,
      email: null,
      phone: null,
      company: null,
      notes: null,
    })
  })

  it('keeps inner whitespace and never turns firstName into null [AC-038]', () => {
    const request = toCreateContactRequest(blankForm({ firstName: ' Mary  Ann ', notes: 'a  b' }))

    expect(request.firstName).toBe('Mary  Ann')
    expect(request.notes).toBe('a  b')
  })

  it('leaves values at their maximum lengths unchanged [AC-034]', () => {
    const request = toCreateContactRequest(
      blankForm({ firstName: 'x'.repeat(100), notes: 'n'.repeat(4000) }),
    )

    expect(request.firstName).toHaveLength(100)
    expect(request.notes).toHaveLength(4000)
  })
})
