// The use-case landings. Each page is one argument for one kind of reader,
// built from the same facts as the home page. Shots are routes of the design
// QA website export; scripts/export-website-shots.sh lists them.

export interface UseCaseSection {
  eyebrow: string
  title: string
  lede: string
  points?: string[]
  shot: string
  alt: string
}

export interface UseCase {
  slug: string
  /** Short label for navigation and cards. */
  label: string
  /** One line for the card on the home page. */
  teaser: string
  title: string
  description: string
  hero: {
    eyebrow: string
    title: string
    lede: string
    shot: string
    alt: string
  }
  sections: UseCaseSection[]
  faqs: { q: string; a: string }[]
}

export const useCases: UseCase[] = [
  {
    slug: 'remote-servers',
    label: 'Servers over SSH',
    teaser:
      'Browse a remote localhost, watch processes, edit files, and run an agent on a box that only sees an SSH session.',
    title: 'Asura for servers you reach over SSH',
    description:
      'Open a remote localhost in a browser panel, point files, processes, and databases at the same host, and keep builds running through a dropped connection. Nothing to install on the server.',
    hero: {
      eyebrow: 'For servers you reach over SSH',
      title: 'One SSH connection, every panel',
      lede: 'One SSH connection drives a terminal, a browser that opens the box\'s own localhost ports, a file manager, process and statistics views, and database panels. The server sees an ordinary SSH user and nothing else.',
      shot: 'workspace.webp',
      alt: 'An SSH terminal running systemctl beside a browser panel on the same workspace',
    },
    sections: [
      {
        eyebrow: 'Remote localhost',
        title: 'Open localhost:3000 on the server, in a tab, without ssh -L',
        lede: 'A browser panel has a host selector like every other panel. Pick the SSH connection and type the address. Asura carries the page through the tunnel, so a dev server, an admin UI on a private subnet, or a metrics dashboard loads like a local page.',
        points: [
          'Private addresses resolve on the server side, so 10.0.x.x dashboards and internal hostnames work.',
          'Each workspace keeps its own cookies and logins, so two clients\' admin panels never share a session.',
          'The agent can read and click those pages too, through the same tunnel and the same approvals.',
        ],
        shot: 'workspace-browser-remote.webp',
        alt: 'A browser panel bound to the production-api SSH connection showing a dev server page at localhost:3000',
      },
      {
        eyebrow: 'Every panel goes remote',
        title: 'Files, processes, statistics, and databases over the same connection',
        lede: 'Flip a panel from Local to a connection and it works against that machine. The file panel browses over SFTP with queued transfers. Processes and Statistics sample with plain system commands. Database panels tunnel to the engine listening on the server.',
        points: [
          'No daemon or agent on the host. Monitoring runs standard commands over the SSH session.',
          'Terminals can also target Docker containers and WSL, with SSH keys, ssh-agent, or passwords and a host-key review dialog.',
          'File previews for text, images, PDFs, hex, and archives, plus a permissions editor.',
        ],
        shot: 'workspace-process-monitor.webp',
        alt: 'Processes panel listing CPU and memory per process on a remote host',
      },
      {
        eyebrow: 'Session continuity',
        title: 'Sessions that survive a dropped connection',
        lede: 'Remote terminals run inside a tmux or GNU Screen session that is already on the server. Drop Wi-Fi, switch networks, or restart the app, and the compile keeps going. The terminal reattaches when you come back, scrollback included.',
        points: [
          'Managed sessions are named asura-* and stay separate from anything else you run on the box.',
          'If continuity is not possible on a host, Asura offers a plain SSH session instead of failing silently.',
          'A global Quick Terminal drops a shell over whatever you are doing. macOS today; Windows and Linux X11 when those builds ship.',
        ],
        shot: 'workspace-file-viewer.webp',
        alt: 'File panel listing a remote directory with a transfer queue',
      },
      {
        eyebrow: 'The agent, remotely',
        title: 'An agent on the server with zero footprint',
        lede: 'The agent runs inside Asura on your Mac and reaches the server through the SSH session you already opened. It reads the terminal screen, types real keystrokes, and waits for prompts, so it can drive installers, vim, and cloud CLI wizards. Every change waits for your approval.',
        points: [
          'The remote host sees keystrokes in an SSH session. There is no sidecar, no extra port, no binary to audit.',
          'Touch the keyboard and the agent loses its input lease at once.',
          'Secrets stay in your Keychain. The agent works with session handles, never passwords.',
        ],
        shot: 'workspace-agent.webp',
        alt: 'The agent reading a failed deploy in an SSH terminal and proposing a command',
      },
    ],
    faqs: [
      {
        q: 'Do I have to install anything on my servers?',
        a: 'No. Everything remote runs over standard SSH and SFTP. Session continuity uses the tmux or GNU Screen already on the box, monitoring samples with plain system commands, and the agent drives a normal PTY. No daemon, no sidecar, no extra open ports.',
      },
      {
        q: 'How does the browser reach a remote localhost?',
        a: 'A browser panel bound to an SSH connection sends its traffic through that connection. Name resolution happens on the server side, so localhost, private IPs, and internal hostnames resolve there. No port forwarding to set up by hand.',
      },
      {
        q: 'Which SSH authentication methods work?',
        a: 'ssh-agent, a private key, or a password. Keys and passwords live in your Keychain. A new or changed host key opens a review dialog before the connection proceeds.',
      },
    ],
  },
  {
    slug: 'ai-agent',
    label: 'AI agent',
    teaser:
      'An agent that types into real terminals and reads real pages, with each change run only after you approve it. Bring your own model.',
    title: 'An AI agent for terminals, browsers, and servers',
    description:
      'The Asura agent runs in-process, works through the panels you already have open, and needs your approval for every change. Anthropic, OpenAI, xAI, DeepSeek, OpenRouter, GitHub Copilot, Ollama, and OpenAI-compatible endpoints.',
    hero: {
      eyebrow: 'For agent work in terminals and browsers',
      title: 'An agent inside your terminals, browser, and servers',
      lede: 'Ask it to fix a failed deploy and it reads the logs in your terminal, checks the service, and proposes the command. It runs inside the app, native .NET with no Node sidecar. Each command, click, or write it proposes runs after you approve it.',
      shot: 'workspace-agent.webp',
      alt: 'The docked agent panel beside an SSH terminal and a browser',
    },
    sections: [
      {
        eyebrow: 'Control',
        title: 'Permissions per capability',
        lede: 'Every tool call is a typed action: run a command, click an element, write a file, change a container, apply a manifest. You set a permission level per capability. Ask is the default for anything that changes state.',
        points: [
          'Four levels per capability: Off, Ask, Auto for observation and routine actions, and a time-bounded Full access you grant on purpose.',
          'Kubernetes capabilities start Off. Git pushes, Docker lifecycle actions, and file writes each have their own switch.',
          'Start typing in a terminal the agent is using and its input lease is revoked immediately.',
        ],
        shot: 'workspace-agent-approval.webp',
        alt: 'An approval card for a terminal command, waiting for a click',
      },
      {
        eyebrow: 'Terminals',
        title: 'Real keystrokes into real terminals',
        lede: 'The agent reads the screen, the scrollback, and the diff since it last looked. It sends keystrokes, chords, and mouse events to the PTY, so interactive prompts, TUIs, and full-screen editors work. It waits for output instead of guessing.',
        points: [
          'Local, SSH, Docker, and WSL terminals, plus pod terminals in Kubernetes.',
          'Runs with a scope you choose: this panel, this tab, selected panels, or the workspace.',
          'Steer it mid-run, resume from an idle checkpoint, and compact long conversations when context fills up.',
        ],
        shot: 'workspace-agent-reasoning.webp',
        alt: 'The agent streaming its reasoning while a tool call waits for approval',
      },
      {
        eyebrow: 'Browser and web',
        title: 'Pages as accessibility snapshots',
        lede: 'The agent sees pages as accessibility snapshots with element references, and clicks, fills, and checks by reference. Stale references are rejected. When it needs coordinates, a screenshot is tied to a viewport revision so it cannot click an old layout.',
        points: [
          'Page content is untrusted input. Instructions on a page never become actions without your approval.',
          'Web search, page reading, and HTTP fetch for tasks that need fresh information.',
          'Point the browser through an SSH tunnel and the agent can work your internal web apps.',
        ],
        shot: 'workspace-browser.webp',
        alt: 'Browser panel showing a page the agent can inspect',
      },
      {
        eyebrow: 'Models and tools',
        title: 'Your model, your MCP servers',
        lede: 'Asura speaks to Anthropic, OpenAI (API key or ChatGPT sign-in), xAI, DeepSeek, Moonshot AI, OpenRouter, GitHub Copilot, Ollama, and any OpenAI-compatible endpoint. Reasoning effort and service tier are per profile. Google and Amazon Bedrock are in the catalog and coming soon.',
        points: [
          'MCP client: connect stdio and Streamable HTTP servers, with a trust prompt and credentials from your Keychain.',
          'MCP server: let another harness drive Asura\'s panels over a localhost endpoint with a bearer token, under the same approvals.',
          'Secrets pasted into chat become hidden placeholders the model never sees.',
        ],
        shot: 'overlay-command-palette.webp',
        alt: 'The command palette listing commands, panels, and connections',
      },
    ],
    faqs: [
      {
        q: 'What does the agent actually control?',
        a: 'Whatever you allow, per capability. Ask is the default for actions that change state, so a terminal command, a file write, a Docker action, or a Kubernetes apply waits for a click. Auto lets it observe and do routine actions on its own. Full access is time-bounded and explicit. Kubernetes capabilities are Off until you turn them on.',
      },
      {
        q: 'Which models can I use?',
        a: 'Anthropic, OpenAI, xAI, DeepSeek, Moonshot AI, OpenRouter, GitHub Copilot, Ollama, and any OpenAI-compatible endpoint. Keys are stored in your Keychain. Google and Amazon Bedrock appear in the provider list but are not usable yet.',
      },
      {
        q: 'Does the agent see my passwords?',
        a: 'No. Connections reference secrets in your Keychain, and the agent works with session handles. A passphrase a subprocess needs travels over a single-use pipe and is wiped from memory after use. Secrets you paste into the chat are replaced by placeholders before the model sees them.',
      },
    ],
  },
  {
    slug: 'devops',
    label: 'DevOps',
    teaser:
      'Docker engines and Kubernetes clusters as panels beside the terminal, deploys that survive a dropped connection, and an agent whose changes wait for your approval.',
    title: 'Asura for DevOps',
    description:
      'Docker containers, images, volumes, and logs on local or remote engines. Kubernetes workloads, pod logs, exec, port forwards, manifests, Helm, and node drains. Remote sessions that keep running through a dropped connection. An agent that reads logs and proposes changes you review.',
    hero: {
      eyebrow: 'For DevOps',
      title: 'Containers, clusters, and the shells between them',
      lede: 'Docker engines, local or reached over SSH, and Kubernetes clusters from your kubeconfig open as panels next to the terminal. A deploy you started keeps running when the laptop sleeps. The agent reads logs and proposes changes; each mutation waits for your approval.',
      shot: 'workspace-kubernetes.webp',
      alt: 'Kubernetes panel listing pods across namespaces with CPU and memory',
    },
    sections: [
      {
        eyebrow: 'Docker',
        title: 'Containers, logs, shells, and files, local or remote',
        lede: 'Containers, images, volumes, and networks for a local daemon or a remote engine over SSH. Start, stop, restart, pause, and remove. Stream logs and search them, open a shell inline or in a new tab, and browse the files inside a container or a volume.',
        points: [
          'Compose project and service labels show which stack a container belongs to.',
          'Per-container stats from the engine; inspect output as JSON.',
          'A remote engine needs only SSH access to the host. Nothing is installed on it.',
        ],
        shot: 'workspace-docker-logs.webp',
        alt: 'Docker panel streaming and searching container logs',
      },
      {
        eyebrow: 'Kubernetes, day to day',
        title: 'Logs, exec, and forwards in the same window',
        lede: 'Pick a context from your kubeconfig and a namespace. Workloads update through watches. Pod logs stream with search. Exec opens a real terminal into any container. A service or pod forward lands in a browser or database panel in the same tab, so the admin UI and the Postgres behind it sit side by side.',
        points: [
          'Metrics from the Metrics API, with Prometheus or VictoriaMetrics discovered automatically for pod history charts.',
          'Read-only browsing of container files.',
          'Authentication with tokens, client certificates, or approved exec helpers, on the host or inside an isolated workspace.',
        ],
        shot: 'workspace-kubernetes-logs.webp',
        alt: 'Streaming pod logs in the Kubernetes panel',
      },
      {
        eyebrow: 'Kubernetes, changes',
        title: 'Manifests, Helm, and nodes, each with a review step',
        lede: 'View a resource as YAML or JSON with highlighting, then apply, patch, or delete after a review of what will change. Helm releases can be upgraded, rolled back, or uninstalled. Nodes can be cordoned and drained with evictions that respect pod disruption budgets.',
        shot: 'workspace-kubernetes-helm.webp',
        alt: 'Helm releases with upgrade and rollback actions',
      },
      {
        eyebrow: 'Session continuity',
        title: 'A deploy that outlives the Wi-Fi',
        lede: 'Remote terminals run inside a tmux or GNU Screen session that is already on the server. Close the lid, switch networks, or restart the app: the deploy keeps going, and the terminal reattaches with its scrollback when you come back.',
        points: [
          'Managed sessions are named asura-* and stay separate from anything else you run on the box.',
          'If continuity is not possible on a host, Asura offers a plain SSH session instead of failing silently.',
          'Four terminals in a grid, each on its own host: two SSH servers, the local shell, a Docker container.',
        ],
        shot: 'workspace-terminals.webp',
        alt: 'Four terminal panels in a grid: two SSH hosts, a local shell, and a Docker container',
      },
      {
        eyebrow: 'The agent on servers and clusters',
        title: 'Reads by default. Mutations after a preview and your approval.',
        lede: 'Turn on the capabilities you want. The agent can then discover contexts, list and inspect resources, read pod and container logs, and run commands in terminals you choose. A cluster change goes through a preview the agent must commit, and the commit waits for your approval.',
        points: [
          'Kubernetes capabilities default to Off. Docker lifecycle actions and terminal commands have their own switches.',
          'Agent cluster mutations are limited to apply, scale, rollout restart, and delete. Node drains and Helm operations are UI-only.',
          'In a terminal, the agent sends real keystrokes. Press a key yourself and its input lease is revoked at once.',
        ],
        shot: 'workspace-kubernetes-agent.webp',
        alt: 'The agent docked beside the Kubernetes panel, asking approval to scale deployment/api from 2 to 3 replicas',
      },
    ],
    faqs: [
      {
        q: 'Which clusters and auth methods work?',
        a: 'Any cluster your kubeconfig describes. Tokens, client certificates, and exec helper plugins you approve. Authentication can run on the host or inside an isolated workspace so cloud CLIs stay off your machine.',
      },
      {
        q: 'Can the Docker panel reach a remote engine?',
        a: 'Yes. Pick an SSH connection in the panel\'s host selector and it talks to the engine on that host through the docker CLI over SSH. Containers, images, volumes, networks, logs, shells, and file browsing all work the same way.',
      },
      {
        q: 'Can the agent break my cluster?',
        a: 'Not without you. Its Kubernetes capabilities are Off by default. When you enable them, mutations are limited to apply, scale, rollout restart, and delete, each shown as a preview and committed only after your approval. Node drains and Helm changes are UI-only.',
      },
      {
        q: 'What happens to a running deploy when my connection drops?',
        a: 'It keeps running. The terminal lives in a tmux or GNU Screen session on the server, so the process is not tied to your connection. When you are back online the panel reattaches to the same session, scrollback included.',
      },
    ],
  },
  {
    slug: 'client-work',
    label: 'Client work',
    teaser:
      'One workspace per client: its own VM, its own VPN, its own browser logins, its own connections. Nothing leaks between them.',
    title: 'Asura for consultants and client work',
    description:
      'Give each client a workspace with its own Linux VM, its own VPN or proxy, its own browser profile, and its own connections. The corporate AnyConnect for one client and your homelab Tailscale for another, at the same time, without touching the rest of your Mac.',
    hero: {
      eyebrow: 'For client work',
      title: 'Prod, staging, client A, and client B never meet',
      lede: 'A workspace holds its own connections, tabs, saved screens, browser identity, and network route. Add isolation and the whole workspace runs in a persistent Linux VM that reaches the network only through the tunnel you chose.',
      shot: 'settings-workspaces.webp',
      alt: 'Workspace settings listing separate workspaces, one marked Isolated',
    },
    sections: [
      {
        eyebrow: 'Per-workspace networking',
        title: 'Every workspace on its own VPN',
        lede: 'WireGuard, OpenVPN, Cisco AnyConnect, a Tailscale exit node, or a SOCKS5, HTTP, or HTTPS proxy. The engines ship inside the app as userspace tunnels: no system VPN profile, no kernel extension, no change to your routing table, and no other program dragged along.',
        points: [
          'Different workspaces can be on different tunnels at the same time.',
          'A switch in the title bar swaps or pauses the route without editing anything.',
          'A kill switch stops the workspace\'s traffic if the tunnel drops instead of sliding back to your direct connection.',
          'VPN configs, keys, and passwords live in your Keychain. A password you would rather not store is asked for at connect time.',
        ],
        shot: 'settings-networking.webp',
        alt: 'Networking settings with saved AnyConnect, proxy, Tailscale, WireGuard, and OpenVPN connections',
      },
      {
        eyebrow: 'Isolation',
        title: 'The whole workspace in its own VM',
        lede: 'Flip one switch and the workspace runs inside a persistent Linux container with its own kernel, root filesystem, and network namespace. Terminals, the file panel, browser traffic, database and Redis sessions, remote files, Kubernetes authentication, and the agent itself can all live in there.',
        points: [
          'Persistent by design. Closing the workspace stops the VM, not its disk. apt install once and it is there next week.',
          'The host stays private. Mount chosen folders read-only or read-write, or mount nothing.',
          'Paired with a workspace VPN, the VM has exactly one way out, so every process inside obeys the route, not only the ones that honor proxy settings.',
          'Ubuntu 24.04 by default, or any bootable OCI image with an init. The runtime is bundled; it needs Apple silicon and macOS 26.',
        ],
        shot: 'settings-workspace-editor-isolated.webp',
        alt: 'Workspace editor with the Isolate workspace switch and host mounts',
      },
      {
        eyebrow: 'Identity',
        title: 'Separate logins, separate secrets, separate layouts',
        lede: 'The embedded browser keeps cookies and sessions per workspace, so the AWS console you are signed into for one client does not exist in the next. Connections, saved screens, and the agent\'s view are scoped to the workspace too.',
        points: [
          'Named, encrypted browser profiles plus private sessions. New workspaces default to an isolated profile.',
          'Secrets are named entries in your Keychain that connections reference, with an access audit.',
          'Saved screens reopen a four-panel layout of terminal, browser, database, and monitor exactly as you left it.',
        ],
        shot: 'overlay-new-panel.webp',
        alt: 'The Add Panel chooser with connections and saved screens',
      },
    ],
    faqs: [
      {
        q: 'Does a workspace VPN affect the rest of my machine?',
        a: 'No. Each connection is a userspace tunnel inside Asura: no system VPN profile, no kernel extension, no change to your routing table. Only the workspaces that chose that connection send traffic through it. Your mail, your other browser, and everything else keep using the network as before.',
      },
      {
        q: 'What happens when a VPN drops?',
        a: 'With the kill switch on, a workspace whose route fails stops sending traffic until the route is back or you turn networking off for that workspace. Inside an isolated workspace the VM itself enforces this; it has no other way out. In a non-isolated workspace the kill switch covers everything Asura routes, while a program that ignores proxy settings could still reach the network directly.',
      },
      {
        q: 'What does isolation require?',
        a: 'Apple silicon and macOS 26. The container runtime is bundled with the app, and the Linux boot image is downloaded on first use from the matching release. Linux and Windows backends are planned.',
      },
    ],
  },
  {
    slug: 'full-stack',
    label: 'Full-stack development',
    teaser:
      'Database client, Redis browser, Docker Desktop, SFTP app, and Git GUI, replaced by panels in the window your terminal is already in.',
    title: 'Asura for full-stack development',
    description:
      'Database, Redis, Docker, Git, and file panels next to your terminal and a browser on localhost. Eleven SQL engines, container logs and shells, hunk staging and a commit graph, with local and remote hosts in the same selector.',
    hero: {
      eyebrow: 'For full-stack development',
      title: 'One window instead of five apps',
      lede: 'A terminal running the dev server, a browser on localhost, the database the app writes to, the Redis it caches in, the containers it depends on, and the Git repo it lives in. Split them, stack them, save the layout, and reopen it with one click.',
      shot: 'workspace-database.webp',
      alt: 'Database panel with a table grid and SQL editor beside a terminal',
    },
    sections: [
      {
        eyebrow: 'Database',
        title: 'Eleven engines, one panel',
        lede: 'PostgreSQL, MySQL, MariaDB, SQLite, SQL Server, ClickHouse, DuckDB, Oracle, Firebird, CockroachDB, and Amazon Redshift. Browse tables, edit rows inline including JSON and booleans, and write SQL with completion and validation that know your schema.',
        points: [
          'Schema diagram for the database you are looking at.',
          'Export a page as CSV, JSON, or SQL, or copy it as TSV.',
          'Remote databases connect through SSH tunnels or a workspace VPN. Passwords can be asked for at connect time.',
        ],
        shot: 'workspace-database.webp',
        alt: 'Database panel showing a table grid and SQL editor',
      },
      {
        eyebrow: 'Redis',
        title: 'Keys as a tree, values by type',
        lede: 'A key browser with search, TTLs, and pub/sub monitoring. JSON values render as trees, not strings. Standalone, Sentinel, and Cluster topologies.',
        shot: 'workspace-redis.webp',
        alt: 'Redis panel with a key tree and a JSON value view',
      },
      {
        eyebrow: 'Docker',
        title: 'Containers, logs, shells, and files',
        lede: 'Containers, images, volumes, and networks for a local daemon or a remote engine over SSH. Start, stop, restart, pause, and remove. Stream and search logs, open a shell inline or in a new tab, and browse files inside a container or a volume.',
        shot: 'workspace-docker-logs.webp',
        alt: 'Docker panel streaming and searching container logs',
      },
      {
        eyebrow: 'Git',
        title: 'The repository, not a bundled copy of it',
        lede: 'Stage hunks, write commits, and walk history on a commit graph. Branches, tags, stash, merge, rebase including interactive, cherry-pick, revert, reset, bisect, reflog, blame, and file history. Conflicts open a resolution dialog. Submodules, LFS locks, git-flow, and commit signing are there when you need them.',
        points: [
          'Clone from your GitHub or GitLab account with a repository browser.',
          'HTTP credentials are prompted for and can be stored in your Keychain.',
          'The agent can read state and diffs, stage, commit, and push, each under its own permission switch.',
        ],
        shot: 'workspace-git.webp',
        alt: 'Git panel with staged files and a diff view',
      },
      {
        eyebrow: 'Layouts',
        title: 'Save the screen, reopen it tomorrow',
        lede: 'Every panel docks, splits, and stacks. A screen is a saved arrangement of panels that a tab can open with one click, drawn in the Layout Designer. Tabs and panels move between windows. Closed tabs reopen. Sessions restore on start.',
        shot: 'overlay-new-panel.webp',
        alt: 'The Add Panel chooser listing panel types, connections, and saved screens',
      },
    ],
    faqs: [
      {
        q: 'Which databases are supported?',
        a: 'PostgreSQL, MySQL, MariaDB, SQLite, SQL Server, ClickHouse, DuckDB, Oracle, Firebird, CockroachDB, and Amazon Redshift, plus Redis in standalone, Sentinel, and Cluster topologies.',
      },
      {
        q: 'Can panels reach a remote Docker engine or database?',
        a: 'Yes. Every panel has a host selector. Pick an SSH connection and the Docker panel talks to that engine, the database panel tunnels to that port, and the file panel browses over SFTP.',
      },
    ],
  },
]

export function findUseCase(slug: string) {
  return useCases.find((c) => c.slug === slug)
}
