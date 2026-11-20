import { useEffect, useState } from 'react'
import { Link } from 'react-router-dom'
import { ContainerLifecycleActions } from '../components/ContainerLifecycleActions'
import { ContainerLogs } from '../components/ContainerLogs'
import { SectionHeader } from '../components/SectionHeader'
import { StatusBadge } from '../components/StatusBadge'
import { useContainerDetails } from '../hooks/useContainerDetails'
import type { ContainerDetails } from '../types/container'
import type { GameServer } from '../types/gameServers'

interface ExternalGameServerPageProps {
  server: GameServer
}

export function ExternalGameServerPage({ server }: ExternalGameServerPageProps) {
  const detailsState = useContainerDetails(server.containerName)
  const [updatedContainer, setUpdatedContainer] = useState<ContainerDetails | null>(null)

  useEffect(() => {
    setUpdatedContainer(null)
  }, [server.id])

  useEffect(() => {
    if (detailsState.status === 'success') {
      setUpdatedContainer(detailsState.container)
    }
  }, [detailsState])

  const container = updatedContainer
    ?? (detailsState.status === 'success' ? detailsState.container : null)

  return (
    <section className="container-details-section" aria-labelledby="external-server-title">
      <Link className="secondary-button" to="/servers">Back to game servers</Link>

      <header className="details-header">
        <div>
          <span className="section-eyebrow">LegacyExternal game server</span>
          <h2 id="external-server-title">{server.displayName}</h2>
          <p>{formatGameType(server.gameType)} managed through its configured Docker container.</p>
        </div>
        <div className="status-stack">
          <StatusBadge state={container?.state || 'Unknown'} />
          <strong>{container?.status || 'Docker state unavailable'}</strong>
        </div>
      </header>

      <section className="details-block" aria-labelledby="external-runtime-title">
        <SectionHeader
          titleId="external-runtime-title"
          title="External Runtime"
          description="GamesHud observes and controls the existing container but does not own, provision or adopt it."
        />
        <dl className="details-grid">
          <dt>Game</dt>
          <dd>{formatGameType(server.gameType)}</dd>
          <dt>Runtime</dt>
          <dd>{formatGameType(server.runtimeType)}</dd>
          <dt>Installation</dt>
          <dd>LegacyExternal</dd>
          <dt>Configured container</dt>
          <dd>{server.containerName}</dd>
          <dt>Docker state</dt>
          <dd>{container?.state || 'Unknown'}</dd>
        </dl>
        <p className="state-message state-message-warning">
          Docker running state confirms only that the container process is running. It does not prove game readiness or player connectivity.
        </p>
      </section>

      {detailsState.status === 'loading' && (
        <p className="state-message">Loading configured container...</p>
      )}

      {detailsState.status === 'not-found' && (
        <p className="state-message state-message-error">
          The configured container was not found. Check the external server registration and container name.
        </p>
      )}

      {detailsState.status === 'unavailable' && (
        <p className="state-message state-message-error">{detailsState.message}</p>
      )}

      {detailsState.status === 'error' && (
        <p className="state-message state-message-error">{detailsState.message}</p>
      )}

      {container && (
        <>
          <ContainerLifecycleActions
            container={container}
            description="These manual actions target only the container reference configured for this LegacyExternal server."
            onContainerUpdated={setUpdatedContainer}
            title="Container Lifecycle"
          />
          <ContainerLogs containerId={server.containerName} />
        </>
      )}
    </section>
  )
}

function formatGameType(value: string) {
  const normalized = value.trim()

  if (normalized.length === 0) {
    return 'Unknown'
  }

  return `${normalized[0].toUpperCase()}${normalized.slice(1)}`
}
