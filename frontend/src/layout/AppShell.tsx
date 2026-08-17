import type { ReactNode } from 'react'
import {
  Boxes,
  Gamepad2,
  Gauge,
  LayoutDashboard,
  Settings,
  type LucideIcon,
} from 'lucide-react'
import { NavLink, Outlet, useLocation } from 'react-router-dom'
import { PageHeader } from '../components/PageHeader'

interface AppShellProps {
  children?: ReactNode
}

export function AppShell({ children }: AppShellProps) {
  return (
    <div className="app-layout">
      <Sidebar />
      <div className="app-content">
        <Topbar />
        <main className="app-main">{children ?? <Outlet />}</main>
      </div>
    </div>
  )
}

function Sidebar() {
  return (
    <aside className="sidebar" aria-label="Primary navigation">
      <div className="brand-block">
        <div className="brand-mark" aria-hidden="true">GH</div>
        <div>
          <strong>GamesHud</strong>
          <span>Ops Console</span>
        </div>
      </div>

      <nav className="sidebar-nav">
        <SidebarGroup title="General">
          <SidebarLink icon={LayoutDashboard} label="Dashboard" to="/" end />
        </SidebarGroup>

        <SidebarGroup title="Game Servers">
          <SidebarLink icon={Gamepad2} label="Palworld" to="/servers/palworld" />
        </SidebarGroup>

        <SidebarGroup title="Infrastructure">
          <SidebarLink icon={Boxes} label="Containers" to="/containers" />
        </SidebarGroup>

        <SidebarGroup title="System">
          <SidebarLink icon={Settings} label="Settings" to="/settings" />
        </SidebarGroup>
      </nav>
    </aside>
  )
}

interface SidebarGroupProps {
  title: string
  children: ReactNode
}

function SidebarGroup({ title, children }: SidebarGroupProps) {
  return (
    <div className="sidebar-group">
      <span className="sidebar-group-label">{title}</span>
      {children}
    </div>
  )
}

interface SidebarLinkProps {
  icon: LucideIcon
  label: string
  to: string
  end?: boolean
}

function SidebarLink({
  icon: Icon,
  label,
  to,
  end = false,
}: SidebarLinkProps) {
  return (
    <NavLink
      className={({ isActive }) => (
        isActive ? 'sidebar-link sidebar-link-active' : 'sidebar-link'
      )}
      end={end}
      to={to}
    >
      <span aria-hidden="true" className="sidebar-glyph">
        <Icon size={17} strokeWidth={2.2} />
      </span>
      {label}
    </NavLink>
  )
}

function Topbar() {
  const location = useLocation()
  const pageTitle = getPageTitle(location.pathname)

  return (
    <header className="topbar">
      <PageHeader
        actions={(
          <span className="environment-pill">
            <Gauge size={14} strokeWidth={2.3} />
            Local / Private
          </span>
        )}
        eyebrow="Private infrastructure panel"
        title={pageTitle}
      />
    </header>
  )
}

function getPageTitle(pathname: string) {
  if (pathname === '/') {
    return 'Dashboard'
  }

  if (pathname === '/servers') {
    return 'Game Servers'
  }

  if (pathname.startsWith('/servers/palworld')) {
    return 'Palworld'
  }

  if (pathname.startsWith('/containers')) {
    return 'Containers'
  }

  if (pathname === '/settings') {
    return 'Settings'
  }

  return 'GamesHud'
}
