import styles from './Pagination.module.css'

type PaginationProps = { page: number; totalPages: number; onPageChange: (page: number) => void }

export function Pagination({ page, totalPages, onPageChange }: PaginationProps) {
  return (
    <nav aria-label="Pagination" className={styles.pagination}>
      <button type="button" disabled={page <= 1} onClick={() => onPageChange(page - 1)}>
        Previous
      </button>
      <span>
        Page {page} of {totalPages}
      </span>
      <button type="button" disabled={page >= totalPages} onClick={() => onPageChange(page + 1)}>
        Next
      </button>
    </nav>
  )
}
