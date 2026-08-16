import { useEffect, useState } from 'react'
import { fetchContainers } from '../api/containers'
import type { Container } from '../types/container'

type ContainersState =
  | { status: 'loading'; containers: Container[]; message?: undefined }
  | { status: 'success'; containers: Container[]; message?: undefined }
  | { status: 'error'; containers: Container[]; message: string }

export function useContainers(refreshToken = 0): ContainersState {
  const [state, setState] = useState<ContainersState>({
    status: 'loading',
    containers: [],
  })

  useEffect(() => {
    const abortController = new AbortController()

    async function loadContainers() {
      try {
        setState({ status: 'loading', containers: [] })

        const containers = await fetchContainers(abortController.signal)

        setState({ status: 'success', containers })
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }

        setState({
          status: 'error',
          containers: [],
          message: 'Unable to load containers. Check whether the API is running and Docker is accessible.',
        })
      }
    }

    void loadContainers()

    return () => {
      abortController.abort()
    }
  }, [refreshToken])

  return state
}
