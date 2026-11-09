namespace CodebaseReviewTool.Host.Tray;

/// <summary>
/// Holds the per-user named mutex that marks a running instance. The name carries the port, so an installed copy and
/// a development copy on different ports run side by side while a second copy on the same port is refused. The mutex
/// is in the session's <c>Local\</c> namespace, so other users on the same machine can run their own instance.
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;

    private SingleInstance(Mutex mutex) => _mutex = mutex;

    /// <summary>
    /// Returns the guard when this is the only instance on the port of <paramref name="appUrl"/>, or null when another
    /// instance already holds it.
    /// </summary>
    public static SingleInstance? TryAcquire(string appUrl)
    {
        var port = Uri.TryCreate(appUrl, UriKind.Absolute, out var uri) ? uri.Port : 0;
        var mutex = new Mutex(initiallyOwned: true, $@"Local\CodebaseReviewTool.Host.{port}", out var createdNew);
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
