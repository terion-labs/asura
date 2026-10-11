using Asura.Application;

namespace Asura.App.ViewModels;

/// <summary>
/// What an MCP server's last diagnostic means, in the reader's words.
///
/// The Settings row showed the host's own sentence after a session ID
/// ("Live session 0123abcd · MCP initialization and bounded tool discovery
/// succeeded."). The host's stable code is the contract, so the row maps it
/// to a curated sentence with a next step where there is one, and the
/// session ID stays out of the row.
/// </summary>
internal static class McpDiagnosticText
{
    /// <summary>The sentence for a diagnostic's latest event.</summary>
    public static string Describe(McpServerDiagnosticSummary summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var latest = summary.Events[^1];
        if (string.Equals(latest.StableCode, "mcp_healthy", StringComparison.Ordinal))
        {
            return summary.SessionKind == McpServerSessionKind.Test
                ? "Connected and loaded its tools."
                : "Running for an agent run.";
        }

        if (string.Equals(latest.StableCode, "mcp_stopped", StringComparison.Ordinal))
        {
            return latest.ObservedStderrBytes == 0
                ? "The server stopped."
                : "The server stopped after writing to its error output.";
        }

        // A degraded session failed one call and kept running; say both.
        return latest.State == McpServerLifecycleState.Degraded
            ? $"{ForCode(latest.StableCode) ?? "A tool call failed."} The server is still running."
            : ForCode(latest.StableCode) ?? ForState(latest.State);
    }

    /// <summary>
    /// The sentence for a failed test's code, or the fallback when the code
    /// has no sentence of its own.
    /// </summary>
    public static string DescribeTestFailure(string stableCode) =>
        ForCode(stableCode) ?? "The test didn't finish. Try again.";

    private static string? ForCode(string stableCode) => stableCode switch
    {
        "mcp_testing" => "Testing the connection and loading its tools…",
        "mcp_starting" => "Starting the server for an agent run…",
        "mcp_cleanup_uncertain" =>
            "Asura couldn't confirm the server's process ended. Restart Asura before trying again.",
        "mcp_server_launch_failed" =>
            "The server couldn't start. Check its command and arguments.",
        "mcp_launch_path_invalid" =>
            "The server's command and working folder must be full paths.",
        "mcp_server_exited" or "mcp_server_closed" => "The server quit unexpectedly.",
        "mcp_transport_closed" or "mcp_transport_failed" =>
            "The connection to the server was lost.",
        "mcp_transport_unsupported" => "Asura can't connect to this kind of server.",
        "mcp_protocol_unsupported" =>
            "The server uses a version of MCP that Asura doesn't support.",
        "mcp_tools_unsupported" => "The server doesn't offer any tools.",
        "mcp_message_too_large" => "The server sent a reply larger than Asura accepts.",
        "mcp_message_invalid" or "mcp_result_invalid" =>
            "The server sent a reply Asura couldn't read.",
        "mcp_remote_error" or "mcp_tool_error" => "The server reported an error.",
        "mcp_arguments_invalid" => "The server rejected a tool's arguments.",
        "mcp_limit_exceeded" => "The server went over a limit Asura sets for its replies.",
        "mcp_tool_outcome_unknown" =>
            "Asura couldn't confirm whether the last tool call finished.",
        "mcp_tool_not_listed" or "mcp_tool_catalog_changed" or "mcp_enabled_tool_missing" =>
            "The server's tools changed. Test the server again to review them.",
        "mcp_tool_capacity_exceeded" or "mcp_schema_capacity_exceeded" =>
            "Too many tools are enabled for one run. Turn some off.",
        "mcp_secret_unavailable" => "A credential this server uses isn't available.",
        "mcp_secret_invalid" => "A credential this server uses isn't in a form it can send.",
        "mcp_secret_limit_exceeded" => "This server's credentials are too large to send.",
        "mcp_workspace_route_unavailable" =>
            "The server couldn't reach the workspace environment.",
        "workspace_network_kill_switch_blocked" =>
            "The workspace's network kill switch is blocking the server.",
        "mcp_discovery_failed" => "The server failed while starting or loading its tools.",
        "mcp_test_timed_out" => "The server didn't finish loading its tools in time.",
        "mcp_cancelled" => "The request was canceled.",
        "mcp_test_cancelled" => "The test was canceled.",
        "mcp_profile_revision_mismatch" =>
            "The server's settings changed during the test. Test it again.",
        "mcp_profile_not_found" => "This server was removed.",
        "mcp_profile_untrusted" => "Trust this server before testing it.",
        _ => null,
    };

    private static string ForState(McpServerLifecycleState state) => state switch
    {
        McpServerLifecycleState.Testing => "Testing the connection and loading its tools…",
        McpServerLifecycleState.Starting => "Starting the server…",
        McpServerLifecycleState.Healthy => "Connected and loaded its tools.",
        McpServerLifecycleState.Degraded => "A tool call failed. The server is still running.",
        McpServerLifecycleState.Failed => "The server stopped working. Test it again.",
        McpServerLifecycleState.CleanupUncertain =>
            "Asura couldn't confirm the server's process ended. Restart Asura before trying again.",
        McpServerLifecycleState.Stopped => "The server stopped.",
        McpServerLifecycleState.Untrusted => "Trust this server before using it.",
        McpServerLifecycleState.Disabled => "This server is turned off for agent runs.",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
    };
}
