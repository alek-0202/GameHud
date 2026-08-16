export type ScheduleActionType =
  | 'automatic-backup'
  | 'restart-palworld'
  | 'update-check'
  | 'announcement'
  | 'shutdown-palworld'

export interface ScheduleTask {
  id: string
  actionType: ScheduleActionType
  recurrenceMinutes: number
  enabled: boolean
  nextRunAt: string | null
  lastRunAt: string | null
  lastResult: string
  status: string
  message: string | null
}

export interface ScheduleTaskRequest {
  id?: string
  actionType: ScheduleActionType
  recurrenceMinutes: number
  enabled: boolean
  message?: string | null
}

export interface ScheduleRunResponse {
  id: string
  success: boolean
  result: string
  completedAt: string
}

export interface NotificationSettings {
  discordWebhookConfigured: boolean
  serverStatusEnabled: boolean
  backupsEnabled: boolean
  updatesEnabled: boolean
  playerJoinLeaveEnabled: boolean
  cooldownSeconds: number
  lastTestAt: string | null
  lastTestResult: string | null
}

export interface NotificationTestResponse {
  success: boolean
  message: string
  completedAt: string
}
