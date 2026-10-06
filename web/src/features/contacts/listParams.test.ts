import { describe, expect, it } from 'vitest'
import { readListParams } from './listParams'

const MESSAGE_LIMIT = 254

function searchOf(term: string) {
  return new URLSearchParams({ search: term })
}

describe('readListParams: over-long search term', () => {
  it('is not too long at exactly 254 characters [AC-049]', () => {
    const term = 'x'.repeat(MESSAGE_LIMIT)

    const address = readListParams(searchOf(term))

    expect(address.searchTooLong).toBe(false)
    expect(address.params.search).toBe(term)
  })

  it('is not too long at 254 characters with surrounding spaces, and the term is trimmed [AC-049]', () => {
    const term = 'x'.repeat(MESSAGE_LIMIT)

    const address = readListParams(searchOf(`  ${term}  `))

    expect(address.searchTooLong).toBe(false)
    expect(address.params.search).toBe(term)
  })

  it('is too long at 255 characters, and params.search still holds the term [AC-049]', () => {
    const term = 'x'.repeat(MESSAGE_LIMIT + 1)

    const address = readListParams(searchOf(term))

    expect(address.searchTooLong).toBe(true)
    expect(address.params.search).toBe(term)
  })

  it('is too long at 255 characters after trimming surrounding spaces [AC-049]', () => {
    const term = 'x'.repeat(MESSAGE_LIMIT + 1)

    const address = readListParams(searchOf(`  ${term}  `))

    expect(address.searchTooLong).toBe(true)
    expect(address.params.search).toBe(term)
  })

  it('is not too long when the search is absent [AC-049]', () => {
    const address = readListParams(new URLSearchParams())

    expect(address.searchTooLong).toBe(false)
    expect(address.params.search).toBeNull()
  })
})
