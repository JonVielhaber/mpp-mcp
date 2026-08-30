using System.Runtime.InteropServices;
using System.Text.Json;
using MppMcp;

namespace MppMcp.Tests;

public class FileToolsTests : IDisposable
{
    private readonly ComThread _comThread = new();

    public void Dispose() => _comThread.Dispose();

    [Fact]
    public async Task Close_ReportsSessionLookupFailure()
    {
        var manager = new SessionManager(_comThread, TimeSpan.FromMinutes(5));

        var response = await FileTools.File(manager, "close", sessionId: "missing", save: false);

        using var json = JsonDocument.Parse(response);
        var root = json.RootElement;
        Assert.False(root.GetProperty("closed").GetBoolean());
        Assert.Equal("get_session", root.GetProperty("failedPhase").GetString());
        Assert.Equal(typeof(KeyNotFoundException).FullName, root.GetProperty("exceptionType").GetString());
        Assert.Contains("missing", root.GetProperty("exceptionMessage").GetString());
        Assert.False(root.GetProperty("projectActivateCompleted").GetBoolean());
        Assert.False(root.GetProperty("fileCloseExInvoked").GetBoolean());
        Assert.False(root.GetProperty("sessionRemovalCompleted").GetBoolean());
        Assert.False(root.GetProperty("applicationShutdownCompleted").GetBoolean());
    }

    [Fact]
    public async Task Close_WhenTargetAlreadyActive_SkipsActivationAndCloses()
    {
        var (manager, project, application, sessionId) = CreateSession();
        project.ActivateException = new InvalidOperationException("activation should be skipped");

        var response = await FileTools.File(manager, "close", sessionId: sessionId, save: false);

        using var json = JsonDocument.Parse(response);
        Assert.True(json.RootElement.GetProperty("closed").GetBoolean());
        Assert.False(json.RootElement.GetProperty("activationRequired").GetBoolean());
        Assert.True(json.RootElement.GetProperty("activeProjectAlreadyMatchedTarget").GetBoolean());
        Assert.False(json.RootElement.GetProperty("fallbackActivationAttempted").GetBoolean());
        Assert.True(json.RootElement.GetProperty("projectActivateCompleted").GetBoolean());
        Assert.False(project.ActivateInvoked);
        Assert.True(application.FileCloseInvoked);
        Assert.Equal(0, application.FileCloseSaveOption);
        Assert.True(application.QuitInvoked);
    }

    [Fact]
    public async Task Close_WhenAnotherProjectIsActive_ActivatesAndClosesOnlyTarget()
    {
        var (manager, targetProject, application, sessionId) = CreateSession(targetIsActive: false);
        var otherProject = application.AddProject("other.mpp", makeActive: true);
        manager.TrackSession(otherProject.FullName, otherProject);

        var response = await FileTools.File(manager, "close", sessionId: sessionId, save: false);

        using var json = JsonDocument.Parse(response);
        var root = json.RootElement;
        Assert.True(root.GetProperty("closed").GetBoolean());
        Assert.True(root.GetProperty("activationRequired").GetBoolean());
        Assert.False(root.GetProperty("activeProjectAlreadyMatchedTarget").GetBoolean());
        Assert.True(root.GetProperty("fallbackActivationAttempted").GetBoolean());
        Assert.True(root.GetProperty("projectActivateCompleted").GetBoolean());
        Assert.True(targetProject.ActivateInvoked);
        Assert.False(otherProject.ActivateInvoked);
        Assert.True(application.FileCloseInvoked);
        Assert.Equal(targetProject.FullName, application.ActiveProjectFullNameAtClose);
        Assert.False(application.QuitInvoked);
    }

    [Fact]
    public async Task Close_WhenActivationDoesNotSelectTarget_RefusesToCloseActiveProject()
    {
        var (manager, targetProject, application, sessionId) = CreateSession(targetIsActive: false);
        var otherProject = application.AddProject("other.mpp", makeActive: true);
        targetProject.SelectOnActivate = false;

        var response = await FileTools.File(manager, "close", sessionId: sessionId, save: false);

        using var json = JsonDocument.Parse(response);
        var root = json.RootElement;
        Assert.False(root.GetProperty("closed").GetBoolean());
        Assert.Equal("project_verify_active", root.GetProperty("failedPhase").GetString());
        Assert.True(root.GetProperty("activationRequired").GetBoolean());
        Assert.True(root.GetProperty("fallbackActivationAttempted").GetBoolean());
        Assert.False(root.GetProperty("projectActivateCompleted").GetBoolean());
        Assert.True(targetProject.ActivateInvoked);
        Assert.False(application.FileCloseInvoked);
        Assert.Equal(otherProject.FullName, application.ActiveProject!.FullName);
    }

    [Fact]
    public async Task Close_ReportsActivationFailureWithPhaseState()
    {
        var (manager, project, application, sessionId) = CreateSession(targetIsActive: false);
        application.AddProject("other.mpp", makeActive: true);
        project.ActivateException = new InvalidOperationException("activate failed");

        var response = await FileTools.File(manager, "close", sessionId: sessionId, save: false);

        using var json = JsonDocument.Parse(response);
        var root = json.RootElement;
        Assert.False(root.GetProperty("closed").GetBoolean());
        Assert.Equal("project_activate", root.GetProperty("failedPhase").GetString());
        Assert.Equal(typeof(InvalidOperationException).FullName, root.GetProperty("exceptionType").GetString());
        Assert.Equal("activate failed", root.GetProperty("exceptionMessage").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("comHResult").ValueKind);
        Assert.False(root.GetProperty("projectActivateCompleted").GetBoolean());
        Assert.False(root.GetProperty("fileCloseExInvoked").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("fileCloseExReturnValue").ValueKind);
        Assert.False(root.GetProperty("sessionRemovalCompleted").GetBoolean());
        Assert.False(root.GetProperty("applicationShutdownCompleted").GetBoolean());
    }

    [Fact]
    public async Task Close_ReportsFileCloseExComFailureAndHResult()
    {
        var (manager, _, application, sessionId) = CreateSession();
        application.FileCloseException = new COMException("close failed", unchecked((int)0x80004005));

        var response = await FileTools.File(manager, "close", sessionId: sessionId, save: false);

        using var json = JsonDocument.Parse(response);
        var root = json.RootElement;
        Assert.False(root.GetProperty("closed").GetBoolean());
        Assert.Equal("file_close_ex", root.GetProperty("failedPhase").GetString());
        Assert.Equal(typeof(COMException).FullName, root.GetProperty("exceptionType").GetString());
        Assert.Equal("close failed", root.GetProperty("exceptionMessage").GetString());
        Assert.Equal("0x80004005", root.GetProperty("comHResult").GetString());
        Assert.True(root.GetProperty("projectActivateCompleted").GetBoolean());
        Assert.True(root.GetProperty("fileCloseExInvoked").GetBoolean());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("fileCloseExReturnValue").ValueKind);
        Assert.False(root.GetProperty("sessionRemovalCompleted").GetBoolean());
        Assert.False(root.GetProperty("applicationShutdownCompleted").GetBoolean());
    }

    [Fact]
    public async Task Close_ReportsSessionRemovalFailureWithCompletedCloseState()
    {
        var (manager, _, _, sessionId) = CreateSession();
        manager.IncrementOps(sessionId);

        var response = await FileTools.File(manager, "close", sessionId: sessionId, save: false);

        using var json = JsonDocument.Parse(response);
        var root = json.RootElement;
        Assert.False(root.GetProperty("closed").GetBoolean());
        Assert.Equal("session_removal", root.GetProperty("failedPhase").GetString());
        Assert.True(root.GetProperty("projectActivateCompleted").GetBoolean());
        Assert.True(root.GetProperty("fileCloseExInvoked").GetBoolean());
        Assert.True(root.GetProperty("fileCloseExReturnValue").GetBoolean());
        Assert.False(root.GetProperty("sessionRemovalCompleted").GetBoolean());
        Assert.False(root.GetProperty("applicationShutdownCompleted").GetBoolean());
    }

    [Fact]
    public async Task Close_ReportsApplicationShutdownFailureWithCompletedPriorPhases()
    {
        var (manager, _, application, sessionId) = CreateSession();
        application.QuitException = new COMException("quit failed", unchecked((int)0x80010108));

        var response = await FileTools.File(manager, "close", sessionId: sessionId, save: false);

        using var json = JsonDocument.Parse(response);
        var root = json.RootElement;
        Assert.False(root.GetProperty("closed").GetBoolean());
        Assert.Equal("application_shutdown", root.GetProperty("failedPhase").GetString());
        Assert.Equal("0x80010108", root.GetProperty("comHResult").GetString());
        Assert.True(root.GetProperty("projectActivateCompleted").GetBoolean());
        Assert.True(root.GetProperty("fileCloseExInvoked").GetBoolean());
        Assert.True(root.GetProperty("fileCloseExReturnValue").GetBoolean());
        Assert.True(root.GetProperty("sessionRemovalCompleted").GetBoolean());
        Assert.False(root.GetProperty("applicationShutdownCompleted").GetBoolean());
    }

    private (SessionManager Manager, FakeProject Project, FakeProjectApplication Application, string SessionId)
        CreateSession(bool targetIsActive = true)
    {
        var manager = new SessionManager(_comThread, TimeSpan.FromMinutes(5));
        var application = new FakeProjectApplication();
        var project = application.AddProject("test.mpp", makeActive: targetIsActive);
        var sessionId = manager.TrackSession(project.FullName.ToUpperInvariant(), project);

        typeof(SessionManager)
            .GetField("_app", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(manager, application);

        return (manager, project, application, sessionId);
    }

    public sealed class FakeProject
    {
        public FakeProjectApplication Application { get; }
        public string FullName { get; }
        public bool ActivateInvoked { get; private set; }
        public Exception? ActivateException { get; set; }
        public bool SelectOnActivate { get; set; } = true;

        public FakeProject(FakeProjectApplication application, string fullName)
        {
            Application = application;
            FullName = Path.GetFullPath(fullName);
        }

        public void Activate()
        {
            ActivateInvoked = true;
            if (ActivateException != null)
                throw ActivateException;

            if (SelectOnActivate)
                Application.SetActiveProject(this);
        }
    }

    public sealed class FakeProjectApplication
    {
        public FakeProjectCollection Projects { get; } = new();
        public FakeProject? ActiveProject { get; private set; }
        public bool FileCloseResult { get; set; } = true;
        public Exception? FileCloseException { get; set; }
        public Exception? QuitException { get; set; }
        public bool FileCloseInvoked { get; private set; }
        public int? FileCloseSaveOption { get; private set; }
        public string? ActiveProjectFullNameAtClose { get; private set; }
        public bool QuitInvoked { get; private set; }

        public FakeProject AddProject(string fullName, bool makeActive = false)
        {
            var project = new FakeProject(this, fullName);
            Projects.Add(project);
            if (makeActive)
                ActiveProject = project;

            return project;
        }

        public void SetActiveProject(FakeProject project) => ActiveProject = project;

        public bool FileCloseEx(int saveOption)
        {
            FileCloseInvoked = true;
            FileCloseSaveOption = saveOption;
            ActiveProjectFullNameAtClose = ActiveProject?.FullName;
            if (FileCloseException != null)
                throw FileCloseException;

            return FileCloseResult;
        }

        public void Quit(int saveOption)
        {
            QuitInvoked = true;
            if (QuitException != null)
                throw QuitException;
        }
    }

    public sealed class FakeProjectCollection
    {
        private readonly List<FakeProject> _projects = new();

        public int Count => _projects.Count;

        public void Add(FakeProject project) => _projects.Add(project);

        public FakeProject Item(int index) => _projects[index - 1];
    }
}
