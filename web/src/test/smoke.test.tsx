import { render, screen } from '@testing-library/react'
import { describe, expect, it } from 'vitest'

// Harness smoke test: proves Vitest + jsdom + Testing Library are wired up.
describe('test harness', () => {
  it('renders React into jsdom', () => {
    render(<h1>MicroCRM</h1>)
    expect(screen.getByRole('heading', { name: 'MicroCRM' })).toBeInTheDocument()
  })
})
