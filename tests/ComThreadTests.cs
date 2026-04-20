using MppMcp;

namespace MppMcp.Tests;

public class ComThreadTests : IDisposable
{
    private readonly ComThread _thread = new();

    public void Dispose() => _thread.Dispose();

    [Fact]
    public async Task InvokeAsync_ReturnsValue()
    {
        var result = await _thread.InvokeAsync(() => 42);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task InvokeAsync_PropagatesException()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _thread.InvokeAsync<int>(() => throw new InvalidOperationException("boom")));
    }

    [Fact]
    public async Task InvokeAsync_Action_Completes()
    {
        var flag = false;
        await _thread.InvokeAsync(() => { flag = true; });
        Assert.True(flag);
    }

    [Fact]
    public async Task InvokeAsync_RunsOnStaThread()
    {
        var state = await _thread.InvokeAsync(() => Thread.CurrentThread.GetApartmentState());
        Assert.Equal(ApartmentState.STA, state);
    }

    [Fact]
    public async Task InvokeAsync_RunsOnSameThread()
    {
        var id1 = await _thread.InvokeAsync(() => Environment.CurrentManagedThreadId);
        var id2 = await _thread.InvokeAsync(() => Environment.CurrentManagedThreadId);
        Assert.Equal(id1, id2);
    }

    [Fact]
    public async Task InvokeAsync_ConcurrentCalls_AllComplete()
    {
        var tasks = Enumerable.Range(0, 10)
            .Select(i => _thread.InvokeAsync(() => i * 2))
            .ToList();

        var results = await Task.WhenAll(tasks);
        Assert.Equal(Enumerable.Range(0, 10).Select(i => i * 2), results);
    }
}
