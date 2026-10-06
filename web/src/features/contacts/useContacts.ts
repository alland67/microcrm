import { useQuery } from '@tanstack/react-query'
import type { UseQueryResult } from '@tanstack/react-query'
import { apiGet } from '../../api/client'
import type { Contact, PagedResponse } from '../../api/types'

export const CONTACTS_PAGE_SIZE = 20

export type ContactListParams = { page: number; search: string | null }

export const contactKeys = {
  all: ['contacts'] as const,
  list: (params: ContactListParams) => ['contacts', params] as const,
}

export function useContactsList(
  params: ContactListParams,
): UseQueryResult<PagedResponse<Contact>, Error> {
  return useQuery({
    queryKey: contactKeys.list(params),
    queryFn: ({ signal }) => {
      const query = new URLSearchParams({
        page: String(params.page),
        pageSize: String(CONTACTS_PAGE_SIZE),
      })
      if (params.search !== null) query.set('search', params.search)
      return apiGet<PagedResponse<Contact>>(`/api/contacts?${query.toString()}`, { signal })
    },
  })
}
