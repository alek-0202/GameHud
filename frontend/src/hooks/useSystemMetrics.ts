import { useEffect, useState } from 'react'
import { MetricsApiRequestError, fetchSystemMetrics } from '../api/metrics'
import type { MetricsHistoryWindow, SystemMetrics } from '../types/metrics'

type SystemMetricsState =
  | { status: 'loading'; metrics: null; message?: undefined }
  | { status: 'success'; metrics: SystemMetrics; message?: undefined }
  | { status: 'unavailable'; metrics: null; message: string }
  | { status: 'error'; metrics: null; message: string }

export function useSystemMetrics(
  historyHours: MetricsHistoryWindow = 1,
  pollIntervalMs = 30000,
): SystemMetricsState {
  const [state, setState] = useState<SystemMetricsState>({
    status: 'loading',
    metrics: null,
  })

  useEffect(() => {
    let isMounted = true
    let timeoutId: number | null = null
    let abortController: AbortController | null = null

    async function loadMetrics(showLoading: boolean) {
      if (document.visibilityState === 'hidden') {
        scheduleNext()
        return
      }

      abortController?.abort()
      abortController = new AbortController()

      try {
        if (showLoading) {
          setState({ status: 'loading', metrics: null })
        }

        const metrics = await fetchSystemMetrics(historyHours, abortController.signal)

        if (isMounted) {
          setState({ status: 'success', metrics })
        }
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }

        if (isMounted) {
          setState(getErrorState(error))
        }
      } finally {
        if (isMounted) {
          scheduleNext()
        }
      }
    }

    function scheduleNext() {
      if (timeoutId !== null) {
        window.clearTimeout(timeoutId)
      }

      timeoutId = window.setTimeout(() => {
        void loadMetrics(false)
      }, pollIntervalMs)
    }

    function handleVisibilityChange() {
      if (document.visibilityState === 'visible') {
        void loadMetrics(false)
      }
    }

    void loadMetrics(true)
    document.addEventListener('visibilitychange', handleVisibilityChange)

    return () => {
      isMounted = false
      abortController?.abort()
      document.removeEventListener('visibilitychange', handleVisibilityChange)

      if (timeoutId !== null) {
        window.clearTimeout(timeoutId)
      }
    }
  }, [historyHours, pollIntervalMs])

  return state
}

function getErrorState(error: unknown): SystemMetricsState {
  if (error instanceof MetricsApiRequestError && error.status === 503) {
    return {
      status: 'unavailable',
      metrics: null,
      message: 'System metrics are unavailable in this environment.',
    }
  }

  return {
    status: 'error',
    metrics: null,
    message: 'Unable to load system metrics.',
  }
}
