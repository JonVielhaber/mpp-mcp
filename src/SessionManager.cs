using System.Collections.Concurrent;

namespace MppMcp;

public sealed class SessionManager
{
    private readonly ComThread _comThread;
    private readonly TimeSpan _idleTimeout;
    private readonly ConcurrentDictionary<string, ProjectSession> _sessions = new();
    private readonly Timer _sweepTimer;

    // COM Application — lazily created on first open, released when last session closes
    private dynamic? _app;

    public ComThread ComThread => _comThread;
    public int SessionCount => _sessions.Count;

    public SessionManager(ComThread comThread, TimeSpan idleTimeout)
    {
        _comThread = comThread;
        _idleTimeout = idleTimeout;
        _sweepTimer = new Timer(SweepIdle, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public async Task<dynamic> EnsureAppAsync()
    {
        if (_app != null) return _app;

        _app = await _comThread.InvokeAsync(() =>
        {
            var appType = Type.GetTypeFromProgID("MSProject.Application")
                ?? throw new InvalidOperationException(
                    "Microsoft Project is not installed. ProgID 'MSProject.Application' not found.");
            dynamic app = Activator.CreateInstance(appType)!;
            app.Visible = false;
            return app;
        });

        return _app;
    }

    public string TrackSession(string filePath, object project)
    {
        var sessionId = Guid.NewGuid().ToString("N")[..12];
        var session = new ProjectSession(sessionId, filePath, project, DateTime.UtcNow, 0);
        _sessions[sessionId] = session;
        return sessionId;
    }

    public ProjectSession GetSession(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            throw new KeyNotFoundException($"Session '{sessionId}' not found");

        session.LastAccessed = DateTime.UtcNow;
        return session;
    }

    public void RemoveSession(string sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
            return;

        if (session.ActiveOps > 0)
            throw new InvalidOperationException(
                $"Session '{sessionId}' has {session.ActiveOps} active operations. Wait for them to complete.");

        _sessions.TryRemove(sessionId, out _);
    }

    public void IncrementOps(string sessionId)
    {
        var session = GetSession(sessionId);
        Interlocked.Increment(ref session.ActiveOps);
    }

    public void DecrementOps(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
            Interlocked.Decrement(ref session.ActiveOps);
    }

    public async Task QuitAppIfNoSessionsAsync()
    {
        if (_sessions.IsEmpty && _app != null)
        {
            await _comThread.InvokeAsync(() =>
            {
                try { _app!.Quit(); } catch { /* already closed */ }
            });
            _app = null;
        }
    }

    private void SweepIdle(object? state)
    {
        var cutoff = DateTime.UtcNow - _idleTimeout;
        foreach (var (id, session) in _sessions)
        {
            if (session.LastAccessed < cutoff && session.ActiveOps == 0)
            {
                _sessions.TryRemove(id, out _);
                _ = _comThread.InvokeAsync(() =>
                {
                    try
                    {
                        dynamic proj = session.Project;
                        proj.Activate();
                        proj.Application.FileCloseEx(0); // pjDoNotSave
                    }
                    catch { /* best effort */ }
                });
            }
        }

        _ = QuitAppIfNoSessionsAsync();
    }

    public class ProjectSession
    {
        public string SessionId { get; }
        public string FilePath { get; set; }
        public object Project { get; }
        public DateTime LastAccessed { get; set; }
        public int ActiveOps;

        public ProjectSession(string sessionId, string filePath, object project, DateTime lastAccessed, int activeOps)
        {
            SessionId = sessionId;
            FilePath = filePath;
            Project = project;
            LastAccessed = lastAccessed;
            ActiveOps = activeOps;
        }
    }
}
