import { useEffect, useState } from 'react'
import { fetchContainers } from './api/containers'
import { AppShell } from './components/AppShell'
import { ContainerDetails } from './components/ContainerDetails'
import { ContainerList } from './components/ContainerList'
import { PalworldSettings } from './components/PalworldSettings'
import { SectionHeader } from './components/SectionHeader'
import { ServerCard } from './components/ServerCard'
import type { Container } from './types/container'
import type { PalworldConfig } from './types/palworld'
import './App.css'

function App() {
  const [containers, setContainers] = useState<Container[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)
  const [selectedContainerId, setSelectedContainerId] = useState<string | null>(null)
  const [containersRefreshToken, setContainersRefreshToken] = useState(0)
  const [palworldConfig, setPalworldConfig] = useState<PalworldConfig | null>(null)

  useEffect(() => {
    const abortController = new AbortController()

    async function loadContainers() {
      try {
        setIsLoading(true)
        setErrorMessage(null)

        const result = await fetchContainers(abortController.signal)

        setContainers(result)
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }

        setErrorMessage('Unable to load containers. Check whether the API is running and Docker is accessible.')
      } finally {
        if (!abortController.signal.aborted) {
          setIsLoading(false)
        }
      }
    }

    void loadContainers()

    return () => {
      abortController.abort()
    }
  }, [containersRefreshToken])

  const palworldContainer = findPalworldContainer(containers, palworldConfig)
  const runningContainers = containers.filter((container) => isRunning(container.state)).length
  const stoppedContainers = containers.filter((container) => isStopped(container.state)).length

  return (
    <AppShell>
      {selectedContainerId ? (
        <ContainerDetails
          containerId={selectedContainerId}
          onBack={() => {
            setSelectedContainerId(null)
            setContainersRefreshToken((value) => value + 1)
          }}
        />
      ) : (
        <div className="dashboard-sections" id="dashboard-overview">
          <section className="overview-grid" aria-label="Dashboard overview">
            <div className="metric-card">
              <span>Total containers</span>
              <strong>{containers.length}</strong>
            </div>
            <div className="metric-card metric-card-success">
              <span>Running</span>
              <strong>{runningContainers}</strong>
            </div>
            <div className="metric-card">
              <span>Stopped</span>
              <strong>{stoppedContainers}</strong>
            </div>
            <div className="metric-card metric-card-disabled">
              <span>Metrics</span>
              <strong>Pending</strong>
            </div>
          </section>

          <section className="game-servers-section" aria-labelledby="game-servers-title">
            <SectionHeader
              eyebrow="Game servers"
              titleId="game-servers-title"
              title="Configured servers"
              description="Operational view using only data already available from GamesHud."
              aside="1 configured"
            />
            <ServerCard config={palworldConfig} container={palworldContainer} />
          </section>

          <section className="containers-section" aria-labelledby="containers-title">
            <SectionHeader
              eyebrow="Docker Core"
              titleId="containers-title"
              title="Containers"
              description="Generic Docker container inventory and details."
              aside={`${containers.length} total`}
            />

            {isLoading && <p className="state-message">Loading containers...</p>}

            {!isLoading && errorMessage && (
              <p className="state-message state-message-error">{errorMessage}</p>
            )}

            {!isLoading && !errorMessage && containers.length === 0 && (
              <p className="state-message">No containers found.</p>
            )}

            {!isLoading && !errorMessage && containers.length > 0 && (
              <ContainerList
                containers={containers}
                onSelectContainer={setSelectedContainerId}
              />
            )}
          </section>

          <PalworldSettings
            container={palworldContainer}
            onConfigLoaded={setPalworldConfig}
          />
        </div>
      )}
    </AppShell>
  )
}

function findPalworldContainer(
  containers: Container[],
  config: PalworldConfig | null,
) {
  if (config === null) {
    return null
  }

  const expectedName = normalizeContainerName(config.containerName)

  return containers.find((container) => {
    return normalizeContainerName(container.name) === expectedName
      || normalizeContainerName(container.id) === expectedName
  }) ?? null
}

function normalizeContainerName(value: string) {
  return value.trim().replace(/^\//, '').toLowerCase()
}

function isRunning(state: string) {
  return state.toLowerCase() === 'running'
}

function isStopped(state: string) {
  return ['created', 'exited', 'stopped'].includes(state.toLowerCase())
}

export default App
