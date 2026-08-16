import type {
  NotificationSettings,
  NotificationTestResponse,
  ScheduleRunResponse,
  ScheduleTask,
  ScheduleTaskRequest,
} from '../types/operations'

const apiBaseUrl = (import.meta.env.VITE_API_BASE_URL ?? '').replace(/\/$/, '')

export class OperationsApiRequestError extends Error {
  public readonly status: number

  public constructor(status: number, message: string) {
    super(message)
    this.name = 'OperationsApiRequestError'
    this.status = status
  }
}

export async function fetchNotificationSettings(
  signal?: AbortSignal,
): Promise<NotificationSettings> {
  return fetchJson<NotificationSettings>('/api/settings/notifications', signal)
}

export async function sendTestNotification(
  signal?: AbortSignal,
): Promise<NotificationTestResponse> {
  return fetchJson<NotificationTestResponse>('/api/settings/notifications/test', signal, {
    method: 'POST',
  })
}

export async function fetchScheduleTasks(signal?: AbortSignal): Promise<ScheduleTask[]> {
  return fetchJson<ScheduleTask[]>('/api/scheduler', signal)
}

export async function saveScheduleTask(
  request: ScheduleTaskRequest,
  signal?: AbortSignal,
): Promise<ScheduleTask> {
  return fetchJson<ScheduleTask>('/api/scheduler', signal, {
    method: 'POST',
    body: JSON.stringify(request),
  })
}

export async function runScheduleTask(
  taskId: string,
  signal?: AbortSignal,
): Promise<ScheduleRunResponse> {
  return fetchJson<ScheduleRunResponse>(
    `/api/scheduler/${encodeURIComponent(taskId)}/run`,
    signal,
    { method: 'POST' },
  )
}

async function fetchJson<TResponse>(
  path: string,
  signal?: AbortSignal,
  init?: RequestInit,
): Promise<TResponse> {
  const response = await fetch(`${apiBaseUrl}${path}`, {
    ...init,
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
      ...init?.headers,
    },
    signal,
  })

  if (!response.ok) {
    throw new OperationsApiRequestError(
      response.status,
      `Operations request failed with status ${response.status}.`,
    )
  }

  return response.json() as Promise<TResponse>
}
