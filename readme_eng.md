# VMS — Enterprise Video Management System

An enterprise-grade Video Management System in C#/.NET: ONVIF/RTSP capture with GPU-accelerated
decoding, a write-protected archive with automated retention, offline AI object detection (YOLO)
searchable via Elasticsearch, a role-gated REST API, and a WPF client with a live camera grid.

Designed to scale to 500+ cameras; the current implementation is a working MVP validated live
against **one real ONVIF camera**.

## Status

All 6 milestones are implemented and verified live (against the real camera, real
Elasticsearch/Postgres, and the real WPF client — not mocks):

| Milestone | Module | What it does |
|---|---|---|
| M1 | `VMS.Core` | Domain models, RBAC (`AccessControlManager`), auth (BCrypt), append-only audit log, EF Core / PostgreSQL |
| M2 | `VMS.MediaEngine` | ONVIF discovery (WS-Security), RTSP segment recording via ffmpeg, auto-reconnect on connection drop |
| M3 | `VMS.StorageEngine` | Retention (auto-purge of expired footage), delete-protection via Windows ACL, clip extraction |
| M4 | `VMS.MetadataIndexer` | Offline object detection (YOLOv8 / ONNX Runtime), Elasticsearch indexing and search |
| M5 | `VMS.Backend.Server` | ASP.NET Core Web API (JWT), DB-driven camera orchestration, REST endpoints |
| M6 | `VMS.Frontend.WPF` | Desktop client: login, live camera grid, AI search, clip popup |

## Architecture

```
VMS.Core/               — domain models, RBAC, auth, EF Core DbContext
VMS.MediaEngine/         — ONVIF client, stream recording, AI snapshot sampling
VMS.StorageEngine/       — retention, immutable archive, clip extraction
VMS.MetadataIndexer/     — YOLO detection, Elasticsearch indexing/search
VMS.Backend.Server/      — Worker Service + REST API (ASP.NET Core Minimal API)
VMS.Frontend.WPF/        — WPF client (MVVM, LibVLCSharp)
VMS.Core.Tests/          — unit tests (RBAC, auth)
docker-compose.yml       — PostgreSQL + Elasticsearch for local dev
scripts/setup-tools.ps1  — one-time ffmpeg download + YOLO ONNX export
```

### Key architectural decisions

- **Capture**: a supervised `ffmpeg` child process per camera (not `FFmpeg.AutoGen`) — process
  isolation is the "zero-crash" mechanism: one camera's ffmpeg crashing can't affect any other.
- **Live view**: `LibVLCSharp` — hardware-accelerated decode out of the box (NVDEC/D3D11VA, etc.).
- **ONVIF**: a hand-rolled SOAP client (WS-Security UsernameToken) — most all-in-one ONVIF
  NuGet packages are unmaintained.
- **Archive format**: fragmented MP4 (`-movflags +frag_keyframe+empty_moov+...`) — without it,
  a currently-recording segment can't be opened at all (`moov atom not found`).
- **AI**: YOLOv8 via `YoloDotNet` (ONNX Runtime), fully offline at inference time. Vehicle color
  ("red car") is derived separately by averaging pixels inside the detection's bounding box,
  since COCO has no color classes.
- **Clip auth**: LibVLC can't attach an `Authorization` header, so `/api/clips/{file}` also
  accepts the JWT via `?access_token=` — the same pattern ASP.NET Core uses for SignalR
  over WebSockets.

## Requirements

- .NET SDK 10
- Docker (for PostgreSQL and Elasticsearch)
- Python 3.11+ with `pip` (one-time YOLO ONNX export)
- Windows (WPF client, Windows ACL for the immutable archive)
- NVIDIA GPU — optional, for hardware decode/CUDA

## Quick start

```powershell
# 1. Bring up infrastructure
docker compose up -d

# 2. One-time: download ffmpeg and export the YOLO model to ONNX
.\scripts\setup-tools.ps1

# 3. Apply database migrations
dotnet ef database update --project VMS.Core --startup-project VMS.Core

# 4. Configure a test camera (gitignored, never committed)
#    Create VMS.Backend.Server/appsettings.local.json:
#    {
#      "Vms": {
#        "TestCamera": {
#          "CameraId": "cam-01",
#          "IpAddress": "192.168.1.50",
#          "OnvifPort": 80,
#          "Username": "admin",
#          "Password": "..."
#        }
#      }
#    }

# 5. Run the server
dotnet run --project VMS.Backend.Server --urls http://localhost:5080

# 6. Run the WPF client
dotnet run --project VMS.Frontend.WPF
```

On first run the server seeds a `superadmin` user and logs a one-time password —
use it to log in from the WPF client.

## REST API

Every endpoint except `/api/auth/login` requires a JWT (`Authorization: Bearer ...`).

| Method | Path | Requires | Description |
|---|---|---|---|
| POST | `/api/auth/login` | — | Log in, issues a JWT |
| GET | `/api/cameras` | Viewer+ | List cameras |
| POST | `/api/cameras` | Admin+ | Add a camera (onboards it immediately) |
| DELETE | `/api/cameras/{code}` | Admin+ | Remove a camera |
| GET | `/api/cameras/{code}/status` | Viewer+ | Pipeline status |
| GET | `/api/search?q=...` | Viewer+ | Search detections ("red car", "person") |
| POST | `/api/clips` | Operator+ | Extract a clip around a detection |
| GET | `/api/clips/{file}` | Operator+ | Download/stream the clip |
| POST | `/api/archive/protect` | Admin+ | Write-protect an archive file |
| DELETE | `/api/archive` | Admin+ / SuperAdmin (for protected files) | Delete an archive file |

## Roles (RBAC)

`Viewer < Operator < Admin < SuperAdmin` — one rule set (`AccessControlManager`) is used by
both the server and the WPF client (to gate UI elements), so behavior never drifts between them.

- **Viewer** — view live/archive, search
- **Operator** — + export clips
- **Admin** — + manage cameras, delete standard archive files
- **SuperAdmin** — + delete protected (immutable) archive files

## Known limitations of this MVP

- The CUDA execution provider for YOLO isn't installed (the ~600MB NVIDIA runtime download
  repeatedly failed on this network) — detection runs on CPU. Swapping in GPU is a one-line change.
- No dedicated user-management API yet — users are created directly in the database.
- Camera ONVIF credentials are stored in plaintext in the database (needs DPAPI/secret-store
  encryption before production use).
- No thumbnail images in search results.
- Validated against 1 camera. Components are designed for multi-camera operation
  (process-per-camera isolation, per-camera retention policies), but no load testing has been
  done at tens/hundreds of cameras.

## Tests

```powershell
dotnet test VMS.Core.Tests
```
