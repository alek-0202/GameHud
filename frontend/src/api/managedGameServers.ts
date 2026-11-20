const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')

export interface MinecraftManagedInstallRequest {
  ramMiB: number
  port: number
  eulaAccepted: boolean
}

export interface CreateManagedGameServerResponse {
  gameServerId: string
  provisioningOperationId: string
}

export interface ManagedGameServer {
  id: string
  gameId: string
  displayName: string
  installationType: string
  runtimeType: string
  lifecycleState: string
  createdAtUtc: string
  updatedAtUtc: string
}

export interface ManagedProvisioningStep {
  stepId: string
  sequence: number
  status: string
  attempt: number
  maxAttempts: number
  startedAtUtc: string | null
  completedAtUtc: string | null
}

export interface ManagedProvisioning {
  operationId: string
  gameServerId: string
  status: string
  currentStep: string
  pipelineVersion: string
  requiresInspection: boolean
  failure: { code: string; message: string; requiresInspection: boolean } | null
  steps: ManagedProvisioningStep[]
}

export class ManagedGameServersApiError extends Error {
  public readonly status: number

  public constructor(status: number, message: string) {
    super(message)
    this.name = 'ManagedGameServersApiError'
    this.status = status
  }
}

export async function createMinecraftManagedServer(
  displayName: string,
  minecraft: MinecraftManagedInstallRequest,
  idempotencyKey: string,
  signal?: AbortSignal,
): Promise<CreateManagedGameServerResponse> {
  const csrf = await fetchJson<{ requestToken: string }>('/api/auth/csrf', {
    credentials: 'include', signal,
  })
  return fetchJson<CreateManagedGameServerResponse>('/api/game-servers', {
    method: 'POST',
    credentials: 'include',
    headers: {
      'Content-Type': 'application/json',
      'Idempotency-Key': idempotencyKey,
      'X-CSRF-TOKEN': csrf.requestToken,
    },
    body: JSON.stringify({ gameId: 'minecraft', displayName, minecraft }),
    signal,
  })
}

export function fetchManagedGameServer(id: string, signal?: AbortSignal) {
  return fetchJson<ManagedGameServer>(`/api/game-servers/${encodeURIComponent(id)}`, {
    credentials: 'include', signal,
  })
}

export function fetchManagedProvisioning(id: string, signal?: AbortSignal) {
  return fetchJson<ManagedProvisioning>(`/api/game-servers/${encodeURIComponent(id)}/provisioning`, {
    credentials: 'include', signal,
  })
}

async function fetchJson<T>(path: string, init: RequestInit): Promise<T> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: { Accept: 'application/json', ...init.headers },
  })
  if (!response.ok) {
    const body = await response.json().catch(() => null) as { message?: string } | null
    throw new ManagedGameServersApiError(
      response.status,
      body?.message ?? 'The managed server request could not be completed.',
    )
  }
  return response.json() as Promise<T>
}
