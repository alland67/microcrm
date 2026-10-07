import { ContactForm } from './ContactForm'
import { toCreateContactRequest } from './contactFormRules'
import { useCreateContact } from './useContacts'

export function NewContactPage() {
  const createContact = useCreateContact()

  return (
    <>
      <h1>New contact</h1>
      <ContactForm
        isSaving={createContact.isPending}
        onSubmit={(values) => createContact.mutate(toCreateContactRequest(values))}
        cancelTo="/contacts"
      />
    </>
  )
}
