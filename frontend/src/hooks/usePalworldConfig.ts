import { useEffect, useState } from 'react'
import { PalworldApiRequestError, fetchPalworldConfig } from '../api/palworld'
import type { PalworldConfig } from '../types/palworld'

type PalworldConfigState =
  | { status: 'loading'; config: null; message?: undefined }
  | { status: 'success'; config: PalworldConfig; message?: undefined }
  | { status: 'not-found'; config: null; message: string }
  | { status: 'unavailable'; config: null; message: string }
  | { status: 'error'; config: null; message: string }

export function usePalworldConfig(
  refreshToken = 0,
  serverId?: string,
): PalworldConfigState {
  const [state, setState] = useState<PalworldConfigState>({
    status: 'loading',
    config: null,
  })

  useEffect(() => {
    const abortController = new AbortController()

    async function loadConfig() {
      try {
        setState({ status: 'loading', config: null })

        const config = await fetchPalworldConfig(abortController.signal, serverId)

        setState({ status: 'success', config })
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }

        setState(getErrorState(error))
      }
    }

    void loadConfig()

    return () => {
      abortController.abort()
    }
  }, [refreshToken, serverId])

  return state
}

function getErrorState(error: unknown): PalworldConfigState {
  if (error instanceof PalworldApiRequestError) {
    if (error.status === 404) {
      return {
        status: 'not-found',
        config: null,
        message: 'Palworld settings file was not found in the configured managed path.',
      }
    }

    if (error.status === 503) {
      return {
        status: 'unavailable',
        config: null,
        message: 'Palworld integration is not configured for this environment.',
      }
    }
  }

  return {
    status: 'error',
    config: null,
    message: 'Unable to load Palworld settings.',
  }
}
