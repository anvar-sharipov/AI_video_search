# VMS — Enterprise Video Management System

An enterprise-grade Video Management System in C#/.NET: ONVIF/RTSP capture with GPU-accelerated
decoding, a write-protected archive with automated retention, offline AI object detection (YOLO)
searchable via Elasticsearch, a role-gated REST API, and a WPF client with a live camera grid.

Designed to scale to 500+ cameras; the current implementation is a working MVP validated live
against **3 real ONVIF cameras simultaneously** (live grid, independent recording and
detection per camera).

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
| M6 | `VMS.Frontend.WPF` | Desktop client: login, a Control Panel landing screen (dashboard of feature cards — "Live View", "Archive" (camera + date/time browser, distinct from AI-search clips), and, in its sidebar, "Operation Log" are wired up, the rest still placeholders) alongside the Live View screen, live camera grid with a layout switcher (Auto/1x1/2x2/3x3/4x4) and click-to-select tiles, a dedicated button per tile to open it fullscreen at full quality in its own window, a camera tree sidebar and per-tile snapshot/record/playback controls (present as disabled UI scaffolding, not yet wired up), a RU/EN/TM language toggle covering the whole client, AI search, clip popup, protect-from-deletion |

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
scripts/setup-tools.ps1  — one-time ffmpeg download, YOLO ONNX export, and face-search
                           model download (Haar cascade + SFace)
```

### Key architectural decisions

- **Capture**: a supervised `ffmpeg` child process per camera (not `FFmpeg.AutoGen`) — process
  isolation is the "zero-crash" mechanism: one camera's ffmpeg crashing can't affect any other.
- **Live view**: `LibVLCSharp` — hardware-accelerated decode out of the box (NVDEC/D3D11VA, etc.).
- **Dark theme**: the WPF client's colors and default control styles live in one shared
  `Themes/DarkTheme.xaml`.
- **Localization (RU/EN/TM)**: swappable `ResourceDictionary` per language
  (`Localization/Strings.{ru,en,tk}.xaml`) merged into `Application.Resources` by
  `LocalizationService.SetLanguage`, not `.resx`/`CultureInfo` — that would need either an app
  restart or manually re-binding every element, while `{DynamicResource}` updates the whole
  visual tree the instant the merged dictionary changes. A RU/EN/TM toggle sits in both
  `LoginWindow` and `MainWindow`'s top bar. Status/error messages composed in C# use
  `LocalizationService.Get("Key", args...)` and reflect whatever language was active the moment
  the message was generated (not retroactively re-translated on a later language switch).
- **Layout switcher & tile selection**: single-clicking a tile's name selects it (highlighted
  orange border) without changing what's playing — separate from the fullscreen popup, which now
  has its own small "⤢" button per tile. A toolbar above the grid (Auto/1x1/2x2/3x3/4x4) forces
  a fixed NxN `UniformGrid` layout; "Auto" restores the original size-from-item-count behavior.
- **Single-camera expanded view**: clicking a grid tile's small "⤢" button opens that camera at
  main-stream quality in a brand-new borderless window with its own `MediaPlayer`/`VideoView`,
  rather than swapping the stream on the grid tile's existing video surface in place. Reusing an
  already-rendering VideoView for a different-resolution stream left some cameras' Direct3D11
  output rendering solid white with no error reported anywhere (LibVLC itself reported normal
  playback); a fresh window/player for every expand avoids that outright. The window is
  positioned and sized to exactly cover the camera-grid area (not the whole screen) so the top
  bar and the search side panel both stay visible while a camera is expanded.
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
- **Server-crash resilience**: ffmpeg child processes are assigned to a Windows Job Object with
  kill-on-close — if the backend dies any way at all (crash, `Stop-Process -Force`, Task
  Manager), the OS kills every camera's ffmpeg process automatically instead of leaving them
  orphaned and eating into that camera's limited concurrent-RTSP-connection budget.
- **WPF client resilience**: an unhandled exception on the UI thread (e.g. one camera's stream
  failing) shows an error dialog and writes `crash.log`, instead of taking the whole app down.

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

# 2. One-time: download ffmpeg, export the YOLO model to ONNX, and fetch the
#    face-search models (Haar cascade detector + SFace embedder)
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
| POST | `/api/search/by-face` | Operator+ | Search the archive by an uploaded reference photo — finds every camera/timestamp the matching face was detected at |
| POST | `/api/clips` | Operator+ | Extract a clip around a detection |
| GET | `/api/clips/{file}` | Operator+ | Download/stream the clip |
| POST | `/api/archive/protect` | Admin+ | Write-protect an archive file |
| DELETE | `/api/archive` | Admin+ / SuperAdmin (for protected files) | Delete an archive file |
| POST | `/api/admin/backfill-face-embeddings` | Admin+ | One-off: derives face embeddings for "person" detections indexed before search-by-photo existed (no UI button — see Known limitations) |
| GET | `/api/audit-log?userId=&from=&to=` | Admin+ | Read the append-only audit trail (who did what, when) |
| GET | `/api/archive/coverage?cameraId=&date=` | Viewer+ | Which time ranges actually have footage for a camera on a given day |

## Roles (RBAC)

`Viewer < Operator < Admin < SuperAdmin` — one rule set (`AccessControlManager`) is used by
both the server and the WPF client (to gate UI elements), so behavior never drifts between them.

- **Viewer** — view live/archive, search
- **Operator** — + export clips, search the archive by an uploaded photo (tracks a specific
  person's movements — more sensitive than a plain keyword search, so it's gated one tier up)
- **Admin** — + manage cameras, delete standard archive files, protect a search result's file
  from deletion (the "Protect" button next to each search result in the WPF client), run the
  face-embedding backfill
- **SuperAdmin** — + delete protected (immutable) archive files

## Known limitations of this MVP

- The CUDA execution provider for YOLO isn't installed (the ~600MB NVIDIA runtime download
  repeatedly failed on this network) — detection runs on CPU. Swapping in GPU is a one-line change.
- No dedicated user-management API yet — users are created directly in the database.
- Camera ONVIF credentials are stored in plaintext in the database (needs DPAPI/secret-store
  encryption before production use).
- No thumbnail images in search results.
- The WPF client has a "Protect" button (Admin+) but no "Delete archive file" button yet —
  deletion is only available directly via the API (`DELETE /api/archive`).
- Live View's bottom toolbar — Stop All, Snapshot, Manual Record, Pause/Play, and Fullscreen —
  is wired up (all act on the selected tile except Stop All, which stops every tile at once);
  snapshots save under `Pictures\VMS Snapshots`, recordings under `Videos\VMS Recordings`. The
  archive-fragment step buttons (⏮/⏭) and the camera tree sidebar are still UI scaffolding only
  (`IsEnabled="False"`).
- Validated against 3 cameras simultaneously (2×2 live grid, independent recording/detection).
  Components are designed for multi-camera operation (process-per-camera isolation, per-camera
  retention policies), but no load testing has been done at tens/hundreds of cameras.
- Search-by-photo (`POST /api/search/by-face`) uses a Haar cascade for face detection and
  SFace (ONNX, opencv_zoo) for the embedding — no 5-point-landmark alignment step (that would
  need the newer YuNet detector, which OpenCvSharp doesn't currently wrap), so matches are
  noticeably less reliable for off-angle/profile faces than a full YuNet+SFace pipeline.
- A "person" detection only gets a face embedding if a face was actually found inside its
  bounding box — a person facing away from the camera, or too small/blurry in frame, is
  detected as "person" (searchable by that keyword) but won't be findable by photo.
- The Archive browser (camera + date/time playback) loads footage in fixed 15-minute
  windows via the same `POST /api/clips` extraction the AI-search clip popup uses —
  no true scrubber/seek-bar, just "◀ 15 min" / "15 min ▶" to step through the day, and
  each step re-extracts (`ffmpeg -c copy`, fast but not instant) rather than streaming
  the raw segment files directly.
- Detections indexed before search-by-photo shipped have no face embedding until
  `POST /api/admin/backfill-face-embeddings` (Admin+, no UI button — trigger manually) has been
  run; it re-derives a frame from the archived video per detection, so it also can't recover
  detections whose archive segment the retention policy already deleted.

## Tests

```powershell
dotnet test VMS.Core.Tests
```
