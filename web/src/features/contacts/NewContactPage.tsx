import { ContactForm } from './ContactForm'

export function NewContactPage() {
  return (
    <>
      <h1>New contact</h1>
      {/* Submitting to the API is wired in T-11. */}
      <ContactForm onSubmit={() => {}} cancelTo="/contacts" />
    </>
  )
}
