namespace Asura.Application;

/// <summary>
/// Only Chromium's regenerable caches are excluded from encrypted snapshots.
/// Service Worker/CacheStorage, IndexedDB and similarly named site files remain
/// durable. Paths are relative to either a request context or the engine root.
/// </summary>
public static class BrowserDisposableCache
{
    public static bool Contains(string relativePath)
    {
        var parts = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }
        var index = IsProfileDirectory(parts[0]) ? 1 : 0;
        // Engine snapshots also contain Asura request-context roots, which may
        // themselves use Chromium's Default/Profile N layout.
        if (parts[0].StartsWith("profile-", StringComparison.Ordinal)
            && parts.Length > index && IsProfileDirectory(parts[index]))
        {
            index++;
        }
        return parts.Length > index && parts[index] is
            "Cache" or "Code Cache" or "GPUCache" or "DawnCache"
            or "DawnGraphiteCache" or "DawnWebGPUCache" or "ShaderCache"
            or "GrShaderCache" or "GraphiteDawnCache";
    }

    public static bool IsProfileDirectory(string name) =>
        string.Equals(name, "Default", StringComparison.Ordinal) || name.StartsWith("Profile ", StringComparison.Ordinal)
        || name.StartsWith("profile-", StringComparison.Ordinal);
}
