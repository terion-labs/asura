# Asura

Asura is a native desktop terminal workspace with an integrated AI agent, embedded browser, and developer tools. One window holds your terminals, remote servers, Docker containers, Kubernetes clusters, databases, files, Git repositories, and system monitors.

The agent runs in-process directly inside the desktop app. There is no Electron overhead, no Node.js sidecar, and nothing to install on remote servers.

Website: [asura.sh](https://asura.sh) | License: [MIT](./LICENSE)

## Why Asura

Most developer setups spread work across five different apps: a terminal, a database client, an SFTP tool, Docker Desktop, and browser tabs with localhost tunnels. AI coding assistants usually sit in an editor sidebar or run terminal commands without seeing your screen.

Asura consolidates those tools into one window:

- **Browse remote localhost directly.** When a dev server runs on a remote server at `localhost:3000`, open a browser panel and select that SSH connection. Asura routes the browser traffic through the SSH tunnel. Remote web apps and private subnet dashboards load like local pages without manual `ssh -L` port forwarding. The agent can browse and inspect those pages too.
- **Zero footprint on remote servers.** The AI agent runs on your local machine and connects over standard SSH. It reads terminal output and sends real keystrokes, which means it can navigate interactive prompts, TUIs, and full-screen tools like `vim` or CLI setup wizards. Remote hosts only see an ordinary SSH session.
- **Panels work locally or remotely.** Terminals, file managers, database browsers, Docker, Kubernetes clusters, process lists, and system statistics all share a host selector. Switch from local to an SSH connection, and the panel operates against the remote server over the same tunnel.
- **Isolated workspaces and per-workspace VPNs.** Workspaces can run inside lightweight, persistent Linux containers with dedicated network namespaces. Assign a WireGuard tunnel, OpenVPN connection, Cisco AnyConnect tunnel, SOCKS5 proxy, or Tailscale exit node to a specific workspace without modifying host routing tables or affecting your other apps.

## How the agent works

The agent is designed to work alongside you rather than running uncontrolled in the background:

- **Approval for every mutation.** The agent proposes typed actions for terminal commands, browser clicks, file edits, database queries, and Kubernetes cluster operations. Every action requires your click to approve, or an explicit, time-bounded run window you choose to grant.
- **Immediate manual override.** The agent types directly into the terminal. The moment you press any key on your keyboard, the agent's input lease is revoked immediately. You always retain control.
- **Semantic browser navigation.** The agent inspects pages through accessibility tree snapshots and element references rather than guessed screen coordinates. Stale element references are rejected automatically.
- **Credentials stay in your OS vault.** Passwords, SSH keys, and API tokens are stored in the macOS Keychain, Windows Credential Manager, or Linux Secret Service. The agent receives only opaque session handles, never raw secrets.
- **Bring your own model.** Asura supports Anthropic Claude, OpenAI, Google Gemini, Ollama, DeepSeek, xAI Grok, Moonshot AI, OpenRouter, GitHub Copilot, Amazon Bedrock, or any custom OpenAI-compatible endpoint.
- **MCP support.** Connects to Model Context Protocol servers over stdio or Streamable HTTP. External harnesses can also access Asura tools via an opt-in localhost MCP server. See [tool sequences and MCP setup](./docs/agent-tool-sequences-and-mcp.md).

## Built-in panels

Every panel can be docked, split, stacked, and saved into workspace layouts that persist across restarts:

- **Terminal.** Powered by the Ghostty engine (`libghostty-vt`) rendered natively with Avalonia. Supports Kitty graphics, clickable links, semantic prompt navigation, and terminal sessions that persist through disconnects via `tmux` or screen.
- **Browser.** Embedded Chromium (CEF) rendered directly into the UI. Routes through SSH connections to load remote private ports.
- **Databases.** Table browser, inline row editor, and schema-aware query editor for PostgreSQL, MySQL, MariaDB, SQLite, SQL Server, ClickHouse, DuckDB, Oracle, Firebird, CockroachDB, and Redshift.
- **Redis.** Key browser, search, TTL inspection, pub/sub monitor, and type-aware value viewers with JSON tree formatting.
- **Docker.** Container management, image inspection, volume and network management, log streaming with search, and interactive container shells.
- **Kubernetes.** Browse clusters, namespaces, and workloads. Inspect streaming pod logs, launch interactive container exec terminals, manage port forwards, inspect Prometheus metrics and resource history, view and edit manifests, track Helm releases and rollbacks, and handle node maintenance.
- **Files.** File manager supporting local disks, SFTP, FTP, S3, WebDAV, and SMB with queued background file transfers.
- **Git.** Visual commit history graph, branch management, staged hunk review, and diff viewer.
- **Processes and stats.** Live process monitor with search and kill commands, alongside CPU, memory, disk, and network charts. Operates locally or over plain SSH.

## Tech stack

- [.NET 10 LTS](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)
- [Avalonia 12](https://docs.avaloniaui.net/docs/get-started/) for cross-platform desktop UI
- [libghostty-vt](https://github.com/ghostty-org/ghostty) through a private C ABI for terminal emulation and protocol encoding
- [Chromium Embedded Framework (CEF)](https://bitbucket.org/chromiumembedded/cef) for off-screen browser rendering
- [Porta.Pty](https://github.com/IvanJosipovic/Porta.Pty) for cross-platform PTY process transport

The terminal is rendered directly through Avalonia on macOS, Windows, and Linux. It does not use embedded web views or platform-specific display shims.

## Getting started

Asura is in early alpha.

- **macOS (Apple Silicon):** Download signed and notarized application builds from [GitHub Releases](https://github.com/terion-labs/asura/releases/latest).
- **Linux and Windows:** Build from source using the steps below.

## Build from source

### Prerequisites

- Git
- Pinned .NET 10 SDK (installed locally via bootstrap script)
- Zig 0.16.x (downloaded automatically during the native build)
- Host C compiler (Clang or GCC)

### Setup and build

1. Clone the repository and install the repository-local .NET SDK:

   ```bash
   git clone https://github.com/terion-labs/asura.git
   cd asura
   ASURA_SKIP_NATIVE=1 ./scripts/bootstrap.sh
   ```

2. Build the native terminal and browser runtimes for your platform:

   ```bash
   ./scripts/build-libghostty-vt.sh
   ./scripts/build-cef-runtime.sh --rid osx-arm64  # replace with your target RID
   ```

   The first build downloads the pinned Zig toolchain and Ghostty source, applies the reviewed patch overlay, runs the VT test suite, and outputs libraries to `native/artifacts/`.

3. Run the quick validation check and start the desktop app:

   ```bash
   ./scripts/check.sh --quick
   ./.dotnet/dotnet run --project src/Asura.Desktop/Asura.Desktop.csproj
   ```

To run the full suite of unit, integration, and architecture contract tests:

```bash
./scripts/check.sh --full
```

## Project structure

| Path | Description |
| --- | --- |
| `src/Asura.Core` | Domain models, IDs, and core state machines |
| `src/Asura.Application` | Session operations, lifecycle management, and input lease handling |
| `src/Asura.Protocol` | Versioned communication envelopes and event streams |
| `src/Asura.Agent` | Provider-neutral agent loop, tool dispatch, and conversation history |
| `src/Asura.Agent.Providers` | LLM adapters for Anthropic, OpenAI, Ollama, Gemini, and others |
| `src/Asura.Agent.Runtime` | Workspace-level tool execution and panel orchestration |
| `src/Asura.SessionHost` | In-process runtime registry, event coordination, and action guards |
| `src/Asura.Terminal` | `libghostty-vt` terminal integration and PTY transport |
| `src/Asura.Browser` | CEF browser integration and SSH-tunneled network routing |
| `src/Asura.Databases` | Database panel clients and SQL dialect adapters |
| `src/Asura.Redis` | Redis key browser and session handling |
| `src/Asura.Docker` | Docker engine client for local and remote daemons |
| `src/Asura.Kubernetes` | Kubernetes cluster client, pod logs, exec channels, port forwarding, and Helm operations |
| `src/Asura.Files` | File providers for local disk, SFTP, S3, SMB, and WebDAV |
| `src/Asura.Git` | Git panel integration |
| `src/Asura.Monitoring` | System statistics and process sampling |
| `src/Asura.App` | Avalonia UI presentation layer |
| `src/Asura.Desktop` | Desktop application composition root |
| `website/` | Marketing website ([asura.sh](https://asura.sh)) |
| `tests/*` | Unit, integration, desktop, and architecture contract test suites |

## Documentation

- [Architecture overview](./docs/architecture.md)
- [Architecture decision records (ADRs)](./docs/adr/)
- [Agent tool sequences and MCP](./docs/agent-tool-sequences-and-mcp.md)
- [macOS packaging](./docs/macos-packaging.md)
- [macOS release rehearsal](./docs/macos-release-rehearsal.md)
- [Platform terminal acceptance](./docs/platform-terminal-acceptance.md)
