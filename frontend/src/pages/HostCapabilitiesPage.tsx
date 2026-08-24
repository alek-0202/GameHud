import type { ReactNode } from 'react'
import { useEffect, useState } from 'react'
import {
  Cpu,
  Database,
  HardDrive,
  MemoryStick,
  Network,
  RefreshCw,
  Server,
} from 'lucide-react'
import { fetchHostCapabilities } from '../api/hostCapabilities'
import { fetchPersistenceStatus } from '../api/persistence'
import { SectionHeader } from '../components/SectionHeader'
import type {
  HostCapabilities,
  HostCapabilityIssue,
  HostRuntime,
} from '../types/hostCapabilities'
import type { PersistenceStatus } from '../types/persistence'

type HostCapabilitiesState =
  | { status: 'loading'; capabilities?: undefined; message?: undefined }
  | { status: 'success'; capabilities: HostCapabilities; message?: undefined }
  | { status: 'error'; capabilities?: undefined; message: string }

export function HostCapabilitiesPage() {
  const [state, setState] = useState<HostCapabilitiesState>({ status: 'loading' })
  const [refreshing, setRefreshing] = useState(false)

  async function loadCapabilities(signal?: AbortSignal) {
    try {
      setRefreshing(true)
      const capabilities = await fetchHostCapabilities(signal)
      setState({ status: 'success', capabilities })
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') {
        return
      }

      setState({
        status: 'error',
        message: 'Unable to inspect host capabilities.',
      })
    } finally {
      setRefreshing(false)
    }
  }

  useEffect(() => {
    const abortController = new AbortController()

    setState({ status: 'loading' })
    void loadCapabilities(abortController.signal)

    return () => {
      abortController.abort()
    }
  }, [])

  return (
    <section className="page-section" aria-labelledby="host-capabilities-title">
      <SectionHeader
        actions={(
          <button
            className="secondary-button"
            disabled={refreshing}
            type="button"
            onClick={() => {
              void loadCapabilities()
            }}
          >
            <RefreshCw size={16} strokeWidth={2.2} />
            Refresh
          </button>
        )}
        description="Read-only inspection of the machine running GamesHud."
        eyebrow="System"
        title="Host"
        titleId="host-capabilities-title"
      />

      {state.status === 'loading' && (
        <p className="state-message">Inspecting host capabilities...</p>
      )}

      {state.status === 'error' && (
        <p className="state-message state-message-error">{state.message}</p>
      )}

      {state.status === 'success' && (
        <HostCapabilitiesContent capabilities={state.capabilities} />
      )}
    </section>
  )
}

interface HostCapabilitiesContentProps {
  capabilities: HostCapabilities
}

function HostCapabilitiesContent({ capabilities }: HostCapabilitiesContentProps) {
  const docker = capabilities.runtimes.find((runtime) => runtime.id === 'docker')

  return (
    <div className="host-capabilities-layout">
      <section className="host-readiness-panel" aria-labelledby="host-readiness-title">
        <div>
          <span className="section-eyebrow">Overall</span>
          <h3 id="host-readiness-title">{formatStatus(capabilities.overallReadiness.status)}</h3>
          <p>{capabilities.overallReadiness.message}</p>
        </div>
        <StatusPill status={capabilities.overallReadiness.status} />
      </section>

      <div className="host-capability-grid">
        <CapabilityCard
          icon={<Server size={18} strokeWidth={2.2} />}
          label="Host"
          title={`${formatOsFamily(capabilities.operatingSystem.family)} ${capabilities.operatingSystem.architecture}`}
          details={[
            capabilities.operatingSystem.description,
            `${capabilities.cpu.logicalProcessors} logical processors`,
          ]}
          status="available"
        />
        <CapabilityCard
          icon={<Cpu size={18} strokeWidth={2.2} />}
          label="CPU"
          title={`${capabilities.cpu.logicalProcessors} logical processors`}
          details={[`Architecture ${capabilities.cpu.architecture}`]}
          status="available"
        />
        <CapabilityCard
          icon={<MemoryStick size={18} strokeWidth={2.2} />}
          label="Memory"
          title={formatBytes(capabilities.memory.totalBytes)}
          details={[`${formatBytes(capabilities.memory.availableBytes)} available`]}
          status={capabilities.memory.status}
        />
        <CapabilityCard
          icon={<HardDrive size={18} strokeWidth={2.2} />}
          label="Storage"
          title={formatBytes(capabilities.storage.availableBytes)}
          details={[
            `${formatBytes(capabilities.storage.totalBytes)} total`,
            capabilities.storage.observedRoot || 'Primary storage target',
          ]}
          status={capabilities.storage.status}
        />
        <CapabilityCard
          icon={<Network size={18} strokeWidth={2.2} />}
          label="Network"
          title={capabilities.network.canInspectInterfaces ? 'Inspectable' : 'Unavailable'}
          details={[
            `${capabilities.network.interfaceCount} active interfaces`,
            capabilities.network.ipv4Available ? 'IPv4 available' : 'IPv4 not detected',
            capabilities.network.loopbackAvailable ? 'Loopback available' : 'Loopback not detected',
          ]}
          status={capabilities.network.status}
        />
        <PersistenceCard />
        {docker && <RuntimeCard runtime={docker} />}
      </div>

      <IssueList issues={capabilities.issues} />
    </div>
  )
}

type PersistenceState =
  | { status: 'loading'; persistence?: undefined; message?: undefined }
  | { status: 'success'; persistence: PersistenceStatus; message?: undefined }
  | { status: 'error'; persistence?: undefined; message: string }

function PersistenceCard() {
  const [state, setState] = useState<PersistenceState>({ status: 'loading' })

  useEffect(() => {
    const abortController = new AbortController()

    async function loadPersistence() {
      try {
        const persistence = await fetchPersistenceStatus(abortController.signal)
        setState({ status: 'success', persistence })
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }

        setState({ status: 'error', message: 'Persistence unavailable' })
      }
    }

    void loadPersistence()

    return () => {
      abortController.abort()
    }
  }, [])

  if (state.status === 'loading') {
    return (
      <CapabilityCard
        icon={<Database size={18} strokeWidth={2.2} />}
        label="Persistence"
        title="Checking"
        details={['SQLite', 'Migration status unavailable']}
        status="partial"
      />
    )
  }

  if (state.status === 'error') {
    return (
      <CapabilityCard
        icon={<Database size={18} strokeWidth={2.2} />}
        label="Persistence"
        title="Unavailable"
        details={[state.message]}
        status="unavailable"
      />
    )
  }

  return (
    <CapabilityCard
      icon={<Database size={18} strokeWidth={2.2} />}
      label="Persistence"
      title={state.persistence.available ? 'Ready' : 'Unavailable'}
      details={[
        formatIdentifier(state.persistence.provider),
        formatStatus(state.persistence.migrationStatus),
        state.persistence.appliedMigration ?? 'No applied migration',
      ]}
      status={state.persistence.available ? 'available' : 'unavailable'}
    />
  )
}

interface CapabilityCardProps {
  icon: ReactNode
  label: string
  title: string
  details: string[]
  status: string
}

function CapabilityCard({ icon, label, title, details, status }: CapabilityCardProps) {
  return (
    <article className="host-capability-card">
      <div className="host-card-heading">
        <span className="host-card-icon" aria-hidden="true">{icon}</span>
        <div>
          <span>{label}</span>
          <strong>{title}</strong>
        </div>
      </div>
      <StatusPill status={status} />
      <ul>
        {details.map((detail) => (
          <li key={detail}>{detail}</li>
        ))}
      </ul>
    </article>
  )
}

interface RuntimeCardProps {
  runtime: HostRuntime
}

function RuntimeCard({ runtime }: RuntimeCardProps) {
  return (
    <CapabilityCard
      icon={<Server size={18} strokeWidth={2.2} />}
      label="Runtime"
      title={runtime.displayName}
      details={[
        runtime.reachable ? 'Daemon reachable' : 'Daemon not reachable',
        runtime.endpointConfigured ? 'Endpoint configured' : 'Default endpoint',
        runtime.version ? `Version ${runtime.version}` : 'Version unavailable',
        runtime.operatingSystem ? `Runtime OS ${runtime.operatingSystem}` : 'Runtime OS unavailable',
      ]}
      status={runtime.status}
    />
  )
}

interface IssueListProps {
  issues: HostCapabilityIssue[]
}

function IssueList({ issues }: IssueListProps) {
  if (issues.length === 0) {
    return (
      <p className="state-message state-message-success">
        No host capability issues detected.
      </p>
    )
  }

  return (
    <section className="summary-panel" aria-labelledby="host-issues-title">
      <SectionHeader title="Capability Issues" titleId="host-issues-title" />
      <div className="host-issue-list">
        {issues.map((issue) => (
          <article className="host-issue-row" key={issue.code}>
            <StatusPill status={issue.severity} />
            <div>
              <strong>{formatIdentifier(issue.code)}</strong>
              <p>{issue.message}</p>
            </div>
          </article>
        ))}
      </div>
    </section>
  )
}

interface StatusPillProps {
  status: string
}

function StatusPill({ status }: StatusPillProps) {
  return (
    <span className={`status-badge ${getStatusClassName(status)}`}>
      <span aria-hidden="true" />
      {formatStatus(status)}
    </span>
  )
}

function getStatusClassName(status: string) {
  if (status === 'ready' || status === 'available') {
    return 'status-badge-success'
  }

  if (status === 'partial' || status === 'warning' || status === 'not_configured') {
    return 'status-badge-warning'
  }

  if (status === 'not_ready' || status === 'blocking' || status === 'unavailable') {
    return 'status-badge-danger'
  }

  return 'status-badge-muted'
}

function formatStatus(status: string) {
  if (status === 'not_ready') {
    return 'Not ready'
  }

  if (status === 'not_configured') {
    return 'Not configured'
  }

  return formatIdentifier(status)
}

function formatOsFamily(family: string) {
  const labels: Record<string, string> = {
    linux: 'Linux',
    macos: 'macOS',
    windows: 'Windows',
  }

  return labels[family] ?? formatIdentifier(family)
}

function formatIdentifier(value: string) {
  return value
    .split('_')
    .flatMap((part) => part.split('-'))
    .filter((part) => part.length > 0)
    .map((part) => `${part[0].toUpperCase()}${part.slice(1)}`)
    .join(' ')
}

function formatBytes(value: number | null) {
  if (value === null) {
    return 'Unavailable'
  }

  return new Intl.NumberFormat(undefined, {
    maximumFractionDigits: 1,
    minimumFractionDigits: 0,
    style: 'unit',
    unit: 'gigabyte',
    unitDisplay: 'short',
  }).format(value / 1_000_000_000)
}
