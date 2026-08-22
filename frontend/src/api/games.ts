import type {
  GameCatalogGame,
  GameCatalogResponse,
  GameCompatibilityAssessment,
  GamePortPlan,
  GameStoragePlan,
} from '../types/games'

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')

export class GamesApiRequestError extends Error {
  public readonly status: number

  public constructor(status: number, message: string) {
    super(message)
    this.name = 'GamesApiRequestError'
    this.status = status
  }
}

export async function fetchGameCatalog(signal?: AbortSignal): Promise<GameCatalogGame[]> {
  const response = await fetchJson<GameCatalogResponse>('/api/games', signal)

  return response.games
}

export async function fetchGame(
  gameId: string,
  signal?: AbortSignal,
): Promise<GameCatalogGame> {
  return fetchJson<GameCatalogGame>(`/api/games/${encodeURIComponent(gameId)}`, signal)
}

export async function fetchGameCompatibility(
  gameId: string,
  signal?: AbortSignal,
): Promise<GameCompatibilityAssessment> {
  return fetchJson<GameCompatibilityAssessment>(
    `/api/games/${encodeURIComponent(gameId)}/compatibility`,
    signal,
  )
}

export async function fetchGamePortPlan(
  gameId: string,
  signal?: AbortSignal,
): Promise<GamePortPlan> {
  return fetchJson<GamePortPlan>(
    `/api/games/${encodeURIComponent(gameId)}/ports/plan`,
    signal,
    { method: 'POST' },
  )
}

export async function fetchGameStoragePlan(
  gameId: string,
  gameServerId: string,
  signal?: AbortSignal,
): Promise<GameStoragePlan> {
  return fetchJson<GameStoragePlan>(
    `/api/games/${encodeURIComponent(gameId)}/storage/plan`,
    signal,
    {
      body: JSON.stringify({ gameServerId }),
      headers: {
        'Content-Type': 'application/json',
      },
      method: 'POST',
    },
  )
}

async function fetchJson<TResponse>(
  path: string,
  signal?: AbortSignal,
  init: RequestInit = {},
): Promise<TResponse> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      Accept: 'application/json',
      ...init.headers,
    },
    signal,
  })

  if (!response.ok) {
    throw new GamesApiRequestError(
      response.status,
      `Game catalog request failed with status ${response.status}.`,
    )
  }

  return response.json() as Promise<TResponse>
}
