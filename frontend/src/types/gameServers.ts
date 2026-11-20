export interface GameServer {
  id: string
  gameType: string
  displayName: string
  runtimeType: string
  installationType: 'legacyExternal' | 'managed'
  containerName: string
  brandingImage: string | null
  capabilities: string[]
}

export interface GameServersResponse {
  servers: GameServer[]
}
