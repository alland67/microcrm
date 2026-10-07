import type { CreateContactRequest } from '../../api/types'
import { INVALID_EMAIL, REQUIRED, maxLengthMessage } from '../../lib/validationMessages'

export type ContactFormField = 'firstName' | 'lastName' | 'email' | 'phone' | 'company' | 'notes'

/** Fields in form order. */
export const CONTACT_FORM_FIELDS: readonly ContactFormField[] = [
  'firstName',
  'lastName',
  'email',
  'phone',
  'company',
  'notes',
]

export type ContactFormValues = Record<ContactFormField, string>
export type ContactFieldErrors = Partial<Record<ContactFormField, string>>

/** Mirrors the API's limits (ContactInput.cs). Lengths are UTF-16 code units, as in .NET. */
export const CONTACT_FIELD_MAX: Record<ContactFormField, number> = {
  firstName: 100,
  lastName: 100,
  email: 254,
  phone: 50,
  company: 200,
  notes: 4000,
}

/** Exactly one '@', at least one character on each side, no whitespace (mirrors the API). */
export function isValidEmail(trimmed: string): boolean {
  const at = trimmed.indexOf('@')
  return (
    at > 0 && at < trimmed.length - 1 && trimmed.indexOf('@', at + 1) < 0 && !/\s/.test(trimmed)
  )
}

export function validateContactField(field: ContactFormField, value: string): string | undefined {
  const trimmed = value.trim()
  if (trimmed === '') {
    return field === 'firstName' ? REQUIRED : undefined
  }
  // An invalid email replaces a max-length error, as on the server.
  if (field === 'email' && !isValidEmail(trimmed)) {
    return INVALID_EMAIL
  }
  const max = CONTACT_FIELD_MAX[field]
  return trimmed.length > max ? maxLengthMessage(max) : undefined
}

export function validateContactForm(values: ContactFormValues): ContactFieldErrors {
  const errors: ContactFieldErrors = {}
  for (const field of CONTACT_FORM_FIELDS) {
    const error = validateContactField(field, values[field])
    if (error) {
      errors[field] = error
    }
  }
  return errors
}

/** Trimmed values, with blank optional fields as null (mirrors the API's normalization). */
export function toCreateContactRequest(values: ContactFormValues): CreateContactRequest {
  const optional = (field: ContactFormField) => values[field].trim() || null
  return {
    firstName: values.firstName.trim(),
    lastName: optional('lastName'),
    email: optional('email'),
    phone: optional('phone'),
    company: optional('company'),
    notes: optional('notes'),
  }
}
