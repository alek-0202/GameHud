import { useEffect, useState } from 'react'
import { PalworldApiRequestError, fetchPalworldOverview } from '../api/palworld'
import type { PalworldOverview } from '../types/palworld'

type PalworldOverviewState =
  | { status: 'loading'; overview: null; message?: undefined }
  | { status: 'success'; overview: PalworldOverview; message?: undefined }
  | { status: 'unavailable'; overview: null; message: string }
  | { status: 'error'; overview: null; message: string }

export function usePalworldOverview(pollIntervalMs = 15000): PalworldOverviewState {
  const [state, setState] = useState<PalworldOverviewState>({
    status: 'loading',
    overview: null,
  })

  useEffect(() => {
    let isMounted = true
    let timeoutId: number | null = null
    let abortController: AbortController | null = null

    async function loadOverview(showLoading: boolean) {
      if (document.visibilityState === 'hidden') {
        scheduleNext()
        return
      }

      abortController?.abort()
      abortController = new AbortController()

      try {
        if (showLoading) {
          setState({ status: 'loading', overview: null })
        }

        const overview = await fetchPalworldOverview(abortController.signal)

        if (isMounted) {
          setState({ status: 'success', overview })
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
        void loadOverview(false)
      }, pollIntervalMs)
    }

    function handleVisibilityChange() {
      if (document.visibilityState === 'visible') {
        void loadOverview(false)
      }
    }

    void loadOverview(true)
    document.addEventListener('visibilitychange', handleVisibilityChange)

    return () => {
      isMounted = false
      abortController?.abort()
      document.removeEventListener('visibilitychange', handleVisibilityChange)

      if (timeoutId !== null) {
        window.clearTimeout(timeoutId)
      }
    }
  }, [pollIntervalMs])

  return state
}

function getErrorState(error: unknown): PalworldOverviewState {
  if (error instanceof PalworldApiRequestError && error.status === 503) {
    return {
      status: 'unavailable',
      overview: null,
      message: 'Palworld integration is not available. Check GamesHud backend configuration.',
    }
  }

  return {
    status: 'error',
    overview: null,
    message: 'Unable to load Palworld overview.',
  }
}
