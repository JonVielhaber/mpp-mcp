# mpp-mcp

MCP server that lets AI agents read and write Microsoft Project (.mpp) files via COM interop.

## Build & Test

```bash
dotnet build              # Build
dotnet test               # Run unit tests (no Project needed)
dotnet run --project .    # Run as stdio MCP server
```

## Install as global tool (local dev)

```bash
dotnet pack -c Release -o ./nupkg
dotnet tool install -g --add-source ./nupkg MppMcp
```

Then `mpp-mcp` is available as a command.

## Architecture

- `src/Program.cs` — Entry point, host builder, DI, stdio transport
- `src/MppServer.cs` — Server instructions text
- `src/SessionManager.cs` — COM Application singleton, session tracking, idle sweep
- `src/Interop/ComThread.cs` — Dedicated STA thread for all COM calls
- `src/FileTools.cs` — file tool: open, close, save, save_as, create
- `src/TaskTools.cs` — task tool (CRUD), tasks tool (bulk list/summary), task_move tool
- `src/DependencyTools.cs` — dependency tool (add/remove/list), critical_path tool
- `src/WindowTools.cs` — window tool (show/hide/arrange), project_info tool

## COM Notes

- Uses dynamic COM interop (Activator.CreateInstance with ProgID MSProject.Application)
- All COM calls marshaled through ComThread (STA thread requirement)
- MS Project must be installed on the machine
- Files must be closed in the Project desktop app before opening via COM
- Task collections may contain null entries (deleted tasks leave gaps) — always null-check
- Duration values are strings in Project format: "5d", "2w", "8h"
- Task UniqueID is stable; Task.ID (row number) shifts on insert/delete
