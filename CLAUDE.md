# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Mandatory rule: keep the READMEs in sync

**Whenever a change adds, removes, or changes user-visible functionality** (a new API endpoint,
a new config option, a new module/milestone, a changed command, a changed limitation) **you must
update both `readme_rus.md` and `readme_eng.md` in the same change** — not just one of them, and
not as a follow-up. Treat this as part of the definition of done, the same way you'd update a
test. Internal refactors with no user-visible effect don't require a README update.

## Mandatory rule: all WPF client text must be localizable (RU/EN/TK)

**Every user-facing string in `VMS.Frontend.WPF`** (window title, label, tooltip, button text,
status message, error message) **must go through the localization system, never a literal in
XAML or C#.** Add the string's key to all three dictionaries —
`VMS.Frontend.WPF/Localization/Strings.ru.xaml`, `Strings.en.xaml`, `Strings.tk.xaml` — with a
real translation in each (Russian, English, Turkmen), then reference it via
`{DynamicResource KeyName}` in XAML or `LocalizationService.Get("KeyName", args...)` in C#. This
is what makes the RU/EN/TM toggle (`LocalizationService.SetLanguage`, in `MainWindow`'s top bar
and `LoginWindow`) actually cover the whole app instead of silently missing new text.

## Commands

```powershell
# Build / test
dotnet build
dotnet test VMS.Core.Tests
dotnet test VMS.Core.Tests --filter "FullyQualifiedName~AccessControlManagerTests"   # single test class
dotnet test VMS.Core.Tests --filter "FullyQualifiedName~Only_SuperAdmin_can_delete_immutable_archives"  # single test

# Infra (Postgres :5433, Elasticsearch :9200 — see docker-compose.yml)
docker compose up -d

# One-time tooling (downloads ffmpeg, exports YOLOv8n to ONNX) — see scripts/setup-tools.ps1
.\scripts\setup-tools.ps1

# EF Core migrations (VMS.Core hosts the DbContext + a design-time factory)
dotnet ef migrations add <Name> --project VMS.Core --startup-project VMS.Core -o Data/Migrations
dotnet ef database update --project VMS.Core --startup-project VMS.Core

# Run
dotnet run --project VMS.Backend.Server --urls http://localhost:5080
dotnet run --project VMS.Frontend.WPF
```

`VMS.Backend.Server/appsettings.local.json` (gitignored) supplies `Vms:TestCamera` — the only
place camera credentials should ever live locally. See the READMEs for its shape.

## Architecture

Six projects, each owning one concern; data flows in one direction through them:

```
ONVIF discovery ──▶ RtspRecorder / SnapshotSampler ──▶ DetectionWorker ──▶ ElasticsearchIndexer
  (MediaEngine)         (MediaEngine)                  (MetadataIndexer)    (MetadataIndexer)
                                                                                    │
                                                              VideoClipExtractor ◀──┤
                                                              (StorageEngine)       │
                                                                                    ▼
                                                                          Backend.Server REST API
                                                                                    │
                                                                                    ▼
                                                                          Frontend.WPF (MVVM)
```

- **`VMS.Core`** — domain models, `AccessControlManager` (RBAC), `AuthService` (BCrypt),
  append-only `AuditLogger`, EF Core `VmsDbContext` + migrations (Postgres). Every other project
  depends on this one; it depends on nothing else in the solution. RBAC is a single rule table
  (`AccessControlManager.MinimumRole`) — both `Backend.Server` and `Frontend.WPF` construct their
  own `AccessControlManager` instance and get identical answers, so server-side authorization and
  client-side UI gating never drift apart. `Camera.Code` (a stable string like `"cam-01"`) is the
  identity used everywhere *outside* Postgres — archive/snapshot directory names, Elasticsearch's
  `cameraId` field, clip filenames — specifically so filesystem paths never depend on the
  database's `Camera.Id` Guid.

- **`VMS.MediaEngine`** — `OnvifCameraDiscoveryService` (hand-rolled WS-Security SOAP client,
  not a NuGet ONVIF package — those are mostly unmaintained). `RtspRecorder` and `SnapshotSampler`
  each wrap a supervised ffmpeg child process (`FfmpegProcessSupervisor`: auto-restart with
  backoff on exit) — one process per camera is the "zero-crash" mechanism: one camera's ffmpeg
  crashing can't touch another camera's pipeline. Recorded segments **must** use fragmented MP4
  (`-segment_format_options movflags=+frag_keyframe+empty_moov+...`) — without it a
  currently-recording segment has no moov atom and can't be opened by anything (including
  `VideoClipExtractor`) until the segment closes.

- **`VMS.StorageEngine`** — `RetentionEngine` (per-camera policy via `IRetentionPolicySource`,
  always skips immutable files, never needs RBAC since it's an automated system policy, not a
  user action). `ImmutableArchiveManager` (Windows ACL deny-delete + ReadOnly attribute; its
  `Delete()` is the only place that actually authorizes a delete, via `IAccessControlManager` —
  `RetentionEngine` and the archive API both route through it rather than duplicating the check).
  `VideoClipExtractor` parses segment start times from filenames (`yyyy-MM-dd_HH-mm-ss.mp4`,
  local time), infers each segment's *end* as the next segment's start (or last-write-time for
  the newest one), and concatenates two segments (ffmpeg concat demuxer, still `-c copy`) when a
  requested window spans a boundary.

- **`VMS.MetadataIndexer`** — `YoloDotNetDetector` wraps `YoloDotNet`
  (`ExecutionProvider.Cpu`/`.Cuda` are separate NuGet packages; only Cpu is currently referenced —
  see the README's known-limitations section for why). `ColorTagger` adds a color attribute to
  vehicle-class detections only, by averaging pixels inside the bounding box — COCO has no color
  classes, needed for "red car"-style search. `SerializedObjectDetector` wraps a detector with a
  semaphore because YoloDotNet's execution providers keep mutable per-call state and aren't safe
  to call concurrently from multiple cameras' `DetectionWorker`s sharing one loaded model.
  `SearchQueryParser` maps free text ("red car") to structured filters via closed-vocabulary
  keyword matching, not NLP.

- **`VMS.Backend.Server`** — ASP.NET Core Minimal API + a `BackgroundService`
  (`OrchestrationHostedService`) in one host (`WebApplication`, not the Worker SDK's plain
  `Host` — note the `<FrameworkReference Include="Microsoft.AspNetCore.App">` in the csproj and
  `GlobalUsings.cs` pulling in ASP.NET namespaces the Worker SDK doesn't add implicitly).
  `CameraOrchestrator` loads enabled cameras from Postgres at startup and onboards each
  independently (one camera failing to connect never stops the others). JWT auth
  (`TokenService`); `/api/clips/{file}` additionally accepts the token via `?access_token=`
  because LibVLC (the WPF client's player) fetches that URL with its own HTTP client and can't
  attach an `Authorization` header — same pattern ASP.NET Core uses for SignalR over WebSockets.
  Endpoint handlers call `IAccessControlManager.Authorize(role, permission)` inline and catch
  `UnauthorizedAccessException` → 403, rather than using policy-based `[Authorize]` attributes.

- **`VMS.Frontend.WPF`** — MVVM (CommunityToolkit.Mvvm source generators: `[ObservableProperty]`,
  `[RelayCommand]`). `App.xaml.cs` forces `RenderOptions.ProcessRenderMode = SoftwareOnly` at
  startup — needed for remote/RDP viewing (this client is meant to be watched from a control-room
  PC), and incidentally required to make PrintWindow-style screenshotting work in headless test
  environments. One shared `LibVLC` instance lives in `MainViewModel`; each `CameraTileViewModel`
  and `ClipPopupViewModel` owns its own `MediaPlayer` against it. `SessionService` holds the
  logged-in role and answers `CanManageCameras`/`CanExportClip`/etc. via the same `Core`
  `AccessControlManager` the server uses, for UI gating that can't disagree with the server.
  Views live under `Views/` in the `VMS.Frontend.WPF.Views` namespace (the default WPF template's
  root-namespace `MainWindow` was removed).

## Milestones / status

The READMEs (`readme_rus.md`, `readme_eng.md`) are the source of truth for current status,
the full REST API table, RBAC rules, and known limitations — read those before assuming
something is or isn't implemented.
