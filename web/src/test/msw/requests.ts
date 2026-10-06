import { onTestFinished } from 'vitest'
import { server } from './server'

export type RecordedRequest = { method: string; url: URL; body?: unknown }

/**
 * Starts recording every request MSW sees. Returns a live array that grows as requests start.
 * The listener is removed when the current test finishes. For non-GET requests `body` is
 * filled in shortly after the request starts (it is read asynchronously), so assert on it
 * after awaiting the UI outcome (e.g. with waitFor / findBy*).
 */
export function recordRequests(): RecordedRequest[] {
  const recorded: RecordedRequest[] = []
  const listener = ({ request }: { request: Request }) => {
    const entry: RecordedRequest = { method: request.method, url: new URL(request.url) }
    recorded.push(entry)
    if (request.method !== 'GET' && request.method !== 'HEAD') {
      void request
        .clone()
        .text()
        .then((text) => {
          if (text === '') return
          try {
            entry.body = JSON.parse(text)
          } catch {
            entry.body = text
          }
        })
    }
  }
  server.events.on('request:start', listener)
  onTestFinished(() => {
    server.events.removeListener('request:start', listener)
  })
  return recorded
}
