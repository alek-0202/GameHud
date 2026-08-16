import type {
  ContainerMetrics,
  MetricsHistoryWindow,
  PalworldMetrics,
  SystemMetrics,
} from '../types/metrics'

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')

export class MetricsApiRequestError extends Error {
  public readonly status: number

  public constructor(status: number, message: string) {
    super(message)
    this.name = 'MetricsApiRequestError'
    this.status = status
  }
}

export async function fetchSystemMetrics(
  historyHours: MetricsHistoryWindow,
  signal?: AbortSignal,
): Promise<SystemMetrics> {
  const parameters = new URLSearchParams({
    historyHours: historyHours.toString(),
  })

  return fetchJson<SystemMetrics>(`/api/system/metrics?${parameters.toString()}`, signal)
}

export async function fetchContainerMetrics(
  containerId: string,
  signal?: AbortSignal,
): Promise<ContainerMetrics> {
  return fetchJson<ContainerMetrics>(
    `/api/containers/${encodeURIComponent(containerId)}/metrics`,
    signal,
  )
}

export async function fetchPalworldMetrics(
  historyHours: MetricsHistoryWindow,
  signal?: AbortSignal,
): Promise<PalworldMetrics> {
  const parameters = new URLSearchParams({
    historyHours: historyHours.toString(),
  })

  return fetchJson<PalworldMetrics>(`/api/palworld/metrics?${parameters.toString()}`, signal)
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
    throw new MetricsApiRequestError(
      response.status,
      `Metrics request failed with status ${response.status}.`,
    )
  }

  return response.json() as Promise<TResponse>
}
