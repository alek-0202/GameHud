import { NavLink, Outlet, useParams } from 'react-router-dom'
import { SectionHeader } from '../components/SectionHeader'
import { StatusBadge } from '../components/StatusBadge'
import { usePalworldOverview } from '../hooks/usePalworldOverview'

export function PalworldLayout() {
  const { serverId = 'palworld' } = useParams()
  const overviewState = usePalworldOverview(20000, serverId)
  const overview = overviewState.status === 'success' ? overviewState.overview : null
  const basePath = `/servers/${encodeURIComponent(serverId)}`

  return (
    <section className="palworld-section" aria-labelledby="palworld-title">
      <div className="palworld-hero">
        <div>
          <span className="section-eyebrow">Game server administration</span>
          <h2 id="palworld-title">Palworld</h2>
          <p>{overview?.displayName ?? 'Friendly operations for the configured Palworld server.'}</p>
        </div>
        <StatusBadge state={overview?.healthLabel ?? 'Unknown'} />
      </div>

      <nav className="tab-strip" aria-label="Palworld sections">
        <PalworldTab label="Overview" to={basePath} end />
        <PalworldTab label="Players" to={`${basePath}/players`} />
        <PalworldTab label="Settings" to={`${basePath}/settings`} />
        <PalworldTab label="Backups" to={`${basePath}/backups`} />
        <PalworldTab label="Logs" to={`${basePath}/logs`} />
        <PalworldTab label="Advanced" to={`${basePath}/advanced`} />
      </nav>

      <Outlet />
    </section>
  )
}

interface PalworldTabProps {
  label: string
  to: string
  end?: boolean
}

function PalworldTab({
  label,
  to,
  end = false,
}: PalworldTabProps) {
  return (
    <NavLink
      className={({ isActive }) => (
        isActive ? 'tab-item tab-item-active' : 'tab-item'
      )}
      end={end}
      to={to}
    >
      {label}
    </NavLink>
  )
}

export function PalworldUnavailableState({ message }: { message: string }) {
  return (
    <section className="details-block">
      <SectionHeader
        title="Palworld unavailable"
        description="GamesHud could not load the configured Palworld server."
      />
      <p className="state-message state-message-error">{message}</p>
    </section>
  )
}
