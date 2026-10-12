namespace Asura.Application;

/// <summary>
/// How previews transferred onto the host spend the user's bandwidth and disk.
///
/// One threshold carries two decisions on purpose. Below it a transferred file is
/// fetched the moment it is selected and its bytes live in memory; above it
/// the preview waits to be asked for, and once asked for, the bytes go to the
/// on-disk cache. A small file is cheap to fetch and cheap to hold; a large
/// one is neither, so it is fetched deliberately and held on disk. Splitting
/// those into two settings would ask the user to reason about this
/// application's storage tiers.
/// </summary>
public sealed record FilePreviewSettings(
    long AutoLoadThresholdBytes,
    bool KeepPreviewsBetweenRuns,
    long CacheBudgetBytes)
{
    // Decimal megabytes, the unit every size in the app is written in, so
    // the settings page and the preview's caption name the same number.
    public static FilePreviewSettings Default { get; } = new(
        AutoLoadThresholdBytes: 2_000_000,
        KeepPreviewsBetweenRuns: true,
        CacheBudgetBytes: 512_000_000);

    private const long BinaryMegabyte = 1024 * 1024;
    private const long DecimalMegabyte = 1_000_000;

    private readonly long _autoLoadThresholdBytes = ClampThreshold(AutoLoadThresholdBytes);
    private readonly long _cacheBudgetBytes = ClampBudget(CacheBudgetBytes);

    /// <summary>
    /// Bounds hold whatever arrives from storage or a binding — including
    /// through <c>with</c>: a zero or negative threshold would make every
    /// preview wait to be asked for, and an absurd budget is a typo, not an
    /// intent.
    /// </summary>
    public long AutoLoadThresholdBytes
    {
        get => _autoLoadThresholdBytes;
        init => _autoLoadThresholdBytes = ClampThreshold(value);
    }

    public long CacheBudgetBytes
    {
        get => _cacheBudgetBytes;
        init => _cacheBudgetBytes = ClampBudget(value);
    }

    /// <summary>
    /// Settings as storage holds them. Before sizes were decimal, the settings
    /// page saved whole binary megabytes (2 MiB, 512 MiB); read back as they
    /// are, those would show as 2.097152 MB and step to 3.097152. Such a value
    /// becomes the same count of decimal megabytes. A value that is already a
    /// whole number of decimal megabytes is kept as it is.
    /// </summary>
    public static FilePreviewSettings FromStorage(
        long autoLoadThresholdBytes,
        bool keepPreviewsBetweenRuns,
        long cacheBudgetBytes) =>
        new(
            FromBinaryMegabytes(autoLoadThresholdBytes),
            keepPreviewsBetweenRuns,
            FromBinaryMegabytes(cacheBudgetBytes));

    internal static long FromBinaryMegabytes(long bytes) =>
        bytes > 0 && bytes % BinaryMegabyte == 0 && bytes % DecimalMegabyte != 0
            ? bytes / BinaryMegabyte * DecimalMegabyte
            : bytes;

    private static long ClampThreshold(long value) =>
        Math.Clamp(value, 64_000, 256_000_000);

    private static long ClampBudget(long value) =>
        Math.Clamp(value, 16_000_000, 16_384_000_000);
}

/// <summary>
/// The live preview settings, shared by every file panel and the settings
/// page. A change applies the moment it is made: it is persisted, published
/// through <see cref="Changed"/>, and read by the next preview — there is no
/// save step anywhere.
/// </summary>
public interface IFilePreviewPreferences
{
    FilePreviewSettings Current { get; }

    event EventHandler? Changed;

    ValueTask ApplyAsync(FilePreviewSettings settings, CancellationToken cancellationToken);
}

/// <summary>
/// The settings page's window into the preview cache: how much disk it holds
/// and the one deliberate way to empty it. Separate from the preferences
/// because usage is a fact about the cache, not a choice the user made.
/// </summary>
public interface IPreviewCacheControl
{
    /// <summary>Bytes currently held on disk across every container.</summary>
    long CachedBytes { get; }

    ValueTask ClearAsync(CancellationToken cancellationToken);
}
