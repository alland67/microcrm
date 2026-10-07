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
import styles from './ContactForm.module.css'

type ContactFormProps = {
  isSaving: boolean
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

export function ContactForm({ isSaving, onSubmit, cancelTo }: ContactFormProps) {
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

  function handleChange(field: ContactFormField, value: string) {
    setValues((current) => ({ ...current, [field]: value }))
    if (hasFailedSubmit) {
      setErrors((current) => ({ ...current, [field]: validateContactField(field, value) }))
    }
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    if (isSaving) return
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
        const error = errors[field]
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
      <div className={styles.actions}>
        <button type="submit" disabled={isSaving}>
          {isSaving ? 'Saving…' : 'Save'}
        </button>
        <Link to={cancelTo}>Cancel</Link>
      </div>
    </form>
  )
}
