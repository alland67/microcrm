import { ApiError } from '../../api/client'
import {
  CONTACT_FORM_FIELDS,
  type ContactFieldErrors,
  type ContactFormField,
} from './contactFormRules'

export type ContactFormServerErrors = { fields: ContactFieldErrors; form: string[] }

export const DUPLICATE_EMAIL_MESSAGE = 'A contact with this email already exists.'

function isContactFormField(key: string): key is ContactFormField {
  return (CONTACT_FORM_FIELDS as readonly string[]).includes(key)
}

/** Maps a failed create to what the form shows. Other failures (T-14) map to nothing yet. */
export function toContactFormErrors(error: unknown): ContactFormServerErrors {
  const result: ContactFormServerErrors = { fields: {}, form: [] }
  if (!(error instanceof ApiError)) return result

  if (error.kind === 'conflict') {
    result.fields.email = DUPLICATE_EMAIL_MESSAGE
  } else if (error.kind === 'validation') {
    for (const [key, messages] of Object.entries(error.fieldErrors)) {
      if (isContactFormField(key)) {
        result.fields[key] = messages.join(' ')
      } else {
        result.form.push(...messages)
      }
    }
  }
  return result
}
