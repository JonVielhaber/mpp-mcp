using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace MppMcp;

[McpServerToolType]
public static class FileTools
{
    [McpServerTool(Name = "file"), Description(
        "Open, close, save, or create Microsoft Project (.mpp) files. " +
        "Actions: open (returns sessionId), close (with save flag), save, save_as, create (returns sessionId). " +
        "CRITICAL: File must be CLOSED in MS Project desktop app before opening.")]
    public static async Task<string> File(
        SessionManager sessions,
        [Description("Action to perform: open, close, save, save_as, create")] string action,
        [Description("File path — required for open, save_as, create")] string? path = null,
        [Description("Session ID — required for close, save, save_as")] string? sessionId = null,
        [Description("Save changes before closing — for close action (default: false)")] bool save = false)
    {
        return action.ToLowerInvariant() switch
        {
            "open" => await OpenAsync(sessions, path
                ?? throw new ArgumentException("path is required for open")),
            "close" => await CloseAsync(sessions, sessionId
                ?? throw new ArgumentException("sessionId is required for close"), save),
            "save" => await SaveAsync(sessions, sessionId
                ?? throw new ArgumentException("sessionId is required for save")),
            "save_as" => await SaveAsAsync(sessions,
                sessionId ?? throw new ArgumentException("sessionId is required for save_as"),
                path ?? throw new ArgumentException("path is required for save_as")),
            "create" => await CreateAsync(sessions, path
                ?? throw new ArgumentException("path is required for create")),
            _ => throw new ArgumentException($"Unknown action: {action}. Use: open, close, save, save_as, create"),
        };
    }

    private static async Task<string> OpenAsync(SessionManager sessions, string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!System.IO.File.Exists(fullPath))
            throw new FileNotFoundException($"File not found: {fullPath}");

        var app = await sessions.EnsureAppAsync();
        var project = await sessions.ComThread.InvokeAsync(() =>
        {
            app.FileOpenEx(fullPath);
            return app.ActiveProject;
        });

        var sessionId = sessions.TrackSession(fullPath, project);
        return JsonSerializer.Serialize(new { sessionId, path = fullPath });
    }

    private static async Task<string> CloseAsync(SessionManager sessions, string sessionId, bool save)
    {
        var session = sessions.GetSession(sessionId);

        await sessions.ComThread.InvokeAsync(() =>
        {
            dynamic proj = session.Project;
            proj.Application.FileCloseEx(save ? 1 : 0); // 1=pjSave, 0=pjDoNotSave
        });

        sessions.RemoveSession(sessionId);
        await sessions.QuitAppIfNoSessionsAsync();
        return JsonSerializer.Serialize(new { closed = true, saved = save });
    }

    private static async Task<string> SaveAsync(SessionManager sessions, string sessionId)
    {
        var session = sessions.GetSession(sessionId);

        await sessions.ComThread.InvokeAsync(() =>
        {
            dynamic proj = session.Project;
            proj.Application.FileSave();
        });

        return JsonSerializer.Serialize(new { saved = true, path = session.FilePath });
    }

    private static async Task<string> SaveAsAsync(SessionManager sessions, string sessionId, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var session = sessions.GetSession(sessionId);

        await sessions.ComThread.InvokeAsync(() =>
        {
            dynamic proj = session.Project;
            proj.Application.FileSaveAs(fullPath);
        });

        return JsonSerializer.Serialize(new { saved = true, path = fullPath });
    }

    private static async Task<string> CreateAsync(SessionManager sessions, string path)
    {
        var fullPath = Path.GetFullPath(path);
        var app = await sessions.EnsureAppAsync();

        var project = await sessions.ComThread.InvokeAsync(() =>
        {
            app.FileNew();
            var proj = app.ActiveProject;
            app.FileSaveAs(fullPath);
            return proj;
        });

        var sessionId = sessions.TrackSession(fullPath, project);
        return JsonSerializer.Serialize(new { sessionId, path = fullPath });
    }
}
