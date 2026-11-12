import { describe, expect, it } from 'vitest'
import { MAX_PAGE, readListParams, toSearchParams } from './listParams'

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

function pageOf(value: string) {
  return readListParams(new URLSearchParams({ page: value })).params.page
}

describe('readListParams: page', () => {
  it('is page 1 when the page is absent [AC-027]', () => {
    expect(readListParams(new URLSearchParams()).params.page).toBe(1)
  })

  it('exposes MAX_PAGE as the API int maximum [AC-027]', () => {
    expect(MAX_PAGE).toBe(2147483647)
  })

  it.each([
    ['1', 1],
    ['2', 2],
    ['02', 2],
    ['2147483647', 2147483647],
  ])('accepts the whole number %j as page %i [AC-027]', (value, expected) => {
    expect(pageOf(value)).toBe(expected)
  })

  it.each([
    '0',
    '-1',
    '2.5',
    'abc',
    '',
    ' 2',
    '2 ',
    '+2',
    '1e3',
    '0x10',
    '2147483648',
    '99999999999',
  ])('treats the page %j as page 1 [AC-027]', (value) => {
    expect(pageOf(value)).toBe(1)
  })

  it('keeps the page and the trimmed search together [AC-027]', () => {
    const { params } = readListParams(new URLSearchParams('search=%20ada%20&page=3'))

    expect(params).toEqual({ page: 3, search: 'ada' })
  })
})

describe('readListParams: search', () => {
  it.each(['', '   '])('treats the blank search %j as null [AC-027]', (value) => {
    expect(readListParams(new URLSearchParams({ search: value })).params.search).toBeNull()
  })

  it('trims the search term [AC-027]', () => {
    expect(readListParams(new URLSearchParams({ search: '  ada ' })).params.search).toBe('ada')
  })
})

describe('toSearchParams', () => {
  it('leaves out page 1 and a null search [AC-027]', () => {
    expect(toSearchParams({ page: 1, search: null }).toString()).toBe('')
  })

  it('writes a page above 1 [AC-022]', () => {
    expect(toSearchParams({ page: 2, search: null }).toString()).toBe('page=2')
  })

  it('writes the search and leaves out page 1 [AC-027]', () => {
    expect(toSearchParams({ page: 1, search: 'ada' }).toString()).toBe('search=ada')
  })

  it('writes both the search and the page [AC-022]', () => {
    const result = toSearchParams({ page: 3, search: 'ada' })

    expect(result.get('search')).toBe('ada')
    expect(result.get('page')).toBe('3')
  })
})
