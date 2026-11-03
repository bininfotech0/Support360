import { useCallback, useEffect, useRef, useState } from 'react'

interface LoadState<T> {
  data: T | null
  error: string | null
  loading: boolean
}

export function useLoad<T>(key: string, loader: () => Promise<T>) {
  const loaderRef = useRef(loader)
  const [version, setVersion] = useState(0)

  useEffect(() => {
    loaderRef.current = loader
  }, [loader])
  const [state, setState] = useState<LoadState<T>>({
    data: null,
    error: null,
    loading: true,
  })

  useEffect(() => {
    let cancelled = false

    loaderRef
      .current()
      .then((data) => {
        if (!cancelled) {
          setState({ data, error: null, loading: false })
        }
      })
      .catch((error: unknown) => {
        if (!cancelled) {
          setState((previous) => ({
            data: previous.data,
            error: error instanceof Error ? error.message : 'Unable to load data.',
            loading: false,
          }))
        }
      })

    return () => {
      cancelled = true
    }
  }, [key, version])

  const refresh = useCallback(() => {
    setState((previous) => ({ ...previous, loading: true, error: null }))
    setVersion((value) => value + 1)
  }, [])

  return { ...state, refresh }
}
