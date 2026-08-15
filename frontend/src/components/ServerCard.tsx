import type { Container } from '../types/container'
import type { PalworldConfig } from '../types/palworld'
import { StatusBadge } from './StatusBadge'

interface ServerCardProps {
  config: PalworldConfig | null
  container: Container | null
}

export function ServerCard({ config, container }: ServerCardProps) {
  const serverName = config?.serverName || 'Palworld'
  const containerName = config?.containerName || container?.name || 'Not configured'
  const state = container?.state || 'Unknown'

  return (
    <article className="server-card">
      <div className="server-card-header">
        <div>
          <span className="section-eyebrow">Game server</span>
          <h3>{serverName}</h3>
        </div>
        <StatusBadge state={state} />
      </div>

      <dl className="server-meta">
        <div>
          <dt>Container</dt>
          <dd>{containerName}</dd>
        </div>
        <div>
          <dt>Image</dt>
          <dd>{container?.image || 'Unavailable'}</dd>
        </div>
        <div>
          <dt>Status</dt>
          <dd>{container?.status || 'Unavailable'}</dd>
        </div>
      </dl>

      <div className="server-card-actions">
        <a className="secondary-button" href="#palworld-settings">Manage</a>
      </div>
    </article>
  )
}
