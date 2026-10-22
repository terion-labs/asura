using Asura.App.ViewModels;
using Asura.Core;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Asura.DesignQa;

internal enum WebsiteDummyPanelContent
{
    /// <summary>An SSH terminal on production-api checking a service.</summary>
    Terminal,
    /// <summary>An SSH terminal on staging-web running a deploy.</summary>
    TerminalDeploy,
    /// <summary>A local zsh running a dev server.</summary>
    TerminalLocal,
    /// <summary>A shell inside the edge-proxy Docker container.</summary>
    TerminalContainer,
    /// <summary>A local browser on example.com.</summary>
    Browser,
    /// <summary>A browser bound to an SSH connection, on that host's own localhost.</summary>
    RemoteDevServer,
}

/// <summary>One row of a dummy terminal. Blank text is an empty line.</summary>
internal sealed record WebsiteTerminalLine(string Text, bool IsMuted = false, bool IsSuccess = false);

internal sealed class WebsiteDummyRuntimePanelViewModel(
    PanelInstanceId id,
    PanelKind kind,
    string title,
    string kindLabel,
    WebsiteDummyPanelContent content)
    : RuntimePanelViewModel(id, kind, title, kindLabel)
{
    public string BrowserAddress => content == WebsiteDummyPanelContent.RemoteDevServer
        ? "http://localhost:3000/"
        : "https://example.com/";

    public string ConnectionDisplayName => content == WebsiteDummyPanelContent.RemoteDevServer
        ? "production-api"
        : "Local";

    public bool ShowsTerminal => content is WebsiteDummyPanelContent.Terminal
        or WebsiteDummyPanelContent.TerminalDeploy
        or WebsiteDummyPanelContent.TerminalLocal
        or WebsiteDummyPanelContent.TerminalContainer;

    public bool ShowsBrowser => content is WebsiteDummyPanelContent.Browser
        or WebsiteDummyPanelContent.RemoteDevServer;

    public bool ShowsExamplePage => content == WebsiteDummyPanelContent.Browser;

    public bool ShowsDevServerPage => content == WebsiteDummyPanelContent.RemoteDevServer;

    public IReadOnlyList<WebsiteTerminalLine> TerminalLines => content switch
    {
        WebsiteDummyPanelContent.TerminalDeploy =>
        [
            new("deploy@staging-web:~/app$ ./deploy.sh --env staging"),
            new("Building image staging-web:2026.10.22.3 ...", IsMuted: true),
            new("Pushing to registry.internal/staging-web ... done", IsMuted: true),
            new("Rolled out 3/3 replicas in 41 s", IsSuccess: true),
            new(""),
            new("deploy@staging-web:~/app$ curl -s localhost:8080/health"),
            new("{\"status\":\"ok\",\"release\":\"2026.10.22.3\"}", IsMuted: true),
            new("deploy@staging-web:~/app$ _"),
        ],
        WebsiteDummyPanelContent.TerminalLocal =>
        [
            new("~/work/asura-web on main", IsMuted: true),
            new("$ pnpm dev"),
            new("VITE v7.1.4  ready in 312 ms", IsMuted: true),
            new("Local:   http://localhost:5173/", IsSuccess: true),
            new("Network: use --host to expose", IsMuted: true),
            new(""),
            new("$ git status --short"),
            new(" M src/App.tsx", IsMuted: true),
            new("?? src/components/HostPicker.tsx", IsMuted: true),
            new("$ _"),
        ],
        WebsiteDummyPanelContent.TerminalContainer =>
        [
            new("root@edge-proxy:/# nginx -t"),
            new("nginx: the configuration file /etc/nginx/nginx.conf syntax is ok", IsMuted: true),
            new("nginx: configuration file /etc/nginx/nginx.conf test is successful", IsSuccess: true),
            new(""),
            new("root@edge-proxy:/# tail -n 3 /var/log/nginx/access.log"),
            new("10.0.1.12 - - [22/Oct/2026:13:02:11 +0000] \"GET /api/health HTTP/1.1\" 200 17", IsMuted: true),
            new("10.0.1.12 - - [22/Oct/2026:13:02:14 +0000] \"GET /api/workspaces HTTP/1.1\" 200 1842", IsMuted: true),
            new("10.0.1.30 - - [22/Oct/2026:13:02:15 +0000] \"POST /api/sessions HTTP/1.1\" 201 96", IsMuted: true),
            new("root@edge-proxy:/# _"),
        ],
        _ =>
        [
            new("Last login: Thu Aug 20 22:38:14 on ttys004", IsMuted: true),
            new("deploy@production-api:~$ systemctl status asura-api"),
            new("● asura-api.service — Asura production API", IsSuccess: true),
            new("     Loaded: loaded (/etc/systemd/system/asura-api.service; enabled)"),
            new("     Active: active (running) since Thu 2026-08-20 21:04:18 UTC"),
            new("   Main PID: 1842 (dotnet) · Tasks: 24 · Memory: 186.4M", IsMuted: true),
            new(""),
            new("deploy@production-api:~$ tail -n 4 /var/log/asura/api.log"),
            new("22:41:03 INF request completed  GET /health             200  12ms", IsMuted: true),
            new("22:41:07 INF deployment healthy  release=2026.08.20.4", IsMuted: true),
            new("22:41:12 INF request completed  GET /api/workspaces     200  31ms", IsMuted: true),
            new("deploy@production-api:~$ _"),
        ],
    };

    public string TerminalFooterLeft => content switch
    {
        WebsiteDummyPanelContent.TerminalDeploy => "SSH · deploy@10.0.1.57",
        WebsiteDummyPanelContent.TerminalLocal => "Local · /bin/zsh",
        WebsiteDummyPanelContent.TerminalContainer => "Docker · edge-proxy",
        _ => "SSH · deploy@10.0.1.42",
    };

    public string TerminalFooterRight => content == WebsiteDummyPanelContent.TerminalLocal
        ? "UTF-8"
        : "Connected · UTF-8";
}

internal sealed partial class WebsiteDummyRuntimePanelView : UserControl
{
    public WebsiteDummyRuntimePanelView()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
