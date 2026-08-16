import { useEffect, useState } from 'react'
import { ContainerLifecycleActions } from '../components/ContainerLifecycleActions'
import { SectionHeader } from '../components/SectionHeader'
import { StatusBadge } from '../components/StatusBadge'
import { useContainerDetails } from '../hooks/useContainerDetails'
import { usePalworldOverview } from '../hooks/usePalworldOverview'
import type { ContainerDetails } from '../types/container'
import { formatDuration, formatPlayerLimit, getInitials } from '../utils/palworldDisplay'
import { toFriendlyState } from '../utils/containerStatus'
import { PalworldUnavailableState } from './PalworldLayout'

export function PalworldOverviewPage() {
  const overviewState = usePalworldOverview()
  const containerName = overviewState.status === 'success' ? overviewState.overview.containerName : null
  const detailsState = useContainerDetails(containerName)
  const [updatedContainer, setUpdatedContainer] = useState<ContainerDetails | null>(null)
  const [copyFeedback, setCopyFeedback] = useState<string | null>(null)
  const visibleContainer = updatedContainer ?? detailsState.container

  useEffect(() => {
    if (detailsState.status === 'success') {
      setUpdatedContainer(detailsState.container)
    }
  }, [detailsState])

  if (overviewState.status === 'loading') {
    return <p className="state-message">Loading Palworld overview...</p>
  }

  if (overviewState.status !== 'success') {
    return <PalworldUnavailableState message={overviewState.message} />
  }

  const overview = overviewState.overview

  return (
    <div className="page-section">
      <SectionHeader
        eyebrow="Overview"
        title={overview.displayName}
        description={overview.description ?? 'Friendly summary using Palworld REST API and Docker status.'}
      />

      <div className="server-overview-grid">
        <OverviewCard label="Players" value={formatPlayerLimit(overview.onlinePlayers, overview.maxPlayers)} />
        <OverviewCard label="Uptime" value={formatDuration(overview.uptimeSeconds)} />
        <OverviewCard label="Version" value={overview.version ?? 'Unknown'} />
        <div className="server-overview-card">
          <span className="section-eyebrow">Server Health</span>
          <StatusBadge state={overview.healthLabel} />
          {overview.restApiMessage && <p>{overview.restApiMessage}</p>}
        </div>
      </div>

      <section className="details-block" aria-labelledby="palworld-online-players">
        <SectionHeader titleId="palworld-online-players" title="Players Online" />
        {overview.players.length === 0 ? (
          <p className="empty-message">No players online</p>
        ) : (
          <div className="players-list compact-players-list">
            {overview.players.map((player) => (
              <article className="player-row" key={player.publicId ?? player.name}>
                <div className="player-avatar" aria-hidden="true">
                  {getInitials(player.name)}
                </div>
                <div>
                  <strong>{player.name}</strong>
                  <span>
                    {player.ping === null ? 'Ping unavailable' : `${Math.round(player.ping)} ms`}
                  </span>
                </div>
              </article>
            ))}
          </div>
        )}
      </section>

      <section className="details-block" aria-labelledby="palworld-connection">
        <SectionHeader titleId="palworld-connection" title="Connection" />
        <div className="summary-panel connection-panel">
          <div>
            <span className="section-eyebrow">Server address</span>
            <strong>{overview.connectionAddress ?? 'Not configured'}</strong>
          </div>
          <button
            className="secondary-button"
            disabled={overview.connectionAddress === null}
            type="button"
            onClick={() => {
              void copyConnectionAddress(overview.connectionAddress, setCopyFeedback)
            }}
          >
            Copy
          </button>
        </div>
        {copyFeedback && <p className="state-message state-message-success">{copyFeedback}</p>}
      </section>

      <section className="details-block" aria-labelledby="palworld-controls">
        <SectionHeader
          titleId="palworld-controls"
          title="Server Controls"
          description={`Docker state: ${toFriendlyState(visibleContainer?.state ?? overview.containerState)}.`}
        />

        {detailsState.status === 'loading' && (
          <p className="state-message">Loading server operations...</p>
        )}

        {detailsState.status !== 'success'
          && detailsState.status !== 'loading'
          && detailsState.status !== 'idle' && (
          <p className="state-message state-message-error">{detailsState.message}</p>
        )}

        {detailsState.status === 'success' && (
          <ContainerLifecycleActions
            container={visibleContainer ?? detailsState.container}
            onContainerUpdated={setUpdatedContainer}
          />
        )}
      </section>
    </div>
  )
}

interface OverviewCardProps {
  label: string
  value: string
}

function OverviewCard({ label, value }: OverviewCardProps) {
  return (
    <div className="server-overview-card">
      <span className="section-eyebrow">{label}</span>
      <strong>{value}</strong>
    </div>
  )
}

async function copyConnectionAddress(
  connectionAddress: string | null,
  setCopyFeedback: (message: string | null) => void,
) {
  if (connectionAddress === null) {
    return
  }

  await navigator.clipboard.writeText(connectionAddress)
  setCopyFeedback('Connection address copied.')
}
