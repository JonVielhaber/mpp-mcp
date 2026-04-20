using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace MppMcp;

[McpServerToolType]
public static class TaskTools
{
    [McpServerTool(Name = "task"), Description(
        "Single-task operations on a Microsoft Project file. " +
        "Actions: add, get, update, delete. " +
        "Tasks are identified by UniqueID (stable), not row number.")]
    public static async Task<string> Task(
        SessionManager sessions,
        [Description("Action: add, get, update, delete")] string action,
        [Description("Session ID from file open/create")] string sessionId,
        [Description("Task UniqueID — required for get, update, delete")] int? uniqueId = null,
        [Description("Task name")] string? name = null,
        [Description("Duration string, e.g. '5d', '2w', '8h'")] string? duration = null,
        [Description("Start date (ISO 8601, e.g. '2026-05-01')")] string? start = null,
        [Description("Finish date (ISO 8601)")] string? finish = null,
        [Description("Percent complete (0-100)")] int? percentComplete = null,
        [Description("Is milestone")] bool? milestone = null,
        [Description("Notes text")] string? notes = null,
        [Description("Insert before this row number (for add; default: end)")] int? beforeRow = null)
    {
        var session = sessions.GetSession(sessionId);
        sessions.IncrementOps(sessionId);

        try
        {
            return action.ToLowerInvariant() switch
            {
                "add" => await AddAsync(sessions, session, name
                    ?? throw new ArgumentException("name is required for add"),
                    duration, start, finish, percentComplete, milestone, notes, beforeRow),
                "get" => await GetAsync(sessions, session, uniqueId
                    ?? throw new ArgumentException("uniqueId is required for get")),
                "update" => await UpdateAsync(sessions, session, uniqueId
                    ?? throw new ArgumentException("uniqueId is required for update"),
                    name, duration, start, finish, percentComplete, milestone, notes),
                "delete" => await DeleteAsync(sessions, session, uniqueId
                    ?? throw new ArgumentException("uniqueId is required for delete")),
                _ => throw new ArgumentException($"Unknown action: {action}. Use: add, get, update, delete"),
            };
        }
        finally
        {
            sessions.DecrementOps(sessionId);
        }
    }

    [McpServerTool(Name = "tasks"), Description(
        "Bulk read operations on project tasks. " +
        "Actions: list (all tasks with optional filters), summary (rollup stats).")]
    public static async Task<string> Tasks(
        SessionManager sessions,
        [Description("Action: list, summary")] string action,
        [Description("Session ID from file open/create")] string sessionId,
        [Description("Filter by outline level (for list)")] int? outlineLevel = null,
        [Description("Filter: minimum percent complete (for list)")] int? minPercentComplete = null,
        [Description("Filter: maximum percent complete (for list)")] int? maxPercentComplete = null)
    {
        var session = sessions.GetSession(sessionId);
        sessions.IncrementOps(sessionId);

        try
        {
            return action.ToLowerInvariant() switch
            {
                "list" => await ListAsync(sessions, session, outlineLevel, minPercentComplete, maxPercentComplete),
                "summary" => await SummaryAsync(sessions, session),
                _ => throw new ArgumentException($"Unknown action: {action}. Use: list, summary"),
            };
        }
        finally
        {
            sessions.DecrementOps(sessionId);
        }
    }

    [McpServerTool(Name = "task_move"), Description(
        "Restructure WBS hierarchy by indenting, outdenting, or moving tasks. " +
        "Actions: indent, outdent, move.")]
    public static async Task<string> TaskMove(
        SessionManager sessions,
        [Description("Action: indent, outdent, move")] string action,
        [Description("Session ID")] string sessionId,
        [Description("Task UniqueID to move")] int uniqueId,
        [Description("Target row number to move before (for move action)")] int? beforeRow = null)
    {
        var session = sessions.GetSession(sessionId);
        sessions.IncrementOps(sessionId);

        try
        {
            return await sessions.ComThread.InvokeAsync(() =>
            {
                dynamic proj = session.Project;
                dynamic task = FindTaskByUniqueId(proj, uniqueId);

                switch (action.ToLowerInvariant())
                {
                    case "indent":
                        task.OutlineIndent();
                        return JsonSerializer.Serialize(new
                        {
                            uniqueId = (int)task.UniqueID,
                            name = (string)task.Name,
                            outlineLevel = (int)task.OutlineLevel,
                        });
                    case "outdent":
                        task.OutlineOutdent();
                        return JsonSerializer.Serialize(new
                        {
                            uniqueId = (int)task.UniqueID,
                            name = (string)task.Name,
                            outlineLevel = (int)task.OutlineLevel,
                        });
                    case "move":
                        if (beforeRow == null)
                            throw new ArgumentException("beforeRow is required for move");
                        proj.Application.SelectTaskField(Row: task.ID, Column: "Name");
                        proj.Application.EditCut();
                        proj.Application.SelectTaskField(Row: beforeRow.Value, Column: "Name");
                        proj.Application.EditPaste();
                        return JsonSerializer.Serialize(new
                        {
                            uniqueId = (int)task.UniqueID,
                            name = (string)task.Name,
                            moved = true,
                        });
                    default:
                        throw new ArgumentException($"Unknown action: {action}. Use: indent, outdent, move");
                }
            });
        }
        finally
        {
            sessions.DecrementOps(sessionId);
        }
    }

    private static async Task<string> AddAsync(
        SessionManager sessions, SessionManager.ProjectSession session,
        string name, string? duration, string? start, string? finish,
        int? percentComplete, bool? milestone, string? notes, int? beforeRow)
    {
        return await sessions.ComThread.InvokeAsync(() =>
        {
            dynamic proj = session.Project;
            dynamic task = beforeRow.HasValue
                ? proj.Tasks.Add(name, beforeRow.Value)
                : proj.Tasks.Add(name);

            if (duration != null) task.Duration = duration;
            if (start != null) task.Start = start;
            if (finish != null) task.Finish = finish;
            if (percentComplete.HasValue) task.PercentComplete = percentComplete.Value;
            if (milestone.HasValue) task.Milestone = milestone.Value;
            if (notes != null) task.Notes = notes;

            return JsonSerializer.Serialize(new
            {
                uniqueId = (int)task.UniqueID,
                id = (int)task.ID,
                name = (string)task.Name,
                duration = (string)task.Duration.ToString(),
                start = ((DateTime)task.Start).ToString("yyyy-MM-dd"),
                finish = ((DateTime)task.Finish).ToString("yyyy-MM-dd"),
                percentComplete = (int)task.PercentComplete,
                milestone = (bool)task.Milestone,
                outlineLevel = (int)task.OutlineLevel,
            });
        });
    }

    private static async Task<string> GetAsync(
        SessionManager sessions, SessionManager.ProjectSession session, int uniqueId)
    {
        return await sessions.ComThread.InvokeAsync(() =>
        {
            dynamic proj = session.Project;
            dynamic task = FindTaskByUniqueId(proj, uniqueId);

            return JsonSerializer.Serialize(new
            {
                uniqueId = (int)task.UniqueID,
                id = (int)task.ID,
                name = (string)task.Name,
                duration = (string)task.Duration.ToString(),
                start = ((DateTime)task.Start).ToString("yyyy-MM-dd"),
                finish = ((DateTime)task.Finish).ToString("yyyy-MM-dd"),
                percentComplete = (int)task.PercentComplete,
                milestone = (bool)task.Milestone,
                notes = (string)(task.Notes ?? ""),
                outlineLevel = (int)task.OutlineLevel,
                critical = (bool)task.Critical,
                predecessors = (string)(task.Predecessors ?? ""),
                successors = (string)(task.Successors ?? ""),
            });
        });
    }

    private static async Task<string> UpdateAsync(
        SessionManager sessions, SessionManager.ProjectSession session, int uniqueId,
        string? name, string? duration, string? start, string? finish,
        int? percentComplete, bool? milestone, string? notes)
    {
        return await sessions.ComThread.InvokeAsync(() =>
        {
            dynamic proj = session.Project;
            dynamic task = FindTaskByUniqueId(proj, uniqueId);

            if (name != null) task.Name = name;
            if (duration != null) task.Duration = duration;
            if (start != null) task.Start = start;
            if (finish != null) task.Finish = finish;
            if (percentComplete.HasValue) task.PercentComplete = percentComplete.Value;
            if (milestone.HasValue) task.Milestone = milestone.Value;
            if (notes != null) task.Notes = notes;

            return JsonSerializer.Serialize(new
            {
                uniqueId = (int)task.UniqueID,
                id = (int)task.ID,
                name = (string)task.Name,
                duration = (string)task.Duration.ToString(),
                start = ((DateTime)task.Start).ToString("yyyy-MM-dd"),
                finish = ((DateTime)task.Finish).ToString("yyyy-MM-dd"),
                percentComplete = (int)task.PercentComplete,
                milestone = (bool)task.Milestone,
                outlineLevel = (int)task.OutlineLevel,
            });
        });
    }

    private static async Task<string> DeleteAsync(
        SessionManager sessions, SessionManager.ProjectSession session, int uniqueId)
    {
        return await sessions.ComThread.InvokeAsync(() =>
        {
            dynamic proj = session.Project;
            dynamic task = FindTaskByUniqueId(proj, uniqueId);
            var taskName = (string)task.Name;
            var taskId = (int)task.UniqueID;
            task.Delete();
            return JsonSerializer.Serialize(new { deleted = true, uniqueId = taskId, name = taskName });
        });
    }

    private static async Task<string> ListAsync(
        SessionManager sessions, SessionManager.ProjectSession session,
        int? outlineLevel, int? minPct, int? maxPct)
    {
        return await sessions.ComThread.InvokeAsync(() =>
        {
            dynamic proj = session.Project;
            var tasks = new List<object>();

            foreach (dynamic task in proj.Tasks)
            {
                if (task == null) continue;

                if (outlineLevel.HasValue && (int)task.OutlineLevel != outlineLevel.Value) continue;
                var pct = (int)task.PercentComplete;
                if (minPct.HasValue && pct < minPct.Value) continue;
                if (maxPct.HasValue && pct > maxPct.Value) continue;

                tasks.Add(new
                {
                    uniqueId = (int)task.UniqueID,
                    id = (int)task.ID,
                    name = (string)task.Name,
                    duration = (string)task.Duration.ToString(),
                    start = ((DateTime)task.Start).ToString("yyyy-MM-dd"),
                    finish = ((DateTime)task.Finish).ToString("yyyy-MM-dd"),
                    percentComplete = pct,
                    milestone = (bool)task.Milestone,
                    outlineLevel = (int)task.OutlineLevel,
                    critical = (bool)task.Critical,
                });
            }

            return JsonSerializer.Serialize(new { count = tasks.Count, tasks });
        });
    }

    private static async Task<string> SummaryAsync(
        SessionManager sessions, SessionManager.ProjectSession session)
    {
        return await sessions.ComThread.InvokeAsync(() =>
        {
            dynamic proj = session.Project;
            int total = 0, completed = 0, critical = 0, milestones = 0;

            foreach (dynamic task in proj.Tasks)
            {
                if (task == null) continue;
                total++;
                if ((int)task.PercentComplete == 100) completed++;
                if ((bool)task.Critical) critical++;
                if ((bool)task.Milestone) milestones++;
            }

            return JsonSerializer.Serialize(new
            {
                totalTasks = total,
                completedTasks = completed,
                criticalTasks = critical,
                milestones,
                percentComplete = total > 0 ? (completed * 100 / total) : 0,
            });
        });
    }

    internal static dynamic FindTaskByUniqueId(dynamic project, int uniqueId)
    {
        foreach (dynamic task in project.Tasks)
        {
            if (task == null) continue;
            if ((int)task.UniqueID == uniqueId) return task;
        }
        throw new KeyNotFoundException($"Task with UniqueID {uniqueId} not found");
    }
}
