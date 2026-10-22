// Product facts the pages share. Each list mirrors a registry in the app so
// the site cannot drift from what ships; the source is named next to each.

export const siteUrl = 'https://asura.sh'
export const repoUrl = 'https://github.com/terion-labs/asura'
export const releasesUrl = `${repoUrl}/releases/latest`
export const downloadUrl = `${releasesUrl}/download/Asura-macOS-arm64.zip`
export const checksumUrl = `${downloadUrl}.sha256`

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
  isolation: 'macOS 26 on Apple silicon',
  upcoming: 'Windows and Linux builds are on the way',
}
