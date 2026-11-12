import { useState } from 'react'
import type { FormEvent } from 'react'
import { maxLengthMessage } from '../../lib/validationMessages'
import fieldErrorStyles from '../../components/FieldError.module.css'
import { MAX_SEARCH_LENGTH } from './listParams'

type ContactSearchFormProps = {
  initialTerm: string
  /** Error that comes from the address (an over-long term); re-applied when the term changes. */
  initialError: string | null
  onSearch: (term: string | null) => void
}

const ERROR_ID = 'contact-search-error'

export function ContactSearchForm({ initialTerm, initialError, onSearch }: ContactSearchFormProps) {
  const [term, setTerm] = useState(initialTerm)
  const [error, setError] = useState(initialError)
  // Reset the box and error when the address term changes (Back, Clear). Done during render
  // rather than with a `key`, so the input keeps its identity and focus across a submit.
  const [seenTerm, setSeenTerm] = useState(initialTerm)
  if (seenTerm !== initialTerm) {
    setSeenTerm(initialTerm)
    setTerm(initialTerm)
    setError(initialError)
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const trimmed = term.trim()
    if (trimmed.length > MAX_SEARCH_LENGTH) {
      setError(maxLengthMessage(MAX_SEARCH_LENGTH))
      return
    }
    setError(null)
    onSearch(trimmed === '' ? null : trimmed)
  }

  return (
    <form role="search" onSubmit={handleSubmit}>
      <label htmlFor="contact-search">Search contacts</label>
      <input
        id="contact-search"
        type="search"
        value={term}
        onChange={(e) => setTerm(e.target.value)}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? ERROR_ID : undefined}
      />
      <button type="submit">Search</button>
      {error && (
        <p id={ERROR_ID} className={fieldErrorStyles.fieldError}>
          {error}
        </p>
      )}
    </form>
  )
}
