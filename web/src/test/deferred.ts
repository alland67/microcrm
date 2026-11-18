/**
 * A promise that the test resolves by hand. A handler awaits `promise` to hold a response
 * open (loading and saving states, late responses); the test calls `resolve` to release it.
 */
export type Deferred<T> = { promise: Promise<T>; resolve: (value: T) => void }

export function deferred<T = void>(): Deferred<T> {
  let resolve!: (value: T) => void // assigned synchronously by the Promise executor below
  const promise = new Promise<T>((res) => {
    resolve = res
  })
  return { promise, resolve }
}
