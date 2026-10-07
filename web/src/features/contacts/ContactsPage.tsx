import { Pagination } from '../../components/Pagination'
import { Link, useLocation, useSearchParams } from 'react-router'
import { ContactSearchForm } from './ContactSearchForm'
import { maxLengthMessage } from '../../lib/validationMessages'
import { MAX_SEARCH_LENGTH, readListParams, toSearchParams } from './listParams'
import { ContactsTable } from './ContactsTable'
import { CONTACTS_PAGE_SIZE, useContactsList } from './useContacts'
import styles from './ContactsPage.module.css'

/** `location.state` is `unknown`; only a string `createdContactName` counts. */
function readCreatedContactName(state: unknown): string | null {
  if (typeof state === 'object' && state !== null && 'createdContactName' in state) {
    const name = state.createdContactName
    return typeof name === 'string' ? name : null
  }
  return null
}

export function ContactsPage() {
  const createdContactName = readCreatedContactName(useLocation().state)
  const [searchParams, setSearchParams] = useSearchParams()
  const { params, searchTooLong } = readListParams(searchParams)
  const { data, isFetching, isError, refetch } = useContactsList(params, {
    enabled: !searchTooLong,
  })

  function handleSearch(term: string | null) {
    setSearchParams(toSearchParams({ page: 1, search: term }))
  }

  function handlePageChange(page: number) {
    setSearchParams(toSearchParams({ ...params, page }))
  }

  return (
    <>
      <h1>Contacts</h1>
      <Link to="/contacts/new">New contact</Link>
      {createdContactName !== null && (
        <p role="status" className={styles.message}>
          Contact "{createdContactName}" created.
        </p>
      )}
      <ContactSearchForm
        initialTerm={params.search ?? ''}
        initialError={searchTooLong ? maxLengthMessage(MAX_SEARCH_LENGTH) : null}
        onSearch={handleSearch}
      />
      {searchTooLong ? null : isFetching && (!data || isError) ? (
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
      ) : data && data.totalCount === 0 && params.search !== null ? (
        <div className={styles.message}>
          <p>No contacts match "{params.search}".</p>
          <button type="button" onClick={() => handleSearch(null)}>
            Clear search
          </button>
        </div>
      ) : data && data.totalCount === 0 ? (
        <p className={styles.message}>
          No contacts yet. <Link to="/contacts/new">Add a contact</Link>
        </p>
      ) : data && data.items.length === 0 ? (
        <div className={styles.message}>
          <p>This page is empty.</p>
          <button type="button" onClick={() => handlePageChange(1)}>
            Go to first page
          </button>
        </div>
      ) : (
        data && (
          <>
            <p>{data.totalCount === 1 ? '1 contact' : `${data.totalCount} contacts`}</p>
            <ContactsTable contacts={data.items} />
            {data.totalCount > CONTACTS_PAGE_SIZE && (
              <Pagination
                page={params.page}
                totalPages={Math.ceil(data.totalCount / CONTACTS_PAGE_SIZE)}
                onPageChange={handlePageChange}
              />
            )}
          </>
        )
      )}
    </>
  )
}
