import type {
  AttendanceDto,
  AuditLogDto,
  AuthResponse,
  CameraDto,
  DailyAttendanceSummaryDto,
  DashboardDto,
  HomeDto,
  PagedResult,
  PersonDto,
  UpsertCameraRequest,
  UserDto,
} from './types';

const TOKEN_KEY = 'sp.accessToken';

export class ApiError extends Error {
  constructor(public readonly status: number, message: string) {
    super(message);
  }
}

type QueryValue = string | number | boolean | null | undefined;
type Query = Record<string, QueryValue>;

function buildUrl(path: string, query?: Query): string {
  if (!query) return path;
  const params = new URLSearchParams();
  for (const [key, value] of Object.entries(query)) {
    if (value !== undefined && value !== null && value !== '') params.set(key, String(value));
  }
  const qs = params.toString();
  return qs ? `${path}?${qs}` : path;
}

function authHeaders(): Record<string, string> {
  const token = localStorage.getItem(TOKEN_KEY);
  return token ? { Authorization: `Bearer ${token}` } : {};
}

async function errorFrom(res: Response): Promise<ApiError> {
  let message = res.statusText || `Request failed (${res.status})`;
  try {
    const body = (await res.json()) as { detail?: string; title?: string; errors?: Record<string, string[]> };
    const firstValidation = body.errors ? Object.values(body.errors)[0]?.[0] : undefined;
    message = firstValidation ?? body.detail ?? body.title ?? message;
  } catch {
    // Non-JSON error body: keep the status text.
  }
  return new ApiError(res.status, message);
}

async function send(method: string, path: string, body?: unknown, query?: Query): Promise<Response> {
  const headers: Record<string, string> = { ...authHeaders() };
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  const res = await fetch(buildUrl(path, query), {
    method,
    headers,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  });
  if (res.status === 401 && headers.Authorization) {
    localStorage.removeItem(TOKEN_KEY);
    window.dispatchEvent(new Event('auth:expired'));
  }
  if (!res.ok) throw await errorFrom(res);
  return res;
}

async function json<T>(method: string, path: string, body?: unknown, query?: Query): Promise<T> {
  const res = await send(method, path, body, query);
  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}

/** Fetches a binary resource with the bearer token and returns it as a Blob. */
async function blob(path: string, query?: Query): Promise<Blob> {
  const res = await send('GET', path, undefined, query);
  return res.blob();
}

export const api = {
  setToken(token: string) {
    localStorage.setItem(TOKEN_KEY, token);
  },
  clearToken() {
    localStorage.removeItem(TOKEN_KEY);
  },
  hasToken(): boolean {
    return localStorage.getItem(TOKEN_KEY) !== null;
  },

  async login(email: string, password: string): Promise<AuthResponse> {
    const res = await json<AuthResponse>('POST', '/api/auth/login', { email, password });
    this.setToken(res.accessToken);
    return res;
  },
  async register(tenantName: string, fullName: string, email: string, password: string): Promise<AuthResponse> {
    const res = await json<AuthResponse>('POST', '/api/auth/register', { tenantName, fullName, email, password });
    this.setToken(res.accessToken);
    return res;
  },
  me: () => json<UserDto>('GET', '/api/auth/me'),

  dashboard: () => json<DashboardDto>('GET', '/api/dashboard'),

  listHomes: () => json<HomeDto[]>('GET', '/api/homes'),
  getHome: (homeId: string) => json<HomeDto>('GET', `/api/homes/${homeId}`),
  createHome: (name: string, address: string | null) =>
    json<HomeDto>('POST', '/api/homes', { name, address }),
  updateHome: (homeId: string, name: string, address: string | null) =>
    json<HomeDto>('PUT', `/api/homes/${homeId}`, { name, address }),
  deleteHome: (homeId: string) => json<void>('DELETE', `/api/homes/${homeId}`),

  listCameras: (homeId: string) => json<CameraDto[]>('GET', `/api/homes/${homeId}/cameras`),
  createCamera: (homeId: string, body: UpsertCameraRequest) =>
    json<CameraDto>('POST', `/api/homes/${homeId}/cameras`, body),
  updateCamera: (cameraId: string, body: UpsertCameraRequest) =>
    json<CameraDto>('PUT', `/api/cameras/${cameraId}`, body),
  deleteCamera: (cameraId: string) => json<void>('DELETE', `/api/cameras/${cameraId}`),

  listAttendance: (params: {
    homeId?: string;
    from?: string;
    to?: string;
    personKey?: string;
    page: number;
    pageSize: number;
  }) => json<PagedResult<AttendanceDto>>('GET', '/api/attendance', undefined, params),
  attendanceSummary: (params: { homeId?: string; from: string; to: string }) =>
    json<DailyAttendanceSummaryDto[]>('GET', '/api/attendance/summary', undefined, params),
  exportAttendanceCsv: (params: { homeId?: string; from?: string; to?: string }) =>
    blob('/api/attendance/export', params),
  snapshot: (recordId: string, kind: 'entry' | 'exit') =>
    blob(`/api/attendance/${recordId}/snapshots/${kind}`),

  listPersons: () => json<PersonDto[]>('GET', '/api/persons'),
  deletePerson: (personId: string) => json<void>('DELETE', `/api/persons/${personId}`),

  listAuditLogs: (page: number, pageSize: number) =>
    json<PagedResult<AuditLogDto>>('GET', '/api/audit-logs', undefined, { page, pageSize }),
};
