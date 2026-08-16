import { NavLink, Outlet } from 'react-router-dom'
import { SectionHeader } from '../components/SectionHeader'

export function PalworldLayout() {
  return (
    <section className="palworld-section" aria-labelledby="palworld-title">
      <div className="palworld-hero">
        <div>
          <span className="section-eyebrow">Game server administration</span>
          <h2 id="palworld-title">Palworld</h2>
          <p>Friendly operations for the configured Palworld server.</p>
        </div>
      </div>

      <nav className="tab-strip" aria-label="Palworld sections">
        <PalworldTab label="Overview" to="/servers/palworld" end />
        <PalworldTab label="Settings" to="/servers/palworld/settings" />
        <PalworldTab label="Logs" to="/servers/palworld/logs" />
        <PalworldTab label="Advanced" to="/servers/palworld/advanced" />
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
