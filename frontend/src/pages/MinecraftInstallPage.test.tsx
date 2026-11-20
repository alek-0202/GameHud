import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { MinecraftInstallPage } from './MinecraftInstallPage'

describe('MinecraftInstallPage', () => {
  beforeEach(() => {
    vi.stubGlobal('crypto', { randomUUID: () => 'install-key-1' })
  })

  afterEach(() => {
    cleanup()
    vi.unstubAllGlobals()
  })

  it('uses the authenticated Managed API with CSRF, idempotency and persistent progress', async () => {
    const requests: Array<{ path: string; init?: RequestInit }> = []
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
      const path = typeof input === 'string' ? input : input.toString()
      requests.push({ path, init })
      if (path === '/api/auth/csrf') return json({ requestToken: 'csrf-token' })
      if (path === '/api/game-servers') return json({ gameServerId: 'gs-minecraft', provisioningOperationId: 'op-1' }, 202)
      if (path === '/api/game-servers/gs-minecraft/provisioning') return json({
        operationId: 'op-1', gameServerId: 'gs-minecraft', status: 'completed',
        currentStep: 'complete', pipelineVersion: 'gh-mc01-v1', requiresInspection: false,
        failure: null,
        steps: [
          { stepId: 'prepare_storage', sequence: 1, status: 'completed', attempt: 1, maxAttempts: 3, startedAtUtc: null, completedAtUtc: null },
          { stepId: 'complete', sequence: 8, status: 'completed', attempt: 1, maxAttempts: 1, startedAtUtc: null, completedAtUtc: null },
        ],
      })
      return json({}, 404)
    }))

    render(<MemoryRouter><MinecraftInstallPage /></MemoryRouter>)
    fireEvent.click(screen.getByRole('checkbox'))
    fireEvent.click(screen.getByRole('button', { name: 'Install' }))

    expect(await screen.findByText(/Pipeline/)).toHaveTextContent('gh-mc01-v1')
    const create = requests.find((request) => request.path === '/api/game-servers')
    expect(create?.init?.method).toBe('POST')
    expect(create?.init?.credentials).toBe('include')
    expect(create?.init?.headers).toMatchObject({
      'Idempotency-Key': 'install-key-1',
      'X-CSRF-TOKEN': 'csrf-token',
    })
    expect(JSON.parse(create?.init?.body as string)).toEqual({
      gameId: 'minecraft',
      displayName: 'Lunchtime Minecraft',
      minecraft: { ramMiB: 6144, port: 25565, eulaAccepted: true },
    })
    expect(requests.some((request) => request.path.startsWith('/api/containers'))).toBe(false)
    expect(requests.some((request) => request.path.includes('/api/palworld'))).toBe(false)
    expect(screen.getByRole('link', { name: 'Open managed server' })).toHaveAttribute('href', '/managed-servers/gs-minecraft')
  })

  it('does not schedule installation without explicit EULA acceptance', async () => {
    const fetchMock = vi.fn()
    vi.stubGlobal('fetch', fetchMock)
    render(<MemoryRouter><MinecraftInstallPage /></MemoryRouter>)

    fireEvent.click(screen.getByRole('button', { name: 'Install' }))

    expect(await screen.findByRole('alert')).toHaveTextContent(/explicitly accept/i)
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('shows only the sanitized API message on scheduling failure', async () => {
    vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL) => {
      const path = typeof input === 'string' ? input : input.toString()
      return path === '/api/auth/csrf'
        ? json({ requestToken: 'csrf-token' })
        : json({ code: 'port_conflict', message: 'The selected port is already reserved.' }, 409)
    }))
    render(<MemoryRouter><MinecraftInstallPage /></MemoryRouter>)
    fireEvent.click(screen.getByRole('checkbox'))
    fireEvent.click(screen.getByRole('button', { name: 'Install' }))

    await waitFor(() => expect(screen.getByRole('alert')).toHaveTextContent('The selected port is already reserved.'))
    expect(screen.queryByText(/stack/i)).not.toBeInTheDocument()
  })
})

function json(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
}
