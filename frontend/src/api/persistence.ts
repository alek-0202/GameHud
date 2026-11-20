import type { PersistenceStatus } from '../types/persistence'

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')

export class PersistenceApiRequestError extends Error {
  public readonly status: number

  public constructor(status: number, message: string) {
    super(message)
    this.name = 'PersistenceApiRequestError'
    this.status = status
  }
}

export async function fetchPersistenceStatus(signal?: AbortSignal): Promise<PersistenceStatus> {
  const response = await fetch(`${apiBaseUrl}/api/system/persistence`, {
    headers: {
      Accept: 'application/json',
    },
    signal,
  })

  if (!response.ok) {
    throw new PersistenceApiRequestError(
      response.status,
      `Persistence status request failed with status ${response.status}.`,
    )
  }

  return response.json() as Promise<PersistenceStatus>
}
