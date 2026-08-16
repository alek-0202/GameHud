import { useCallback, useEffect, useState } from 'react'
import {
  createPalworldBackup,
  deletePalworldBackup,
  fetchPalworldBackups,
  PalworldApiRequestError,
  restorePalworldBackup,
} from '../api/palworld'
import type {
  PalworldBackupSummary,
  PalworldCreateBackupResponse,
  PalworldDeleteBackupResponse,
  PalworldRestoreBackupResponse,
} from '../types/palworld'

type PalworldBackupsState =
  | { status: 'loading'; summary: null; message: null }
  | { status: 'success'; summary: PalworldBackupSummary; message: string | null }
  | { status: 'unavailable'; summary: null; message: string }
  | { status: 'error'; summary: null; message: string }

export function usePalworldBackups(pollIntervalMs = 30000) {
  const [state, setState] = useState<PalworldBackupsState>({
    status: 'loading',
    summary: null,
    message: null,
  })
  const [action, setAction] = useState<string | null>(null)

  const load = useCallback(async (signal?: AbortSignal, message: string | null = null) => {
    try {
      const summary = await fetchPalworldBackups(signal)
      setState({ status: 'success', summary, message })
    } catch (error) {
      if (signal?.aborted) {
        return
      }

      const status = error instanceof PalworldApiRequestError && error.status === 503
        ? 'unavailable'
        : 'error'

      setState({
        status,
        summary: null,
        message: status === 'unavailable'
          ? 'Palworld backups are not configured yet.'
          : 'Unable to load Palworld backups.',
      })
    }
  }, [])

  useEffect(() => {
    const controller = new AbortController()

    void load(controller.signal)

    const interval = window.setInterval(() => {
      if (document.visibilityState === 'visible' && action === null) {
        void load()
      }
    }, pollIntervalMs)

    return () => {
      controller.abort()
      window.clearInterval(interval)
    }
  }, [action, load, pollIntervalMs])

  const createBackup = useCallback(async (note: string | null) => {
    setAction('create')

    try {
      const result: PalworldCreateBackupResponse = await createPalworldBackup(note)
      await load(undefined, result.message)
    } finally {
      setAction(null)
    }
  }, [load])

  const restoreBackup = useCallback(async (backupId: string, confirmationText: string) => {
    setAction(`restore:${backupId}`)

    try {
      const result: PalworldRestoreBackupResponse = await restorePalworldBackup(
        backupId,
        confirmationText,
      )
      await load(undefined, `${result.message} Health check: ${result.healthCheckStatus}.`)
    } finally {
      setAction(null)
    }
  }, [load])

  const deleteBackup = useCallback(async (backupId: string, confirmationText: string) => {
    setAction(`delete:${backupId}`)

    try {
      const result: PalworldDeleteBackupResponse = await deletePalworldBackup(
        backupId,
        confirmationText,
      )
      await load(undefined, result.message)
    } finally {
      setAction(null)
    }
  }, [load])

  return {
    ...state,
    action,
    createBackup,
    restoreBackup,
    deleteBackup,
    refresh: load,
  }
}
