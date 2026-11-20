import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import App from '../App'
import type { ContainerDetails } from '../types/container'
import type { GameServer } from '../types/gameServers'
import { isSupportedExternalServer } from './GameServerRoutePage'

vi.mock('./PalworldLayout', () => ({
  PalworldLayout: () => <p>Palworld route preserved</p>,
}))

const externalServer: GameServer = {
  id: 'minecraft-lunchtime',
  gameType: 'minecraft',
  displayName: 'Lunchtime',
  runtimeType: 'docker',
  installationType: 'legacyExternal',
  containerName: 'minecraft-lunchtime',
  brandingImage: null,
  capabilities: [],
}

const palworldServer: GameServer = {
  ...externalServer,
  id: 'palworld',
  gameType: 'palworld',
  displayName: 'Palworld',
  containerName: 'palworld-server',
}

const stoppedContainer: ContainerDetails = {
  id: 'container-id',
  name: 'minecraft-lunchtime',
  image: 'minecraft/server:fixed',
  imageId: 'image-id',
  state: 'exited',
  status: 'Exited (0)',
  createdAt: '2026-11-20T12:00:00Z',
  startedAt: '2026-11-20T12:01:00Z',
  finishedAt: '2026-11-20T12:02:00Z',
  restartCount: 0,
  platform: 'linux',
  driver: 'overlay2',
  ports: [],
  mounts: [],
  networks: [],
  labels: {},
}

let requestedUrls: string[]

beforeEach(() => {
  requestedUrls = []
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('external game server routing', () => {
  it('does not request Palworld data for an external server card', async () => {
    installFetchMock(({ path }) => {
      if (path === '/api/containers') {
        return jsonResponse([])
      }

      if (path === '/api/servers') {
        return jsonResponse({ servers: [externalServer] })
      }

      return jsonResponse({}, 404)
    })

    renderApp('/servers')

    expect(await screen.findByRole('heading', { name: 'Lunchtime' })).toBeInTheDocument()
    expect(requestedUrls.some(isPalworldRequest)).toBe(false)
  })

  it('routes LegacyExternal Docker servers to generic lifecycle and logs', async () => {
    installFetchMock(({ path, method }) => {
      if (path === '/api/servers/minecraft-lunchtime') {
        return jsonResponse(externalServer)
      }

      if (path === '/api/containers/minecraft-lunchtime') {
        return jsonResponse(stoppedContainer)
      }

      if (path.startsWith('/api/containers/minecraft-lunchtime/logs?')) {
        return jsonResponse({
          containerId: stoppedContainer.id,
          lines: ['Server stopped'],
          entries: [],
          retrievedAt: '2026-11-20T12:03:00Z',
        })
      }

      if (path === '/api/containers/container-id/start' && method === 'POST') {
        return jsonResponse({
          containerId: stoppedContainer.id,
          action: 'start',
          success: true,
          message: 'Container started.',
          previousState: 'exited',
          currentState: 'running',
          completedAt: '2026-11-20T12:04:00Z',
        })
      }

      if (path === '/api/containers/container-id') {
        return jsonResponse({
          ...stoppedContainer,
          state: 'running',
          status: 'Up 1 second',
        })
      }

      return jsonResponse({}, 404)
    })

    renderApp('/servers/minecraft-lunchtime')

    expect(await screen.findByRole('heading', { name: 'Lunchtime' })).toBeInTheDocument()
    expect(screen.getByText(/does not prove game readiness/i)).toBeInTheDocument()
    expect(await screen.findByRole('button', { name: 'Start' })).toBeInTheDocument()
    expect(await screen.findByText('Server stopped')).toBeInTheDocument()

    fireEvent.click(screen.getByRole('button', { name: 'Start' }))

    expect(await screen.findByText('Container started.')).toBeInTheDocument()
    await waitFor(() => {
      expect(requestedUrls).toContain('/api/containers/container-id/start')
    })
    expect(await screen.findByRole('button', { name: 'Stop' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Restart' })).toBeInTheDocument()
    expect(requestedUrls.some(isPalworldRequest)).toBe(false)
  })

  it('shows a focused error and skips logs when the configured container is absent', async () => {
    installFetchMock(({ path }) => {
      if (path === '/api/servers/minecraft-lunchtime') {
        return jsonResponse(externalServer)
      }

      if (path === '/api/containers/minecraft-lunchtime') {
        return jsonResponse({}, 404)
      }

      return jsonResponse({}, 404)
    })

    renderApp('/servers/minecraft-lunchtime')

    expect(await screen.findByText(/configured container was not found/i)).toBeInTheDocument()
    expect(requestedUrls.some((url) => url.includes('/logs'))).toBe(false)
  })

  it('preserves the Palworld route', async () => {
    installFetchMock(({ path }) => (
      path === '/api/servers/palworld'
        ? jsonResponse(palworldServer)
        : jsonResponse({}, 404)
    ))

    renderApp('/servers/palworld')

    expect(await screen.findByText('Palworld route preserved')).toBeInTheDocument()
  })

  it('rejects Managed and non-Docker servers from the legacy route', () => {
    expect(isSupportedExternalServer({
      ...externalServer,
      installationType: 'managed',
    })).toBe(false)
    expect(isSupportedExternalServer({
      ...externalServer,
      runtimeType: 'process',
    })).toBe(false)
  })
})

interface MockRequest {
  path: string
  method: string
}

function installFetchMock(resolver: (request: MockRequest) => Response) {
  vi.stubGlobal('fetch', vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const path = typeof input === 'string'
      ? input
      : input instanceof URL
        ? input.toString()
        : input.url
    const method = init?.method ?? (input instanceof Request ? input.method : 'GET')

    requestedUrls.push(path)
    return resolver({ path, method })
  }))
}

function renderApp(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <App />
    </MemoryRouter>,
  )
}

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function isPalworldRequest(url: string) {
  return url.includes('/api/palworld')
    || url.includes('/overview')
    || url.includes('/players')
    || url.includes('/settings')
}
