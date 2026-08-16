import { Link } from 'react-router-dom'
import { useContainers } from '../hooks/useContainers'
import { usePalworldConfig } from '../hooks/usePalworldConfig'
import { usePalworldMetrics } from '../hooks/usePalworldMetrics'
import { usePalworldOverview } from '../hooks/usePalworldOverview'
import { useSystemMetrics } from '../hooks/useSystemMetrics'
import { MetricProgressCard } from '../components/MetricProgressCard'
import { SectionHeader } from '../components/SectionHeader'
import { ServerCard } from '../components/ServerCard'
import {
  countRunningContainers,
  countStoppedContainers,
  findContainerByName,
  findPalworldContainer,
} from '../utils/containerStatus'
import { formatBytePair, formatPercent, toPercent } from '../utils/metricsDisplay'

export function DashboardPage() {
  const containersState = useContainers()
  const palworldState = usePalworldConfig()
  const palworldOverviewState = usePalworldOverview()
  const systemMetricsState = useSystemMetrics()
  const palworldMetricsState = usePalworldMetrics()
  const containers = containersState.containers
  const systemMetrics = systemMetricsState.status === 'success' ? systemMetricsState.metrics : null
  const palworldMetrics = palworldMetricsState.status === 'success' ? palworldMetricsState.metrics : null
  const palworldConfig = palworldState.status === 'success' ? palworldState.config : null
  const palworldOverview = palworldOverviewState.status === 'success' ? palworldOverviewState.overview : null
  const palworldContainer = palworldOverview === null
    ? findPalworldContainer(containers, palworldConfig)
    : findContainerByName(containers, palworldOverview.containerName)
  const runningContainers = systemMetrics?.docker.runningContainers ?? countRunningContainers(containers)
  const stoppedContainers = systemMetrics?.docker.stoppedContainers ?? countStoppedContainers(containers)

  return (
    <div className="dashboard-sections">
      <section className="overview-grid" aria-label="Dashboard overview">
        <MetricProgressCard
          label="CPU"
          percent={systemMetrics?.host.cpuPercent ?? null}
          value={formatPercent(systemMetrics?.host.cpuPercent ?? null)}
        />
        <MetricProgressCard
          label="RAM"
          percent={toPercent(
            systemMetrics?.host.memoryUsedBytes ?? null,
            systemMetrics?.host.memoryTotalBytes ?? null,
          )}
          value={formatBytePair(
            systemMetrics?.host.memoryUsedBytes ?? null,
            systemMetrics?.host.memoryTotalBytes ?? null,
          )}
        />
        <MetricProgressCard
          label="Disk"
          percent={toPercent(
            systemMetrics?.host.diskUsedBytes ?? null,
            systemMetrics?.host.diskTotalBytes ?? null,
          )}
          value={formatBytePair(
            systemMetrics?.host.diskUsedBytes ?? null,
            systemMetrics?.host.diskTotalBytes ?? null,
          )}
        />
        <MetricProgressCard
          label="Players"
          percent={toPercent(
            palworldMetrics?.playersOnline ?? null,
            palworldMetrics?.maxPlayers ?? null,
          )}
          value={`${palworldMetrics?.playersOnline ?? '-'} / ${palworldMetrics?.maxPlayers ?? '-'}`}
        />
      </section>

      {systemMetricsState.status !== 'success' && systemMetricsState.status !== 'loading' && (
        <p className="state-message state-message-error">{systemMetricsState.message}</p>
      )}

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
        {palworldMetricsState.status !== 'success'
          && palworldMetricsState.status !== 'loading' && (
          <p className="state-message state-message-error">{palworldMetricsState.message}</p>
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
