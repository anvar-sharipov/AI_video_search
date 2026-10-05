using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace VMS.MediaEngine.Capture;

/// <summary>
/// Starts/stops per-camera <see cref="LiveHlsRelay"/> processes on demand — a mobile
/// client asking for live view spins one up; nobody watching for <see cref="IdleTimeout"/>
/// stops it. Continuous HLS transcoding for every camera around the clock would double
/// ffmpeg process count for no benefit (live HLS is only ever consumed by an active
/// viewer, unlike archive recording), so this only exists while at least one client is
/// actually asking for it.
/// </summary>
public sealed class LiveHlsRelayManager(
    string ffmpegPath, string outputRoot, ILoggerFactory loggerFactory) : IAsyncDisposable
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromSeconds(30);

    private sealed record Entry(LiveHlsRelay Relay, DateTimeOffset LastAccessedUtc);

    private readonly ConcurrentDictionary<string, Entry> _entries = new();
    private readonly SemaphoreSlim _startLock = new(1, 1);
    private Timer? _sweepTimer;

    /// <summary>
    /// Ensures a relay is running for <paramref name="cameraId"/> and returns its
    /// playlist path once at least the first segment exists, or null if ffmpeg never
    /// produced one within the startup window (bad/unreachable RTSP URI).
    /// </summary>
    public async Task<string?> EnsureStartedAsync(string cameraId, string rtspUri, CancellationToken ct = default)
    {
        if (!_entries.ContainsKey(cameraId))
        {
            await _startLock.WaitAsync(ct);
            try
            {
                if (!_entries.ContainsKey(cameraId))
                {
                    var dir = Path.Combine(outputRoot, cameraId);
                    // A relay restarted after being swept idle must not serve a stale
                    // playlist referencing .ts files it's about to delete/replace.
                    if (Directory.Exists(dir))
                    {
                        Directory.Delete(dir, recursive: true);
                    }

                    var relay = new LiveHlsRelay(
                        ffmpegPath, rtspUri, cameraId, dir, loggerFactory.CreateLogger($"LiveHls:{cameraId}"));
                    relay.Start();
                    _entries[cameraId] = new Entry(relay, DateTimeOffset.UtcNow);
                }
            }
            finally
            {
                _startLock.Release();
            }
        }

        Touch(cameraId);

        var playlistPath = Path.Combine(outputRoot, cameraId, "index.m3u8");
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (File.Exists(playlistPath))
            {
                return playlistPath;
            }
            await Task.Delay(100, ct);
        }
        return File.Exists(playlistPath) ? playlistPath : null;
    }

    public string GetOutputDir(string cameraId) => Path.Combine(outputRoot, cameraId);

    /// <summary>Marks a camera's relay as recently used — call on every playlist/segment fetch, not just the first.</summary>
    public void Touch(string cameraId)
    {
        if (_entries.TryGetValue(cameraId, out var entry))
        {
            _entries[cameraId] = entry with { LastAccessedUtc = DateTimeOffset.UtcNow };
        }
    }

    /// <summary>Starts the periodic idle sweep — call once at app startup.</summary>
    public void StartIdleSweep() => _sweepTimer ??= new Timer(_ => _ = SweepIdleAsync(), null, IdleTimeout, IdleTimeout);

    private async Task SweepIdleAsync()
    {
        var cutoff = DateTimeOffset.UtcNow - IdleTimeout;
        foreach (var (cameraId, entry) in _entries)
        {
            if (entry.LastAccessedUtc <= cutoff && _entries.TryRemove(cameraId, out var removed))
            {
                await removed.Relay.DisposeAsync();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _sweepTimer.DisposeAsyncIfNotNull();
        foreach (var entry in _entries.Values)
        {
            await entry.Relay.DisposeAsync();
        }
    }
}

file static class TimerExtensions
{
    public static ValueTask DisposeAsyncIfNotNull(this Timer? timer)
    {
        timer?.Dispose();
        return ValueTask.CompletedTask;
    }
}
