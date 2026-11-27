# AI Attendance and Visitor Tracking Platform

A multi-tenant SaaS platform that counts people entering and leaving homes using RTSP camera streams. It has two independent applications:

1. **Admin Portal**: ASP.NET Core 9 Web API, React + TypeScript + Material UI frontend, PostgreSQL, and Redis.
2. **Monitoring Engine**: Python service that reads RTSP streams, detects faces with YOLO, tracks them with ByteTrack, recognizes enrolled people with InsightFace, and posts entry and exit events to the Admin API.

## Architecture

```
 RTSP cameras ──► Monitoring Engine (Python) ──X-Service-Key──► Admin API (.NET 9) ──► PostgreSQL
                    YOLO · ByteTrack · InsightFace              │       └──► Redis (cache)
                                                                 │       └──► Snapshots (local or S3)
 Browser ──► web (nginx + React) ──/api/──────────────────────────┘
```

- **Tenant isolation**: every table is scoped by tenant through EF Core global query filters. Cross-tenant lookups return `404`.
- **Auth**: JWT Bearer for users (roles `Admin` and `Viewer`). Shared `X-Service-Key` for the engine.
- **Audit logging**: write operations are recorded and viewable by admins.
- **Retention**: a background job deletes snapshots and attendance records older than 7 days (configurable).
- **Security**: RTSP URLs are encrypted at rest and masked in responses. Auth endpoints are rate-limited. Forwarded headers are trusted only from configured proxies.

## Folder structure

```
.
├── backend/                      # .NET 9 Admin API
│   ├── src/SurveillancePlatform.Api/
│   │   ├── Background/           # Retention job
│   │   ├── Controllers/          # Public and internal endpoints
│   │   ├── Services/             # Auth, homes, cameras, attendance, dashboard
│   │   ├── Storage/              # Local and S3 snapshot storage
│   │   ├── Data/                 # EF Core context and migrations
│   │   └── Program.cs
│   ├── tests/SurveillancePlatform.Tests/
│   └── Dockerfile
├── frontend/                     # React + TypeScript + MUI
│   ├── src/                      # Pages, typed API client, auth context
│   ├── nginx.conf                # Serves the SPA, proxies /api/
│   └── Dockerfile
├── monitoring/                   # Python Monitoring Engine
│   ├── engine/                   # Config, camera workers, detector, tracker, matcher
│   ├── tests/                    # pytest suite
│   ├── enroll.py                 # Enroll a person from a photo
│   ├── requirements*.txt
│   ├── Dockerfile
│   └── README.md
├── docs/
│   ├── API.md                    # Endpoint reference
│   └── DEPLOYMENT.md             # Deployment and operations
├── .github/workflows/ci.yml      # CI pipeline
├── docker-compose.yml
└── .env.example
```

## Quick start

```bash
cp .env.example .env              # then set POSTGRES_PASSWORD, JWT_KEY, SERVICE_API_KEY
mkdir -p models && cp /path/to/yolo11-face.pt models/
docker compose up -d --build
```

Open `http://localhost:8080`, register a tenant, add a home and cameras, then enroll people:

```bash
cd monitoring
python enroll.py --api http://localhost:8080 --email admin@example.com --name "Jane Doe" --image jane.jpg
```

### Important: face weights

Stock Ultralytics `yolo11*.pt` weights detect **persons**, not faces. The engine needs YOLO weights trained for face detection. This project does not ship them. See [monitoring/README.md](monitoring/README.md#face-weights-required).

## Local development

| Component | Command |
|---|---|
| Backend | `cd backend && dotnet run --project src/SurveillancePlatform.Api` |
| Backend tests | `cd backend && dotnet test SurveillancePlatform.slnx` |
| Frontend | `cd frontend && npm ci && npm run dev` |
| Frontend build | `cd frontend && npm run build` |
| Monitoring tests | `cd monitoring && pip install -r requirements-dev.txt && pytest` |

Local .NET runs on hosts without the .NET 9 runtime can set `DOTNET_ROLL_FORWARD=Major`.

## Documentation

- [docs/API.md](docs/API.md): endpoints, authentication, and Swagger setup.
- [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md): configuration, deployment steps, scaling, hardening, and CI/CD.
- [monitoring/README.md](monitoring/README.md): engine configuration and face-weights requirements.

## CI/CD

[.github/workflows/ci.yml](.github/workflows/ci.yml) runs backend build and tests, engine tests, frontend build, compose validation, and image builds. Pushes to `main` publish images to GHCR.
