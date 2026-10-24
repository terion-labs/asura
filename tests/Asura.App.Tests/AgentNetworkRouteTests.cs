using Asura.App.ViewModels;

namespace Asura.App.Tests;

/// <summary>
/// "Run the AI agent in the workspace environment" says that when it is off,
/// the provider is reached over this computer's network. The agent used the
/// workspace's route regardless, so a workspace VPN that broke took its agent
/// down with "The AI provider is unavailable".
/// </summary>
public sealed class AgentNetworkRouteTests
{
    private static readonly Uri WorkspaceProxy = new("socks5://127.0.0.1:58665");

    [Fact]
    public void Off_the_agent_uses_this_computers_network_even_with_a_workspace_route()
    {
        Assert.Equal((false, (Uri?)null), MainWindowViewModel.AgentNetworkRoute(false, false, WorkspaceProxy));
    }

    [Fact]
    public void Off_the_agent_stays_available_while_the_workspace_route_is_blocked()
    {
        Assert.Equal((false, (Uri?)null), MainWindowViewModel.AgentNetworkRoute(false, true, null));
    }

    [Fact]
    public void On_the_agent_uses_the_workspace_route()
    {
        Assert.Equal((true, (Uri?)WorkspaceProxy), MainWindowViewModel.AgentNetworkRoute(true, false, WorkspaceProxy));
    }

    [Fact]
    public void On_the_agent_is_unavailable_while_the_workspace_route_is_blocked()
    {
        Assert.Equal((true, (Uri?)null), MainWindowViewModel.AgentNetworkRoute(true, true, WorkspaceProxy));
    }
}
