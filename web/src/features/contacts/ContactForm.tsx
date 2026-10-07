import { useEffect, useRef, useState } from 'react'
import type { FormEvent } from 'react'
import { Link } from 'react-router'
import fieldErrorStyles from '../../components/FieldError.module.css'
import {
  CONTACT_FORM_FIELDS,
  validateContactField,
  validateContactForm,
  type ContactFieldErrors,
  type ContactFormField,
  type ContactFormValues,
} from './contactFormRules'
import type { ContactFormServerErrors } from './contactFormErrors'
import styles from './ContactForm.module.css'

type ContactFormProps = {
  isSaving: boolean
  /** From the latest attempt; null while pending or before any attempt. */
  serverErrors: ContactFormServerErrors | null
  /** Only called when client validation passes. */
  onSubmit: (values: ContactFormValues) => void
  /** Cancel link target. */
  cancelTo: string
}

const LABELS: Record<ContactFormField, string> = {
  firstName: 'First name',
  lastName: 'Last name',
  email: 'Email',
  phone: 'Phone',
  company: 'Company',
  notes: 'Notes',
}

const EMPTY_VALUES: ContactFormValues = {
  firstName: '',
  lastName: '',
  email: '',
  phone: '',
  company: '',
  notes: '',
}

export function ContactForm({ isSaving, serverErrors, onSubmit, cancelTo }: ContactFormProps) {
  const [values, setValues] = useState<ContactFormValues>(EMPTY_VALUES)
  const [errors, setErrors] = useState<ContactFieldErrors>({})
  // Fields are only re-checked as they change once a submit has failed.
  const [hasFailedSubmit, setHasFailedSubmit] = useState(false)

  // A new object per blocked submit, so a repeat submit focuses again. The effect runs after
  // the render that shows the errors, so the field is already invalid when focus arrives.
  const [focusRequest, setFocusRequest] = useState<{ field: ContactFormField } | null>(null)
  const fieldRefs = useRef<Partial<Record<ContactFormField, HTMLElement | null>>>({})

  useEffect(() => {
    if (focusRequest) {
      fieldRefs.current[focusRequest.field]?.focus()
    }
  }, [focusRequest])

  // Server fields whose error the owner has since edited away. Reset when a new attempt's errors arrive;
  // done during render (not an effect) so the errors never show with stale state.
  const [dismissed, setDismissed] = useState<ReadonlySet<ContactFormField>>(new Set())
  // Set on every submit: the previous attempt's server errors (fields and alert) are hidden until new ones arrive.
  const [serverHidden, setServerHidden] = useState(false)
  const [seenServerErrors, setSeenServerErrors] = useState(serverErrors)
  if (seenServerErrors !== serverErrors) {
    setSeenServerErrors(serverErrors)
    setDismissed(new Set())
    setServerHidden(false)
    if (serverErrors) {
      setHasFailedSubmit(true)
      const first = CONTACT_FORM_FIELDS.find((field) => serverErrors.fields[field])
      if (first) setFocusRequest({ field: first })
    }
  }

  function handleChange(field: ContactFormField, value: string) {
    setValues((current) => ({ ...current, [field]: value }))
    setDismissed((current) => new Set(current).add(field))
    if (hasFailedSubmit) {
      setErrors((current) => ({ ...current, [field]: validateContactField(field, value) }))
    }
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (isSaving) return
    setServerHidden(true)
    const found = validateContactForm(values)
    setErrors(found)
    const firstInvalid = CONTACT_FORM_FIELDS.find((field) => found[field])
    if (firstInvalid) {
      setHasFailedSubmit(true)
      setFocusRequest({ field: firstInvalid })
      return
    }
    onSubmit(values)
  }

  return (
    <form className={styles.form} noValidate onSubmit={handleSubmit}>
      {CONTACT_FORM_FIELDS.map((field) => {
        const id = `contact-${field}`
        const errorId = `${id}-error`
        const serverError =
          serverHidden || dismissed.has(field) ? undefined : serverErrors?.fields[field]
        const error = serverError ?? errors[field]
        const common = {
          id,
          ref: (element: HTMLElement | null) => {
            fieldRefs.current[field] = element
          },
          value: values[field],
          onChange: (e: { target: { value: string } }) => handleChange(field, e.target.value),
          'aria-required': field === 'firstName' ? true : undefined,
          'aria-invalid': error ? true : undefined,
          'aria-describedby': error ? errorId : undefined,
        }
        return (
          <div key={field} className={styles.field}>
            <label htmlFor={id}>
              {LABELS[field]}
              {field === 'firstName' && (
                <span aria-hidden="true" className={styles.required}>
                  {' *'}
                </span>
              )}
            </label>
            {field === 'notes' ? (
              <textarea rows={5} {...common} />
            ) : (
              <input type="text" {...common} />
            )}
            {error && (
              <p id={errorId} className={fieldErrorStyles.fieldError}>
                {error}
              </p>
            )}
          </div>
        )
      })}
      {!serverHidden && serverErrors && serverErrors.form.length > 0 && (
        <div role="alert" className={fieldErrorStyles.fieldError}>
          {serverErrors.form.map((message, index) => (
            <p key={index}>{message}</p>
          ))}
        </div>
      )}
      <div className={styles.actions}>
        <button type="submit" disabled={isSaving}>
          {isSaving ? 'Saving…' : 'Save'}
        </button>
        <Link to={cancelTo}>Cancel</Link>
      </div>
    </form>
  )
}
