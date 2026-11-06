namespace CodebaseReviewTool.Host.Tray;

/// <summary>
/// Holds the per-user named mutex that marks a running instance. The mutex is in the session's <c>Local\</c>
/// namespace, so other users on the same machine can run their own instance.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\CodebaseReviewTool.Host";

    private readonly Mutex _mutex;

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>Returns the guard when this is the only instance, or null when another instance already holds it.</summary>
    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (createdNew)
        {
            return new SingleInstance(mutex);
        }

        try
        {
            if (mutex.WaitOne(TimeSpan.Zero))
            {
                return new SingleInstance(mutex);
            }
        }
        catch (AbandonedMutexException)
        {
            // The previous owner ended without releasing it; the wait still handed ownership to this process.
            return new SingleInstance(mutex);
        }

        mutex.Dispose();
        return null;
    }

    /// <summary>Releases the mutex; call on the thread that acquired it.</summary>
    public void Dispose()
    {
        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Not owned by this thread; closing the handle below frees it when the process ends.
        }

        _mutex.Dispose();
    }
}
