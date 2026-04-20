using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;

namespace MppMcp;

[McpServerToolType]
public static class WindowTools
{
    [McpServerTool(Name = "window"), Description(
        "Show, hide, or arrange the Microsoft Project window. " +
        "Use for 'agent mode' where the user watches changes live.")]
    public static async Task<string> Window(
        SessionManager sessions,
        [Description("Action: show, hide, arrange, set_status_bar, clear_status_bar")] string action,
        [Description("Arrange preset: right-half, left-half, full (for arrange)")] string? preset = null,
        [Description("Status bar text (for set_status_bar)")] string? text = null)
    {
        var app = await sessions.EnsureAppAsync();

        return await sessions.ComThread.InvokeAsync(() =>
        {
            switch (action.ToLowerInvariant())
            {
                case "show":
                    app.Visible = true;
                    return JsonSerializer.Serialize(new { visible = true });

                case "hide":
                    app.Visible = false;
                    return JsonSerializer.Serialize(new { visible = false });

                case "arrange":
                    app.Visible = true;
                    if (preset != null)
                    {
                        switch (preset.ToLowerInvariant())
                        {
                            case "right-half":
                                app.WindowState = 0; // pjNormal
                                app.Left = (int)(app.UsableWidth / 2);
                                app.Top = 0;
                                app.Width = (int)(app.UsableWidth / 2);
                                app.Height = app.UsableHeight;
                                break;
                            case "left-half":
                                app.WindowState = 0;
                                app.Left = 0;
                                app.Top = 0;
                                app.Width = (int)(app.UsableWidth / 2);
                                app.Height = app.UsableHeight;
                                break;
                            case "full":
                                app.WindowState = 1; // pjMaximized
                                break;
                            default:
                                throw new ArgumentException(
                                    $"Unknown preset: {preset}. Use: right-half, left-half, full");
                        }
                    }
                    return JsonSerializer.Serialize(new { visible = true, preset });

                case "set_status_bar":
                case "set-status-bar":
                    app.StatusBar = text ?? "";
                    return JsonSerializer.Serialize(new { statusBar = text ?? "" });

                case "clear_status_bar":
                case "clear-status-bar":
                    app.StatusBar = false; // resets to default
                    return JsonSerializer.Serialize(new { statusBar = "cleared" });

                default:
                    throw new ArgumentException(
                        $"Unknown action: {action}. Use: show, hide, arrange, set_status_bar, clear_status_bar");
            }
        });
    }

    [McpServerTool(Name = "project_info", ReadOnly = true), Description(
        "Read project-level properties: title, dates, calendar, task statistics.")]
    public static async Task<string> ProjectInfo(
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

                int totalTasks = 0, completedTasks = 0, criticalTasks = 0, milestones = 0;
                foreach (dynamic task in proj.Tasks)
                {
                    if (task == null) continue;
                    totalTasks++;
                    if ((int)task.PercentComplete == 100) completedTasks++;
                    if ((bool)task.Critical) criticalTasks++;
                    if ((bool)task.Milestone) milestones++;
                }

                return JsonSerializer.Serialize(new
                {
                    name = (string)proj.Name,
                    title = (string)(proj.Title ?? ""),
                    path = (string)proj.FullName,
                    start = ((DateTime)proj.ProjectStart).ToString("yyyy-MM-dd"),
                    finish = ((DateTime)proj.ProjectFinish).ToString("yyyy-MM-dd"),
                    calendar = (string)proj.Calendar,
                    stats = new
                    {
                        totalTasks,
                        completedTasks,
                        criticalTasks,
                        milestones,
                        percentComplete = totalTasks > 0 ? (completedTasks * 100 / totalTasks) : 0,
                    },
                });
            });
        }
        finally
        {
            sessions.DecrementOps(sessionId);
        }
    }
}
