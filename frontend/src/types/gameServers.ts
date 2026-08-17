export interface GameServer {
  id: string
  gameType: string
  displayName: string
  containerName: string
  brandingImage: string | null
  capabilities: string[]
}

export interface GameServersResponse {
  servers: GameServer[]
}
