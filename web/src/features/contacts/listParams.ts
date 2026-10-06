import type { ContactListParams } from './useContacts'

/** API `ListQuery.MaxSearchLength`. */
export const MAX_SEARCH_LENGTH = 254

export type ListAddress = { params: ContactListParams; searchTooLong: boolean }

/**
 * Reads the list state from the address. Search is trimmed; blank or absent becomes null.
 * An over-long term is kept in `params.search` (so the box can show it) and flagged.
 */
export function readListParams(searchParams: URLSearchParams): ListAddress {
  const page = Number(searchParams.get('page'))
  const search = searchParams.get('search')?.trim() ?? ''
  return {
    params: {
      page: Number.isInteger(page) && page >= 1 ? page : 1,
      search: search === '' ? null : search,
    },
    searchTooLong: search.length > MAX_SEARCH_LENGTH,
  }
}

/** Writes the list state to the address; page 1 and a null search are left out. */
export function toSearchParams(params: ContactListParams): URLSearchParams {
  const searchParams = new URLSearchParams()
  if (params.search !== null) searchParams.set('search', params.search)
  if (params.page !== 1) searchParams.set('page', String(params.page))
  return searchParams
}
