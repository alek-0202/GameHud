import { Link } from 'react-router-dom'
import { useContainers } from '../hooks/useContainers'
import { usePalworldConfig } from '../hooks/usePalworldConfig'
import { usePalworldOverview } from '../hooks/usePalworldOverview'
import { SectionHeader } from '../components/SectionHeader'
import { ServerCard } from '../components/ServerCard'
import {
  countRunningContainers,
  countStoppedContainers,
  findContainerByName,
  findPalworldContainer,
} from '../utils/containerStatus'

export function DashboardPage() {
  const containersState = useContainers()
  const palworldState = usePalworldConfig()
  const palworldOverviewState = usePalworldOverview()
  const containers = containersState.containers
  const palworldConfig = palworldState.status === 'success' ? palworldState.config : null
  const palworldOverview = palworldOverviewState.status === 'success' ? palworldOverviewState.overview : null
  const palworldContainer = palworldOverview === null
    ? findPalworldContainer(containers, palworldConfig)
    : findContainerByName(containers, palworldOverview.containerName)
  const runningContainers = countRunningContainers(containers)
  const stoppedContainers = countStoppedContainers(containers)

  return (
    <div className="dashboard-sections">
      <section className="overview-grid" aria-label="Dashboard overview">
        <div className="metric-card">
          <span>Total containers</span>
          <strong>{containers.length}</strong>
        </div>
        <div className="metric-card metric-card-success">
          <span>Running</span>
          <strong>{runningContainers}</strong>
        </div>
        <div className="metric-card">
          <span>Stopped</span>
          <strong>{stoppedContainers}</strong>
        </div>
        <div className="metric-card metric-card-disabled">
          <span>Metrics</span>
          <strong>Pending</strong>
        </div>
      </section>

      <section className="game-servers-section" aria-labelledby="dashboard-servers-title">
        <SectionHeader
          eyebrow="Game servers"
          titleId="dashboard-servers-title"
          title="Configured servers"
          description="Friendly operations view for configured game servers."
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

      <section className="infrastructure-summary" aria-labelledby="infrastructure-summary-title">
        <SectionHeader
          eyebrow="Infrastructure"
          titleId="infrastructure-summary-title"
          title="Docker summary"
          description="Technical container details remain available in Infrastructure."
          aside={containersState.status === 'loading' ? 'Loading' : `${containers.length} total`}
        />

        <div className="summary-panel">
          {containersState.status === 'error' ? (
            <p className="state-message state-message-error">{containersState.message}</p>
          ) : (
            <p>
              {runningContainers} running and {stoppedContainers} stopped containers are visible
              through the Docker Core.
            </p>
          )}
          <Link className="secondary-button" to="/containers">Open Containers</Link>
        </div>
      </section>
    </div>
  )
}
