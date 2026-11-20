import type { HostCapabilities } from '../types/hostCapabilities'

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')

export class HostCapabilitiesApiRequestError extends Error {
  public readonly status: number

  public constructor(status: number, message: string) {
    super(message)
    this.name = 'HostCapabilitiesApiRequestError'
    this.status = status
  }
}

export async function fetchHostCapabilities(signal?: AbortSignal): Promise<HostCapabilities> {
  const response = await fetch(`${apiBaseUrl}/api/system/capabilities`, {
    headers: {
      Accept: 'application/json',
    },
    signal,
  })

  if (!response.ok) {
    throw new HostCapabilitiesApiRequestError(
      response.status,
      `Host capabilities request failed with status ${response.status}.`,
    )
  }

  return response.json() as Promise<HostCapabilities>
}
