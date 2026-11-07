// Product facts the pages share. Each list mirrors a registry in the app so
// the site cannot drift from what ships; the source is named next to each.

export const siteUrl = 'https://asura.sh'
export const repoUrl = 'https://github.com/terion-labs/asura'
export const releasesUrl = `${repoUrl}/releases/latest`
// README.md, "Linux": the install steps the Linux archives need.
export const linuxInstallUrl = `${repoUrl}#linux`

// .github/workflows/repository-gate.yml: the assets a tagged release
// publishes. Archive names carry no version, so these "latest" links hold.
export type LinuxCpu = 'x64' | 'arm64'
export type BuildId =
  | 'macos-arm64'
  | `linux-${LinuxCpu}-deb`
  | `linux-${LinuxCpu}-rpm`
  | `linux-${LinuxCpu}-appimage`
  | `linux-${LinuxCpu}-tar`
export type BuildOs = 'macOS' | 'Linux'

export interface Build {
  id: BuildId
  os: BuildOs
  /** The CPU the download is built for, as the button prints it. */
  arch: string
  file: '.zip' | '.deb' | '.rpm' | '.AppImage' | '.tar.gz'
  /** Which systems the download installs on, as the menu prints it. */
  fits: string
  url: string
  checksumUrl: string
}

function build(id: BuildId, os: BuildOs, arch: string, file: Build['file'], fits: string, asset: string): Build {
  const url = `${releasesUrl}/download/${asset}`
  return { id, os, arch, file, fits, url, checksumUrl: `${url}.sha256` }
}

function linux(cpu: LinuxCpu): Build[] {
  return [
    build(`linux-${cpu}-deb`, 'Linux', cpu, '.deb', 'Debian, Ubuntu', `Asura-linux-${cpu}.deb`),
    build(`linux-${cpu}-rpm`, 'Linux', cpu, '.rpm', 'Fedora, RHEL', `Asura-linux-${cpu}.rpm`),
    build(`linux-${cpu}-appimage`, 'Linux', cpu, '.AppImage', 'any distribution, updates in-app', `Asura-linux-${cpu}.AppImage`),
    build(`linux-${cpu}-tar`, 'Linux', cpu, '.tar.gz', 'any distribution', `Asura-linux-${cpu}.tar.gz`),
  ]
}

export const builds: readonly Build[] = [
  build('macos-arm64', 'macOS', 'Apple silicon', '.zip', 'macOS 13 or later', 'Asura-macOS-arm64.zip'),
  ...linux('x64'),
  ...linux('arm64'),
]

/** What the page shows before the visitor's platform is known. */
export const defaultBuild = builds[0]!

// src/Asura.Core/AiProviderCatalog.cs. Only runtime-supported providers.
export const providers = [
  'Anthropic',
  'OpenAI',
  'xAI',
  'DeepSeek',
  'Moonshot AI',
  'OpenRouter',
  'GitHub Copilot',
  'Ollama',
  'Any OpenAI-compatible endpoint',
]

// Catalog entries with isRuntimeSupported: false.
export const providersComingSoon = ['Google', 'Amazon Bedrock']

// src/Asura.Databases/BuiltInDatabaseDrivers.cs
export const databases = [
  'PostgreSQL',
  'MySQL',
  'MariaDB',
  'SQLite',
  'SQL Server',
  'ClickHouse',
  'DuckDB',
  'Oracle',
  'Firebird',
  'CockroachDB',
  'Amazon Redshift',
]

// src/Asura.Core/FileProviderKind.cs
export const fileProviders = ['local disk', 'SFTP', 'FTP', 'S3', 'WebDAV', 'SMB']

// src/Asura.Core/NetworkConnectionKind.cs and NetworkProxyProtocol.cs
export const networkRoutes = [
  'WireGuard',
  'OpenVPN',
  'Cisco AnyConnect',
  'Tailscale exit node',
  'SOCKS5, HTTP, or HTTPS proxy',
]

export const platforms = {
  shipped: 'macOS 13 or later on Apple silicon',
  linux: 'Linux on x64 and arm64 (Ubuntu 24.04, Debian 13, Fedora 39 or newer), on X11 or through XWayland',
  isolation: 'macOS 26 on Apple silicon',
  upcoming: 'A Windows build is on the way',
}
