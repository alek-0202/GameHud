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
