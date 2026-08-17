import type {
  PalworldConfig,
  PalworldConfigUpdateRequest,
  PalworldConfigUpdateResponse,
  PalworldAdminActionResponse,
  PalworldBackupSummary,
  PalworldCreateBackupResponse,
  PalworldDeleteBackupResponse,
  PalworldOverview,
  PalworldPlayers,
  PalworldRestoreBackupResponse,
  PalworldMods,
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

export async function fetchPalworldConfig(
  signal?: AbortSignal,
  serverId?: string,
): Promise<PalworldConfig> {
  return fetchJson<PalworldConfig>(resolvePalworldPath('/config', serverId, '/settings'), signal)
}

export async function fetchPalworldOverview(
  signal?: AbortSignal,
  serverId?: string,
): Promise<PalworldOverview> {
  return fetchJson<PalworldOverview>(resolvePalworldPath('/overview', serverId, '/overview'), signal)
}

export async function fetchPalworldPlayers(
  signal?: AbortSignal,
  serverId?: string,
): Promise<PalworldPlayers> {
  return fetchJson<PalworldPlayers>(resolvePalworldPath('/players', serverId, '/players'), signal)
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
  serverId?: string,
): Promise<PalworldConfigUpdateResponse> {
  const parameters = new URLSearchParams({
    restart: restart.toString(),
  })

  return fetchJson<PalworldConfigUpdateResponse>(
    `${resolvePalworldPath('/config', serverId, '/settings')}?${parameters.toString()}`,
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

export async function sendPalworldAnnouncement(
  message: string,
  serverId: string,
  signal?: AbortSignal,
): Promise<PalworldAdminActionResponse> {
  return fetchJson<PalworldAdminActionResponse>(
    `/api/servers/${encodeURIComponent(serverId)}/announcements`,
    signal,
    jsonPost({ message }),
  )
}

export async function kickPalworldPlayer(
  serverId: string,
  userId: string,
  confirmationText: string,
  message: string | null,
  signal?: AbortSignal,
): Promise<PalworldAdminActionResponse> {
  return postPalworldPlayerAction(serverId, userId, 'kick', confirmationText, message, signal)
}

export async function banPalworldPlayer(
  serverId: string,
  userId: string,
  confirmationText: string,
  message: string | null,
  signal?: AbortSignal,
): Promise<PalworldAdminActionResponse> {
  return postPalworldPlayerAction(serverId, userId, 'ban', confirmationText, message, signal)
}

export async function unbanPalworldPlayer(
  serverId: string,
  userId: string,
  confirmationText: string,
  signal?: AbortSignal,
): Promise<PalworldAdminActionResponse> {
  return fetchJson<PalworldAdminActionResponse>(
    `/api/servers/${encodeURIComponent(serverId)}/players/unban`,
    signal,
    jsonPost({ userId, confirmationText }),
  )
}

export async function fetchPalworldMods(
  serverId: string,
  signal?: AbortSignal,
): Promise<PalworldMods> {
  return fetchJson<PalworldMods>(`/api/servers/${encodeURIComponent(serverId)}/mods`, signal)
}

function postPalworldPlayerAction(
  serverId: string,
  userId: string,
  action: 'kick' | 'ban',
  confirmationText: string,
  message: string | null,
  signal?: AbortSignal,
): Promise<PalworldAdminActionResponse> {
  return fetchJson<PalworldAdminActionResponse>(
    `/api/servers/${encodeURIComponent(serverId)}/players/${encodeURIComponent(userId)}/${action}`,
    signal,
    jsonPost({ confirmationText, message }),
  )
}

function jsonPost(body: unknown): RequestInit {
  return {
    body: JSON.stringify(body),
    headers: {
      'Content-Type': 'application/json',
    },
    method: 'POST',
  }
}

function resolvePalworldPath(
  legacyPath: string,
  serverId: string | undefined,
  serverPath: string,
): string {
  return serverId === undefined
    ? `/api/palworld${legacyPath}`
    : `/api/servers/${encodeURIComponent(serverId)}${serverPath}`
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
