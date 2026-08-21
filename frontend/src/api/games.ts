import type {
  GameCatalogGame,
  GameCatalogResponse,
  GameCompatibilityAssessment,
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
    throw new GamesApiRequestError(
      response.status,
      `Game catalog request failed with status ${response.status}.`,
    )
  }

  return response.json() as Promise<TResponse>
}
