<script setup lang="ts">
import {
  databases,
  networkRoutes,
  platforms,
  providers,
  providersComingSoon,
  repoUrl,
  siteUrl,
} from '~/content/facts'

const base = useRuntimeConfig().app.baseURL

const title = 'Asura — terminals, servers, and an AI agent in one window'
const description =
  'A native terminal workspace with an AI agent that works in your terminals, browser, and servers over plain SSH. Terminal, browser, files, databases, Docker, Kubernetes, and Git in one window, each workspace in its own VM and on its own VPN. Nothing to install on your servers.'

useHead({ link: [{ rel: 'canonical', href: `${siteUrl}/` }] })

useSeoMeta({
  title,
  description,
  ogTitle: title,
  ogDescription: description,
  ogType: 'website',
  ogUrl: `${siteUrl}/`,
  ogSiteName: 'Asura',
  ogImage: `${siteUrl}/share.png`,
  ogImageWidth: 1200,
  ogImageHeight: 630,
  ogImageType: 'image/png',
  ogImageAlt: 'Asura app icon beside the words: terminals, servers, and an AI agent in one window',
  twitterCard: 'summary_large_image',
  twitterTitle: title,
  twitterDescription: description,
  twitterImage: `${siteUrl}/share.png`,
})

const faqs = [
  {
    q: 'Do I have to install anything on my servers?',
    a: 'No. Everything remote runs over standard SSH and SFTP. Session continuity uses the tmux or GNU Screen already on the box, monitoring samples with plain system commands, and the agent drives a normal PTY. No daemon, no sidecar, no extra open ports.',
  },
  {
    q: 'How does the browser reach a remote localhost?',
    a: 'A browser panel bound to an SSH connection sends its traffic through that connection. Name resolution happens on the server side, so localhost, private IPs, and internal hostnames resolve there. No port forwarding to set up by hand.',
  },
  {
    q: 'Does the agent see my secrets?',
    a: 'No. Secrets live in your Keychain on macOS (DPAPI on Windows, Secret Service on Linux) and the app passes around references to them. The agent works with session handles, not credentials. When a subprocess needs a passphrase, it arrives through a single-use, current-user-only pipe and is wiped from memory after use. Secrets you paste into the chat are replaced by placeholders before the model sees them.',
  },
  {
    q: 'What does the agent actually control?',
    a: 'Whatever you allow, per capability. Ask is the default for actions that change state, so a terminal command, a file write, a Docker action, or a Kubernetes apply waits for your click. Auto lets it observe and do routine actions on its own. Full access is time-bounded and granted on purpose. Kubernetes capabilities are Off until you turn them on. If you start typing in a terminal the agent is using, it loses its input lease immediately.',
  },
  {
    q: 'Which platforms are supported?',
    a: `macOS, Windows, and Linux from one codebase. The current early release ships a signed, notarized build for ${platforms.shipped}. ${platforms.upcoming}. Workspace isolation needs ${platforms.isolation}.`,
  },
  {
    q: 'Does a workspace VPN affect the rest of my machine?',
    a: 'No. Each connection runs as a userspace tunnel inside Asura: no system VPN profile, no kernel extension, no change to your routing table. Only the workspaces that chose that connection send traffic through it, and different workspaces can be on different tunnels at the same time. Your other browser, mail, and everything else keep using the network as before.',
  },
  {
    q: 'What happens when a VPN drops?',
    a: 'With the kill switch on, a workspace whose route fails stops sending traffic until the route is back or you turn networking off for that workspace. It never quietly falls back to your direct connection. Inside an isolated workspace the VM itself enforces this; it has no other way out. In a non-isolated workspace the kill switch covers everything Asura routes, while a program that ignores proxy settings could still reach the network directly.',
  },
  {
    q: 'What does workspace isolation actually isolate?',
    a: `Each isolated workspace is a persistent Linux container with its own kernel, root filesystem, and network namespace. On macOS it runs on Apple's container runtime, where every container is its own lightweight VM. Terminals, the file panel, browser traffic, database and Redis sessions, remote files, and Kubernetes authentication run inside it, and you can run the agent there too. The host stays invisible except for the folders you mount. The runtime ships with the app; it needs ${platforms.isolation}.`,
  },
  {
    q: 'Is it open source?',
    a: 'Yes. Asura is MIT licensed. The whole app, the native terminal and browser builds, and the website are in one GitHub repository.',
  },
]
</script>

<template>
  <div>
    <div class="notice">
      <div class="wrap notice__row">
        <span class="notice__badge">Alpha</span>
        <span>
          Asura is in early alpha and ships for macOS on Apple silicon.
          Windows and Linux are on the way.
        </span>
      </div>
    </div>

    <SiteHeader />

    <main>
      <!-- Hero -->
      <section class="section section--flush hero">
        <div class="wrap">
          <div class="hero__inner">
            <p class="eyebrow">The AI-native terminal workspace</p>
            <h1 class="hero__title">Terminals, servers, and an AI agent in one window</h1>
            <p class="hero__lede">
              Asura is a native desktop workspace. Terminal, browser, files,
              databases, Docker, Kubernetes, and Git, each able to point at a
              local or remote machine over plain SSH. An AI agent works inside
              those panels; a command, click, or file write it proposes runs
              only after you approve it. Nothing to install on your servers.
            </p>
            <div class="ctas">
              <a class="btn btn--primary" href="#download">
                Download for macOS
                <span class="btn__sub">Apple silicon</span>
              </a>
              <a class="btn" :href="repoUrl">View on GitHub</a>
            </div>
            <ul class="hero__badges">
              <li>Ghostty terminal engine</li>
              <li>Embedded Chromium</li>
              <li>Agent actions gated by approval</li>
              <li>A VM and a VPN per workspace</li>
              <li>MIT licensed</li>
            </ul>
          </div>
          <div class="shot hero__shot">
            <img
              :src="`${base}shots/workspace-agent.webp`"
              alt="Asura workspace: an SSH terminal, a browser panel, and the AI agent fixing a failed deploy"
              width="1760"
              height="1100"
              fetchpriority="high"
            />
          </div>
        </div>
      </section>

      <UseCaseCards />

      <!-- The four things -->
      <section id="different" class="section">
        <div class="wrap">
          <p class="eyebrow">Why it exists</p>
          <h2 data-reveal class="section-title">Four things your current terminal does not do</h2>
          <div class="grid quad">
            <div class="card" data-reveal>
              <h3>Browse a remote's localhost</h3>
              <p>
                Your dev server runs on a cloud box at
                <code>localhost:3000</code>. Open a browser panel, pick that
                SSH connection, type the address. Asura routes the panel
                through the tunnel and the page loads, private subnet
                dashboards included. No <code>ssh -L</code>. The agent can
                browse those pages too.
              </p>
            </div>
            <div class="card" data-reveal>
              <h3>An agent with zero footprint</h3>
              <p>
                The agent runs inside the app on your machine and reaches
                servers over the SSH session you already have. It reads the
                terminal, types, and handles interactive prompts and TUIs.
                The remote host sees an ordinary SSH user, because that is all
                there is.
              </p>
            </div>
            <div class="card" data-reveal>
              <h3>Every panel goes remote</h3>
              <p>
                Files, databases, Docker, Kubernetes, processes, system
                statistics: each panel has a host selector. Flip it from Local
                to a connection and the same panel works against the remote
                machine, over SSH, without a remote agent. One app instead of
                seven.
              </p>
            </div>
            <div class="card" data-reveal>
              <h3>A workspace in its own VM, on its own VPN</h3>
              <p>
                Flip a switch and the whole workspace runs in a persistent,
                lightweight Linux VM: own kernel, own filesystem, own
                network. Give it its own tunnel too: the corporate
                AnyConnect, your homelab's Tailscale, a WireGuard peer.
                Everything inside goes out that way and nothing else on
                your machine does.
              </p>
            </div>
          </div>
        </div>
      </section>

      <!-- Agent -->
      <section id="agent" class="section section--alt">
        <div class="wrap split">
          <div class="split__text" data-reveal>
            <p class="eyebrow">The agent</p>
            <h2 class="section-title">An agent inside your terminals and browser</h2>
            <p class="section-lede">
              Ask it to fix a failed deploy and it reads the logs, checks the
              service, and proposes the command. It runs in-process, native
              .NET, no Node sidecar on your laptop and nothing at all on the
              server.
            </p>
            <ul class="checks">
              <li>
                Types like you do: real keystrokes in real terminals, so it
                can drive <code>vim</code>, installers, and cloud CLI
                wizards, not only run commands.
              </li>
              <li>
                Drives the browser too. It reads the page as an accessibility
                snapshot and clicks, fills, and checks elements by reference.
                Point the browser through an SSH tunnel and that includes
                your internal web apps.
              </li>
              <li>
                Permissions per capability: Off, Ask, Auto, or a time-bounded
                Full access. Ask is the default for anything that changes
                state.
              </li>
              <li>
                Press a key in a terminal the agent is using and its input
                lease is revoked at once.
              </li>
              <li>
                It never sees your secrets. Keys and passwords stay in your
                Keychain, and secrets pasted into chat become placeholders.
              </li>
              <li>
                Searches the web when a task needs fresh information, calls
                your MCP servers, and can itself be driven by another agent
                over MCP. Same approvals on every call.
              </li>
            </ul>
            <div class="providers">
              <p class="providers__label">Bring your own model</p>
              <ul class="chips">
                <li v-for="p in providers" :key="p">{{ p }}</li>
                <li v-for="p in providersComingSoon" :key="p" class="chips__soon">
                  {{ p }} · soon
                </li>
              </ul>
            </div>
            <NuxtLink class="more" to="/for/ai-agent">More about the agent</NuxtLink>
          </div>
          <div class="shot split__shot" data-reveal style="--rd: 1">
            <img
              :src="`${base}shots/workspace-agent-reasoning.webp`"
              alt="The docked agent streaming its reasoning while a tool call waits for approval"
              width="1760"
              height="1100"
              loading="lazy"
            />
          </div>
        </div>
      </section>

      <!-- Panels tour -->
      <section id="panels" class="section">
        <div class="wrap">
          <p class="eyebrow">One window</p>
          <h2 data-reveal class="section-title">The panels</h2>
          <p data-reveal style="--rd: 1" class="section-lede">
            Split them, stack them, save the layout as a screen you can reopen
            with one click. Every panel picks its own host: local, SSH, a
            Docker container, or WSL.
          </p>
          <PanelTour class="panels-tour" data-reveal style="--rd: 1" />
        </div>
      </section>

      <SplitSection
        eyebrow="Session continuity"
        title="Close the lid. The build keeps running."
        lede="Remote terminals keep their own session running on the server, inside the tmux or GNU Screen already on the box. Drop Wi-Fi, switch networks, restart the app: the compile keeps going and the terminal reattaches when you return. Scrollback included, nothing installed."
        :points="[
          'A global Quick Terminal drops a shell over whatever you are doing. Hotkey, height, opacity, and blur are yours to set.',
          'A command palette reaches every command, panel, connection, saved screen, and recent session.',
          'Sessions restore on start. Closed tabs reopen. A crash opens a recovery workspace.',
        ]"
        shot="overlay-command-palette.webp"
        alt="The command palette listing new panels, commands, and settings"
        flip
        alt_bg
      />

      <SplitSection
        id="workspaces"
        eyebrow="Workspaces"
        title="Prod, staging, and personal never meet"
        lede="A workspace holds its own connections, tabs, saved screens, network route, and browser identity. The embedded browser keeps cookies and sessions per workspace, so the AWS console you are signed into in Client A does not exist in Client B."
        :points="[
          'Isolated browser profiles per workspace: separate logins, separate sessions, zero cookie bleed between clients or between work and personal.',
          'Saved screens: a four-panel layout of terminal, browser, database, and monitor reopens exactly as you left it.',
          'Workspace-scoped agent: the agent only sees the panels of the workspace it lives in.',
        ]"
        shot="settings-workspaces.webp"
        alt="Workspace settings listing separate workspaces, one marked Isolated"
      />

      <SplitSection
        id="isolation"
        eyebrow="Workspace isolation"
        title="The whole workspace in its own VM"
        lede="Flip one switch and a workspace runs inside its own persistent Linux environment: a lightweight VM with its own kernel, filesystem, and network namespace. Terminals, files, browser traffic, database sessions, and the agent can all live in there. Install whatever the project needs. The host stays clean."
        :points="[
          'Persistent by design. Closing the workspace stops the VM, not its disk. Run apt install once and it is still there next week.',
          'The host stays private. Nothing is shared unless you mount it: pick host folders per workspace, read-only or read-write, or mount nothing at all.',
          'Conflicting stacks stop conflicting. One project on Node 18 and Postgres 14, another on Node 22 and Postgres 17, side by side.',
          'Ubuntu 24.04 by default, or any bootable OCI image with an init.',
          'Pair it with a per-workspace VPN and the VM\'s only way out is the route you chose, so every process inside obeys it.',
          `The runtime ships with the app. It needs ${platforms.isolation}; Linux and Windows backends are planned.`,
        ]"
        shot="settings-workspace-editor-isolated.webp"
        alt="Workspace editor with the Isolate workspace switch and host mount options"
        flip
        alt_bg
      />

      <SplitSection
        id="networking"
        eyebrow="Per-workspace networking"
        title="Every workspace on its own VPN"
        lede="The client workspace on the corporate AnyConnect. The homelab workspace on your Tailscale. The personal one wrapped in WireGuard. All at the same time, in one app, and none of it touching the rest of your machine."
        :points="[
          `Five kinds of route: ${networkRoutes.join(', ')}. The engines ship inside the app. No VPN client to install, no kernel extension, no admin prompt.`,
          'Nothing system-wide. Each connection is a userspace tunnel the app owns: no system VPN, no change to your routing table, no other program\'s traffic dragged along with it.',
          'The whole workspace goes through it: terminals and SSH, browser panels, files, databases, Docker, Kubernetes, Git, MCP servers, and the agent if you run it in there. Inside an isolated workspace, DNS too.',
          'One switch in the title bar. Set an app-wide default, give a workspace its own list of allowed connections, then swap or pause the route from the window.',
          'A kill switch. If the tunnel drops, the workspace\'s traffic stops rather than sliding back onto your direct connection.',
          'Configs, keys, and passwords live in your Keychain, never in the connection definition. A password you would rather not store is asked for at connect time and forgotten afterwards.',
        ]"
        shot="settings-networking.webp"
        alt="Networking settings: the application default route and a list of saved AnyConnect, proxy, Tailscale, WireGuard, and OpenVPN connections"
      />

      <!-- Security -->
      <section id="security" class="section section--alt">
        <div class="wrap">
          <p class="eyebrow">Security</p>
          <h2 data-reveal class="section-title">Encrypted at rest, secrets in Keychain, source in the open</h2>
          <p data-reveal style="--rd: 1" class="section-lede">
            Storage is encrypted, secrets never leave the system store, and
            the code that does both is public.
          </p>
          <div class="grid trio">
            <div class="card" data-reveal>
              <h3>Encrypted at rest</h3>
              <p>
                Connection profiles, session history, and browser profiles
                live in encrypted databases whose keys are random and kept in
                your Keychain. Turn on startup protection and those keys are
                wrapped under a PIN through 600,000 rounds of PBKDF2, with a
                guessing backoff that survives restarts.
              </p>
            </div>
            <div class="card" data-reveal>
              <h3>Secrets stay in your Keychain</h3>
              <p>
                SSH passphrases, API keys, VPN credentials, and database
                passwords live in the macOS Keychain (DPAPI on Windows, Secret
                Service on Linux), never in config files. The app passes
                around references, hands the real value over a single-use
                channel at the moment of use, and wipes it from memory after.
                Every access is audited.
              </p>
            </div>
            <div class="card" data-reveal>
              <h3>Touch ID, PIN, and idle lock</h3>
              <p>
                Unlock with Touch ID or your PIN, and let an idle timer lock
                the app again. A remote process that wants to read your
                clipboard asks first. Unsafe control characters in a paste
                never reach the shell.
              </p>
            </div>
          </div>
          <p class="security__list" data-reveal>
            Databases the panel speaks to: {{ databases.join(', ') }}, and
            Redis.
          </p>
        </div>
      </section>

      <FaqList :faqs="faqs" />

      <DownloadSection />
    </main>

    <SiteFooter />
  </div>
</template>

<style scoped>
/* alpha notice */
.notice {
  background: var(--accent-soft);
  border-bottom: 1px solid color-mix(in srgb, var(--accent) 30%, transparent);
}

.notice__row {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 10px;
  padding-block: 9px;
  font-size: 13px;
  color: var(--muted);
  text-align: center;
}

.notice__badge {
  flex-shrink: 0;
  font-family: var(--mono);
  font-size: 10.5px;
  font-weight: 700;
  letter-spacing: 0.09em;
  text-transform: uppercase;
  color: #16120c;
  background: var(--accent);
  border-radius: 999px;
  padding: 2px 9px;
}

/* hero */
.hero { padding-top: 88px; }

@keyframes rise {
  from {
    opacity: 0;
    transform: translateY(18px);
  }
  to {
    opacity: 1;
    transform: none;
  }
}

.hero__inner > * {
  animation: rise 0.7s cubic-bezier(0.22, 0.61, 0.36, 1) backwards;
}

.hero__inner > *:nth-child(2) { animation-delay: 0.08s; }
.hero__inner > *:nth-child(3) { animation-delay: 0.16s; }
.hero__inner > *:nth-child(4) { animation-delay: 0.24s; }
.hero__inner > *:nth-child(5) { animation-delay: 0.32s; }

.hero__shot {
  animation: rise 0.85s cubic-bezier(0.22, 0.61, 0.36, 1) 0.3s backwards;
  margin-top: 64px;
}

.hero__inner {
  max-width: 820px;
  margin-inline: auto;
  text-align: center;
  display: flex;
  flex-direction: column;
  align-items: center;
}

.hero__title {
  font-size: clamp(38px, 6vw, 68px);
  letter-spacing: -0.03em;
}

.hero__lede {
  margin-top: 22px;
  font-size: clamp(16px, 2vw, 18.5px);
  color: var(--muted);
  max-width: 62ch;
}

.hero__badges {
  display: flex;
  flex-wrap: wrap;
  gap: 10px 26px;
  justify-content: center;
  list-style: none;
  padding: 0;
  margin: 36px 0 0;
  color: var(--faint);
  font-size: 13px;
}

.providers { margin-top: 34px; }

.providers__label {
  font-size: 12px;
  font-family: var(--mono);
  letter-spacing: 0.1em;
  text-transform: uppercase;
  color: var(--faint);
  margin-bottom: 12px;
}

.more {
  display: inline-block;
  margin-top: 26px;
  font-size: 14px;
  font-weight: 500;
  color: var(--accent);
}

.panels-tour { margin-top: 40px; }

.security__list {
  margin-top: 28px;
  color: var(--faint);
  font-size: 13.5px;
  max-width: 80ch;
}
</style>
