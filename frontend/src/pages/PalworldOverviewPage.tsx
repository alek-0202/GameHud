import { useEffect, useState } from 'react'
import { ContainerLifecycleActions } from '../components/ContainerLifecycleActions'
import { SectionHeader } from '../components/SectionHeader'
import { StatusBadge } from '../components/StatusBadge'
import { useContainerDetails } from '../hooks/useContainerDetails'
import { usePalworldConfig } from '../hooks/usePalworldConfig'
import type { ContainerDetails } from '../types/container'
import { toFriendlyState } from '../utils/containerStatus'
import { PalworldUnavailableState } from './PalworldLayout'

export function PalworldOverviewPage() {
  const palworldState = usePalworldConfig()
  const config = palworldState.status === 'success' ? palworldState.config : null
  const detailsState = useContainerDetails(config?.containerName ?? null)
  const [updatedContainer, setUpdatedContainer] = useState<ContainerDetails | null>(null)
  const visibleContainer = updatedContainer ?? detailsState.container

  useEffect(() => {
    if (detailsState.status === 'success') {
      setUpdatedContainer(detailsState.container)
    }
  }, [detailsState])

  if (palworldState.status === 'loading') {
    return <p className="state-message">Loading Palworld overview...</p>
  }

  if (palworldState.status !== 'success') {
    return <PalworldUnavailableState message={palworldState.message} />
  }

  const loadedConfig = palworldState.config

  return (
    <div className="page-section">
      <SectionHeader
        eyebrow="Overview"
        title="Server overview"
        description="Friendly summary using currently available GamesHud data."
      />

      <div className="server-overview-grid">
        <div className="server-overview-card">
          <span className="section-eyebrow">Server Name</span>
          <strong>{loadedConfig.serverName || 'Palworld'}</strong>
        </div>
        <div className="server-overview-card">
          <span className="section-eyebrow">State</span>
          <StatusBadge state={visibleContainer?.state || 'Unknown'} />
        </div>
        <div className="server-overview-card">
          <span className="section-eyebrow">Operational Status</span>
          <strong>{getOperationalStatus(visibleContainer)}</strong>
        </div>
      </div>

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
    </div>
  )
}

function getOperationalStatus(container: ContainerDetails | null) {
  if (container === null) {
    return 'Unavailable'
  }

  return container.status || toFriendlyState(container.state)
}
