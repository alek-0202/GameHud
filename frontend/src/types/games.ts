export interface GameCatalogBranding {
  iconKey: string
  imageReference: string | null
}

export interface GameCatalogGame {
  id: string
  displayName: string
  description: string
  branding: GameCatalogBranding
  supportedRuntimes: string[]
  capabilities: string[]
}

export interface GameCatalogResponse {
  games: GameCatalogGame[]
}

export interface GameCompatibilityAssessment {
  gameId: string
  displayName: string
  status: string
  checks: GameCompatibilityCheck[]
  blockingIssues: GameCompatibilityIssue[]
  warnings: GameCompatibilityIssue[]
}

export interface GameCompatibilityCheck {
  id: string
  label: string
  required: string
  detected: string
  status: string
  message: string
}

export interface GameCompatibilityIssue {
  code: string
  severity: string
  message: string
}

export interface NetworkPort {
  number: number
  protocol: string
}

export interface GamePortPlan {
  gameId: string
  displayName: string
  status: string
  ports: GamePortPlanItem[]
  message: string
}

export interface GamePortPlanItem {
  definitionId: string
  label: string
  purpose: string
  exposure: string
  required: boolean
  allowAlternative: boolean
  availability: PortAvailability
  allocation: PortAllocation
}

export interface PortAvailability {
  port: number
  protocol: string
  status: string
  available: boolean
  dockerPublished: boolean
  message: string
}

export interface PortAllocation {
  requestedPort: NetworkPort
  allocatedPort: NetworkPort | null
  usedAlternative: boolean
  status: string
  errorCode: string | null
  message: string
  checkedPorts: NetworkPort[]
}
