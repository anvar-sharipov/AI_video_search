using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using VMS.Frontend.WPF.Services;

namespace VMS.Frontend.WPF.Api;

public class ApiException(string message) : Exception(message);

/// <summary>Thin wrapper over the Backend.Server REST API. Attaches the session's JWT to every request once logged in.</summary>
public class ApiClient
{
    private readonly HttpClient _http;
    private string? _token;

    public string BaseAddress { get; }

    public ApiClient(string baseAddress)
    {
        BaseAddress = baseAddress.TrimEnd('/');
        _http = new HttpClient { BaseAddress = new Uri(BaseAddress) };
    }

    public void SetToken(string? token)
    {
        _token = token;
        _http.DefaultRequestHeaders.Authorization = token is null
            ? null
            : new AuthenticationHeaderValue("Bearer", token);
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

    public async Task<List<SearchResultDto>> SearchAsync(string query, string? cameraId = null, CancellationToken ct = default)
    {
        var url = $"/api/search?q={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrWhiteSpace(cameraId))
        {
            url += $"&cameraId={Uri.EscapeDataString(cameraId)}";
        }

        return await GetAsync<List<SearchResultDto>>(url, ct) ?? [];
    }

    /// <summary>Search-by-photo: uploads a reference photo, gets back the same shape SearchAsync returns (ranked by face similarity, not recency).</summary>
    public async Task<List<SearchResultDto>> SearchByFaceAsync(string imagePath, CancellationToken ct = default)
    {
        using var content = new MultipartFormDataContent();
        using var fileStream = File.OpenRead(imagePath);
        using var fileContent = new StreamContent(fileStream);
        content.Add(fileContent, "photo", Path.GetFileName(imagePath));

        var response = await _http.PostAsync("/api/search/by-face", content, ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<List<SearchResultDto>>(cancellationToken: ct) ?? [];
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

    /// <summary>
    /// Includes the JWT as ?access_token= because this URL is handed to LibVLC, which
    /// fetches it with its own HTTP client and never sees our Authorization header.
    /// </summary>
    public Uri GetClipDownloadUri(string fileName) =>
        new($"{BaseAddress}/api/clips/{Uri.EscapeDataString(fileName)}?access_token={Uri.EscapeDataString(_token ?? string.Empty)}");

    private async Task<T?> GetAsync<T>(string url, CancellationToken ct)
    {
        var response = await _http.GetAsync(url, ct);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<T>(cancellationToken: ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        if (response.StatusCode is System.Net.HttpStatusCode.Forbidden)
        {
            throw new ApiException(LocalizationService.Get("Api_Forbidden"));
        }

        var body = await response.Content.ReadAsStringAsync();
        throw new ApiException(string.IsNullOrWhiteSpace(body) ? LocalizationService.Get("Api_ServerError", response.StatusCode) : body);
    }
}
