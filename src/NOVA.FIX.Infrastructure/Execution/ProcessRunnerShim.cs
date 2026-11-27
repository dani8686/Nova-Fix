using System.Diagnostics;

namespace NOVA.FIX.Infrastructure.Execution;

/// <summary>
/// Static entry point used by modules that do not hold a ProcessRunner instance
/// (updates, recovery). Same safety rules: fixed executable, argument arrays,
/// timeout, captured output — no shell string construction.
/// </summary>
public static class ProcessRunnerShim
{
    public static async Task<ProcessRunResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        var psi = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in arguments) psi.ArgumentList.Add(a);

        try
        {
            using var process = new Process { StartInfo = psi };
            if (!process.Start())
            {
                return new ProcessRunResult(-1, "", "Failed to start.",
                    sw.Elapsed, false, false);
            }

            var outTask = process.StandardOutput.ReadToEndAsync(ct);
            var errTask = process.StandardError.ReadToEndAsync(ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            if (timeout.HasValue) timeoutCts.CancelAfter(timeout.Value);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // already exited
                }
                var timedOut = !ct.IsCancellationRequested;
                return new ProcessRunResult(-1,
                    await SafeRead(outTask),
                    timedOut ? "Timed out." : "Cancelled.",
                    sw.Elapsed, timedOut, true);
            }

            return new ProcessRunResult(process.ExitCode,
                await outTask.ConfigureAwait(false),
                await errTask.ConfigureAwait(false),
                sw.Elapsed, false, true);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new ProcessRunResult(-1, "", ex.Message, sw.Elapsed, false, false);
        }
    }

    private static async Task<string> SafeRead(Task<string> t)
    {
        try { return await t.ConfigureAwait(false); }
        catch { return string.Empty; }
    }
}
