using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace KittyClaw.Core.Automation.Runtimes;

public sealed class ProcessRunner
{
    private readonly ILogger<ProcessRunner>? _logger;

    public ProcessRunner(ILogger<ProcessRunner>? logger = null)
    {
        _logger = logger;
    }

    public async Task<ProcessRunResult> RunAsync(ProcessRunRequest request, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var psi = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        foreach (var arg in request.Arguments)
            psi.ArgumentList.Add(arg);

        foreach (var (key, value) in request.Environment)
            psi.Environment[key] = value;

        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {request.FileName}");

        var job = ProcessJobObject.TryCreateAndAssign(proc);
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(request.Timeout);

            if (!string.IsNullOrEmpty(request.StandardInput))
            {
                try
                {
                    await proc.StandardInput.WriteAsync(request.StandardInput);
                    await proc.StandardInput.FlushAsync();
                    proc.StandardInput.Close();
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Failed to write stdin to process {FileName}", request.FileName);
                }
            }
            else
            {
                try { proc.StandardInput.Close(); } catch { /* best-effort */ }
            }

            var stdoutTask = proc.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = proc.StandardError.ReadToEndAsync(cts.Token);

            using var killReg = cts.Token.Register(() =>
            {
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* cleanup */ }
                job?.Dispose(); // kill descendants on Windows
            });

            int exitCode;
            bool timedOut = false;
            try
            {
                await proc.WaitForExitAsync(cts.Token);
                exitCode = proc.ExitCode;
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    // Genuine cancellation from the caller
                    try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
                    job?.Dispose();
                    throw;
                }
                // Timeout fired
                try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { }
                job?.Dispose();
                timedOut = true;
                exitCode = -1;
            }

            var stdout = await stdoutTask;
            var stderr = await stderrTask;

            var finishedAt = DateTimeOffset.UtcNow;
            return new ProcessRunResult(exitCode, stdout, stderr, startedAt, finishedAt, timedOut);
        }
        finally
        {
            job?.Dispose();
            if (!proc.HasExited)
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            }
        }
    }
}
