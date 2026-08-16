import type {
  PalworldConfig,
  PalworldConfigUpdateRequest,
  PalworldConfigUpdateResponse,
  PalworldOverview,
  PalworldPlayers,
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
