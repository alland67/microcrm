import { Link, Outlet } from 'react-router'
import styles from './AppLayout.module.css'

export function AppLayout() {
  return (
    <>
      <header className={styles.header}>
        <span className={styles.brand}>MicroCRM</span>
        <nav aria-label="Main">
          <Link to="/contacts">Contacts</Link>
        </nav>
      </header>
      <main className={styles.main}>
        <Outlet />
      </main>
    </>
  )
}
