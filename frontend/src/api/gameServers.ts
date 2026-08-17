import type { GameServer, GameServersResponse } from '../types/gameServers'

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')

export class GameServersApiRequestError extends Error {
  public readonly status: number

  public constructor(status: number, message: string) {
    super(message)
    this.name = 'GameServersApiRequestError'
    this.status = status
  }
}

export async function fetchGameServers(signal?: AbortSignal): Promise<GameServer[]> {
  const response = await fetchJson<GameServersResponse>('/api/servers', signal)

  return response.servers
}

export async function fetchGameServer(
  serverId: string,
  signal?: AbortSignal,
): Promise<GameServer> {
  return fetchJson<GameServer>(`/api/servers/${encodeURIComponent(serverId)}`, signal)
}

async function fetchJson<TResponse>(
  path: string,
  signal?: AbortSignal,
): Promise<TResponse> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    headers: {
      Accept: 'application/json',
    },
    signal,
  })

  if (!response.ok) {
    throw new GameServersApiRequestError(
      response.status,
      `Game servers request failed with status ${response.status}.`,
    )
  }

  return response.json() as Promise<TResponse>
}
