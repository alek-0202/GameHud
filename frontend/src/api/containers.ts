import type {
  Container,
  ContainerDetails,
  ContainerLifecycleActionResponse,
  ContainerLogs,
  LogTailOption,
} from '../types/container'

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')

export class ApiRequestError extends Error {
  public readonly status: number

  public constructor(status: number, message: string) {
    super(message)
    this.name = 'ApiRequestError'
    this.status = status
  }
}

export async function fetchContainers(signal?: AbortSignal): Promise<Container[]> {
  return fetchJson<Container[]>('/api/containers', signal)
}

export async function fetchContainerDetails(
  containerId: string,
  signal?: AbortSignal,
): Promise<ContainerDetails> {
  return fetchJson<ContainerDetails>(
    `/api/containers/${encodeURIComponent(containerId)}`,
    signal,
  )
}

export async function fetchContainerLogs(
  containerId: string,
  tail: LogTailOption,
  timestamps: boolean,
  signal?: AbortSignal,
): Promise<ContainerLogs> {
  const parameters = new URLSearchParams({
    tail: tail.toString(),
    timestamps: timestamps.toString(),
  })

  return fetchJson<ContainerLogs>(
    `/api/containers/${encodeURIComponent(containerId)}/logs?${parameters.toString()}`,
    signal,
  )
}

export async function startContainer(
  containerId: string,
  signal?: AbortSignal,
): Promise<ContainerLifecycleActionResponse> {
  return fetchJson<ContainerLifecycleActionResponse>(
    `/api/containers/${encodeURIComponent(containerId)}/start`,
    signal,
    { method: 'POST' },
  )
}

export async function stopContainer(
  containerId: string,
  timeoutSeconds: number,
  signal?: AbortSignal,
): Promise<ContainerLifecycleActionResponse> {
  const parameters = new URLSearchParams({
    timeoutSeconds: timeoutSeconds.toString(),
  })

  return fetchJson<ContainerLifecycleActionResponse>(
    `/api/containers/${encodeURIComponent(containerId)}/stop?${parameters.toString()}`,
    signal,
    { method: 'POST' },
  )
}

export async function restartContainer(
  containerId: string,
  timeoutSeconds: number,
  signal?: AbortSignal,
): Promise<ContainerLifecycleActionResponse> {
  const parameters = new URLSearchParams({
    timeoutSeconds: timeoutSeconds.toString(),
  })

  return fetchJson<ContainerLifecycleActionResponse>(
    `/api/containers/${encodeURIComponent(containerId)}/restart?${parameters.toString()}`,
    signal,
    { method: 'POST' },
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
    throw new ApiRequestError(
      response.status,
      `Container request failed with status ${response.status}.`,
    )
  }

  return response.json() as Promise<TResponse>
}
