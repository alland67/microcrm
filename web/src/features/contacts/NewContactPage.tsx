import { useMemo } from 'react'
import { useNavigate } from 'react-router'
import { ContactForm } from './ContactForm'
import { toContactFormErrors } from './contactFormErrors'
import { contactDisplayName } from './contactName'
import { toCreateContactRequest } from './contactFormRules'
import { useCreateContact } from './useContacts'

export function NewContactPage() {
  const createContact = useCreateContact()
  const navigate = useNavigate()
  const { error } = createContact
  // Memoised: the form resets its per-attempt state when this identity changes.
  const serverErrors = useMemo(() => (error ? toContactFormErrors(error) : null), [error])

  return (
    <>
      <h1>New contact</h1>
      <ContactForm
        isSaving={createContact.isPending}
        serverErrors={serverErrors}
        onSubmit={(values) =>
          createContact.mutate(toCreateContactRequest(values), {
            onSuccess: (created) =>
              navigate('/contacts', { state: { createdContactName: contactDisplayName(created) } }),
          })
        }
        cancelTo="/contacts"
      />
    </>
  )
}
