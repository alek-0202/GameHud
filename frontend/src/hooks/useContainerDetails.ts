import { useEffect, useState } from 'react'
import { ApiRequestError, fetchContainerDetails } from '../api/containers'
import type { ContainerDetails } from '../types/container'

type ContainerDetailsState =
  | { status: 'idle'; container: null; message?: undefined }
  | { status: 'loading'; container: null; message?: undefined }
  | { status: 'success'; container: ContainerDetails; message?: undefined }
  | { status: 'not-found'; container: null; message: string }
  | { status: 'unavailable'; container: null; message: string }
  | { status: 'error'; container: null; message: string }

export function useContainerDetails(
  containerId: string | null,
  refreshToken = 0,
): ContainerDetailsState {
  const [state, setState] = useState<ContainerDetailsState>({ status: 'idle', container: null })

  useEffect(() => {
    if (containerId === null) {
      setState({ status: 'idle', container: null })
      return
    }

    const resolvedContainerId = containerId
    const abortController = new AbortController()

    async function loadContainerDetails() {
      try {
        setState({ status: 'loading', container: null })

        const container = await fetchContainerDetails(resolvedContainerId, abortController.signal)

        setState({ status: 'success', container })
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }

        setState(getErrorState(error))
      }
    }

    void loadContainerDetails()

    return () => {
      abortController.abort()
    }
  }, [containerId, refreshToken])

  return state
}

function getErrorState(error: unknown): ContainerDetailsState {
  if (error instanceof ApiRequestError) {
    if (error.status === 404) {
      return {
        status: 'not-found',
        container: null,
        message: 'Container was not found.',
      }
    }

    if (error.status === 503) {
      return {
        status: 'unavailable',
        container: null,
        message: 'Docker is unavailable. Check whether the API can reach Docker Engine.',
      }
    }
  }

  return {
    status: 'error',
    container: null,
    message: 'Unable to load container details.',
  }
}
