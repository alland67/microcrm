import { Link, useSearchParams } from 'react-router'
import { ContactSearchForm } from './ContactSearchForm'
import { readListParams, toSearchParams } from './listParams'
import { ContactsTable } from './ContactsTable'
import { useContactsList } from './useContacts'
import styles from './ContactsPage.module.css'

export function ContactsPage() {
  const [searchParams, setSearchParams] = useSearchParams()
  const params = readListParams(searchParams)
  const { data, isFetching, isError, refetch } = useContactsList(params)

  function handleSearch(term: string | null) {
    setSearchParams(toSearchParams({ page: 1, search: term }))
  }

  return (
    <>
      <h1>Contacts</h1>
      <Link to="/contacts/new">New contact</Link>
      <ContactSearchForm initialTerm={params.search ?? ''} onSearch={handleSearch} />
      {isFetching && (!data || isError) ? (
        <p role="status" className={styles.message}>
          Loading contacts…
        </p>
      ) : isError ? (
        <div role="alert" className={styles.message}>
          <p>Couldn't load contacts.</p>
          <button type="button" onClick={() => void refetch()}>
            Try again
          </button>
        </div>
      ) : data && data.totalCount === 0 ? (
        <p className={styles.message}>
          No contacts yet. <Link to="/contacts/new">Add a contact</Link>
        </p>
      ) : (
        data && (
          <>
            <p>{data.totalCount === 1 ? '1 contact' : `${data.totalCount} contacts`}</p>
            <ContactsTable contacts={data.items} />
          </>
        )
      )}
    </>
  )
}
