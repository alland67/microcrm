import type { Contact } from '../../api/types'
import { contactDisplayName } from './contactName'

export function ContactsTable({ contacts }: { contacts: readonly Contact[] }) {
  return (
    <table>
      <caption>Contacts</caption>
      <thead>
        <tr>
          <th scope="col">Name</th>
          <th scope="col">Email</th>
          <th scope="col">Company</th>
          <th scope="col">Phone</th>
        </tr>
      </thead>
      <tbody>
        {contacts.map((c) => (
          <tr key={c.id}>
            <td>{contactDisplayName(c)}</td>
            <td>{c.email}</td>
            <td>{c.company}</td>
            <td>{c.phone}</td>
          </tr>
        ))}
      </tbody>
    </table>
  )
}
