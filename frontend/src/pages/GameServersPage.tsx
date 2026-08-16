import { useContainers } from '../hooks/useContainers'
import { usePalworldConfig } from '../hooks/usePalworldConfig'
import { SectionHeader } from '../components/SectionHeader'
import { ServerCard } from '../components/ServerCard'
import { findPalworldContainer } from '../utils/containerStatus'

export function GameServersPage() {
  const containersState = useContainers()
  const palworldState = usePalworldConfig()
  const palworldConfig = palworldState.status === 'success' ? palworldState.config : null
  const palworldContainer = findPalworldContainer(containersState.containers, palworldConfig)

  return (
    <section className="page-section" aria-labelledby="game-servers-title">
      <SectionHeader
        eyebrow="Game servers"
        titleId="game-servers-title"
        title="Game Servers"
        description="Friendly server administration, prepared for more games later."
        aside="1 configured"
      />

      <ServerCard config={palworldConfig} container={palworldContainer} />

      {palworldState.status !== 'success' && palworldState.status !== 'loading' && (
        <p className="state-message state-message-error">{palworldState.message}</p>
      )}
    </section>
  )
}
