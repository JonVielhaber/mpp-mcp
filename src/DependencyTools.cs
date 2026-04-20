using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace MppMcp;

[McpServerToolType]
public static class DependencyTools
{
    private const int PjFinishToFinish = 0;
    private const int PjFinishToStart = 1;
    private const int PjStartToStart = 2;
    private const int PjStartToFinish = 3;

    [McpServerTool(Name = "dependency"), Description(
        "Manage task dependencies (links) in a project. " +
        "Actions: add, remove, list. " +
        "Types: FS (Finish-to-Start, default), SS, FF, SF. " +
        "Optional lag in duration format (e.g. '2d', '-1d').")]
    public static async Task<string> Dependency(
        SessionManager sessions,
        [Description("Action: add, remove, list")] string action,
        [Description("Session ID")] string sessionId,
        [Description("Successor task UniqueID — the task that depends on another")] int uniqueId,
        [Description("Predecessor task UniqueID — required for add, remove")] int? predecessorUniqueId = null,
        [Description("Dependency type: FS, SS, FF, SF (default: FS)")] string type = "FS",
        [Description("Lag duration, e.g. '2d', '-1d' (default: no lag)")] string? lag = null)
    {
        var session = sessions.GetSession(sessionId);
        sessions.IncrementOps(sessionId);

        try
        {
            return action.ToLowerInvariant() switch
            {
                "add" => await AddAsync(sessions, session, uniqueId,
                    predecessorUniqueId ?? throw new ArgumentException("predecessorUniqueId is required for add"),
                    type, lag),
                "remove" => await RemoveAsync(sessions, session, uniqueId,
                    predecessorUniqueId ?? throw new ArgumentException("predecessorUniqueId is required for remove")),
                "list" => await ListAsync(sessions, session, uniqueId),
                _ => throw new ArgumentException($"Unknown action: {action}. Use: add, remove, list"),
            };
        }
        finally
        {
            sessions.DecrementOps(sessionId);
        }
    }

    [McpServerTool(Name = "critical_path", ReadOnly = true), Description(
        "Get the critical path — the chain of tasks that determines the project end date.")]
    public static async Task<string> CriticalPath(
        SessionManager sessions,
        [Description("Session ID")] string sessionId)
    {
        var session = sessions.GetSession(sessionId);
        sessions.IncrementOps(sessionId);

        try
        {
            return await sessions.ComThread.InvokeAsync(() =>
            {
                dynamic proj = session.Project;
                var criticalTasks = new List<object>();

                foreach (dynamic task in proj.Tasks)
                {
                    if (task == null) continue;
                    if (!(bool)task.Critical) continue;

                    criticalTasks.Add(new
                    {
                        uniqueId = (int)task.UniqueID,
                        id = (int)task.ID,
                        name = (string)task.Name,
                        duration = (string)task.Duration.ToString(),
                        start = ((DateTime)task.Start).ToString("yyyy-MM-dd"),
                        finish = ((DateTime)task.Finish).ToString("yyyy-MM-dd"),
                        predecessors = (string)(task.Predecessors ?? ""),
                        successors = (string)(task.Successors ?? ""),
                    });
                }

                return JsonSerializer.Serialize(new
                {
                    projectFinish = ((DateTime)proj.ProjectFinish).ToString("yyyy-MM-dd"),
                    criticalTaskCount = criticalTasks.Count,
                    tasks = criticalTasks,
                });
            });
        }
        finally
        {
            sessions.DecrementOps(sessionId);
        }
    }

    private static int ParseDependencyType(string type) => type.ToUpperInvariant() switch
    {
        "FS" => PjFinishToStart,
        "FF" => PjFinishToFinish,
        "SS" => PjStartToStart,
        "SF" => PjStartToFinish,
        _ => throw new ArgumentException($"Unknown dependency type: {type}. Use: FS, SS, FF, SF"),
    };

    private static string FormatDependencyType(int type) => type switch
    {
        PjFinishToFinish => "FF",
        PjFinishToStart => "FS",
        PjStartToStart => "SS",
        PjStartToFinish => "SF",
        _ => "Unknown",
    };

    private static async Task<string> AddAsync(
        SessionManager sessions, SessionManager.ProjectSession session,
        int successorUniqueId, int predecessorUniqueId, string type, string? lag)
    {
        return await sessions.ComThread.InvokeAsync(() =>
        {
            dynamic proj = session.Project;
            dynamic successor = TaskTools.FindTaskByUniqueId(proj, successorUniqueId);
            dynamic predecessor = TaskTools.FindTaskByUniqueId(proj, predecessorUniqueId);

            int depType = ParseDependencyType(type);
            dynamic dep = successor.TaskDependencies.Add(predecessor, depType);

            if (lag != null) dep.Lag = lag;

            return JsonSerializer.Serialize(new
            {
                successor = new { uniqueId = (int)successor.UniqueID, name = (string)successor.Name },
                predecessor = new { uniqueId = (int)predecessor.UniqueID, name = (string)predecessor.Name },
                type = FormatDependencyType(depType),
                lag = lag ?? "0d",
            });
        });
    }

    private static async Task<string> RemoveAsync(
        SessionManager sessions, SessionManager.ProjectSession session,
        int successorUniqueId, int predecessorUniqueId)
    {
        return await sessions.ComThread.InvokeAsync(() =>
        {
            dynamic proj = session.Project;
            dynamic successor = TaskTools.FindTaskByUniqueId(proj, successorUniqueId);

            foreach (dynamic dep in successor.TaskDependencies)
            {
                if ((int)dep.From.UniqueID == predecessorUniqueId)
                {
                    dep.Delete();
                    return JsonSerializer.Serialize(new
                    {
                        removed = true,
                        successorUniqueId,
                        predecessorUniqueId,
                    });
                }
            }

            throw new KeyNotFoundException(
                $"No dependency from task {predecessorUniqueId} to task {successorUniqueId}");
        });
    }

    private static async Task<string> ListAsync(
        SessionManager sessions, SessionManager.ProjectSession session, int uniqueId)
    {
        return await sessions.ComThread.InvokeAsync(() =>
        {
            dynamic proj = session.Project;
            dynamic task = TaskTools.FindTaskByUniqueId(proj, uniqueId);

            var predecessors = new List<object>();
            var successors = new List<object>();

            foreach (dynamic dep in task.TaskDependencies)
            {
                var entry = new
                {
                    uniqueId = (int)dep.From.UniqueID,
                    name = (string)dep.From.Name,
                    type = FormatDependencyType((int)dep.Type),
                    lag = (string)dep.Lag.ToString(),
                };

                if ((int)dep.From.UniqueID != uniqueId)
                    predecessors.Add(entry);
                else
                    successors.Add(new
                    {
                        uniqueId = (int)dep.To.UniqueID,
                        name = (string)dep.To.Name,
                        type = FormatDependencyType((int)dep.Type),
                        lag = (string)dep.Lag.ToString(),
                    });
            }

            return JsonSerializer.Serialize(new
            {
                taskUniqueId = uniqueId,
                taskName = (string)task.Name,
                predecessors,
                successors,
            });
        });
    }
}
