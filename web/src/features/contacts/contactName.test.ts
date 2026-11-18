import { describe, expect, it } from 'vitest'
import { contactDisplayName } from './contactName'

describe('contactDisplayName', () => {
  it('joins first and last name with one space [AC-006]', () => {
    expect(contactDisplayName({ firstName: 'Ada', lastName: 'Lovelace' })).toBe('Ada Lovelace')
  })

  it('is just the first name when there is no last name [AC-006]', () => {
    expect(contactDisplayName({ firstName: 'Cher', lastName: null })).toBe('Cher')
  })
})
