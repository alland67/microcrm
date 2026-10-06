import type { ContactListParams } from './useContacts'

/** Reads the list state from the address. Search is trimmed; blank or absent becomes null. */
export function readListParams(searchParams: URLSearchParams): ContactListParams {
  const page = Number(searchParams.get('page'))
  const search = searchParams.get('search')?.trim() ?? ''
  return {
    page: Number.isInteger(page) && page >= 1 ? page : 1,
    search: search === '' ? null : search,
  }
}

/** Writes the list state to the address; page 1 and a null search are left out. */
export function toSearchParams(params: ContactListParams): URLSearchParams {
  const searchParams = new URLSearchParams()
  if (params.search !== null) searchParams.set('search', params.search)
  if (params.page !== 1) searchParams.set('page', String(params.page))
  return searchParams
}
