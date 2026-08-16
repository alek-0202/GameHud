import type { Container } from '../types/container'
import type { PalworldConfig } from '../types/palworld'

export function countRunningContainers(containers: Container[]) {
  return containers.filter((container) => isRunning(container.state)).length
}

export function countStoppedContainers(containers: Container[]) {
  return containers.filter((container) => isStopped(container.state)).length
}

export function findPalworldContainer(
  containers: Container[],
  config: PalworldConfig | null,
) {
  return findContainerByName(containers, config?.containerName ?? null)
}

export function findContainerByName(
  containers: Container[],
  containerName: string | null,
) {
  if (containerName === null) {
    return null
  }

  const expectedName = normalizeContainerName(containerName)

  return containers.find((container) => {
    return normalizeContainerName(container.name) === expectedName
      || normalizeContainerName(container.id) === expectedName
  }) ?? null
}

export function isRunning(state: string) {
  return state.toLowerCase() === 'running'
}

export function isStopped(state: string) {
  return ['created', 'exited', 'stopped'].includes(state.toLowerCase())
}

export function toFriendlyState(state: string) {
  if (isRunning(state)) {
    return 'Running'
  }

  if (isStopped(state)) {
    return 'Stopped'
  }

  return state || 'Unknown'
}

function normalizeContainerName(value: string) {
  return value.trim().replace(/^\//, '').toLowerCase()
}
