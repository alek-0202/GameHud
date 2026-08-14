export interface PalworldConfig {
  containerName: string
  serverName: string | null
  hasServerPassword: boolean
  expRate: number | null
  playerDamageRateAttack: number | null
  palCaptureRate: number | null
  playerStomachDecreaceRate: number | null
  playerStaminaDecreaceRate: number | null
  workSpeedRate: number | null
  collectionDropRate: number | null
  enemyDropItemRate: number | null
  palEggDefaultHatchingTime: number | null
  deathPenalty: string | null
  guildPlayerMaxNum: number | null
  baseCampMaxNum: number | null
  baseCampWorkerMaxNum: number | null
}

export interface PalworldConfigUpdateRequest {
  serverName: string | null
  serverPassword: string | null
  expRate: number | null
  playerDamageRateAttack: number | null
  palCaptureRate: number | null
  playerStomachDecreaceRate: number | null
  playerStaminaDecreaceRate: number | null
  workSpeedRate: number | null
  collectionDropRate: number | null
  enemyDropItemRate: number | null
  palEggDefaultHatchingTime: number | null
  deathPenalty: string | null
  guildPlayerMaxNum: number | null
  baseCampMaxNum: number | null
  baseCampWorkerMaxNum: number | null
}

export interface PalworldConfigUpdateResponse {
  message: string
  containerName: string
  restartRequested: boolean
  lifecycleApplied: boolean
  backupFileName: string
  config: PalworldConfig
}
