namespace YouTubeViewer;

public sealed class ViewerInstance : IDisposable
{
    private readonly Mutex _mutex;
    private bool _owned;

    public ViewerInstance(string name = @"Local\Dimosfil.VideoMem.YouTubeViewer.Session") =>
        _mutex = new Mutex(false, name);

    // Acquire and dispose on the UI thread: Windows mutex ownership is thread-bound.
    public bool TryAcquire()
    {
        if (_owned) return true;
        try
        {
            _owned = _mutex.WaitOne(0);
        }
        catch (AbandonedMutexException)
        {
            _owned = true;
        }
        return _owned;
    }

    public void Dispose()
    {
        if (_owned)
        {
            _mutex.ReleaseMutex();
            _owned = false;
        }
        _mutex.Dispose();
    }
}
