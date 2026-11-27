export interface UserDto {
  id: string;
  email: string;
  fullName: string;
  role: 'Admin' | 'Viewer';
  tenantId: string;
  tenantName: string;
}

export interface AuthResponse {
  accessToken: string;
  expiresAt: string;
  user: UserDto;
}

export interface HomeDto {
  id: string;
  name: string;
  address: string | null;
  cameraCount: number;
  onlineCameraCount: number;
  createdAt: string;
}

export type CameraStatus = 'Active' | 'Inactive';
export type EntryDirection = 'TopToBottom' | 'BottomToTop';

export interface CameraDto {
  id: string;
  homeId: string;
  name: string;
  rtspUrlMasked: string;
  location: string | null;
  status: CameraStatus;
  entryDirection: EntryDirection;
  linePosition: number;
  lastHeartbeatAt: string | null;
  isOnline: boolean;
}

export interface UpsertCameraRequest {
  name: string;
  rtspUrl: string;
  location: string | null;
  status: CameraStatus;
  entryDirection: EntryDirection;
  linePosition: number;
}

export interface DashboardDto {
  totalHomes: number;
  totalCameras: number;
  onlineCameras: number;
  peopleCurrentlyInside: number;
  entriesToday: number;
  exitsToday: number;
  generatedAt: string;
}

export interface AttendanceDto {
  id: string;
  homeId: string;
  cameraId: string;
  personKey: string;
  personId: string | null;
  displayName: string;
  entryTime: string;
  exitTime: string | null;
  durationSeconds: number | null;
  hasEntrySnapshot: boolean;
  hasExitSnapshot: boolean;
}

export interface DailyAttendanceSummaryDto {
  date: string;
  entries: number;
  exits: number;
  uniquePeople: number;
  averageDurationSeconds: number;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface PersonDto {
  id: string;
  displayName: string;
  createdAt: string;
}

export interface AuditLogDto {
  id: number;
  tenantId: string | null;
  actor: string;
  action: string;
  entityType: string;
  entityId: string | null;
  changes: string | null;
  ipAddress: string | null;
  timestamp: string;
}
