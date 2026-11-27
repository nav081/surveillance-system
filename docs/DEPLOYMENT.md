# Deployment Guide

This guide covers running the full platform on a single Docker host with `docker compose`, then the production hardening steps.

## 1. Prerequisites

- Docker Engine 24+ with the Compose v2 plugin (`docker compose version`).
- For GPU inference: an NVIDIA driver and the NVIDIA Container Toolkit, plus `INFERENCE_DEVICE=cuda:0` and a CUDA build of the engine dependencies.
- A DNS name and a TLS-terminating load balancer or reverse proxy in front of the `web` service for production.
- Face-trained YOLO weights (see [monitoring/README.md](/D:/Project/surveillance-system/monitoring/README.md#face-weights-required)). This project does not ship them.

## 2. Configure

```bash
cp .env.example .env
mkdir -p models
# Copy your face-trained YOLO weights to models/yolo11-face.pt
```

Edit `.env` and set real values for:

| Variable | Purpose |
|---|---|
| `POSTGRES_PASSWORD` | Database password. Required. |
| `JWT_KEY` | Signing key, at least 32 characters. Required. Rotating it invalidates all sessions. |
| `SERVICE_API_KEY` | Shared secret between the API and the engine. Required. |
| `PUBLIC_WEB_ORIGIN` | Public origin of the web app, used for CORS. |
| `SWAGGER_ENABLED` | Keep `false` in production. |

Generate secrets with:

```bash
openssl rand -base64 48   # JWT_KEY
openssl rand -hex 32      # SERVICE_API_KEY
```

`docker compose` refuses to start if a required secret is missing.

## 3. Start

```bash
docker compose up -d --build
docker compose ps
```

Services:

| Service | Port | Notes |
|---|---|---|
| `web` | `WEB_PORT` (default 8080) | React app. nginx proxies `/api/` to the API. |
| `api` | internal only | ASP.NET Core Admin API. Migrations run on startup (`Database__AutoMigrate=true`). |
| `postgres` | internal only | PostgreSQL 16. Data in the `postgres-data` volume. |
| `redis` | internal only | Dashboard cache. Data in the `redis-data` volume. |
| `monitoring` | none | Monitoring Engine. Pulls active cameras from the API. |

Verify:

```bash
docker compose exec web wget -qO- http://api:8080/health/ready   # nginx only proxies /api/, so probe the API directly
docker compose logs -f api monitoring
```

Open `http://localhost:8080`, register a tenant, add a home and cameras, then enroll people with `monitoring/enroll.py`.

## 4. Network and trusted proxies

The `app` network is fixed at `172.28.0.0/16`. The API trusts `X-Forwarded-For` only from that subnet (`Proxy__TrustedProxies__0`). This keeps per-client rate limiting working behind nginx. If you change the subnet, change `Proxy__TrustedProxies__0` in [docker-compose.yml](/D:/Project/surveillance-system/docker-compose.yml) too.

If you add another proxy in front of `web`, its IP or CIDR must be trusted by the API as well, or clients will share one rate-limit bucket.

## 5. Data retention

Retention runs inside the API (`RetentionService`). Every `Retention:IntervalMinutes` it deletes snapshots and attendance records older than `RETENTION_DAYS` (default 7). Nothing else needs to be scheduled.

## 6. Scaling the Monitoring Engine

Each engine process owns a shard of cameras. A camera is assigned by `crc32(cameraId) % ENGINE_SHARD_COUNT`.

To run more than one shard on one host, copy the `monitoring` service in `docker-compose.yml` and set a different `ENGINE_SHARD_INDEX` on each, with `ENGINE_SHARD_COUNT` set to the total. Give each shard enough CPU or GPU for its cameras. Models are not shared across shards in a single process.

## 7. Production hardening checklist

- [ ] Terminate TLS in front of `web` (for example a load balancer, Caddy, or an nginx TLS listener). Set `PUBLIC_WEB_ORIGIN` to the `https://` origin.
- [ ] Use secrets from your secret manager instead of a plain `.env` file on disk.
- [ ] Restrict the host firewall: expose only the web port, not PostgreSQL or Redis.
- [ ] Back up the `postgres-data` volume and the `dataprotection-keys` volume. Losing the keys makes stored RTSP URLs unreadable.
- [ ] If you store snapshots in S3 instead of the local `snapshots` volume, set `Storage__Provider=S3`, `Storage__S3__Bucket`, `Storage__S3__Region`, and provide AWS credentials through the standard environment or instance role. Remove the local volume mount if unused.
- [ ] Keep `SWAGGER_ENABLED=false`.
- [ ] Review the audit log regularly (`/api/audit-logs`, Admin only).

## 8. Upgrades

```bash
git pull
docker compose build
docker compose up -d
```

Database migrations run automatically at API startup. Take a database backup before upgrading.

## 9. CI/CD

[.github/workflows/ci.yml](/D:/Project/surveillance-system/.github/workflows/ci.yml) runs on every push and pull request:

1. Backend: restore, build, and test with the .NET 9 SDK.
2. Monitoring Engine: install dev requirements and run pytest.
3. Frontend: `npm ci` and `npm run build` (type-check and bundle).
4. Compose: render the config from `.env.example`.
5. Images: build the backend, frontend, and monitoring images. On pushes to `main`, push them to GHCR as `ghcr.io/<owner>/<repo>/<service>:<sha>`.

Deployment is intentionally left to your environment. Pull the tagged images on the host, set `.env`, and run `docker compose up -d` with the image names overridden to the GHCR tags.

## 10. Troubleshooting

| Symptom | Check |
|---|---|
| `docker compose up` exits immediately with a "required" error | A required variable is missing from `.env`. |
| API logs show `Jwt:Key must be set` | `JWT_KEY` is shorter than 32 characters. |
| Engine restarts with a weights error | `models/yolo11-face.pt` is missing or not face-trained. |
| All users hit `429` at once | The proxy subnet is not trusted. Check `Proxy__TrustedProxies__0`. |
| Dashboard numbers are stale | Redis is down. The API falls back to in-memory cache only when Redis is not configured. |
