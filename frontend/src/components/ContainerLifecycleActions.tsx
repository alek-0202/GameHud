import { useEffect, useRef, useState } from 'react'
import {
  ApiRequestError,
  fetchContainerDetails,
  restartContainer,
  startContainer,
  stopContainer,
} from '../api/containers'
import type {
  ContainerDetails,
  ContainerLifecycleAction,
  ContainerLifecycleActionResponse,
} from '../types/container'

const lifecycleTimeoutSeconds = 10

type Feedback = {
  type: 'success' | 'error'
  message: string
}

interface ContainerLifecycleActionsProps {
  container: ContainerDetails
  onContainerUpdated: (container: ContainerDetails) => void
}

export function ContainerLifecycleActions({
  container,
  onContainerUpdated,
}: ContainerLifecycleActionsProps) {
  const [pendingAction, setPendingAction] = useState<ContainerLifecycleAction | null>(null)
  const [confirmationAction, setConfirmationAction] = useState<ContainerLifecycleAction | null>(null)
  const [feedback, setFeedback] = useState<Feedback | null>(null)
  const activeActionRef = useRef<ContainerLifecycleAction | null>(null)
  const activeAbortControllerRef = useRef<AbortController | null>(null)
  const isMountedRef = useRef(true)

  const availableActions = getAvailableActions(container.state)
  const isProcessing = pendingAction !== null

  useEffect(() => {
    return () => {
      isMountedRef.current = false
      activeAbortControllerRef.current?.abort()
    }
  }, [])

  async function runAction(action: ContainerLifecycleAction) {
    if (activeActionRef.current !== null) {
      return
    }

    const abortController = new AbortController()
    activeActionRef.current = action
    activeAbortControllerRef.current = abortController

    try {
      setPendingAction(action)
      setFeedback(null)

      const result = await executeLifecycleAction(container.id, action, abortController.signal)
      const updatedContainer = await fetchContainerDetails(container.id, abortController.signal)

      if (isMountedRef.current) {
        onContainerUpdated(updatedContainer)
        setFeedback({
          type: result.success ? 'success' : 'error',
          message: result.message,
        })
      }
    } catch (error) {
      if (error instanceof DOMException && error.name === 'AbortError') {
        return
      }

      if (isMountedRef.current) {
        setFeedback({
          type: 'error',
          message: getActionErrorMessage(error),
        })
      }
    } finally {
      activeActionRef.current = null
      activeAbortControllerRef.current = null

      if (isMountedRef.current) {
        setPendingAction(null)
        setConfirmationAction(null)
      }
    }
  }

  if (availableActions.length === 0) {
    return (
      <section className="details-block lifecycle-block" aria-labelledby="lifecycle-title">
        <h3 id="lifecycle-title">Lifecycle</h3>
        <p className="empty-message">No lifecycle action is available for the current state.</p>
      </section>
    )
  }

  return (
    <section className="details-block lifecycle-block" aria-labelledby="lifecycle-title">
      <h3 id="lifecycle-title">Lifecycle</h3>

      <div className="lifecycle-actions">
        {availableActions.map((action) => (
          <button
            className={getActionButtonClass(action)}
            disabled={isProcessing}
            key={action}
            type="button"
            onClick={() => {
              if (action === 'start') {
                void runAction(action)
                return
              }

              setConfirmationAction(action)
            }}
          >
            {pendingAction === action ? getProcessingText(action) : getActionLabel(action)}
          </button>
        ))}
      </div>

      {isProcessing && (
        <p className="state-message">Processing {pendingAction} request...</p>
      )}

      {feedback && (
        <p className={feedback.type === 'success' ? 'state-message state-message-success' : 'state-message state-message-error'}>
          {feedback.message}
        </p>
      )}

      {confirmationAction && (
        <ConfirmLifecycleActionDialog
          action={confirmationAction}
          containerName={container.name || container.id}
          isProcessing={isProcessing}
          onCancel={() => {
            if (!isProcessing) {
              setConfirmationAction(null)
            }
          }}
          onConfirm={() => {
            void runAction(confirmationAction)
          }}
        />
      )}
    </section>
  )
}

interface ConfirmLifecycleActionDialogProps {
  action: ContainerLifecycleAction
  containerName: string
  isProcessing: boolean
  onCancel: () => void
  onConfirm: () => void
}

function ConfirmLifecycleActionDialog({
  action,
  containerName,
  isProcessing,
  onCancel,
  onConfirm,
}: ConfirmLifecycleActionDialogProps) {
  const titleId = `${action}-container-title`
  const descriptionId = `${action}-container-description`

  return (
    <div className="modal-backdrop" role="presentation">
      <div
        aria-describedby={descriptionId}
        aria-labelledby={titleId}
        aria-modal="true"
        className="modal-panel"
        role="dialog"
      >
        <h4 id={titleId}>{getActionLabel(action)} container</h4>
        <p id={descriptionId}>
          This action will affect {containerName} and may make the service temporarily unavailable.
        </p>
        <div className="modal-actions">
          <button
            className="secondary-button"
            disabled={isProcessing}
            type="button"
            onClick={onCancel}
          >
            Cancel
          </button>
          <button
            className={getActionButtonClass(action)}
            disabled={isProcessing}
            type="button"
            onClick={onConfirm}
          >
            {getConfirmText(action)}
          </button>
        </div>
      </div>
    </div>
  )
}

function getAvailableActions(state: string): ContainerLifecycleAction[] {
  const normalizedState = state.toLowerCase()

  if (normalizedState === 'running') {
    return ['stop', 'restart']
  }

  if (['created', 'exited', 'stopped'].includes(normalizedState)) {
    return ['start']
  }

  return []
}

async function executeLifecycleAction(
  containerId: string,
  action: ContainerLifecycleAction,
  signal: AbortSignal,
): Promise<ContainerLifecycleActionResponse> {
  if (action === 'start') {
    return startContainer(containerId, signal)
  }

  if (action === 'stop') {
    return stopContainer(containerId, lifecycleTimeoutSeconds, signal)
  }

  return restartContainer(containerId, lifecycleTimeoutSeconds, signal)
}

function getActionLabel(action: ContainerLifecycleAction) {
  if (action === 'start') {
    return 'Start'
  }

  if (action === 'stop') {
    return 'Stop'
  }

  return 'Restart'
}

function getConfirmText(action: ContainerLifecycleAction) {
  if (action === 'stop') {
    return 'Stop container'
  }

  if (action === 'restart') {
    return 'Restart container'
  }

  return 'Start container'
}

function getProcessingText(action: ContainerLifecycleAction) {
  if (action === 'start') {
    return 'Starting...'
  }

  if (action === 'stop') {
    return 'Stopping...'
  }

  return 'Restarting...'
}

function getActionButtonClass(action: ContainerLifecycleAction) {
  return action === 'start' ? 'primary-button' : 'danger-button'
}

function getActionErrorMessage(error: unknown) {
  if (error instanceof ApiRequestError) {
    if (error.status === 404) {
      return 'Container was not found.'
    }

    if (error.status === 503) {
      return 'Docker is unavailable. Check whether the API can reach Docker Engine.'
    }
  }

  return 'Unable to complete the container action.'
}
