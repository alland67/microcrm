import { useNavigate } from 'react-router'
import { ContactForm } from './ContactForm'
import { contactDisplayName } from './contactName'
import { toCreateContactRequest } from './contactFormRules'
import { useCreateContact } from './useContacts'

export function NewContactPage() {
  const createContact = useCreateContact()
  const navigate = useNavigate()

  return (
    <>
      <h1>New contact</h1>
      <ContactForm
        isSaving={createContact.isPending}
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
