import type { Contact } from '../../api/types'

export function contactDisplayName(c: Pick<Contact, 'firstName' | 'lastName'>): string {
  return c.lastName === null ? c.firstName : `${c.firstName} ${c.lastName}`
}
