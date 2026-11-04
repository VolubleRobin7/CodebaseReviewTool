using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace CodebaseReviewTool.Core.Processes;

public sealed class ProcessRunner(ILogger<ProcessRunner> logger) : IProcessRunner
{
    /// <summary>
    /// Environment variables removed from every child. <c>CLAUDECODE</c> makes the Claude CLI refuse to start when
    /// the tool itself was launched from inside a Claude Code session.
    /// </summary>
    private static readonly string[] _strippedEnvironmentVariables = ["CLAUDECODE"];

    private static readonly UTF8Encoding _utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private readonly ILogger<ProcessRunner> _logger = logger;

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = new ProcessStartInfo(request.FileName)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = request.StandardInput is not null,
            StandardOutputEncoding = _utf8NoBom,
            StandardErrorEncoding = _utf8NoBom,
        };

        if (request.StandardInput is not null)
        {
            startInfo.StandardInputEncoding = _utf8NoBom;
        }

        if (!string.IsNullOrEmpty(request.WorkingDirectory))
        {
            startInfo.WorkingDirectory = request.WorkingDirectory;
        }

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        foreach (var name in _strippedEnvironmentVariables)
        {
            startInfo.Environment.Remove(name);
        }

        using var process = new Process { StartInfo = startInfo };
        var stopwatch = Stopwatch.StartNew();

        process.Start();
        _logger.LogDebug("Started {FileName} (pid {ProcessId}) in {WorkingDirectory}", request.FileName, process.Id, request.WorkingDirectory);

        // Read both streams concurrently so neither pipe fills up and blocks the child.
        var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var timeoutSource = request.Timeout is { } timeout ? new CancellationTokenSource(timeout) : new CancellationTokenSource();
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutSource.Token);

        var timedOut = false;
        try
        {
            if (request.StandardInput is not null)
            {
                await process.StandardInput.WriteAsync(request.StandardInput.AsMemory(), linkedSource.Token);
                process.StandardInput.Close();
            }

            await process.WaitForExitAsync(linkedSource.Token);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            timedOut = true;
        }
        catch (IOException) when (process.HasExited)
        {
            // The child exited before reading all of stdin (broken pipe); its exit code and output still tell the story.
            await process.WaitForExitAsync(CancellationToken.None);
        }

        // Once the process has exited (or been killed) the pipes close and these complete.
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        stopwatch.Stop();

        var exitCode = timedOut ? -1 : process.ExitCode;
        if (timedOut)
        {
            _logger.LogWarning("{FileName} timed out after {Timeout} and was killed", request.FileName, request.Timeout);
        }

        return new ProcessResult(exitCode, stdout, stderr, stopwatch.Elapsed, timedOut);
    }

    private void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.WaitForExit(TimeSpan.FromSeconds(10));
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            _logger.LogWarning(ex, "Could not kill process {ProcessId}", process.Id);
        }
    }
}
