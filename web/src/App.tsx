import { Navigate, Route, Routes } from 'react-router'
import { AppLayout } from './components/AppLayout'
import { NotFoundPage } from './components/NotFoundPage'
import { ContactsPage } from './features/contacts/ContactsPage'
import { NewContactPage } from './features/contacts/NewContactPage'

export function App() {
  return (
    <Routes>
      <Route element={<AppLayout />}>
        <Route index element={<Navigate to="/contacts" replace />} />
        <Route path="contacts" element={<ContactsPage />} />
        <Route path="contacts/new" element={<NewContactPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  )
}
