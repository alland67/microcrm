import type { Contact, PagedResponse } from '../../api/types'

let nextId = 0

/** A contact with a unique id and realistic values. Override any field. */
export function aContact(overrides: Partial<Contact> = {}): Contact {
  nextId += 1
  const id = `00000000-0000-7000-8000-${String(nextId).padStart(12, '0')}`
  return {
    id,
    firstName: 'Ada',
    lastName: 'Lovelace',
    email: `contact${nextId}@example.com`,
    phone: '+44 20 7946 0000',
    company: 'Analytical Engines Ltd',
    notes: null,
    createdAt: '2026-01-01T09:00:00+00:00',
    updatedAt: '2026-01-01T09:00:00+00:00',
    ...overrides,
  }
}

/** A list response. `totalCount` defaults to the number of items. */
export function contactsPage(
  items: Contact[],
  options: { page?: number; pageSize?: number; totalCount?: number } = {},
): PagedResponse<Contact> {
  const { page = 1, pageSize = 20, totalCount = items.length } = options
  return { items, page, pageSize, totalCount }
}
