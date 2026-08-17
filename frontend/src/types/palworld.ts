export type PalworldSettingType =
  | 'boolean'
  | 'integer'
  | 'decimal'
  | 'string'
  | 'password'
  | 'select'

export interface PalworldSettingOption {
  value: string
  label: string
}

export interface PalworldSetting {
  key: string
  label: string
  description: string
  category: string
  type: PalworldSettingType
  min: number | null
  max: number | null
  step: number | null
  options: PalworldSettingOption[]
  defaultValue: string | null
  restartRequired: boolean
  advanced: boolean
  securitySensitive: boolean
  value: string | null
  hasValue: boolean
}

export interface PalworldConfig {
  containerName: string
  settings: PalworldSetting[]
}

export interface PalworldSettingUpdateRequest {
  key: string
  value: string | null
}

export interface PalworldConfigUpdateRequest {
  settings: PalworldSettingUpdateRequest[]
}

export interface PalworldConfigUpdateResponse {
  message: string
  containerName: string
  restartRequested: boolean
  lifecycleApplied: boolean
  changedSettings: number
  backupFileName: string | null
  config: PalworldConfig
}

export interface PalworldPlayer {
  name: string
  accountName: string | null
  userId: string | null
  publicId: string | null
  ping: number | null
  level: number | null
}

export interface PalworldPlayers {
  onlineCount: number
  maxPlayers: number | null
  players: PalworldPlayer[]
  retrievedAt: string
}

export interface PalworldOverview {
  serverName: string
  displayName: string
  containerName: string
  containerState: string
  containerStatus: string
  health: string
  healthLabel: string
  version: string | null
  description: string | null
  connectionAddress: string | null
  onlinePlayers: number
  maxPlayers: number | null
  uptimeSeconds: number | null
  serverFps: number | null
  serverFrameTime: number | null
  baseCampCount: number | null
  inGameDays: number | null
  restApiAvailable: boolean
  restApiMessage: string | null
  players: PalworldPlayer[]
  retrievedAt: string
}

export interface PalworldBackupSchedule {
  enabled: boolean
  intervalMinutes: number
  retentionCount: number
  retentionDays: number
  nextScheduledAt: string | null
}

export interface PalworldBackupStorage {
  totalBytes: number
  backupCount: number
}

export interface PalworldBackup {
  id: string
  createdAt: string
  sizeBytes: number
  filename: string
  status: string
  type: string
  note: string | null
  worldSaveStatus: string | null
  downloadUrl: string | null
}

export interface PalworldBackupSummary {
  schedule: PalworldBackupSchedule
  storage: PalworldBackupStorage
  latestBackup: PalworldBackup | null
  backups: PalworldBackup[]
}

export interface PalworldCreateBackupResponse {
  message: string
  backup: PalworldBackup
}

export interface PalworldRestoreBackupResponse {
  message: string
  restoredBackupId: string
  preRestoreBackup: PalworldBackup
  playersOnlineBeforeRestore: number | null
  stopStatus: string
  startStatus: string
  healthCheckStatus: string
  completedAt: string
}

export interface PalworldDeleteBackupResponse {
  message: string
  backupId: string
  deletedAt: string
}

export interface PalworldUpdateStatus {
  installedVersion: string | null
  availableVersion: string | null
  updateStatus: string
  lastCheckedAt: string
  strategy: string
  message: string
}

export interface PalworldUpdateResponse {
  message: string
  installedVersionBefore: string | null
  installedVersionAfter: string | null
  availableVersion: string | null
  updateApplied: boolean
  playersOnlineBeforeUpdate: number | null
  announcementStatus: string
  saveStatus: string
  backupId: string
  stopStatus: string
  updateStatus: string
  startStatus: string
  healthCheckStatus: string
  completedAt: string
}

export interface PalworldAdminActionResponse {
  message: string
  action: string
  userId: string
  completedAt: string
}

export interface PalworldDetectedMod {
  name: string
  relativePath: string
  sizeBytes: number
}

export interface PalworldMods {
  serverId: string
  managementSupported: boolean
  message: string
  detectedMods: PalworldDetectedMod[]
}
