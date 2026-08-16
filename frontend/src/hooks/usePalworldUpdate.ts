import { useCallback, useEffect, useState } from 'react'
import {
  applyPalworldUpdate,
  fetchPalworldUpdateStatus,
  PalworldApiRequestError,
} from '../api/palworld'
import type { PalworldUpdateResponse, PalworldUpdateStatus } from '../types/palworld'

type PalworldUpdateState =
  | { status: 'idle'; update: null; message: null }
  | { status: 'checking'; update: PalworldUpdateStatus | null; message: null }
  | { status: 'success'; update: PalworldUpdateStatus; message: string | null }
  | { status: 'unavailable'; update: null; message: string }
  | { status: 'error'; update: null; message: string }

export function usePalworldUpdate() {
  const [state, setState] = useState<PalworldUpdateState>({
    status: 'idle',
    update: null,
    message: null,
  })
  const [isUpdating, setIsUpdating] = useState(false)
  const [lastResult, setLastResult] = useState<PalworldUpdateResponse | null>(null)

  const check = useCallback(async (signal?: AbortSignal, message: string | null = null) => {
    setState((current) => ({
      status: 'checking',
      update: current.status === 'success' || current.status === 'checking'
        ? current.update
        : null,
      message: null,
    }))

    try {
      const update = await fetchPalworldUpdateStatus(signal)
      setState({ status: 'success', update, message })
    } catch (error) {
      if (signal?.aborted) {
        return
      }

      const status = error instanceof PalworldApiRequestError && error.status === 503
        ? 'unavailable'
        : 'error'

      setState({
        status,
        update: null,
        message: status === 'unavailable'
          ? 'Palworld update integration is not configured.'
          : 'Unable to check Palworld updates.',
      })
    }
  }, [])

  useEffect(() => {
    const controller = new AbortController()

    void check(controller.signal)

    return () => {
      controller.abort()
    }
  }, [check])

  const updateServer = useCallback(async (confirmationText: string) => {
    setIsUpdating(true)
    setLastResult(null)

    try {
      const result = await applyPalworldUpdate(confirmationText)
      setLastResult(result)
      await check(undefined, result.message)

      return result
    } finally {
      setIsUpdating(false)
    }
  }, [check])

  return {
    ...state,
    isUpdating,
    lastResult,
    check,
    updateServer,
  }
}
