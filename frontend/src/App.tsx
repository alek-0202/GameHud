import { Navigate, Route, Routes } from 'react-router-dom'
import { AppShell } from './layout/AppShell'
import { ContainerDetailsPage } from './pages/ContainerDetailsPage'
import { ContainersPage } from './pages/ContainersPage'
import { DashboardPage } from './pages/DashboardPage'
import { GameServersPage } from './pages/GameServersPage'
import { PalworldAdvancedPage } from './pages/PalworldAdvancedPage'
import { PalworldLayout } from './pages/PalworldLayout'
import { PalworldLogsPage } from './pages/PalworldLogsPage'
import { PalworldOverviewPage } from './pages/PalworldOverviewPage'
import { PalworldPlayersPage } from './pages/PalworldPlayersPage'
import { PalworldSettingsPage } from './pages/PalworldSettingsPage'
import { SettingsPage } from './pages/SettingsPage'
import './App.css'

function App() {
  return (
    <Routes>
      <Route element={<AppShell />}>
        <Route index element={<DashboardPage />} />
        <Route path="servers" element={<GameServersPage />} />
        <Route path="servers/palworld" element={<PalworldLayout />}>
          <Route index element={<PalworldOverviewPage />} />
          <Route path="players" element={<PalworldPlayersPage />} />
          <Route path="settings" element={<PalworldSettingsPage />} />
          <Route path="logs" element={<PalworldLogsPage />} />
          <Route path="advanced" element={<PalworldAdvancedPage />} />
        </Route>
        <Route path="containers" element={<ContainersPage />} />
        <Route path="containers/:containerId" element={<ContainerDetailsPage />} />
        <Route path="settings" element={<SettingsPage />} />
        <Route path="*" element={<Navigate replace to="/" />} />
      </Route>
    </Routes>
  )
}

export default App
