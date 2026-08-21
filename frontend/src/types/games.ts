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
