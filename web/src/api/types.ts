export type Contact = {
  id: string
  firstName: string
  lastName: string | null
  email: string | null
  phone: string | null
  company: string | null
  notes: string | null
  createdAt: string
  updatedAt: string
}

export type PagedResponse<T> = { items: T[]; page: number; pageSize: number; totalCount: number }

export type CreateContactRequest = {
  firstName: string
  lastName: string | null
  email: string | null
  phone: string | null
  company: string | null
  notes: string | null
}
