// Product facts the pages share. Each list mirrors a registry in the app so
// the site cannot drift from what ships; the source is named next to each.

export const siteUrl = 'https://asura.sh'
export const repoUrl = 'https://github.com/terion-labs/asura'
export const releasesUrl = `${repoUrl}/releases/latest`
export const downloadUrl = `${releasesUrl}/download/Asura-macOS-arm64.zip`
export const checksumUrl = `${downloadUrl}.sha256`

// .github/workflows/repository-gate.yml, linux-release-packages. The
// archive names carry no version so these "latest" links stay valid.
export const linuxDownloads = [
  { arch: 'x64', url: `${releasesUrl}/download/Asura-linux-x64.tar.gz` },
  { arch: 'arm64', url: `${releasesUrl}/download/Asura-linux-arm64.tar.gz` },
]

// README.md, "Linux": the install steps the archives need.
export const linuxInstallUrl = `${repoUrl}#linux`

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
  linux: 'Linux on x64 and arm64, on X11 or through XWayland',
  isolation: 'macOS 26 on Apple silicon',
  upcoming: 'A Windows build is on the way',
}
