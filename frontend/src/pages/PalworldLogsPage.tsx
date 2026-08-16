import { ContainerLogs } from '../components/ContainerLogs'
import { SectionHeader } from '../components/SectionHeader'
import { usePalworldConfig } from '../hooks/usePalworldConfig'
import { PalworldUnavailableState } from './PalworldLayout'

export function PalworldLogsPage() {
  const palworldState = usePalworldConfig()

  if (palworldState.status === 'loading') {
    return <p className="state-message">Loading Palworld logs...</p>
  }

  if (palworldState.status !== 'success') {
    return <PalworldUnavailableState message={palworldState.message} />
  }

  return (
    <div className="page-section">
      <SectionHeader
        eyebrow="Logs"
        title="Palworld logs"
        description="Recent log snapshot for the configured Palworld container."
      />
      <ContainerLogs containerId={palworldState.config.containerName} />
    </div>
  )
}
