import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link } from 'react-router'
import fieldErrorStyles from '../../components/FieldError.module.css'
import {
  CONTACT_FORM_FIELDS,
  validateContactForm,
  type ContactFieldErrors,
  type ContactFormField,
  type ContactFormValues,
} from './contactFormRules'
import styles from './ContactForm.module.css'

type ContactFormProps = {
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

export function ContactForm({ onSubmit, cancelTo }: ContactFormProps) {
  const [values, setValues] = useState<ContactFormValues>(EMPTY_VALUES)
  const [errors, setErrors] = useState<ContactFieldErrors>({})

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const found = validateContactForm(values)
    setErrors(found)
    if (Object.keys(found).length === 0) {
      onSubmit(values)
    }
  }

  return (
    <form className={styles.form} noValidate onSubmit={handleSubmit}>
      {CONTACT_FORM_FIELDS.map((field) => {
        const id = `contact-${field}`
        const errorId = `${id}-error`
        const error = errors[field]
        const common = {
          id,
          value: values[field],
          onChange: (e: { target: { value: string } }) =>
            setValues((current) => ({ ...current, [field]: e.target.value })),
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
        <button type="submit">Save</button>
        <Link to={cancelTo}>Cancel</Link>
      </div>
    </form>
  )
}
