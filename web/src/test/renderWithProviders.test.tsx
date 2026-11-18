import { screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'
import { useLocation } from 'react-router'
import { renderWithProviders } from './renderWithProviders'

function Where({ label }: { label: string }) {
  return (
    <p>
      {label}:{useLocation().pathname}
    </p>
  )
}

describe('renderWithProviders', () => {
  it('keeps the router context and location probe after rerender', () => {
    const { rerender, getLocation } = renderWithProviders(<Where label="first" />, {
      route: '/contacts/new',
    })
    expect(screen.getByText('first:/contacts/new')).toBeInTheDocument()

    rerender(<Where label="second" />)

    expect(screen.getByText('second:/contacts/new')).toBeInTheDocument()
    expect(getLocation().pathname).toBe('/contacts/new')
  })
})
