import { useEffect, useState } from 'react'
import { fetchContainers } from './api/containers'
import { ContainerList } from './components/ContainerList'
import type { Container } from './types/container'
import './App.css'

function App() {
  const [containers, setContainers] = useState<Container[]>([])
  const [isLoading, setIsLoading] = useState(true)
  const [errorMessage, setErrorMessage] = useState<string | null>(null)

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
  }, [])

  return (
    <main className="app-shell">
      <header className="app-header">
        <h1>GamesHud</h1>
        <p>Docker and game server management panel</p>
      </header>

      <section className="containers-section" aria-labelledby="containers-title">
        <div className="section-heading">
          <h2 id="containers-title">Containers</h2>
          <span>{containers.length} total</span>
        </div>

        {isLoading && <p className="state-message">Loading containers...</p>}

        {!isLoading && errorMessage && (
          <p className="state-message state-message-error">{errorMessage}</p>
        )}

        {!isLoading && !errorMessage && containers.length === 0 && (
          <p className="state-message">No containers found.</p>
        )}

        {!isLoading && !errorMessage && containers.length > 0 && (
          <ContainerList containers={containers} />
        )}
      </section>
    </main>
  )
}

export default App
