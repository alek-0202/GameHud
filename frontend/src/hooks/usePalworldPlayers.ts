import { useEffect, useState } from 'react'
import { PalworldApiRequestError, fetchPalworldPlayers } from '../api/palworld'
import type { PalworldPlayers } from '../types/palworld'

type PalworldPlayersState =
  | { status: 'loading'; players: null; message?: undefined }
  | { status: 'success'; players: PalworldPlayers; message?: undefined }
  | { status: 'unavailable'; players: null; message: string }
  | { status: 'error'; players: null; message: string }

export function usePalworldPlayers(pollIntervalMs = 15000): PalworldPlayersState {
  const [state, setState] = useState<PalworldPlayersState>({
    status: 'loading',
    players: null,
  })

  useEffect(() => {
    let isMounted = true
    let timeoutId: number | null = null
    let abortController: AbortController | null = null

    async function loadPlayers(showLoading: boolean) {
      if (document.visibilityState === 'hidden') {
        scheduleNext()
        return
      }

      abortController?.abort()
      abortController = new AbortController()

      try {
        if (showLoading) {
          setState({ status: 'loading', players: null })
        }

        const players = await fetchPalworldPlayers(abortController.signal)

        if (isMounted) {
          setState({ status: 'success', players })
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
        void loadPlayers(false)
      }, pollIntervalMs)
    }

    function handleVisibilityChange() {
      if (document.visibilityState === 'visible') {
        void loadPlayers(false)
      }
    }

    void loadPlayers(true)
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

function getErrorState(error: unknown): PalworldPlayersState {
  if (error instanceof PalworldApiRequestError && error.status === 503) {
    return {
      status: 'unavailable',
      players: null,
      message: 'Palworld REST API is unavailable.',
    }
  }

  return {
    status: 'error',
    players: null,
    message: 'Unable to load Palworld players.',
  }
}
