import { useEffect, useState } from 'react'
import { ApiRequestError, fetchContainerLogs } from '../api/containers'
import { logTailOptions, type ContainerLogs as ContainerLogsType, type LogTailOption } from '../types/container'

type LogsState =
  | { status: 'loading' }
  | { status: 'success'; logs: ContainerLogsType }
  | { status: 'error' }

interface ContainerLogsProps {
  containerId: string
}

export function ContainerLogs({ containerId }: ContainerLogsProps) {
  const [tail, setTail] = useState<LogTailOption>(200)
  const [refreshToken, setRefreshToken] = useState(0)
  const [state, setState] = useState<LogsState>({ status: 'loading' })

  useEffect(() => {
    const abortController = new AbortController()

    async function loadLogs() {
      try {
        setState({ status: 'loading' })

        const logs = await fetchContainerLogs(containerId, tail, true, abortController.signal)

        setState({ status: 'success', logs })
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }

        if (error instanceof ApiRequestError) {
          setState({ status: 'error' })
          return
        }

        setState({ status: 'error' })
      }
    }

    void loadLogs()

    return () => {
      abortController.abort()
    }
  }, [containerId, refreshToken, tail])

  return (
    <section className="details-block logs-block" aria-labelledby="logs-title">
      <div className="logs-heading">
        <div>
          <h3 id="logs-title">Logs</h3>
          {state.status === 'success' && (
            <span>Retrieved {formatDate(state.logs.retrievedAt)}</span>
          )}
        </div>
        <div className="logs-actions">
          <label>
            Lines
            <select
              value={tail}
              onChange={(event) => {
                const nextTail = Number(event.currentTarget.value)

                if (isLogTailOption(nextTail)) {
                  setTail(nextTail)
                }
              }}
            >
              {logTailOptions.map((option) => (
                <option key={option} value={option}>
                  {option}
                </option>
              ))}
            </select>
          </label>
          <button
            className="secondary-button"
            type="button"
            onClick={() => {
              setRefreshToken((value) => value + 1)
            }}
          >
            Refresh logs
          </button>
        </div>
      </div>

      {state.status === 'loading' && <p className="state-message">Loading logs...</p>}

      {state.status === 'error' && (
        <p className="state-message state-message-error">Unable to load logs.</p>
      )}

      {state.status === 'success' && state.logs.lines.length === 0 && (
        <p className="empty-message">No logs found.</p>
      )}

      {state.status === 'success' && state.logs.lines.length > 0 && (
        <pre className="logs-output">{state.logs.lines.join('\n')}</pre>
      )}
    </section>
  )
}

function isLogTailOption(value: number): value is LogTailOption {
  return logTailOptions.some((option) => option === value)
}

function formatDate(value: string) {
  const date = new Date(value)

  if (Number.isNaN(date.getTime())) {
    return value
  }

  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date)
}
