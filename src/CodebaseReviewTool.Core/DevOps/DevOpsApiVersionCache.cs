namespace CodebaseReviewTool.Core.DevOps;

/// <summary>
/// Singleton holding the detected api-version. <see cref="DevOpsClient"/> is a transient typed client, so the cache
/// lives here; the semaphore makes concurrent first callers share one probe.
/// </summary>
internal sealed class DevOpsApiVersionCache : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile string? _version;

    public string? Version => _version;

    public async Task<DevOpsApiVersionResult> GetOrDetectAsync(
        Func<CancellationToken, Task<DevOpsApiVersionResult>> detect,
        CancellationToken cancellationToken)
    {
        if (_version is { } cached)
        {
            return DevOpsApiVersionResult.Detected(cached);
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_version is { } detectedMeanwhile)
            {
                return DevOpsApiVersionResult.Detected(detectedMeanwhile);
            }

            var result = await detect(cancellationToken);
            if (result.Succeeded)
            {
                // Only successes are cached, so fixing the PAT or the server makes the next call try again.
                _version = result.Version;
            }

            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose() => _gate.Dispose();
}
