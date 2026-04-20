using System.Collections.Concurrent;

namespace MppMcp;

public sealed class ComThread : IDisposable
{
    private readonly Thread _thread;
    private readonly BlockingCollection<Action> _queue = new();
    private int _disposed;

    public ComThread()
    {
        _thread = new Thread(Run)
        {
            Name = "MppMcp-COM",
            IsBackground = true,
        };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
    }

    private void Run()
    {
        foreach (var action in _queue.GetConsumingEnumerable())
        {
            action();
        }
    }

    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            _queue.Add(() =>
            {
                try
                {
                    tcs.SetResult(func());
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });
        }
        catch (InvalidOperationException)
        {
            tcs.SetException(new ObjectDisposedException(nameof(ComThread)));
        }
        return tcs.Task;
    }

    public Task InvokeAsync(Action action) =>
        InvokeAsync<object?>(() => { action(); return null; });

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _queue.CompleteAdding();
        if (!_thread.Join(TimeSpan.FromSeconds(5)))
            Console.Error.WriteLine("ComThread: worker thread did not exit within timeout.");
        _queue.Dispose();
    }
}
