import { useEffect, useState } from 'react'
import { fetchGameServers } from '../api/gameServers'
import { useContainers } from '../hooks/useContainers'
import { usePalworldOverview } from '../hooks/usePalworldOverview'
import { SectionHeader } from '../components/SectionHeader'
import { ServerCard } from '../components/ServerCard'
import type { Container } from '../types/container'
import type { GameServer } from '../types/gameServers'
import { findContainerByName } from '../utils/containerStatus'

export function GameServersPage() {
  const containersState = useContainers()
  const [serversState, setServersState] = useState<
    | { status: 'loading'; servers: GameServer[]; message?: undefined }
    | { status: 'success'; servers: GameServer[]; message?: undefined }
    | { status: 'error'; servers: GameServer[]; message: string }
  >({ status: 'loading', servers: [] })

  useEffect(() => {
    const abortController = new AbortController()

    async function loadServers() {
      try {
        const servers = await fetchGameServers(abortController.signal)
        setServersState({ status: 'success', servers })
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }

        setServersState({
          status: 'error',
          servers: [],
          message: 'Unable to load configured game servers.',
        })
      }
    }

    void loadServers()

    return () => abortController.abort()
  }, [])

  return (
    <section className="page-section" aria-labelledby="game-servers-title">
      <SectionHeader
        eyebrow="Game servers"
        titleId="game-servers-title"
        title="Game Servers"
        description="Friendly server administration, prepared for more games later."
        aside={`${serversState.servers.length} configured`}
      />

      {serversState.status === 'loading' && (
        <p className="state-message">Loading game servers...</p>
      )}

      {serversState.status === 'error' && (
        <p className="state-message state-message-error">{serversState.message}</p>
      )}

      {serversState.status === 'success' && serversState.servers.length === 0 && (
        <p className="empty-message">No game servers configured.</p>
      )}

      {serversState.servers.map((server) => (
        <GameServerCard
          containers={containersState.containers}
          key={server.id}
          server={server}
        />
      ))}
    </section>
  )
}

interface GameServerCardProps {
  server: GameServer
  containers: Container[]
}

function GameServerCard({ server, containers }: GameServerCardProps) {
  const overviewState = usePalworldOverview(20000, server.id)
  const overview = overviewState.status === 'success' ? overviewState.overview : null
  const container = findContainerByName(containers, overview?.containerName ?? server.containerName)

  return (
    <>
      <ServerCard
        config={null}
        container={container}
        displayName={server.displayName}
        overview={overview}
        serverId={server.id}
      />
      {overviewState.status !== 'success' && overviewState.status !== 'loading' && (
        <p className="state-message state-message-error">{overviewState.message}</p>
      )}
    </>
  )
}
