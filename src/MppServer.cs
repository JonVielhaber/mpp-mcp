namespace MppMcp;

public static class MppServer
{
    public const string Instructions = """
        mpp-mcp lets you read and write Microsoft Project (.mpp) files.

        CRITICAL: The .mpp file must be CLOSED in MS Project desktop app (COM requires exclusive access).

        SESSION LIFECYCLE:
        1. file(action:'open', path:'...') or file(action:'create', path:'...') — returns sessionId
        2. Use sessionId with ALL subsequent tools
        3. file(action:'close', save:true/false) — ONLY when completely done

        SHOW PROJECT — "Agent Mode":
        Default is hidden. Use window(action:'show') to watch changes live.

        TASK IDENTIFICATION:
        Tasks are identified by UniqueID (stable), not row number (shifts on insert/delete).

        IMPORTANT: Every open/create MUST have a matching close.
        Sessions auto-close after 5 minutes of inactivity as a safety net.
        """;
}
