import type { Container } from '../types/container'
import { StatusBadge } from './StatusBadge'

interface ContainerListProps {
  containers: Container[]
  onSelectContainer: (containerId: string) => void
}

export function ContainerList({ containers, onSelectContainer }: ContainerListProps) {
  return (
    <div className="container-list">
      {containers.map((container) => (
        <button
          className="container-card"
          key={container.id}
          type="button"
          onClick={() => {
            onSelectContainer(container.id)
          }}
        >
          <div className="container-card-main">
            <div>
              <span className="section-eyebrow">Container</span>
              <h3>{container.name || container.id}</h3>
            </div>
            <StatusBadge state={container.state || 'Unknown'} />
          </div>

          <dl className="container-card-meta">
            <div>
              <dt>Image</dt>
              <dd>{container.image || 'Unknown image'}</dd>
            </div>
            <div>
              <dt>Status</dt>
              <dd>{container.status || 'Unknown status'}</dd>
            </div>
          </dl>

          <span className="card-link">Open details</span>
        </button>
      ))}
    </div>
  )
}
