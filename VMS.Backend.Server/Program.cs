using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using VMS.Backend.Server.Api;
using VMS.Backend.Server.Orchestration;
using VMS.Backend.Server.Startup;
using VMS.Core.Auditing;
using VMS.Core.Data;
using VMS.Core.Security;
using VMS.MediaEngine.Capture;
using VMS.MediaEngine.Onvif;
using VMS.MetadataIndexer.Detection;
using VMS.MetadataIndexer.Indexing;
using VMS.StorageEngine.Clips;
using VMS.StorageEngine.Immutable;
using VMS.StorageEngine.Retention;

var builder = WebApplication.CreateBuilder(args);

// No-op unless actually launched by the Service Control Manager (e.g. `dotnet run` and running
// the exe directly from a console are unaffected) — lets the installer register this exe as a
// Windows Service (auto-start on boot) instead of requiring someone to keep a console window
// open. Also switches the default logger to the Windows Event Log when running as a service,
// since a service has no console to write to.
builder.Host.UseWindowsService();

builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

builder.Services.Configure<VmsOptions>(builder.Configuration.GetSection(VmsOptions.SectionName));
var vmsOptions = builder.Configuration.GetSection(VmsOptions.SectionName).Get<VmsOptions>() ?? new VmsOptions();

// AddDbContextFactory (not AddDbContext) because KnownPersonMatcher is a singleton (shared
// cache across every camera's DetectionWorker) and needs its own short-lived DbContext per
// cache refresh — a singleton can't consume a scoped DbContext directly. Everything else in
// this app still gets a plain scoped VmsDbContext injected the same way AddDbContext would
// have provided it, via this factory-backed AddScoped — EF Core's own documented pattern for
// needing both a factory and ordinary scoped injection from a single registration.
builder.Services.AddDbContextFactory<VmsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("VmsDatabase")));
builder.Services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<VmsDbContext>>().CreateDbContext());

// --- Core: RBAC, auth, audit ---
builder.Services.AddSingleton<IAuthService, AuthService>();
builder.Services.AddSingleton<IAccessControlManager, AccessControlManager>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddSingleton(new TokenService(vmsOptions.JwtSigningKey, vmsOptions.JwtExpiryMinutes));

// --- MediaEngine: ONVIF ---
builder.Services.AddHttpClient(nameof(OnvifClient));
builder.Services.AddSingleton<OnvifCameraDiscoveryService>();
builder.Services.AddSingleton<OnvifDiscoveryClient>();

// --- StorageEngine: retention, immutability, clips ---
builder.Services.AddSingleton<IRetentionPolicySource, DbRetentionPolicySource>();
builder.Services.AddSingleton(sp => new ImmutableArchiveManager(
    sp.GetRequiredService<ILoggerFactory>().CreateLogger<ImmutableArchiveManager>()));
builder.Services.AddSingleton(sp => new VideoClipExtractor(
    vmsOptions.FfmpegPath, vmsOptions.ArchiveRootPath, vmsOptions.ClipsRootPath,
    sp.GetRequiredService<ILoggerFactory>().CreateLogger<VideoClipExtractor>()));
builder.Services.AddSingleton(sp => new LiveHlsRelayManager(
    vmsOptions.FfmpegPath, vmsOptions.LiveHlsRootPath, sp.GetRequiredService<ILoggerFactory>()));

// --- MetadataIndexer: detection + search ---
builder.Services.AddSingleton<IObjectDetector>(sp => new SerializedObjectDetector(
    new YoloDotNetDetector(vmsOptions.YoloModelPath, useCuda: false, confidenceThreshold: 0.4,
        sp.GetRequiredService<ILoggerFactory>().CreateLogger<YoloDotNetDetector>())));
builder.Services.AddSingleton<IFaceEmbedder>(sp => new SerializedFaceEmbedder(
    new OnnxFaceEmbedder(vmsOptions.FaceDetectorModelPath, vmsOptions.EyeDetectorModelPath, vmsOptions.FaceEmbedderModelPath,
        sp.GetRequiredService<ILoggerFactory>().CreateLogger<OnnxFaceEmbedder>())));
builder.Services.AddSingleton<IKnownPersonMatcher, KnownPersonMatcher>();
builder.Services.AddSingleton<ICountingLineProvider, CountingLineProvider>();
builder.Services.AddSingleton<IPlateOcrReader>(sp => new SerializedPlateOcrReader(
    new TesseractPlateOcrReader(vmsOptions.TessDataPath,
        sp.GetRequiredService<ILoggerFactory>().CreateLogger<TesseractPlateOcrReader>())));
builder.Services.AddSingleton<IMetadataIndexer>(_ => new ElasticsearchIndexer(vmsOptions.ElasticsearchUri));

// --- Orchestration ---
builder.Services.AddSingleton<CameraOrchestrator>();
builder.Services.AddHostedService<OrchestrationHostedService>();

// --- Auth ---
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Without this, the JWT handler silently remaps short claim names ("sub" -> the legacy
        // ClaimTypes.NameIdentifier URI) on every incoming token. ClaimsPrincipalExtensions.
        // GetUserId() reads the literal "sub" claim, so without this flag it always returns
        // null — which made /api/alarms/{id}/acknowledge return 401 for a valid session,
        // tripping the client's SessionExpired handling and force-closing every open window.
        options.MapInboundClaims = false;

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(vmsOptions.JwtSigningKey)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        // Clip playback and live HLS go through LibVLC (WPF client) / a native player
        // (the phone app's ExoPlayer/AVPlayer) — none of these attach a custom
        // Authorization header when fetching the media URL, so both paths also accept
        // the JWT via ?access_token=. Same pattern ASP.NET Core's own docs use for
        // SignalR WebSocket auth.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    (context.HttpContext.Request.Path.StartsWithSegments("/api/clips") ||
                     context.HttpContext.Request.Path.StartsWithSegments("/api/live")))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

await DbInitializer.InitializeAsync(app.Services, app.Services.GetRequiredService<ILogger<Program>>());
app.Services.GetRequiredService<LiveHlsRelayManager>().StartIdleSweep();

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapCameraEndpoints();
app.MapUserEndpoints();
app.MapSearchEndpoints();
app.MapEventThumbnailEndpoints();
app.MapKnownPersonEndpoints();
app.MapEMapEndpoints();
app.MapCameraGroupEndpoints();
app.MapVideoWallEndpoints();
app.MapPeopleCountingEndpoints();
app.MapPersonSightingEndpoints();
app.MapAlarmEndpoints();
app.MapLiveEndpoints();
app.MapClipEndpoints();
app.MapArchiveEndpoints();
app.MapAdminEndpoints();
app.MapAuditEndpoints();

app.Run();
