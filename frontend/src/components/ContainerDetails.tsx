import { useEffect, useState } from 'react'
import { ApiRequestError, fetchContainerDetails } from '../api/containers'
import type { ContainerDetails as ContainerDetailsType } from '../types/container'
import { ContainerLifecycleActions } from './ContainerLifecycleActions'
import { ContainerLogs } from './ContainerLogs'

type DetailsState =
  | { status: 'loading' }
  | { status: 'success'; container: ContainerDetailsType }
  | { status: 'not-found' }
  | { status: 'unavailable' }
  | { status: 'error' }

interface ContainerDetailsProps {
  containerId: string
  onBack: () => void
}

export function ContainerDetails({ containerId, onBack }: ContainerDetailsProps) {
  const [state, setState] = useState<DetailsState>({ status: 'loading' })

  useEffect(() => {
    const abortController = new AbortController()

    async function loadContainerDetails() {
      try {
        setState({ status: 'loading' })

        const container = await fetchContainerDetails(containerId, abortController.signal)

        setState({ status: 'success', container })
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }

        if (error instanceof ApiRequestError && error.status === 404) {
          setState({ status: 'not-found' })
          return
        }

        if (error instanceof ApiRequestError && error.status === 503) {
          setState({ status: 'unavailable' })
          return
        }

        setState({ status: 'error' })
      }
    }

    void loadContainerDetails()

    return () => {
      abortController.abort()
    }
  }, [containerId])

  return (
    <section className="container-details-section" aria-label="Container details">
      <button className="secondary-button" type="button" onClick={onBack}>
        Back
      </button>

      {state.status === 'loading' && <p className="state-message">Loading container details...</p>}

      {state.status === 'not-found' && (
        <p className="state-message state-message-error">Container was not found.</p>
      )}

      {state.status === 'unavailable' && (
        <p className="state-message state-message-error">
          Docker is unavailable. Check whether the API can reach Docker Engine.
        </p>
      )}

      {state.status === 'error' && (
        <p className="state-message state-message-error">Unable to load container details.</p>
      )}

      {state.status === 'success' && (
        <ContainerDetailsContent
          container={state.container}
          onContainerUpdated={(container) => {
            setState({ status: 'success', container })
          }}
        />
      )}
    </section>
  )
}

interface ContainerDetailsContentProps {
  container: ContainerDetailsType
  onContainerUpdated: (container: ContainerDetailsType) => void
}

function ContainerDetailsContent({
  container,
  onContainerUpdated,
}: ContainerDetailsContentProps) {
  return (
    <div className="container-details">
      <header className="details-header">
        <div>
          <h2 id="container-details-title">{container.name || shortId(container.id)}</h2>
          <p>{container.image || 'Unknown image'}</p>
        </div>
        <div className="status-stack">
          <span>{container.state || 'Unknown state'}</span>
          <strong>{container.status || 'Unknown status'}</strong>
        </div>
      </header>

      <section className="details-block" aria-labelledby="general-title">
        <h3 id="general-title">General Information</h3>
        <dl className="details-grid">
          <dt>Container ID</dt>
          <dd>{shortId(container.id)}</dd>
          <dt>Created</dt>
          <dd>{formatDate(container.createdAt)}</dd>
          <dt>Started</dt>
          <dd>{formatDate(container.startedAt)}</dd>
          <dt>Finished</dt>
          <dd>{formatOptionalDate(container.finishedAt)}</dd>
          <dt>Restart count</dt>
          <dd>{container.restartCount}</dd>
          <dt>Platform</dt>
          <dd>{container.platform || 'Unknown platform'}</dd>
          <dt>Driver</dt>
          <dd>{container.driver || 'Unknown driver'}</dd>
          <dt>Image ID</dt>
          <dd>{shortId(container.imageId) || 'Unknown image ID'}</dd>
        </dl>
      </section>

      <ContainerLifecycleActions
        container={container}
        onContainerUpdated={onContainerUpdated}
      />

      <section className="details-block" aria-labelledby="ports-title">
        <h3 id="ports-title">Ports</h3>
        {container.ports.length === 0 ? (
          <p className="empty-message">No ports exposed.</p>
        ) : (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Private port</th>
                  <th>Public port</th>
                  <th>Type</th>
                  <th>Host IP</th>
                </tr>
              </thead>
              <tbody>
                {container.ports.map((port) => (
                  <tr key={`${port.privatePort}-${port.publicPort ?? 'none'}-${port.type}-${port.hostIp}`}>
                    <td>{port.privatePort || 'Unknown'}</td>
                    <td>{port.publicPort ?? 'Not published'}</td>
                    <td>{port.type || 'Unknown'}</td>
                    <td>{port.hostIp || 'Not bound'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="details-block" aria-labelledby="mounts-title">
        <h3 id="mounts-title">Mounts</h3>
        {container.mounts.length === 0 ? (
          <p className="empty-message">No mounts configured.</p>
        ) : (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Type</th>
                  <th>Source</th>
                  <th>Destination</th>
                  <th>Read-only</th>
                </tr>
              </thead>
              <tbody>
                {container.mounts.map((mount) => (
                  <tr key={`${mount.type}-${mount.source}-${mount.destination}`}>
                    <td>{mount.type || 'Unknown'}</td>
                    <td>{mount.source || 'Unknown source'}</td>
                    <td>{mount.destination || 'Unknown destination'}</td>
                    <td>{mount.readOnly ? 'Yes' : 'No'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <section className="details-block" aria-labelledby="networks-title">
        <h3 id="networks-title">Networks</h3>
        {container.networks.length === 0 ? (
          <p className="empty-message">No networks connected.</p>
        ) : (
          <div className="table-scroll">
            <table>
              <thead>
                <tr>
                  <th>Name</th>
                  <th>IP address</th>
                  <th>Gateway</th>
                  <th>MAC address</th>
                </tr>
              </thead>
              <tbody>
                {container.networks.map((network) => (
                  <tr key={network.name}>
                    <td>{network.name || 'Unknown network'}</td>
                    <td>{network.ipAddress || 'No IP address'}</td>
                    <td>{network.gateway || 'No gateway'}</td>
                    <td>{network.macAddress || 'No MAC address'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>

      <ContainerLogs containerId={container.id} />
    </div>
  )
}

function shortId(value: string) {
  return value ? value.slice(0, 12) : ''
}

function formatOptionalDate(value: string) {
  if (!value || value.startsWith('0001-') || value.startsWith('0000-')) {
    return 'Not applicable'
  }

  return formatDate(value)
}

function formatDate(value: string) {
  if (!value || value.startsWith('0001-') || value.startsWith('0000-')) {
    return 'Unknown'
  }

  const date = new Date(value)

  if (Number.isNaN(date.getTime())) {
    return value
  }

  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date)
}
