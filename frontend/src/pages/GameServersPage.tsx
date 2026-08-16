import { useContainers } from '../hooks/useContainers'
import { usePalworldConfig } from '../hooks/usePalworldConfig'
import { usePalworldOverview } from '../hooks/usePalworldOverview'
import { SectionHeader } from '../components/SectionHeader'
import { ServerCard } from '../components/ServerCard'
import { findContainerByName, findPalworldContainer } from '../utils/containerStatus'

export function GameServersPage() {
  const containersState = useContainers()
  const palworldState = usePalworldConfig()
  const palworldOverviewState = usePalworldOverview()
  const palworldConfig = palworldState.status === 'success' ? palworldState.config : null
  const palworldOverview = palworldOverviewState.status === 'success' ? palworldOverviewState.overview : null
  const palworldContainer = palworldOverview === null
    ? findPalworldContainer(containersState.containers, palworldConfig)
    : findContainerByName(containersState.containers, palworldOverview.containerName)

  return (
    <section className="page-section" aria-labelledby="game-servers-title">
      <SectionHeader
        eyebrow="Game servers"
        titleId="game-servers-title"
        title="Game Servers"
        description="Friendly server administration, prepared for more games later."
        aside="1 configured"
      />

      <ServerCard
        config={palworldConfig}
        container={palworldContainer}
        overview={palworldOverview}
      />

      {palworldState.status !== 'success' && palworldState.status !== 'loading' && (
        <p className="state-message state-message-error">{palworldState.message}</p>
      )}
      {palworldOverviewState.status !== 'success'
        && palworldOverviewState.status !== 'loading' && (
        <p className="state-message state-message-error">{palworldOverviewState.message}</p>
      )}
    </section>
  )
}
