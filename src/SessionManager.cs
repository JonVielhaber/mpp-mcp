namespace MppMcp;

public sealed class SessionManager
{
    private readonly ComThread _comThread;
    private readonly TimeSpan _idleTimeout;

    public ComThread ComThread => _comThread;

    public SessionManager(ComThread comThread, TimeSpan idleTimeout)
    {
        _comThread = comThread;
        _idleTimeout = idleTimeout;
    }
}
