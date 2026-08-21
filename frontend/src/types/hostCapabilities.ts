export interface HostCapabilities {
  operatingSystem: HostOperatingSystem
  cpu: HostCpu
  memory: HostMemory
  storage: HostStorage
  network: HostNetwork
  runtimes: HostRuntime[]
  overallReadiness: HostReadiness
  issues: HostCapabilityIssue[]
}

export interface HostOperatingSystem {
  family: string
  description: string
  architecture: string
}

export interface HostCpu {
  logicalProcessors: number
  architecture: string
}

export interface HostMemory {
  status: string
  totalBytes: number | null
  availableBytes: number | null
}

export interface HostStorage {
  status: string
  observedRoot: string
  totalBytes: number | null
  availableBytes: number | null
}

export interface HostNetwork {
  status: string
  interfaceCount: number
  loopbackAvailable: boolean
  ipv4Available: boolean
  canInspectInterfaces: boolean
}

export interface HostRuntime {
  id: string
  displayName: string
  status: string
  endpointConfigured: boolean
  reachable: boolean
  version: string | null
  operatingSystem: string | null
  issues: HostCapabilityIssue[]
}

export interface HostReadiness {
  status: string
  message: string
}

export interface HostCapabilityIssue {
  code: string
  severity: string
  message: string
}
