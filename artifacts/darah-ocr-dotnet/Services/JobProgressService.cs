namespace DarahOcr.Services;

public record JobProgressEvent(int JobId, int Page, int TotalPages, string Status);

public class JobProgressService
{
    private readonly List<(Func<JobProgressEvent, Task> handler, int? userId)> _subscribers = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<IAsyncDisposable> SubscribeAsync(Func<JobProgressEvent, Task> handler, int? userId = null)
    {
        await _lock.WaitAsync();
        try { _subscribers.Add((handler, userId)); }
        finally { _lock.Release(); }

        return new Unsubscriber(this, handler);
    }

    public async Task BroadcastAsync(JobProgressEvent evt)
    {
        await _lock.WaitAsync();
        List<Func<JobProgressEvent, Task>> toNotify;
        try { toNotify = [.._subscribers.Select(s => s.handler)]; }
        finally { _lock.Release(); }

        var tasks = toNotify.Select(async h =>
        {
            try { await h(evt); } catch { }
        });
        await Task.WhenAll(tasks);
    }

    public async Task UnsubscribeAsync(Func<JobProgressEvent, Task> handler)
    {
        await _lock.WaitAsync();
        try { _subscribers.RemoveAll(s => s.handler == handler); }
        finally { _lock.Release(); }
    }

    private class Unsubscriber(JobProgressService svc, Func<JobProgressEvent, Task> handler) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync() => await svc.UnsubscribeAsync(handler);
    }
}
