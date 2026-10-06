import { useState } from 'react'
import type { FormEvent } from 'react'

type ContactSearchFormProps = {
  initialTerm: string
  onSearch: (term: string | null) => void
}

export function ContactSearchForm({ initialTerm, onSearch }: ContactSearchFormProps) {
  const [term, setTerm] = useState(initialTerm)
  // Reset the box when the address term changes (Back, Clear). Done during render rather than
  // with a `key`, so the input keeps its identity and focus across a submit.
  const [seenTerm, setSeenTerm] = useState(initialTerm)
  if (seenTerm !== initialTerm) {
    setSeenTerm(initialTerm)
    setTerm(initialTerm)
  }

  function handleSubmit(event: FormEvent) {
    event.preventDefault()
    const trimmed = term.trim()
    onSearch(trimmed === '' ? null : trimmed)
  }

  return (
    <form role="search" onSubmit={handleSubmit}>
      <label>
        Search contacts
        <input type="search" value={term} onChange={(e) => setTerm(e.target.value)} />
      </label>
      <button type="submit">Search</button>
    </form>
  )
}
