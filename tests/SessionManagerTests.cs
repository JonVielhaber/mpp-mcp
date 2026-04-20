using MppMcp;

namespace MppMcp.Tests;

public class SessionManagerTests : IDisposable
{
    private readonly ComThread _comThread = new();

    public void Dispose() => _comThread.Dispose();

    [Fact]
    public void GetSession_UnknownId_Throws()
    {
        var mgr = new SessionManager(_comThread, TimeSpan.FromMinutes(5));
        Assert.Throws<KeyNotFoundException>(() => mgr.GetSession("nonexistent"));
    }

    [Fact]
    public void TrackSession_And_GetSession_RoundTrips()
    {
        var mgr = new SessionManager(_comThread, TimeSpan.FromMinutes(5));
        var fakeProject = new object();
        var sessionId = mgr.TrackSession("test.mpp", fakeProject);

        var session = mgr.GetSession(sessionId);
        Assert.Equal("test.mpp", session.FilePath);
        Assert.Same(fakeProject, session.Project);
    }

    [Fact]
    public void RemoveSession_RemovesIt()
    {
        var mgr = new SessionManager(_comThread, TimeSpan.FromMinutes(5));
        var sessionId = mgr.TrackSession("test.mpp", new object());

        mgr.RemoveSession(sessionId);
        Assert.Throws<KeyNotFoundException>(() => mgr.GetSession(sessionId));
    }

    [Fact]
    public void IncrementOps_BlocksRemoval()
    {
        var mgr = new SessionManager(_comThread, TimeSpan.FromMinutes(5));
        var sessionId = mgr.TrackSession("test.mpp", new object());

        mgr.IncrementOps(sessionId);
        Assert.Throws<InvalidOperationException>(() => mgr.RemoveSession(sessionId));
    }

    [Fact]
    public void DecrementOps_AllowsRemoval()
    {
        var mgr = new SessionManager(_comThread, TimeSpan.FromMinutes(5));
        var sessionId = mgr.TrackSession("test.mpp", new object());

        mgr.IncrementOps(sessionId);
        mgr.DecrementOps(sessionId);
        mgr.RemoveSession(sessionId); // should not throw
    }

    [Fact]
    public void GetSession_TouchesLastAccessed()
    {
        var mgr = new SessionManager(_comThread, TimeSpan.FromMinutes(5));
        var sessionId = mgr.TrackSession("test.mpp", new object());

        var before = mgr.GetSession(sessionId).LastAccessed;
        Thread.Sleep(20);
        mgr.GetSession(sessionId); // touch
        var after = mgr.GetSession(sessionId).LastAccessed;

        Assert.True(after > before);
    }

    [Fact]
    public void SessionCount_TracksCorrectly()
    {
        var mgr = new SessionManager(_comThread, TimeSpan.FromMinutes(5));
        Assert.Equal(0, mgr.SessionCount);

        var id1 = mgr.TrackSession("a.mpp", new object());
        Assert.Equal(1, mgr.SessionCount);

        var id2 = mgr.TrackSession("b.mpp", new object());
        Assert.Equal(2, mgr.SessionCount);

        mgr.RemoveSession(id1);
        Assert.Equal(1, mgr.SessionCount);
    }
}
