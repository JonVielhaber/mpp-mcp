using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MppMcp;

var builder = Host.CreateApplicationBuilder(args);

builder.Logging.AddConsole(options =>
{
    options.LogToStandardErrorThreshold = LogLevel.Trace;
});

var idleTimeoutMs = int.TryParse(
    Environment.GetEnvironmentVariable("IDLE_TIMEOUT_MS"), out var ms) ? ms : 300_000;

builder.Services.AddSingleton(new ComThread());
builder.Services.AddSingleton(sp =>
    new SessionManager(sp.GetRequiredService<ComThread>(), TimeSpan.FromMilliseconds(idleTimeoutMs)));

builder.Services.AddMcpServer(options =>
{
    options.ServerInfo = new() { Name = "mpp-mcp", Version = "0.1.0" };
    options.ServerInstructions = MppServer.Instructions;
})
.WithStdioServerTransport()
.WithToolsFromAssembly();

await builder.Build().RunAsync();
