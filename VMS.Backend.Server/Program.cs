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
using VMS.MediaEngine.Onvif;
using VMS.MetadataIndexer.Detection;
using VMS.MetadataIndexer.Indexing;
using VMS.StorageEngine.Clips;
using VMS.StorageEngine.Immutable;
using VMS.StorageEngine.Retention;

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddJsonFile("appsettings.local.json", optional: true, reloadOnChange: true);

builder.Services.Configure<VmsOptions>(builder.Configuration.GetSection(VmsOptions.SectionName));
var vmsOptions = builder.Configuration.GetSection(VmsOptions.SectionName).Get<VmsOptions>() ?? new VmsOptions();

builder.Services.AddDbContext<VmsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("VmsDatabase")));

// --- Core: RBAC, auth, audit ---
builder.Services.AddSingleton<IAuthService, AuthService>();
builder.Services.AddSingleton<IAccessControlManager, AccessControlManager>();
builder.Services.AddScoped<IAuditLogger, AuditLogger>();
builder.Services.AddSingleton(new TokenService(vmsOptions.JwtSigningKey, vmsOptions.JwtExpiryMinutes));

// --- MediaEngine: ONVIF ---
builder.Services.AddHttpClient(nameof(OnvifClient));
builder.Services.AddSingleton<OnvifCameraDiscoveryService>();

// --- StorageEngine: retention, immutability, clips ---
builder.Services.AddSingleton<IRetentionPolicySource, DbRetentionPolicySource>();
builder.Services.AddSingleton(sp => new ImmutableArchiveManager(
    sp.GetRequiredService<ILoggerFactory>().CreateLogger<ImmutableArchiveManager>()));
builder.Services.AddSingleton(sp => new VideoClipExtractor(
    vmsOptions.FfmpegPath, vmsOptions.ArchiveRootPath, vmsOptions.ClipsRootPath,
    sp.GetRequiredService<ILoggerFactory>().CreateLogger<VideoClipExtractor>()));

// --- MetadataIndexer: detection + search ---
builder.Services.AddSingleton<IObjectDetector>(sp => new SerializedObjectDetector(
    new YoloDotNetDetector(vmsOptions.YoloModelPath, useCuda: false, confidenceThreshold: 0.4,
        sp.GetRequiredService<ILoggerFactory>().CreateLogger<YoloDotNetDetector>())));
builder.Services.AddSingleton<IFaceEmbedder>(sp => new SerializedFaceEmbedder(
    new OnnxFaceEmbedder(vmsOptions.FaceDetectorModelPath, vmsOptions.FaceEmbedderModelPath,
        sp.GetRequiredService<ILoggerFactory>().CreateLogger<OnnxFaceEmbedder>())));
builder.Services.AddSingleton<IMetadataIndexer>(_ => new ElasticsearchIndexer(vmsOptions.ElasticsearchUri));

// --- Orchestration ---
builder.Services.AddSingleton<CameraOrchestrator>();
builder.Services.AddHostedService<OrchestrationHostedService>();

// --- Auth ---
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(vmsOptions.JwtSigningKey)),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        // Clip playback goes through LibVLC (WPF client) / a <video> src (any future
        // web client) — neither attaches a custom Authorization header when fetching
        // the media URL, so this one path also accepts the JWT via ?access_token=.
        // Same pattern ASP.NET Core's own docs use for SignalR WebSocket auth.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken) &&
                    context.HttpContext.Request.Path.StartsWithSegments("/api/clips"))
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

app.UseAuthentication();
app.UseAuthorization();

app.MapAuthEndpoints();
app.MapCameraEndpoints();
app.MapSearchEndpoints();
app.MapClipEndpoints();
app.MapArchiveEndpoints();
app.MapAdminEndpoints();
app.MapAuditEndpoints();

app.Run();
