using System.Diagnostics;
using System.Text;
using NOVA.FIX.Core.Logging;

namespace NOVA.FIX.Infrastructure.Execution;

/// <summary>Outcome of running a fixed, allowlisted system command.</summary>
public sealed record ProcessRunResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    bool TimedOut,
    bool Launched);

/// <summary>
/// Safe process execution: fixed executables, argument ARRAYS (never shell string
/// concatenation), timeouts, cancellation and captured output. The AI layer can
/// never reach this service directly — only validated application services with
/// allowlisted action IDs may call it.
/// </summary>
public sealed class ProcessRunner
{
    private readonly IAppLogger _logger;

    public ProcessRunner(IAppLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Runs an executable with pre-split arguments. The caller is responsible for
    /// validating that the executable is on the application's allowlist.
    /// </summary>
    public async Task<ProcessRunResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken ct = default,
        string? workingDirectory = null)
    {
        var sw = Stopwatch.StartNew();
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = workingDirectory ?? string.Empty,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in arguments)
            psi.ArgumentList.Add(arg);

        _logger.Log(LogLevel.Info, "ProcessRunner",
            $"Launching {Path.GetFileName(executable)} ({arguments.Count} args)");

        try
        {
            using var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                return new ProcessRunResult(-1, string.Empty, "Failed to start process.",
                    sw.Elapsed, false, false);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (timeout.HasValue)
                timeoutCts.CancelAfter(timeout.Value);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // process may have exited in the meantime
                }

                var timedOut = !ct.IsCancellationRequested;
                return new ProcessRunResult(
                    -1,
                    await SafeRead(stdoutTask),
                    timedOut ? "Command timed out." : "Command cancelled.",
                    sw.Elapsed, timedOut, true);
            }

            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            return new ProcessRunResult(
                process.ExitCode, stdout, stderr, sw.Elapsed, false, true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.Log(LogLevel.Error, "ProcessRunner",
                $"Failed to run {executable}", ex);
            return new ProcessRunResult(
                -1, string.Empty, ex.Message, sw.Elapsed, false, false);
        }
    }

    private static async Task<string> SafeRead(Task<string> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }
}
