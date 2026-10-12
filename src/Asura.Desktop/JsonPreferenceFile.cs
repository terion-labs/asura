using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Asura.Application;

namespace Asura.Desktop;

/// <summary>
/// A small JSON document of presentation preferences kept beside the profile
/// database. Like the window frames, these carry nothing private and are read
/// before a startup PIN opens the database. A file that cannot be read means
/// the defaults; a write that fails is reported and otherwise ignored.
/// </summary>
internal sealed class JsonPreferenceFile<T>(
    string path,
    JsonTypeInfo<T> typeInfo,
    string diagnosticName)
    where T : class
{
    private const int MaximumFileBytes = 64 * 1024;

    private readonly string _path = string.IsNullOrWhiteSpace(path)
        ? throw new ArgumentException("A preference file path is required.", nameof(path))
        : path;

    public T? Load()
    {
        try
        {
            var file = new FileInfo(_path);
            if (!file.Exists || file.Length > MaximumFileBytes)
            {
                return null;
            }

            return JsonSerializer.Deserialize(File.ReadAllText(_path, Encoding.UTF8), typeInfo);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException)
        {
            SecretSafeDiagnosticProjection.WriteStandardError(
                $"desktop.{diagnosticName}.load-failed",
                exception);
            return null;
        }
    }

    public void Save(T document)
    {
        ArgumentNullException.ThrowIfNull(document);
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
                JsonSerializer.Serialize(document, typeInfo),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            SecretSafeDiagnosticProjection.WriteStandardError(
                $"desktop.{diagnosticName}.save-failed",
                exception);
            try
            {
                File.Delete(temporary);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                // The temporary file is harmless and named uniquely.
            }
        }
    }
}
