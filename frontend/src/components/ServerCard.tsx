import { Link } from 'react-router-dom'
import type { Container } from '../types/container'
import type { PalworldConfig } from '../types/palworld'
import { StatusBadge } from './StatusBadge'

interface ServerCardProps {
  config: PalworldConfig | null
  container: Container | null
}

export function ServerCard({ config, container }: ServerCardProps) {
  const serverName = config?.serverName || 'Palworld'
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
          <dt>Game</dt>
          <dd>Palworld</dd>
        </div>
        <div>
          <dt>Status</dt>
          <dd>{container?.status || 'Unavailable'}</dd>
        </div>
      </dl>

      <div className="server-card-actions">
        <Link className="secondary-button" to="/servers/palworld">Manage Server</Link>
      </div>
    </article>
  )
}
