import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { act, render, type RenderResult } from '@testing-library/react'
import userEvent, { type UserEvent } from '@testing-library/user-event'
import { useEffect, type ReactElement, type ReactNode } from 'react'
import { MemoryRouter, useLocation, useNavigate, type NavigateFunction } from 'react-router'

type TestLocation = { pathname: string; search: string; state: unknown }

type Options = { route?: string; queryClient?: QueryClient }

export type RenderWithProvidersResult = RenderResult & {
  user: UserEvent
  queryClient: QueryClient
  getLocation(): TestLocation
  goBack(): Promise<void>
}

export function renderWithProviders(
  ui: ReactElement,
  options: Options = {},
): RenderWithProvidersResult {
  const queryClient =
    options.queryClient ?? new QueryClient({ defaultOptions: { queries: { retry: false } } })

  let currentLocation: TestLocation = { pathname: '', search: '', state: null }
  let navigateFn: NavigateFunction | null = null

  function Probe() {
    const location = useLocation()
    const navigate = useNavigate()
    useEffect(() => {
      currentLocation = {
        pathname: location.pathname,
        search: location.search,
        state: location.state,
      }
      navigateFn = navigate
    })
    return null
  }

  const user = userEvent.setup()
  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <MemoryRouter initialEntries={[options.route ?? '/']}>
          <Probe />
          {children}
        </MemoryRouter>
      </QueryClientProvider>
    )
  }

  const result = render(ui, { wrapper: Wrapper })

  return {
    ...result,
    user,
    queryClient,
    getLocation: () => currentLocation,
    goBack: async () => {
      await act(async () => {
        navigateFn?.(-1)
      })
    },
  }
}
