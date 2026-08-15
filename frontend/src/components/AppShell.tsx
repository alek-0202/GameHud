import type { ReactNode } from 'react'

interface AppShellProps {
  children: ReactNode
}

export function AppShell({ children }: AppShellProps) {
  return (
    <div className="app-layout">
      <Sidebar />
      <div className="app-content">
        <Topbar />
        <main className="app-main">{children}</main>
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
        <a className="sidebar-link sidebar-link-active" href="#dashboard-overview">
          <span aria-hidden="true" className="sidebar-glyph">D</span>
          Dashboard
        </a>
        <a className="sidebar-link" href="#containers-panel">
          <span aria-hidden="true" className="sidebar-glyph">C</span>
          Containers
        </a>
        <div className="sidebar-group">
          <span className="sidebar-group-label">Game Servers</span>
          <a className="sidebar-link sidebar-link-nested" href="#palworld-settings">
            <span aria-hidden="true" className="sidebar-glyph">P</span>
            Palworld
          </a>
        </div>
        <span className="sidebar-link sidebar-link-disabled" aria-disabled="true">
          <span aria-hidden="true" className="sidebar-glyph">L</span>
          Logs
          <small>Planned</small>
        </span>
        <span className="sidebar-link sidebar-link-disabled" aria-disabled="true">
          <span aria-hidden="true" className="sidebar-glyph">S</span>
          Settings
          <small>Planned</small>
        </span>
      </nav>
    </aside>
  )
}

function Topbar() {
  return (
    <header className="topbar">
      <div>
        <span className="topbar-eyebrow">Private infrastructure panel</span>
        <h1>GamesHud Dashboard</h1>
      </div>
      <div className="topbar-status">
        <span className="environment-pill">Local / Private</span>
      </div>
    </header>
  )
}
