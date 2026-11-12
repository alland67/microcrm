import { ApiError } from '../../api/client'
import {
  CONTACT_FORM_FIELDS,
  type ContactFieldErrors,
  type ContactFormField,
} from './contactFormRules'

export type ContactFormServerErrors = { fields: ContactFieldErrors; form: string[] }

export const DUPLICATE_EMAIL_MESSAGE = 'A contact with this email already exists.'
export const SAVE_FAILED_MESSAGE = "The contact couldn't be saved. Try again."

function isContactFormField(key: string): key is ContactFormField {
  return (CONTACT_FORM_FIELDS as readonly string[]).includes(key)
}

/** Maps a failed create to what the form shows. Anything unexpected gets the generic message. */
export function toContactFormErrors(error: unknown): ContactFormServerErrors {
  const result: ContactFormServerErrors = { fields: {}, form: [] }
  if (!(error instanceof ApiError)) return { fields: {}, form: [SAVE_FAILED_MESSAGE] }

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
  } else {
    result.form.push(SAVE_FAILED_MESSAGE)
  }
  return result
}
