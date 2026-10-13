using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.Api;

public class ApiException(string message) : Exception(message);

/// <summary>Thin wrapper over the Backend.Server REST API. Attaches the session's JWT to every request once logged in.</summary>
public class ApiClient
{
    private readonly HttpClient _http;
    private string? _token;
    private string? _refreshToken;
    private System.Threading.Timer? _refreshTimer;

    public string BaseAddress { get; }

    /// <summary>Raised when the session is truly over (refresh token missing/expired/revoked, or
    /// the account was deactivated) — the server-side JWT check (see Program.cs's
    /// OnTokenValidated) means this can also fire mid-request well before the access token's own
    /// expiry, e.g. right after an admin deactivates this account. Either way, every open window
    /// shares this one ApiClient, so the only correct response is to drop back to the login
    /// screen instead of leaving whichever window made the call stuck on a raw error.</summary>
    public event Action? SessionExpired;

    /// <summary>Raised after a silent background token refresh succeeds — lets SessionService
    /// pick up a role/permission change made server-side mid-session (see
    /// SessionService.ApplyLogin) instead of only ever reflecting what the role was at login.</summary>
    public event Action<LoginResponseDto>? TokenRefreshed;

    public ApiClient(string baseAddress)
    {
        BaseAddress = baseAddress.TrimEnd('/');
        _http = new HttpClient { BaseAddress = new Uri(BaseAddress) };
    }

    /// <summary>Call with a non-null token after login/refresh (expiresAt/refreshToken required
    /// then, to arm the next silent background refresh); call with null on logout/session-expiry
    /// to clear everything and stop that background refresh.</summary>
    public void SetToken(string? token, DateTimeOffset? expiresAt = null, string? refreshToken = null)
    {
        _token = token;
        _http.DefaultRequestHeaders.Authorization = token is null
            ? null
            : new AuthenticationHeaderValue("Bearer", token);

        _refreshTimer?.Dispose();
        _refreshTimer = null;

        if (token is null)
        {
            _refreshToken = null;
            return;
        }

        _refreshToken = refreshToken ?? _refreshToken;
        if (expiresAt is { } exp)
        {
            ScheduleRefresh(exp);
        }
    }

    public async Task<LoginResponseDto> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/auth/login", new { username, password }, ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new ApiException(LocalizationService.Get("Api_LoginFailed"));
        }

        return (await response.Content.ReadFromJsonAsync<LoginResponseDto>(cancellationToken: ct))!;
    }

    /// <summary>Fires well before the access token expires (see VmsOptions.JwtExpiryMinutes on
    /// the server — deliberately short) so a normal request should never actually hit a 401 for
    /// plain expiry; a 401 still reaching EnsureSuccessAsync means something else is wrong
    /// (revoked, clock skew, the refresh below failing) and SessionExpired is the right call.</summary>
    private void ScheduleRefresh(DateTimeOffset expiresAt)
    {
        var delay = (expiresAt - DateTimeOffset.UtcNow) * 0.7;
        if (delay < TimeSpan.FromSeconds(10))
        {
            delay = TimeSpan.FromSeconds(10);
        }

        _refreshTimer = new System.Threading.Timer(_ => _ = RefreshTokenSilentlyAsync(), null, delay, Timeout.InfiniteTimeSpan);
    }

    private async Task RefreshTokenSilentlyAsync()
    {
        if (_refreshToken is null)
        {
            return;
        }

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = _refreshToken });
        }
        catch
        {
            // Transient network failure, not an invalid session — don't force a logout for this;
            // the reactive 401 path in EnsureSuccessAsync still catches a truly dead session on
            // the next real request.
            return;
        }

        if (!response.IsSuccessStatusCode)
        {
            SessionExpired?.Invoke();
            return;
        }

        var dto = await response.Content.ReadFromJsonAsync<LoginResponseDto>();
        if (dto is null)
        {
            SessionExpired?.Invoke();
            return;
        }

        SetToken(dto.Token, dto.ExpiresAt, dto.RefreshToken);
        TokenRefreshed?.Invoke(dto);
    }

    public async Task<List<CameraDto>> GetCamerasAsync(CancellationToken ct = default)
    {
        return await GetAsync<List<CameraDto>>("/api/cameras", ct) ?? [];
    }

    public async Task<CameraStatusDto?> GetCameraStatusAsync(string code, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/cameras/{Uri.EscapeDataString(code)}/status", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<CameraStatusDto>(cancellationToken: ct);
    }

    public async Task CreateCameraAsync(CreateCameraRequestDto request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/cameras", request, ct);
        await EnsureSuccessAsync(response);
    }

    public async Task UpdateCameraAsync(string code, UpdateCameraRequestDto request, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"/api/cameras/{Uri.EscapeDataString(code)}", request, ct);
        await EnsureSuccessAsync(response);
    }

    public async Task DeleteCameraAsync(string code, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/cameras/{Uri.EscapeDataString(code)}", ct);
        await EnsureSuccessAsync(response);
    }

    public async Task<List<DiscoveredDeviceDto>> DiscoverCamerasAsync(CancellationToken ct = default)
    {
        return await GetAsync<List<DiscoveredDeviceDto>>("/api/cameras/discover", ct) ?? [];
    }

    public async Task AddDiscoveredCameraAsync(AddDiscoveredCameraRequestDto request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/cameras/discover/add", request, ct);
        await EnsureSuccessAsync(response);
    }

    public async Task<List<SearchResultDto>> SearchAsync(string query, string? cameraId = null, string? personName = null, CancellationToken ct = default)
    {
        var url = $"/api/search?q={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrWhiteSpace(cameraId))
        {
            url += $"&cameraId={Uri.EscapeDataString(cameraId)}";
        }

        if (!string.IsNullOrWhiteSpace(personName))
        {
            url += $"&personName={Uri.EscapeDataString(personName)}";
        }

        return await GetAsync<List<SearchResultDto>>(url, ct) ?? [];
    }

    /// <summary>Search-by-photo: uploads a reference photo (optionally cropped to a rough person/face selection, in the photo's own pixel coordinates — e.g. a "crop from player" snapshot), gets back the same shape SearchAsync returns, ranked by face similarity with a real MatchScore per hit.</summary>
    public async Task<List<SearchResultDto>> SearchByFaceAsync(string imagePath, (int X, int Y, int Width, int Height)? cropBox = null, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        using var fileStream = File.OpenRead(imagePath);
        using var fileContent = new StreamContent(fileStream);
        content.Add(fileContent, "photo", Path.GetFileName(imagePath));

        var url = "/api/search/by-face";
        if (cropBox is { } box)
        {
            url += $"?cropX={box.X}&cropY={box.Y}&cropWidth={box.Width}&cropHeight={box.Height}";
        }

        var response = await _http.PostAsync(url, content, ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<List<SearchResultDto>>(cancellationToken: ct) ?? [];
    }

    /// <summary>Enrolls a named person from a reference photo — see KnownPersonMatcher/DetectionWorker for how this powers "search by name".</summary>
    public async Task<KnownPersonDto> RegisterKnownPersonAsync(string name, string photoPath, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(name), "name");
        using var fileStream = File.OpenRead(photoPath);
        using var fileContent = new StreamContent(fileStream);
        content.Add(fileContent, "photo", Path.GetFileName(photoPath));

        var response = await _http.PostAsync("/api/known-persons", content, ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<KnownPersonDto>(cancellationToken: ct))!;
    }

    public async Task<List<KnownPersonDto>> GetKnownPersonsAsync(CancellationToken ct = default)
    {
        return await GetAsync<List<KnownPersonDto>>("/api/known-persons", ct) ?? [];
    }

    public async Task DeleteKnownPersonAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/known-persons/{id}", ct);
        await EnsureSuccessAsync(response);
    }

    public async Task<byte[]?> GetCameraSnapshotAsync(string cameraCode, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/cameras/{Uri.EscapeDataString(cameraCode)}/snapshot", ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(ct) : null;
    }

    /// <summary>A cropped-to-bounding-box still frame for one search result, generated on demand from the archive — powers the search results grid's thumbnail cards.</summary>
    public async Task<byte[]?> GetEventThumbnailBytesAsync(Guid eventId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/events/{eventId}/thumbnail", ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(ct) : null;
    }

    // --- User management ---
    public async Task<List<UserDto>> GetUsersAsync(CancellationToken ct = default)
    {
        return await GetAsync<List<UserDto>>("/api/users", ct) ?? [];
    }

    public async Task<UserDto> CreateUserAsync(CreateUserRequestDto request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/users", request, ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<UserDto>(cancellationToken: ct))!;
    }

    public async Task<UserDto> UpdateUserAsync(Guid id, UpdateUserRequestDto request, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"/api/users/{id}", request, ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<UserDto>(cancellationToken: ct))!;
    }

    public async Task DeleteUserAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/users/{id}", ct);
        await EnsureSuccessAsync(response);
    }

    // --- Camera groups ("screens") ---
    public async Task<List<CameraGroupDto>> GetCameraGroupsAsync(CancellationToken ct = default)
    {
        return await GetAsync<List<CameraGroupDto>>("/api/camera-groups", ct) ?? [];
    }

    public async Task<CameraGroupDto> CreateCameraGroupAsync(SaveCameraGroupRequestDto request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/camera-groups", request, ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<CameraGroupDto>(cancellationToken: ct))!;
    }

    public async Task<CameraGroupDto> UpdateCameraGroupAsync(Guid id, SaveCameraGroupRequestDto request, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"/api/camera-groups/{id}", request, ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<CameraGroupDto>(cancellationToken: ct))!;
    }

    public async Task DeleteCameraGroupAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/camera-groups/{id}", ct);
        await EnsureSuccessAsync(response);
    }

    // --- Video wall layouts ---
    public async Task<List<VideoWallLayoutDto>> GetVideoWallLayoutsAsync(CancellationToken ct = default)
    {
        return await GetAsync<List<VideoWallLayoutDto>>("/api/video-wall-layouts", ct) ?? [];
    }

    public async Task<VideoWallLayoutDto> GetVideoWallLayoutAsync(Guid id, CancellationToken ct = default)
    {
        return (await GetAsync<VideoWallLayoutDto>($"/api/video-wall-layouts/{id}", ct))!;
    }

    public async Task<VideoWallLayoutDto> CreateVideoWallLayoutAsync(SaveVideoWallLayoutRequestDto request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/video-wall-layouts", request, ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<VideoWallLayoutDto>(cancellationToken: ct))!;
    }

    public async Task<VideoWallLayoutDto> UpdateVideoWallLayoutAsync(Guid id, SaveVideoWallLayoutRequestDto request, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"/api/video-wall-layouts/{id}", request, ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<VideoWallLayoutDto>(cancellationToken: ct))!;
    }

    public async Task DeleteVideoWallLayoutAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/video-wall-layouts/{id}", ct);
        await EnsureSuccessAsync(response);
    }

    // --- E-map ---
    public async Task<List<EMapDto>> GetEMapsAsync(CancellationToken ct = default) =>
        await GetAsync<List<EMapDto>>("/api/emaps", ct) ?? [];

    public async Task<EMapDto> CreateEMapAsync(string name, string imagePath, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(name), "name");
        using var fileStream = File.OpenRead(imagePath);
        using var fileContent = new StreamContent(fileStream);
        content.Add(fileContent, "image", Path.GetFileName(imagePath));

        var response = await _http.PostAsync("/api/emaps", content, ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<EMapDto>(cancellationToken: ct))!;
    }

    public async Task DeleteEMapAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/emaps/{id}", ct);
        await EnsureSuccessAsync(response);
    }

    public async Task<byte[]?> GetEMapImageBytesAsync(Guid id, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/emaps/{id}/image", ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(ct) : null;
    }

    public async Task<List<EMapPinDto>> GetEMapPinsAsync(Guid mapId, CancellationToken ct = default) =>
        await GetAsync<List<EMapPinDto>>($"/api/emaps/{mapId}/pins", ct) ?? [];

    public async Task<EMapPinDto> AddEMapPinAsync(Guid mapId, CreateEMapPinRequestDto request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync($"/api/emaps/{mapId}/pins", request, ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<EMapPinDto>(cancellationToken: ct))!;
    }

    public async Task DeleteEMapPinAsync(Guid mapId, Guid pinId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/emaps/{mapId}/pins/{pinId}", ct);
        await EnsureSuccessAsync(response);
    }

    // --- People Counting ---
    public async Task<CountingLineDto?> GetCountingLineAsync(string cameraCode, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/counting-lines/{Uri.EscapeDataString(cameraCode)}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<CountingLineDto>(cancellationToken: ct);
    }

    public async Task<CountingLineDto> SetCountingLineAsync(string cameraCode, SetCountingLineRequestDto request, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync($"/api/counting-lines/{Uri.EscapeDataString(cameraCode)}", request, ct);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<CountingLineDto>(cancellationToken: ct))!;
    }

    public async Task DeleteCountingLineAsync(string cameraCode, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/counting-lines/{Uri.EscapeDataString(cameraCode)}", ct);
        await EnsureSuccessAsync(response);
    }

    public async Task<PeopleCountDto> GetPeopleCountAsync(string cameraId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var url = $"/api/people-count?cameraId={Uri.EscapeDataString(cameraId)}&from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";
        return (await GetAsync<PeopleCountDto>(url, ct))!;
    }

    // --- Person Sightings ("count every person seen + remember with a screenshot") ---
    public async Task<List<LiveDetectionDto>> GetLiveDetectionsAsync(string cameraCode, CancellationToken ct = default)
    {
        return await GetAsync<List<LiveDetectionDto>>($"/api/cameras/{Uri.EscapeDataString(cameraCode)}/detections", ct) ?? [];
    }

    public async Task<List<PersonSightingReportRowDto>> GetPersonSightingReportAsync(IEnumerable<string> cameraIds, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var codes = string.Join(',', cameraIds.Select(Uri.EscapeDataString));
        var url = $"/api/person-sightings/report?cameraIds={codes}&from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";
        return await GetAsync<List<PersonSightingReportRowDto>>(url, ct) ?? [];
    }

    public async Task<List<PersonSightingDto>> GetPersonSightingsAsync(string cameraId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default)
    {
        var url = $"/api/person-sightings?cameraId={Uri.EscapeDataString(cameraId)}&from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}";
        return await GetAsync<List<PersonSightingDto>>(url, ct) ?? [];
    }

    public async Task<byte[]?> GetPersonSightingThumbnailBytesAsync(Guid sightingId, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/person-sightings/{sightingId}/thumbnail", ct);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsByteArrayAsync(ct) : null;
    }

    public async Task DeletePersonSightingAsync(Guid sightingId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync($"/api/person-sightings/{sightingId}", ct);
        await EnsureSuccessAsync(response);
    }

    // --- Alarm Records ---
    public async Task<List<AlarmRecordDto>> GetAlarmsAsync(string? cameraId = null, DateTimeOffset? from = null, DateTimeOffset? to = null, int? size = null, CancellationToken ct = default)
    {
        var url = "/api/alarms?";
        if (!string.IsNullOrWhiteSpace(cameraId)) url += $"cameraId={Uri.EscapeDataString(cameraId)}&";
        if (from is not null) url += $"from={Uri.EscapeDataString(from.Value.ToString("O"))}&";
        if (to is not null) url += $"to={Uri.EscapeDataString(to.Value.ToString("O"))}&";
        if (size is not null) url += $"size={size}&";
        return await GetAsync<List<AlarmRecordDto>>(url, ct) ?? [];
    }

    public async Task AcknowledgeAlarmAsync(Guid detectionEventId, CancellationToken ct = default)
    {
        var response = await _http.PostAsync($"/api/alarms/{detectionEventId}/acknowledge", content: null, ct);
        await EnsureSuccessAsync(response);
    }

    public async Task<string> RequestClipAsync(ClipRequestDto request, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/api/clips", request, ct);
        await EnsureSuccessAsync(response);
        var dto = await response.Content.ReadFromJsonAsync<ClipResponseDto>(cancellationToken: ct);
        return dto!.FileName;
    }

    public async Task<List<AuditLogEntryDto>> GetAuditLogAsync(CancellationToken ct = default)
    {
        return await GetAsync<List<AuditLogEntryDto>>("/api/audit-log", ct) ?? [];
    }

    public async Task<List<ArchiveCoverageSegmentDto>> GetArchiveCoverageAsync(string cameraId, DateOnly date, CancellationToken ct = default)
    {
        var url = $"/api/archive/coverage?cameraId={Uri.EscapeDataString(cameraId)}&date={date:yyyy-MM-dd}";
        return await GetAsync<List<ArchiveCoverageSegmentDto>>(url, ct) ?? [];
    }

    public async Task ProtectArchiveFileAsync(string cameraId, string fileName, CancellationToken ct = default)
    {
        var url = $"/api/archive/protect?cameraId={Uri.EscapeDataString(cameraId)}&fileName={Uri.EscapeDataString(fileName)}";
        var response = await _http.PostAsync(url, content: null, ct);
        await EnsureSuccessAsync(response);
    }

    /// <summary>Admin+ for a standard file, SuperAdmin for a protected one — the server enforces
    /// which via ImmutableArchiveManager.Delete; a Forbid comes back as a normal ApiException
    /// with no special handling needed here.</summary>
    public async Task DeleteArchiveFileAsync(string cameraId, string fileName, CancellationToken ct = default)
    {
        var url = $"/api/archive?cameraId={Uri.EscapeDataString(cameraId)}&fileName={Uri.EscapeDataString(fileName)}";
        var response = await _http.DeleteAsync(url, ct);
        await EnsureSuccessAsync(response);
    }

    /// <summary>
    /// Includes the JWT as ?access_token= because this URL is handed to LibVLC, which
    /// fetches it with its own HTTP client and never sees our Authorization header.
    /// </summary>
    public Uri GetClipDownloadUri(string fileName) =>
        new($"{BaseAddress}/api/clips/{Uri.EscapeDataString(fileName)}?access_token={Uri.EscapeDataString(_token ?? string.Empty)}");

    /// <summary>Fetches a previously-extracted clip's raw bytes (via the normal authenticated
    /// _http client, not the ?access_token= URL above — that variant exists only because LibVLC
    /// needs a plain URL, not because it's otherwise preferred) so the caller can save it to disk
    /// with a SaveFileDialog.</summary>
    public async Task<byte[]> DownloadClipBytesAsync(string fileName, CancellationToken ct = default)
    {
        var response = await _http.GetAsync($"/api/clips/{Uri.EscapeDataString(fileName)}", ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        var response = await _http.GetAsync(url, ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }

    private async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized)
        {
            SessionExpired?.Invoke();
            throw new ApiException(LocalizationService.Get("Api_SessionExpired"));
        }

        if (response.StatusCode is System.Net.HttpStatusCode.Forbidden)
        {
            throw new ApiException(LocalizationService.Get("Api_Forbidden"));
        }

        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new ApiException(LocalizationService.Get("Api_ServerError", response.StatusCode));
        }

        // Results.BadRequest("plain string")/Conflict("plain string") serialize as a bare JSON
        // string — decode it so the user sees the message itself, not raw JSON quoting/escaping.
        string message;
        try
        {
            message = JsonSerializer.Deserialize<string>(body) ?? body;
        }
        catch (JsonException)
        {
            message = body;
        }

        throw new ApiException(message);
    }
}
