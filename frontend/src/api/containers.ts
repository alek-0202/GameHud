import type { Container } from '../types/container'

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')

export async function fetchContainers(signal?: AbortSignal): Promise<Container[]> {
  const response = await fetch(`${apiBaseUrl}/api/containers`, {
    headers: {
      Accept: 'application/json',
    },
    signal,
  })

  if (!response.ok) {
    throw new Error(`Container request failed with status ${response.status}.`)
  }

  return response.json() as Promise<Container[]>
}
