import type {
  PalworldConfig,
  PalworldConfigUpdateRequest,
  PalworldConfigUpdateResponse,
  PalworldBackupSummary,
  PalworldCreateBackupResponse,
  PalworldDeleteBackupResponse,
  PalworldOverview,
  PalworldPlayers,
  PalworldRestoreBackupResponse,
  PalworldUpdateResponse,
  PalworldUpdateStatus,
} from '../types/palworld'

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')

export class PalworldApiRequestError extends Error {
  public readonly status: number

  public constructor(status: number, message: string) {
    super(message)
    this.name = 'PalworldApiRequestError'
    this.status = status
  }
}

export async function fetchPalworldConfig(signal?: AbortSignal): Promise<PalworldConfig> {
  return fetchJson<PalworldConfig>('/api/palworld/config', signal)
}

export async function fetchPalworldOverview(signal?: AbortSignal): Promise<PalworldOverview> {
  return fetchJson<PalworldOverview>('/api/palworld/overview', signal)
}

export async function fetchPalworldPlayers(signal?: AbortSignal): Promise<PalworldPlayers> {
  return fetchJson<PalworldPlayers>('/api/palworld/players', signal)
}

export async function fetchPalworldUpdateStatus(signal?: AbortSignal): Promise<PalworldUpdateStatus> {
  return fetchJson<PalworldUpdateStatus>('/api/palworld/update', signal)
}

export async function applyPalworldUpdate(
  confirmationText: string,
  signal?: AbortSignal,
): Promise<PalworldUpdateResponse> {
  return fetchJson<PalworldUpdateResponse>('/api/palworld/update', signal, {
    body: JSON.stringify({ confirmationText }),
    headers: {
      'Content-Type': 'application/json',
    },
    method: 'POST',
  })
}

export function resolvePalworldBackupDownloadUrl(downloadUrl: string | null): string | null {
  return downloadUrl === null ? null : `${apiBaseUrl}${downloadUrl}`
}

export async function fetchPalworldBackups(signal?: AbortSignal): Promise<PalworldBackupSummary> {
  return fetchJson<PalworldBackupSummary>('/api/palworld/backups', signal)
}

export async function createPalworldBackup(
  note: string | null,
  signal?: AbortSignal,
): Promise<PalworldCreateBackupResponse> {
  return fetchJson<PalworldCreateBackupResponse>('/api/palworld/backups', signal, {
    body: JSON.stringify({ note }),
    headers: {
      'Content-Type': 'application/json',
    },
    method: 'POST',
  })
}

export async function restorePalworldBackup(
  backupId: string,
  confirmationText: string,
  signal?: AbortSignal,
): Promise<PalworldRestoreBackupResponse> {
  return fetchJson<PalworldRestoreBackupResponse>(
    `/api/palworld/backups/${encodeURIComponent(backupId)}/restore`,
    signal,
    {
      body: JSON.stringify({ confirmationText }),
      headers: {
        'Content-Type': 'application/json',
      },
      method: 'POST',
    },
  )
}

export async function deletePalworldBackup(
  backupId: string,
  confirmationText: string,
  signal?: AbortSignal,
): Promise<PalworldDeleteBackupResponse> {
  return fetchJson<PalworldDeleteBackupResponse>(
    `/api/palworld/backups/${encodeURIComponent(backupId)}`,
    signal,
    {
      body: JSON.stringify({ confirmationText }),
      headers: {
        'Content-Type': 'application/json',
      },
      method: 'DELETE',
    },
  )
}

export async function updatePalworldConfig(
  request: PalworldConfigUpdateRequest,
  restart: boolean,
  signal?: AbortSignal,
): Promise<PalworldConfigUpdateResponse> {
  const parameters = new URLSearchParams({
    restart: restart.toString(),
  })

  return fetchJson<PalworldConfigUpdateResponse>(
    `/api/palworld/config?${parameters.toString()}`,
    signal,
    {
      body: JSON.stringify(request),
      headers: {
        'Content-Type': 'application/json',
      },
      method: 'PUT',
    },
  )
}

async function fetchJson<TResponse>(
  path: string,
  signal?: AbortSignal,
  init?: RequestInit,
): Promise<TResponse> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      Accept: 'application/json',
      ...init?.headers,
    },
    signal,
  })

  if (!response.ok) {
    throw new PalworldApiRequestError(
      response.status,
      `Palworld request failed with status ${response.status}.`,
    )
  }

  return response.json() as Promise<TResponse>
}
