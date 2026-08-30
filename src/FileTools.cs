using System.ComponentModel;
using System.Runtime.InteropServices;
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
        var phase = "get_session";
        bool? activationRequired = null;
        string? activeProjectFullNameBeforeClose = null;
        string? targetSessionFullName = null;
        bool? activeProjectAlreadyMatchedTarget = null;
        var fallbackActivationAttempted = false;
        var projectActivateCompleted = false;
        var fileCloseExInvoked = false;
        bool? fileCloseExReturnValue = null;
        var sessionRemovalCompleted = false;
        var applicationShutdownCompleted = false;

        try
        {
            var session = sessions.GetSession(sessionId);
            targetSessionFullName = NormalizeProjectPath(session.FilePath);

            phase = "project_inspect_active";
            await sessions.ComThread.InvokeAsync(() =>
            {
                dynamic trackedProject = session.Project;
                dynamic application = trackedProject.Application;
                object? activeProject = application.ActiveProject;
                activeProjectFullNameBeforeClose = GetProjectFullName(activeProject);
                activeProjectAlreadyMatchedTarget = ProjectPathsMatch(
                    activeProjectFullNameBeforeClose,
                    targetSessionFullName);
                activationRequired = !activeProjectAlreadyMatchedTarget.Value;

                if (activationRequired.Value)
                {
                    fallbackActivationAttempted = true;
                    phase = "project_find";
                    var targetProject = FindProjectByFullName(application, targetSessionFullName)
                        ?? throw new InvalidOperationException(
                            "The tracked project could not be found in the Microsoft Project application. " +
                            "FileCloseEx was not invoked to avoid closing the wrong project.");

                    phase = "project_activate";
                    dynamic projectToActivate = targetProject;
                    projectToActivate.Activate();

                    phase = "project_verify_active";
                    object? activeProjectAfterActivation = application.ActiveProject;
                    var activeProjectFullNameAfterActivation =
                        GetProjectFullName(activeProjectAfterActivation);
                    if (!ProjectPathsMatch(activeProjectFullNameAfterActivation, targetSessionFullName))
                    {
                        throw new InvalidOperationException(
                            "Microsoft Project did not activate the tracked project. " +
                            "FileCloseEx was not invoked to avoid closing the wrong project.");
                    }
                }

                projectActivateCompleted = true;

                phase = "file_close_ex";
                fileCloseExInvoked = true;
                fileCloseExReturnValue = (bool)application.FileCloseEx(save ? 1 : 0); // 1=pjSave, 0=pjDoNotSave
            });

            phase = "session_removal";
            sessions.RemoveSession(sessionId);
            sessionRemovalCompleted = true;

            phase = "application_shutdown";
            applicationShutdownCompleted = await sessions.QuitAppIfNoSessionsAsync(reportFailure: true);

            return JsonSerializer.Serialize(new
            {
                closed = true,
                saved = save,
                activationRequired,
                activeProjectFullNameBeforeClose,
                targetSessionFullName,
                activeProjectAlreadyMatchedTarget,
                fallbackActivationAttempted,
                projectActivateCompleted,
                fileCloseExReturnValue,
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                closed = false,
                saved = save,
                failedPhase = phase,
                exceptionType = ex.GetType().FullName,
                exceptionMessage = ex.Message,
                comHResult = GetComHResult(ex),
                activationRequired,
                activeProjectFullNameBeforeClose,
                targetSessionFullName,
                activeProjectAlreadyMatchedTarget,
                fallbackActivationAttempted,
                projectActivateCompleted,
                fileCloseExInvoked,
                fileCloseExReturnValue,
                sessionRemovalCompleted,
                applicationShutdownCompleted,
            });
        }
    }

    private static object? FindProjectByFullName(dynamic application, string targetFullName)
    {
        dynamic projects = application.Projects;
        var projectCount = (int)projects.Count;
        for (var index = 1; index <= projectCount; index++)
        {
            object? candidate = projects.Item(index);
            if (candidate != null && ProjectPathsMatch(GetProjectFullName(candidate), targetFullName))
                return candidate;
        }

        return null;
    }

    private static string? GetProjectFullName(object? project)
    {
        if (project == null)
            return null;

        dynamic projectProxy = project;
        string? fullName = projectProxy.FullName;
        return string.IsNullOrWhiteSpace(fullName) ? null : NormalizeProjectPath(fullName);
    }

    private static bool ProjectPathsMatch(string? projectFullName, string targetFullName) =>
        projectFullName != null &&
        string.Equals(projectFullName, targetFullName, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeProjectPath(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string? GetComHResult(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            if (current is COMException comException)
                return $"0x{unchecked((uint)comException.HResult):X8}";
        }

        return null;
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

        session.FilePath = fullPath;
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
