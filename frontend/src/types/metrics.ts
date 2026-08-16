export interface HostMetrics {
  cpuPercent: number | null
  memoryUsedBytes: number
  memoryTotalBytes: number
  diskUsedBytes: number
  diskTotalBytes: number
  uptimeSeconds: number
  retrievedAt: string
}

export interface DockerSummaryMetrics {
  runningContainers: number
  stoppedContainers: number
}

export interface MetricHistoryPoint {
  timestamp: string
  hostCpuPercent: number | null
  hostMemoryUsedBytes: number | null
  hostMemoryTotalBytes: number | null
  diskUsedBytes: number | null
  diskTotalBytes: number | null
  palworldCpuPercent: number | null
  palworldMemoryUsageBytes: number | null
  palworldMemoryLimitBytes: number | null
  playersOnline: number | null
}

export interface SystemMetrics {
  host: HostMetrics
  docker: DockerSummaryMetrics
  history: MetricHistoryPoint[]
}

export interface ContainerMetrics {
  containerId: string
  name: string
  cpuPercent: number | null
  memoryUsageBytes: number | null
  memoryLimitBytes: number | null
  memoryPercent: number | null
  retrievedAt: string
}

export interface PalworldMetrics {
  containerName: string
  cpuPercent: number | null
  memoryUsageBytes: number | null
  memoryLimitBytes: number | null
  memoryPercent: number | null
  uptimeSeconds: number | null
  playersOnline: number | null
  maxPlayers: number | null
  history: MetricHistoryPoint[]
  retrievedAt: string
}

export type MetricsHistoryWindow = 1 | 6 | 24
