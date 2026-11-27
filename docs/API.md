# API Reference

The Admin API is ASP.NET Core 9 with JSON in and out. Enums are strings (`Admin`, `Viewer`, `Active`, `Inactive`).

## Interactive documentation

Swagger UI is off by default. Enable it with `SWAGGER_ENABLED=true` in `.env`, then open:

- `http://<host>:<port>/swagger`: Admin API (`v1`).
- Monitoring Engine (`internal`) endpoints appear as a second definition in the same UI.

Swagger is configured in [Program.cs](/D:/Project/surveillance-system/backend/src/SurveillancePlatform.Api/Program.cs). It defines two schemes:

| Scheme | Where | Used by |
|---|---|---|
| `Bearer` (JWT) | `Authorization: Bearer <token>` | Web app and admin clients |
| `X-Service-Key` | Header | Monitoring Engine (`/api/internal/*`) |

## Authentication

Every endpoint needs a Bearer token, except `register`, `login`, and the health checks. Roles are `Admin` and `Viewer`. Write operations on homes, cameras, persons, and the audit log are Admin only.

| Method | Path | Body | Notes |
|---|---|---|---|
| POST | `/api/auth/register` | `tenantName`, `fullName`, `email`, `password` (min 10 chars) | Creates a tenant and its first user, who is an Admin. Rate-limited per IP. |
| POST | `/api/auth/login` | `email`, `password` | Returns `accessToken`, `expiresAt`, and `user`. Rate-limited per IP (10 per minute). |
| GET | `/api/auth/me` | | Current user and tenant. |

Tokens expire after `JWT_EXPIRY_MINUTES` (default 60).

## Homes and cameras

| Method | Path | Notes |
|---|---|---|
| GET | `/api/homes` | Homes for the caller's tenant. |
| POST | `/api/homes` | Admin only. |
| GET | `/api/homes/{homeId}` | |
| PUT | `/api/homes/{homeId}` | Admin only. |
| DELETE | `/api/homes/{homeId}` | Admin only. |
| GET | `/api/homes/{homeId}/cameras` | |
| POST | `/api/homes/{homeId}/cameras` | Admin only. Body: `name`, `rtspUrl`, `location`, `status` (`Active`/`Inactive`), `entryDirection` (`TopToBottom`/`BottomToTop`), `linePosition` (0.05–0.95, required). |
| GET | `/api/cameras/{cameraId}` | Never returns the RTSP URL. |
| PUT | `/api/cameras/{cameraId}` | Admin only. The RTSP URL must be sent again on update. |
| DELETE | `/api/cameras/{cameraId}` | Admin only. |

RTSP URLs are encrypted at rest with ASP.NET Data Protection, and credentials are masked in responses.

Cross-tenant access returns `404`, not `403`, so resource existence is not leaked.

## Dashboard

| Method | Path | Notes |
|---|---|---|
| GET | `/api/dashboard` | Total homes, total cameras, people currently inside, entries today, exits today. Cached for `Cache:DashboardSeconds` (Redis when configured, otherwise in memory) and evicted on ingest. |

## Attendance

| Method | Path | Query parameters |
|---|---|---|
| GET | `/api/attendance` | `homeId`, `from`, `to`, `personKey`, `page` (≥1), `pageSize` (1–200). Newest first. |
| GET | `/api/attendance/summary` | `from`, `to`, and the same filters. Per-day entry and exit counts. |
| GET | `/api/attendance/export` | Same filters as the list. Returns CSV. |
| GET | `/api/attendance/{recordId}/snapshots/{kind}` | `kind` is `entry` or `exit`. Streams the image. |

Each attendance record has a person ID, entry time, exit time (null while the person is still inside), and duration.

## Persons

| Method | Path | Notes |
|---|---|---|
| GET | `/api/persons` | Enrolled persons for the tenant. |
| POST | `/api/persons` | Admin only. Body: `displayName`, `embedding` (float array). Used by [enroll.py](/D:/Project/surveillance-system/monitoring/enroll.py). |
| DELETE | `/api/persons/{personId}` | Admin only. |

## Audit logs

| Method | Path | Notes |
|---|---|---|
| GET | `/api/audit-logs` | Admin only. Tenant-scoped audit trail of changes. |

## Internal (Monitoring Engine)

All require the `X-Service-Key` header.

| Method | Path | Purpose |
|---|---|---|
| GET | `/api/internal/cameras/active` | Active cameras the engine should read. |
| POST | `/api/internal/cameras/{cameraId}/heartbeat` | Camera liveness. |
| GET | `/api/internal/persons/embeddings` | Enrolled face embeddings for matching. |
| POST | `/api/internal/snapshots` | Upload an entry or exit snapshot. Returns the snapshot key. |
| POST | `/api/internal/attendance/events` | Batched entry and exit events. Idempotent. The server never auto-closes an open presence. |

## Health

| Method | Path | Purpose |
|---|---|---|
| GET | `/health/live` | Process is up. |
| GET | `/health/ready` | Database is reachable. Returns `503` otherwise. |

## Errors

Errors use RFC 7807 problem details. Validation errors return `400`. Missing or cross-tenant resources return `404`. Rate-limited auth calls return `429`.
