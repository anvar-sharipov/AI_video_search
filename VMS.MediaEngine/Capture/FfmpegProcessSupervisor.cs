using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace VMS.MediaEngine.Capture;

/// <summary>
/// Supervises a single long-running ffmpeg process: starts it, drains its stderr
/// (ffmpeg logs there) into the app logger, and restarts it with exponential backoff
/// if it exits unexpectedly (e.g. the camera drops the RTSP connection).
///
/// This process-per-camera isolation is the "zero-crash" mechanism: one camera's
/// ffmpeg process crashing/restarting cannot affect any other camera's pipeline.
/// </summary>
public sealed class FfmpegProcessSupervisor(
    string ffmpegPath,
    Func<string> buildArguments,
    string label,
    ILogger logger) : IAsyncDisposable
{
    private static readonly TimeSpan InitialBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private CancellationTokenSource? _cts;
    private Task? _runLoop;
    private Process? _currentProcess;

    /// <summary>Fired each time a new ffmpeg process is started, so callers can attach to its streams.</summary>
    public event Action<Process>? ProcessStarted;

    public void Start()
    {
        if (_runLoop is not null)
        {
            throw new InvalidOperationException($"ffmpeg[{label}] supervisor already started.");
        }

        _cts = new CancellationTokenSource();
        _runLoop = RunLoopAsync(_cts.Token);
    }

    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        await _cts.CancelAsync();
        KillCurrentProcess();

        if (_runLoop is not null)
        {
            await _runLoop;
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        var backoff = InitialBackoff;

        while (!ct.IsCancellationRequested)
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(ffmpegPath, buildArguments())
                {
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            try
            {
                process.Start();
                FfmpegJobObject.Instance.AddProcess(process.Handle);
                _currentProcess = process;
                ProcessStarted?.Invoke(process);
                logger.LogInformation("ffmpeg[{Label}] started (pid {Pid})", label, process.Id);

                var drainStderr = DrainStreamToLogAsync(process.StandardError, ct);

                await process.WaitForExitAsync(ct);
                await drainStderr;

                if (ct.IsCancellationRequested)
                {
                    break;
                }

                logger.LogWarning(
                    "ffmpeg[{Label}] exited with code {ExitCode}, restarting in {Backoff}",
                    label, process.ExitCode, backoff);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "ffmpeg[{Label}] supervisor loop error, restarting in {Backoff}", label, backoff);
            }
            finally
            {
                _currentProcess = null;
            }

            try
            {
                await Task.Delay(backoff, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, MaxBackoff.TotalSeconds));
        }
    }

    private async Task DrainStreamToLogAsync(StreamReader reader, CancellationToken ct)
    {
        try
        {
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) is not null)
            {
                logger.LogDebug("ffmpeg[{Label}]: {Line}", label, line);
            }
        }
        catch (OperationCanceledException)
        {
            // expected on shutdown
        }
    }

    private void KillCurrentProcess()
    {
        var process = _currentProcess;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // process already exited between the check and Kill()
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }
}
