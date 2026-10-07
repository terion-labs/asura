using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Asura.App;
using Asura.Application;

namespace Asura.Desktop;

/// <summary>
/// Keeps window frames in a small file beside the profile database. Frames
/// carry nothing private, and unlike the database they can be read before a
/// startup PIN is entered, which is when the first window is placed.
/// </summary>
internal sealed class FileWindowPlacementStore(string path) : IWindowPlacementStore
{
    private const int SchemaVersion = 1;
    private const int MaximumFileBytes = 64 * 1024;

    private readonly string _path = string.IsNullOrWhiteSpace(path)
        ? throw new ArgumentException("A window placement path is required.", nameof(path))
        : path;

    public WindowPlacementSet Load()
    {
        try
        {
            var file = new FileInfo(_path);
            if (!file.Exists || file.Length > MaximumFileBytes)
            {
                return WindowPlacementSet.Empty;
            }

            var document = JsonSerializer.Deserialize(
                File.ReadAllText(_path, Encoding.UTF8),
                WindowPlacementJsonContext.Default.WindowPlacementDocument);
            if (document is not { SchemaVersion: SchemaVersion })
            {
                return WindowPlacementSet.Empty;
            }

            // A damaged entry costs that window its frame, not the others theirs.
            var windows = (document.Windows ?? [])
                .Where(item => item.IsValid)
                .DistinctBy(item => item.Slot)
                .Take(WindowPlacement.MaximumSlots)
                .ToArray();
            return new WindowPlacementSet(
                windows,
                document.LastClosed is { IsValid: true } lastClosed ? lastClosed : null);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException)
        {
            SecretSafeDiagnosticProjection.WriteStandardError(
                "desktop.window-placement.load-failed",
                exception);
            return WindowPlacementSet.Empty;
        }
    }

    public void Save(WindowPlacementSet placements)
    {
        ArgumentNullException.ThrowIfNull(placements);
        var directory = Path.GetDirectoryName(_path);
        var temporary = $"{_path}.{Guid.NewGuid():N}.tmp";
        try
        {
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                temporary,
                JsonSerializer.Serialize(
                    new WindowPlacementDocument(
                        SchemaVersion,
                        [.. placements.Windows],
                        placements.LastClosed),
                    WindowPlacementJsonContext.Default.WindowPlacementDocument),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            // Losing a frame is not worth interrupting anyone over; the
            // window simply opens where a new one would.
            SecretSafeDiagnosticProjection.WriteStandardError(
                "desktop.window-placement.save-failed",
                exception);
            TryDelete(temporary);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // The temporary file is harmless and named uniquely.
        }
    }
}

internal sealed record WindowPlacementDocument(
    int SchemaVersion,
    WindowPlacement[]? Windows,
    WindowPlacement? LastClosed);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(WindowPlacementDocument))]
internal sealed partial class WindowPlacementJsonContext : JsonSerializerContext;
