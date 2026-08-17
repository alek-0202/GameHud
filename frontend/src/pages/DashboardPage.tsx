import { useEffect, useMemo, useState } from 'react'
import {
  Activity,
  Cpu,
  Database,
  HardDrive,
  MemoryStick,
  Server,
} from 'lucide-react'
import { Link } from 'react-router-dom'
import { fetchGameServers } from '../api/gameServers'
import { PageHeader } from '../components/PageHeader'
import { SectionHeader } from '../components/SectionHeader'
import { StatusBadge } from '../components/StatusBadge'
import { useContainers } from '../hooks/useContainers'
import { usePalworldBackups } from '../hooks/usePalworldBackups'
import { usePalworldConfig } from '../hooks/usePalworldConfig'
import { usePalworldMetrics } from '../hooks/usePalworldMetrics'
import { usePalworldOverview } from '../hooks/usePalworldOverview'
import { usePalworldUpdate } from '../hooks/usePalworldUpdate'
import { useSystemMetrics } from '../hooks/useSystemMetrics'
import type { GameServer } from '../types/gameServers'
import type { PalworldOverview } from '../types/palworld'
import {
  countRunningContainers,
  countStoppedContainers,
  findContainerByName,
  findPalworldContainer,
} from '../utils/containerStatus'
import { formatBytePair, formatPercent, toPercent } from '../utils/metricsDisplay'
import { formatDuration, formatPlayerLimit, getInitials } from '../utils/palworldDisplay'
import { getPalworldServerName } from '../utils/palworldSettings'

export function DashboardPage() {
  const containersState = useContainers()
  const palworldState = usePalworldConfig()
  const palworldOverviewState = usePalworldOverview()
  const systemMetricsState = useSystemMetrics()
  const palworldMetricsState = usePalworldMetrics()
  const backupsState = usePalworldBackups(60000)
  const updateState = usePalworldUpdate()
  const [serversState, setServersState] = useState<
    | { status: 'loading'; servers: GameServer[] }
    | { status: 'success'; servers: GameServer[] }
    | { status: 'error'; servers: GameServer[] }
  >({ status: 'loading', servers: [] })

  useEffect(() => {
    const controller = new AbortController()

    async function loadServers() {
      try {
        const servers = await fetchGameServers(controller.signal)
        setServersState({ status: 'success', servers })
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }

        setServersState({ status: 'error', servers: [] })
      }
    }

    void loadServers()

    return () => controller.abort()
  }, [])

  const containers = containersState.containers
  const systemMetrics = systemMetricsState.status === 'success' ? systemMetricsState.metrics : null
  const palworldConfig = palworldState.status === 'success' ? palworldState.config : null
  const palworldOverview = palworldOverviewState.status === 'success' ? palworldOverviewState.overview : null
  const palworldContainer = palworldOverview === null
    ? findPalworldContainer(containers, palworldConfig)
    : findContainerByName(containers, palworldOverview.containerName)
  const runningContainers = systemMetrics?.docker.runningContainers ?? countRunningContainers(containers)
  const stoppedContainers = systemMetrics?.docker.stoppedContainers ?? countStoppedContainers(containers)
  const configuredServers = serversState.status === 'success'
    ? serversState.servers.length
    : 1
  const memoryPercent = toPercent(
    systemMetrics?.host.memoryUsedBytes ?? null,
    systemMetrics?.host.memoryTotalBytes ?? null,
  )
  const diskPercent = toPercent(
    systemMetrics?.host.diskUsedBytes ?? null,
    systemMetrics?.host.diskTotalBytes ?? null,
  )
  const globalStatus = getGlobalStatus({
    palworldHealth: palworldOverview?.health ?? null,
    palworldOverviewStatus: palworldOverviewState.status,
    palworldMetricsStatus: palworldMetricsState.status,
    systemMetricsStatus: systemMetricsState.status,
  })
  const activityItems = useMemo(() => {
    const items: ActivityItem[] = []

    if (backupsState.status === 'success' && backupsState.summary.latestBackup !== null) {
      items.push({
        label: 'Backup completed',
        detail: backupsState.summary.latestBackup.filename,
        timestamp: backupsState.summary.latestBackup.createdAt,
        tone: 'automation',
      })
    }

    if (updateState.status === 'success') {
      items.push({
        label: 'Update check completed',
        detail: updateState.update.updateStatus,
        timestamp: updateState.update.lastCheckedAt,
        tone: updateState.update.updateStatus.toLowerCase().includes('available')
          ? 'warning'
          : 'info',
      })
    }

    return items
  }, [backupsState, updateState])
  const shouldShowMetricsWarning = palworldOverviewState.status === 'success'
    && palworldOverview?.health === 'healthy'
    && palworldMetricsState.status !== 'success'
    && palworldMetricsState.status !== 'loading'

  return (
    <div className="dashboard-sections">
      <PageHeader
        actions={<StatusBadge state={globalStatus.label} />}
        description="Overview of your infrastructure and game servers."
        title="Dashboard"
      />

      {globalStatus.tone === 'danger' && (
        <p className="state-message state-message-error">{globalStatus.message}</p>
      )}

      <section className="dashboard-server-section" aria-labelledby="dashboard-servers-title">
        <SectionHeader
          eyebrow="Game servers"
          titleId="dashboard-servers-title"
          title="Game Servers"
          description="Friendly operations view for configured game servers."
          aside={serversState.status === 'loading' ? 'Loading' : `${configuredServers} configured`}
        />

        {palworldOverviewState.status === 'loading' ? (
          <div className="dashboard-server-card dashboard-server-card-loading">
            <p className="state-message">Loading Palworld server...</p>
          </div>
        ) : (
          <DashboardPalworldCard
            configName={getPalworldServerName(palworldConfig)}
            containerState={palworldContainer?.state ?? null}
            overview={palworldOverview}
          />
        )}

        {palworldState.status !== 'success' && palworldState.status !== 'loading' && (
          <p className="state-message state-message-warning">{palworldState.message}</p>
        )}
        {palworldOverviewState.status !== 'success'
          && palworldOverviewState.status !== 'loading' && (
          <p className="state-message state-message-error">{palworldOverviewState.message}</p>
        )}
        {shouldShowMetricsWarning && (
          <p className="dashboard-inline-warning">
            <Activity size={16} strokeWidth={2.2} />
            Some metrics are unavailable. Server status is still reported separately.
          </p>
        )}
        {!shouldShowMetricsWarning
          && palworldMetricsState.status !== 'success'
          && palworldMetricsState.status !== 'loading' && (
          <p className="state-message state-message-warning">{palworldMetricsState.message}</p>
        )}
      </section>

      <section className="dashboard-system-section" aria-labelledby="dashboard-system-title">
        <SectionHeader
          eyebrow="System health"
          titleId="dashboard-system-title"
          title="VPS Health"
          description="Host telemetry from the GamesHud metrics collector."
        />

        <div className="dashboard-health-grid">
          <DashboardMetricCard
            icon={Cpu}
            label="CPU"
            percent={systemMetrics?.host.cpuPercent ?? null}
            secondary={getUsageLabel(systemMetrics?.host.cpuPercent ?? null)}
            value={formatPercent(systemMetrics?.host.cpuPercent ?? null)}
          />
          <DashboardMetricCard
            icon={MemoryStick}
            label="RAM"
            percent={memoryPercent}
            secondary={memoryPercent === null ? 'Telemetry unavailable' : `${Math.round(memoryPercent)}% used`}
            value={formatBytePair(
              systemMetrics?.host.memoryUsedBytes ?? null,
              systemMetrics?.host.memoryTotalBytes ?? null,
            )}
          />
          <DashboardMetricCard
            icon={HardDrive}
            label="Disk"
            percent={diskPercent}
            secondary={diskPercent === null ? 'Telemetry unavailable' : `${Math.round(diskPercent)}% used`}
            value={formatBytePair(
              systemMetrics?.host.diskUsedBytes ?? null,
              systemMetrics?.host.diskTotalBytes ?? null,
            )}
          />
        </div>

        {systemMetricsState.status !== 'success' && systemMetricsState.status !== 'loading' && (
          <p className="state-message state-message-warning">{systemMetricsState.message}</p>
        )}
      </section>

      {activityItems.length > 0 && (
        <section className="dashboard-activity-section" aria-labelledby="dashboard-activity-title">
          <SectionHeader
            eyebrow="Recent activity"
            titleId="dashboard-activity-title"
            title="Recent Activity"
            description="Latest operational signals already available to GamesHud."
          />
          <div className="dashboard-activity-list">
            {activityItems.map((item) => (
              <article className="dashboard-activity-item" key={`${item.label}-${item.timestamp}`}>
                <span className={`dashboard-activity-icon dashboard-activity-icon-${item.tone}`}>
                  <Activity size={16} strokeWidth={2.2} />
                </span>
                <div>
                  <strong>{item.label}</strong>
                  <span>{item.detail}</span>
                </div>
                <time>{formatRelativeTime(item.timestamp)}</time>
              </article>
            ))}
          </div>
        </section>
      )}

      <section className="dashboard-infrastructure-section" aria-labelledby="infrastructure-summary-title">
        <SectionHeader
          eyebrow="Infrastructure"
          titleId="infrastructure-summary-title"
          title="Infrastructure"
          description="Technical Docker details remain available outside the game server view."
          aside={containersState.status === 'loading' ? 'Loading' : `${containers.length} total`}
        />

        <div className="dashboard-infrastructure-card">
          {containersState.status === 'error' ? (
            <p className="state-message state-message-warning">{containersState.message}</p>
          ) : (
            <div className="dashboard-docker-summary">
              <span className="dashboard-infrastructure-icon">
                <Database size={18} strokeWidth={2.2} />
              </span>
              <div>
                <span>Docker</span>
                <strong>{runningContainers} running</strong>
                <small>{stoppedContainers} stopped</small>
              </div>
            </div>
          )}
          <Link className="secondary-button" to="/containers">View Infrastructure</Link>
        </div>
      </section>
    </div>
  )
}

interface DashboardMetricCardProps {
  icon: typeof Cpu
  label: string
  value: string
  secondary: string
  percent: number | null
}

function DashboardMetricCard({
  icon: Icon,
  label,
  value,
  secondary,
  percent,
}: DashboardMetricCardProps) {
  const normalizedPercent = percent === null ? 0 : Math.min(100, Math.max(0, percent))
  const tone = getMetricTone(percent)

  return (
    <article className={`dashboard-health-card dashboard-health-card-${tone}`}>
      <div className="dashboard-health-card-heading">
        <span className="dashboard-health-icon">
          <Icon size={18} strokeWidth={2.2} />
        </span>
        <span>{label}</span>
      </div>
      <strong>{value}</strong>
      <div
        aria-label={`${label} usage`}
        aria-valuemax={100}
        aria-valuemin={0}
        aria-valuenow={Math.round(normalizedPercent)}
        className="metric-progress"
        role="progressbar"
      >
        <span style={{ width: `${normalizedPercent}%` }} />
      </div>
      <p>{secondary}</p>
    </article>
  )
}

interface DashboardPalworldCardProps {
  overview: PalworldOverview | null
  configName: string
  containerState: string | null
}

function DashboardPalworldCard({
  overview,
  configName,
  containerState,
}: DashboardPalworldCardProps) {
  const state = overview?.healthLabel ?? containerState ?? 'Unknown'
  const serverName = overview?.displayName ?? configName
  const visiblePlayers = overview?.players.slice(0, 4) ?? []
  const onlinePlayers = overview?.onlinePlayers ?? visiblePlayers.length
  const remainingPlayers = Math.max(0, onlinePlayers - visiblePlayers.length)

  return (
    <article className="dashboard-server-card">
      <div className="dashboard-server-card-topline">
        <span className="dashboard-game-label">
          <GamepadIcon />
          Palworld
        </span>
        <StatusBadge state={state} />
      </div>

      <div className="dashboard-server-card-main">
        <div>
          <h2>{serverName}</h2>
          {overview?.description && <p>{overview.description}</p>}
        </div>
        <Link className="primary-button" to="/servers/palworld">Manage Server</Link>
      </div>

      <dl className="dashboard-server-stats">
        <div>
          <dt>Players</dt>
          <dd>{formatPlayerLimit(overview?.onlinePlayers ?? 0, overview?.maxPlayers ?? null)}</dd>
        </div>
        <div>
          <dt>Uptime</dt>
          <dd>{formatDuration(overview?.uptimeSeconds ?? null)}</dd>
        </div>
        {overview?.version && (
          <div>
            <dt>Version</dt>
            <dd>{overview.version}</dd>
          </div>
        )}
        {overview?.connectionAddress && (
          <div>
            <dt>Connection</dt>
            <dd>{overview.connectionAddress}</dd>
          </div>
        )}
      </dl>

      <div className="dashboard-player-strip">
        <span className="dashboard-player-strip-label">Online players</span>
        {visiblePlayers.length === 0 ? (
          <p>No players online</p>
        ) : (
          <div className="inline-player-list">
            {visiblePlayers.map((player) => (
              <span className="player-chip" key={player.publicId ?? player.name}>
                <span>{getInitials(player.name)}</span>
                {player.name}
              </span>
            ))}
            {remainingPlayers > 0 && <span className="dashboard-more-players">+{remainingPlayers} more</span>}
          </div>
        )}
      </div>
    </article>
  )
}

function GamepadIcon() {
  return <Server size={15} strokeWidth={2.2} />
}

interface GlobalStatusInput {
  palworldHealth: string | null
  palworldOverviewStatus: string
  palworldMetricsStatus: string
  systemMetricsStatus: string
}

function getGlobalStatus({
  palworldHealth,
  palworldOverviewStatus,
  palworldMetricsStatus,
  systemMetricsStatus,
}: GlobalStatusInput) {
  if (palworldOverviewStatus === 'loading' || systemMetricsStatus === 'loading') {
    return {
      label: 'Checking systems',
      message: 'GamesHud is loading the latest dashboard signals.',
      tone: 'info',
    }
  }

  if (palworldHealth !== null && ['not-found', 'container-stopped'].includes(palworldHealth)) {
    return {
      label: 'Needs attention',
      message: 'The configured Palworld server is not reporting as online.',
      tone: 'danger',
    }
  }

  if (systemMetricsStatus !== 'success' || palworldMetricsStatus === 'error') {
    return {
      label: 'Partial telemetry',
      message: 'Some dashboard metrics are unavailable.',
      tone: 'warning',
    }
  }

  return {
    label: 'All systems operational',
    message: 'Available dashboard signals are healthy.',
    tone: 'success',
  }
}

function getMetricTone(percent: number | null) {
  if (percent === null) {
    return 'muted'
  }

  if (percent >= 90) {
    return 'danger'
  }

  if (percent >= 75) {
    return 'warning'
  }

  return 'success'
}

function getUsageLabel(percent: number | null) {
  if (percent === null) {
    return 'Telemetry unavailable'
  }

  if (percent >= 90) {
    return 'Critical usage'
  }

  if (percent >= 75) {
    return 'Elevated usage'
  }

  return 'Normal usage'
}

interface ActivityItem {
  label: string
  detail: string
  timestamp: string
  tone: 'automation' | 'info' | 'warning'
}

function formatRelativeTime(value: string) {
  const timestamp = new Date(value).getTime()

  if (Number.isNaN(timestamp)) {
    return 'Recently'
  }

  const diffSeconds = Math.max(0, Math.floor((Date.now() - timestamp) / 1000))

  if (diffSeconds < 60) {
    return 'Just now'
  }

  const diffMinutes = Math.floor(diffSeconds / 60)

  if (diffMinutes < 60) {
    return `${diffMinutes}m ago`
  }

  const diffHours = Math.floor(diffMinutes / 60)

  if (diffHours < 24) {
    return `${diffHours}h ago`
  }

  const diffDays = Math.floor(diffHours / 24)

  return `${diffDays}d ago`
}
