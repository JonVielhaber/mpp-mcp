# mpp-mcp

MCP server that lets AI agents read and write Microsoft Project (.mpp) files via COM interop.

**Requires:** Microsoft Project installed on the machine.

## Install

```bash
# From local build
dotnet pack -c Release -o ./nupkg
dotnet tool install -g --add-source ./nupkg MppMcp

# Then run
mpp-mcp
```

## Configure in Claude Code

Add to `~/.claude.json`:
```json
{
  "mcpServers": {
    "mpp-mcp": {
      "type": "stdio",
      "command": "mpp-mcp",
      "args": [],
      "env": {}
    }
  }
}
```

## Tools

### File Operations
- **file** — Open, close, save, save_as, create .mpp files (session-based)
- **project_info** — Read project properties and task statistics
- **window** — Show/hide/arrange Project window for agent mode

### Task Operations
- **task** — Add, get, update, delete individual tasks (by UniqueID)
- **tasks** — List all tasks (with filters) or get summary stats
- **task_move** — Indent, outdent, or move tasks in the WBS

### Dependencies
- **dependency** — Add, remove, or list task dependencies (FS, SS, FF, SF)
- **critical_path** — Get the critical path task chain

## Environment Variables

| Variable | Default | Description |
|---|---|---|
| `IDLE_TIMEOUT_MS` | `300000` | Auto-close idle sessions after this many ms |

## License

MIT
