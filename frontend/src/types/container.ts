export interface Container {
  id: string
  name: string
  image: string
  state: string
  status: string
}

export interface ContainerDetails {
  id: string
  name: string
  image: string
  imageId: string
  state: string
  status: string
  createdAt: string
  startedAt: string
  finishedAt: string
  restartCount: number
  platform: string
  driver: string
  ports: ContainerPort[]
  mounts: ContainerMount[]
  networks: ContainerNetwork[]
  labels: Record<string, string>
}

export interface ContainerPort {
  privatePort: number
  publicPort: number | null
  type: string
  hostIp: string
}

export interface ContainerMount {
  type: string
  source: string
  destination: string
  readOnly: boolean
}

export interface ContainerNetwork {
  name: string
  ipAddress: string
  gateway: string
  macAddress: string
}

export interface ContainerLogs {
  containerId: string
  lines: string[]
  retrievedAt: string
}

export type ContainerLifecycleAction = 'start' | 'stop' | 'restart'

export interface ContainerLifecycleActionResponse {
  containerId: string
  action: ContainerLifecycleAction
  success: boolean
  message: string
  previousState: string
  currentState: string
  completedAt: string
}

export const logTailOptions = [100, 200, 500, 1000] as const

export type LogTailOption = (typeof logTailOptions)[number]
