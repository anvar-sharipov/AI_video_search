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
| M4 | `VMS.MetadataIndexer` | Offline object detection (YOLOv8 / ONNX Runtime), Elasticsearch indexing and search, face embedding + known-person name matching, best-effort license-plate OCR, per-person presence tracking with saved screenshots (`PersonSightingEvent`) for the People Seen Report |
| M5 | `VMS.Backend.Server` | ASP.NET Core Web API (JWT), DB-driven camera orchestration, REST endpoints |
| M6 | `VMS.Frontend.WPF` | Desktop client: login, a Control Panel landing screen (dashboard of feature cards — "Live View", "Archive" (camera + date/time browser, distinct from AI-search clips), "Device Management" (Admin+ — list/add/edit/delete cameras, plus an "Online Device" panel that finds ONVIF cameras and NVR/DVRs on the LAN via WS-Discovery so adding one only needs a name/username/password), and, in its sidebar, "Operation Log" are wired up, the rest still placeholders) alongside the Live View screen, live camera grid with a layout switcher (Auto/1x1/2x2/3x3/4x4) and click-to-select tiles, a dedicated button per tile to open it fullscreen at full quality in its own window, a fully wired camera tree sidebar (search, click-to-highlight, double-click-to-assign into a fixed-layout grid cell) and per-tile snapshot/record/playback controls, a RU/EN/TM language toggle covering the whole client, AI search by keyword/name/plate with a results dropdown anchored to the search box, search-by-photo with known-person enrollment, clip popup, protect-from-deletion, a live known(green, named)/unknown(red) overlay on the Live View and Video Wall grid tiles, and a People Seen Report window (per-camera/day total/known/unknown counts, with a double-click drill-down into that day's saved screenshots — each deletable individually, Operator+, with confirmation — and a "Download" button that exports every currently-loaded screenshot to a chosen folder, split into `Known/<name>/` and `Unknown/` subfolders) |

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
- **Light/Dark theme**: the WPF client's colors and default control styles live in two parallel
  dictionaries, `Themes/DarkTheme.xaml` (default) and `Themes/LightTheme.xaml`, sharing the exact
  same brush/style key set — only the color values differ. A ☀/🌙 toggle in `LoginWindow` and
  `MainWindow`'s top bar (`ThemeService.ToggleTheme`) swaps which one is merged into
  `Application.Resources`, the same swappable-`ResourceDictionary` technique as the language
  toggle below. Unlike a plain string resource, most of a theme's colors reach the screen through
  a `Style` object (implicit `TargetType` styles, `ComboBox`/`DatePicker`'s custom
  `ControlTemplate`s, a `Button.Style` with `BasedOn`) rather than a direct
  `{DynamicResource}` binding, and WPF resolves `BasedOn` and captures a `ControlTemplate` once,
  at the moment that particular element was constructed — so swapping the dictionary alone
  doesn't retroactively repaint them. `App.OnThemeChanged` recreates the current top-level window
  (Login or Main) against the same, still-live ViewModel right after the swap (no re-login, no
  camera reload) purely so its XAML re-resolves fresh against the newly active dictionary. Known
  limitation: an already-open secondary window (Archive, User Management, Video Wall, ...) keeps
  whatever theme was active when it was opened, and only picks up the new one the next time it's
  opened.
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

# 2. One-time: download ffmpeg, export the YOLO model to ONNX, fetch the
#    face-search models (Haar cascade detector + SFace embedder), and fetch
#    Tesseract's English language data (best-effort plate OCR)
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
| PUT | `/api/cameras/{code}` | Admin+ | Update a camera (name, IP, ONVIF port, credentials, enabled flag) — restarts its capture pipeline |
| DELETE | `/api/cameras/{code}` | Admin+ | Remove a camera |
| GET | `/api/cameras/{code}/status` | Viewer+ | Pipeline status |
| GET | `/api/cameras/discover` | Admin+ | ONVIF WS-Discovery scan of the local network; returns devices not already in the camera list |
| POST | `/api/cameras/discover/add` | Admin+ | Add a discovered device by IP + credentials only — auto-detects and adds every channel if it's a multi-channel NVR/DVR |
| GET | `/api/search?q=&cameraId=&from=&to=&size=&personName=` | Viewer+ | Search detections — closed-vocabulary color/type keywords ("red car", "person") plus free text matched against an enrolled person's name or a detected plate number ("Alice", "AB1234CD"); `personName` is an additional exact filter (used by the Playback screen's Named Person search tab, picked from the enrolled list, rather than relying on `q`'s fuzzy match) |
| POST | `/api/search/by-face?cropX=&cropY=&cropWidth=&cropHeight=` | Operator+ | Search the archive by an uploaded reference photo — finds every camera/timestamp the matching face was detected at, ranked by a real cosine-similarity score (returned as `matchScore`, 0..1). The optional crop rectangle (pixels, in the uploaded photo's own coordinates) narrows the search to one region of the photo — used by Playback's "crop from player" flow, where the photo is a full-frame snapshot rather than a dedicated reference photo |
| GET | `/api/events/{id}/thumbnail` | Viewer+ | A cropped-to-bounding-box still frame for one search result, generated on demand from the archive (ffmpeg frame extract + crop, not a stored thumbnail) — powers the Playback screen's search-results grid |
| GET | `/api/users` | Admin+ | List user accounts (password hash never included) |
| POST | `/api/users` | Admin+ | Create a user account, optionally with `additionalPermissions` (individual functions granted on top of the Role — see Roles/RBAC below) — creating a SuperAdmin account requires the caller to already be SuperAdmin, and a caller can only grant a permission they themselves currently hold |
| PUT | `/api/users/{id}` | Admin+ | Update a user's role/active flag/`additionalPermissions`, optionally reset their password — blocked from changing your own role/active flag, and from demoting/deactivating/deleting the last remaining SuperAdmin |
| DELETE | `/api/users/{id}` | Admin+ | Delete a user account — same self-service and last-SuperAdmin guards as PUT |
| GET | `/api/camera-groups` | Viewer+ | List camera groups ("screens") — named, ordered camera subsets for the Live View grid |
| POST | `/api/camera-groups` | Admin+ | Create a camera group (name + ordered camera id list) |
| PUT | `/api/camera-groups/{id}` | Admin+ | Rename a group and/or replace its full member list |
| DELETE | `/api/camera-groups/{id}` | Admin+ | Delete a camera group |
| GET | `/api/video-wall-layouts` | Viewer+ | List saved Video Wall layouts (name, row/column grid size, and each cell's position/size/assigned camera) |
| GET | `/api/video-wall-layouts/{id}` | Viewer+ | Fetch one layout with its full cell list |
| POST | `/api/video-wall-layouts` | Admin+ | Create a layout (name, row/column count, initial cell list) |
| PUT | `/api/video-wall-layouts/{id}` | Admin+ | Rename a layout and/or replace its full cell list |
| DELETE | `/api/video-wall-layouts/{id}` | Admin+ | Delete a layout |
| GET | `/api/known-persons` | Operator+ | List enrolled known persons |
| POST | `/api/known-persons` | Operator+ | Enroll a person (name + reference photo) — future matching face detections get tagged with this name, making them findable by `GET /api/search?q=<name>` |
| DELETE | `/api/known-persons/{id}` | Operator+ | Remove an enrolled person |
| POST | `/api/clips` | Operator+ | Extract a clip around a detection |
| GET | `/api/clips/{file}` | Operator+ | Download/stream the clip |
| POST | `/api/archive/protect` | Admin+ | Write-protect an archive file |
| DELETE | `/api/archive` | Admin+ / SuperAdmin (for protected files) | Delete an archive file |
| POST | `/api/admin/backfill-face-embeddings` | Admin+ | One-off: derives face embeddings for "person" detections indexed before search-by-photo existed (no UI button — see Known limitations) |
| GET | `/api/audit-log?userId=&from=&to=` | Admin+ | Read the append-only audit trail (who did what, when) |
| GET | `/api/archive/coverage?cameraId=&date=` | Viewer+ | Which time ranges actually have footage for a camera on a given day |
| GET | `/api/cameras/{code}/detections` | Viewer+ | The last ~3 seconds of "person" detections for one camera, bounding boxes normalized to fractional (0..1) coordinates — powers the Live View / Video Wall grid's live known (green, named)/unknown (red) overlay |
| GET | `/api/person-sightings/report?cameraIds=&from=&to=` | Viewer+ | Per camera, per calendar day: total/known/unknown "new person seen in frame" counts over a date range — the People Seen Report table |
| GET | `/api/person-sightings?cameraId=&from=&to=` | Viewer+ | Individual sightings for one camera/date range, most recent first — the report's drill-down thumbnail gallery |
| GET | `/api/person-sightings/{id}/thumbnail` | Viewer+ | The saved screenshot for one sighting |
| DELETE | `/api/person-sightings/{id}` | Operator+ | Delete one sighting — removes its Elasticsearch document and its saved screenshot file |

## Roles (RBAC)

`Guard < Viewer < Operator < Admin < SuperAdmin` — one rule set (`AccessControlManager`) is used
by both the server and the WPF client (to gate UI elements), so behavior never drifts between
them. `Guard` is `-1` in the `UserRole` enum rather than everyone else being renumbered up: Role
is persisted as a plain integer column, so inserting below the existing values keeps every
already-stored user row meaning exactly what it meant before.

- **Guard** — live camera video only (`Permission.ViewLiveStream`) — no search, no archive, no
  export. A monitor-wall role for someone watching cameras, not investigating footage.
- **Viewer** — + view archive, search
- **Operator** — + export clips, search the archive by an uploaded photo, enroll/remove known
  persons (`/api/known-persons`) — all gated one tier up from plain search since they track or
  name a specific person
- **Admin** — + manage cameras, delete standard archive files, protect a search result's file
  from deletion (the "Protect" button next to each search result in the WPF client), run the
  face-embedding backfill, manage user accounts (`/api/users`, the WPF "User Management" screen)
  up to and including Admin — creating/editing/deleting a SuperAdmin account requires the caller
  to already be SuperAdmin
- **SuperAdmin** — + delete protected (immutable) archive files, manage SuperAdmin accounts.

Beyond the five roles above, the WPF "Add/Edit User" screen also shows a checklist of every
individual function (Live view, Archive, Search, Search by photo, Face recognition, Export
clips, Camera management, Retention policy, Delete archive files, System config, User
management) so an Admin+ can grant one specific function to a user without promoting them a
whole tier — e.g. a Viewer who's also allowed to search by photo, without becoming a full
Operator. This is purely additive: it can only add on top of what the Role already grants, never
take anything away, and a caller can only hand out a function they themselves currently hold (an
Operator can't grant Camera management to anyone, since they don't have it either) — enforced
both by the checkbox list itself (functions the caller lacks aren't shown as options at all) and
again server-side in `POST`/`PUT /api/users` (`UserEndpoints.TryResolveAdditionalPermissions`),
so this can never be bypassed by calling the API directly. One exception: deleting protected
(immutable) archive files can never be granted this way, even by a SuperAdmin — it stays tied to
the Role hierarchy alone (`AccessControlManager.NonGrantablePermissions`), no additive-grant
escape hatch for the single most destructive permission in the system.

  User Management also refuses to demote, deactivate, or delete your own account, or the last
  remaining active SuperAdmin — the one screen where a careless click could otherwise lock every
  admin out of the system

## Known limitations of this MVP

- The CUDA execution provider for YOLO isn't installed (the ~600MB NVIDIA runtime download
  repeatedly failed on this network) — detection runs on CPU. Swapping in GPU is a one-line change.
- Camera ONVIF credentials are stored in plaintext in the database (needs DPAPI/secret-store
  encryption before production use).
- The WPF client has a "Protect" button (Admin+) but no "Delete archive file" button yet —
  deletion is only available directly via the API (`DELETE /api/archive`).
- Live View's bottom toolbar — Stop All, Snapshot, Manual Record, Pause/Play, and Fullscreen —
  is wired up (all act on the selected tile except Stop All, which stops every tile at once);
  snapshots save under `Pictures\VMS Snapshots`, recordings under `Videos\VMS Recordings`. The
  archive-fragment step buttons (⏮/⏭) are still UI scaffolding only (`IsEnabled="False"`); the
  camera tree sidebar (search, click-to-highlight, double-click-to-assign into a fixed-layout
  grid cell) is fully wired up, as is the grid-size dropdown (Auto, 1x1..8x8, replacing an
  earlier row of fixed-size buttons). The left icon rail is: camera groups management (create a
  named, ordered subset of cameras — e.g. "cameras 1-5" as one group, "6-20" as another —
  `/api/camera-groups`), quick-switch between a saved group or "all cameras" (applies to the
  Auto-layout grid), and a toggle that filters the grid down to cameras with a recent (last 5
  minutes), unacknowledged alarm — "alarm" here means a person detection with no matched
  enrolled name, or any vehicle-class detection (this system has no known/unknown-vehicle
  registry beyond plate OCR, so every vehicle counts as alarm-worthy rather than inventing a
  distinction that doesn't exist). The Archive/Playback shortcut this icon used to be is still
  reachable from the Control Panel's Playback card.
- Video Wall (`/api/video-wall-layouts`) opens on a chosen monitor either in "Mirror Live View"
  mode (the original behavior — full-screen copy of whatever Live View's own grid currently
  shows) or as a named, saved layout: a Rows x Columns grid of cells, each pinned to one camera
  and independently sized by merging adjacent cells (Excel-style cell merge) in the layout
  editor (opened via the launcher's "Manage layouts..." button). Merging only absorbs one whole
  adjacent cell of matching height/width at a time — no L-shaped/partial merges — and changing a
  layout's row/column count after cells already exist resets the whole grid back to plain 1x1
  cells (with a confirmation prompt), discarding any merges and camera assignments made so far.
- Plate reading (`plateNumber`, searchable via `GET /api/search?q=...`) has no dedicated
  plate-detector model — it approximates the plate's location inside a vehicle's YOLO bounding
  box with a plain contour heuristic and reads whatever it finds with Tesseract OCR. This is
  meaningfully less accurate than a real ANPR pipeline: expect frequent misses and some garbled
  reads, especially at an angle, at night, or on a small/distant vehicle. Treat plate search as
  "surfaces plausible candidates," not a reliable exact match.
- Known-person matching (`POST /api/known-persons`, tags future "person" detections with a
  name) inherits search-by-photo's Haar-cascade-detection caveat above, but now aligns the face
  before embedding: a second Haar cascade (`haarcascade_eye.xml`) finds both eyes and rotates
  the crop level (`OnnxFaceEmbedder.AlignByEyes`) — a cheaper 2-point alignment than full YuNet
  5-point landmarks, but it directly corrects head-tilt, the dominant real-world source of
  embedding drift for a front-facing CCTV camera; it falls back to an unaligned crop when it
  can't find two plausible eyes (profile view, sunglasses, low resolution). Matching itself uses
  a fixed cosine-similarity threshold (0.363, SFace's published same-identity cutoff) *and* now
  requires the winning name to beat the runner-up by a confidence margin (0.05) — reduces two
  enrolled look-alikes flipping a match on a near-tie, at the cost of occasionally returning no
  match instead of a close guess. Enrolling the same name multiple times with different
  reference photos already works (each is a separate `KnownPerson` row; matching takes the
  best-scoring photo per name) — the WPF enrollment dialog now says so, since an off-angle
  single enrollment photo failing to match its own later detections was the single biggest
  real-world accuracy gap.
- The People Seen Report ("count every person seen + remember with a screenshot",
  `PersonPresenceTracker`) counts a *new track*, not a unique physical person — it's the same
  nearest-centroid matching between consecutive AI snapshots (not continuous video) as People
  Counting's line-crossing tracker, so someone who steps briefly out of frame and back can be
  counted twice, and two people crossing paths closely can be miscounted. The report itself runs
  one Elasticsearch query per camera per day rather than a single aggregation (this codebase's
  existing pattern for count queries — see `GetPeopleCountAsync`), which is fine for a handful of
  cameras over a normal reporting window but gets slow across many cameras and a long date range.
  The live known/unknown grid overlay polls once every ~1.5s per visible tile; it's a display aid,
  not a security-grade recognition feed.
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
- The Archive/Playback browser (camera + date/time playback, plus an integrated AI search
  drawer: Named Person / Attributes / Reverse Image tabs, a visual results grid, and a
  day timeline with purple/gold person/vehicle event ticks) loads footage in fixed
  15-minute windows via the same `POST /api/clips` extraction the AI-search clip popup
  uses — no true scrubber/seek-bar, just "◀ 15 min" / "15 min ▶" to step through the day
  (double-clicking a search-result card jumps straight to that moment instead), and each
  step re-extracts (`ffmpeg -c copy`, fast but not instant) rather than streaming the raw
  segment files directly. Playback speed (1x/2x/4x/16x), single-frame step (while paused),
  and snapshot-to-disk are real LibVLC transport controls, not stubs.
- The Attribute Search tab intentionally offers only filters the detection pipeline can
  actually back: object type, color, license plate, and enrolled person name. Gender, age,
  glasses/mask, and a vehicle sub-type (sedan/SUV/etc.) are not offered at all — YOLO plus
  the color tagger cannot classify any of those, and a disabled/greyed-out control would
  misrepresent what the system can do. For the same reason, the timeline only marks person
  and vehicle search hits (purple/gold); there is no separate "motion" signal to show a red
  tick for, since detection *is* the motion signal in this system.
- Editing a camera (`PUT /api/cameras/{code}`) always restarts its capture/ONVIF pipeline —
  even a name-only change causes a few seconds of downtime for that one camera — there's no
  field-diffing reconnect logic. A camera's `Code` is immutable once created; to change it,
  delete and re-add the camera (archive files and Elasticsearch detections are keyed by `Code`,
  not the internal database `Id`, so this is safe).
- ONVIF WS-Discovery (`GET /api/cameras/discover`) only finds devices on the same L2/broadcast
  network as the server — it cannot discover devices across routed subnets (a standard
  multicast limitation, not a bug). It also assumes the device service lives at the
  conventional `/onvif/device_service` path, and a device's channel count is only known
  after connecting with valid credentials — the Probe itself can't tell whether something
  is a plain IP camera or a multi-channel NVR/DVR in advance. Each NVR/DVR channel is added
  as its own independent `Camera` row (same IP, different code) — there's no "parent device"
  grouping in the UI.
- Detections indexed before search-by-photo shipped have no face embedding until
  `POST /api/admin/backfill-face-embeddings` (Admin+, no UI button — trigger manually) has been
  run; it re-derives a frame from the archived video per detection, so it also can't recover
  detections whose archive segment the retention policy already deleted.

## Tests

```powershell
dotnet test VMS.Core.Tests
```
