using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    private string? _formattedManifestSource;
    private string _formattedManifest = string.Empty;

    public string FormattedManifest
    {
        get
        {
            string source = Manifest;
            if (!string.Equals(source, _formattedManifestSource, StringComparison.Ordinal))
            {
                _formattedManifestSource = source;
                _formattedManifest = FormatManifestJson(source);
            }
            return _formattedManifest;
        }
    }

    public string ManifestGrammarExtension
    {
        get
        {
            var text = ManifestDraft.AsSpan().TrimStart();
            return text.IsEmpty || text[0] is '{' or '[' ? ".json" : ".yaml";
        }
    }

    private static string FormatManifestJson(string source)
    {
        if (string.IsNullOrWhiteSpace(source)) { return source; }
        try
        {
            using var document = JsonDocument.Parse(source);
            var output = new ManifestFormattingBuffer();
            using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true }))
            {
                document.RootElement.WriteTo(writer);
            }
            return Encoding.UTF8.GetString(output.WrittenSpan);
        }
        catch (JsonException) { return source; }
        catch (InvalidDataException) { return source; }
    }

    /// <summary>Limits indentation and escaping expansion while preserving the complete source on overflow.</summary>
    private sealed class ManifestFormattingBuffer : IBufferWriter<byte>
    {
        private const int MaximumBytes = 8 * 1024 * 1024;
        private readonly ArrayBufferWriter<byte> _buffer = new();

        public ReadOnlySpan<byte> WrittenSpan => _buffer.WrittenSpan;

        public void Advance(int count)
        {
            EnsureFits(count);
            _buffer.Advance(count);
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            EnsureFits(Math.Max(1, sizeHint));
            var memory = _buffer.GetMemory(sizeHint);
            return memory[..Math.Min(memory.Length, MaximumBytes - _buffer.WrittenCount)];
        }

        public Span<byte> GetSpan(int sizeHint = 0) => GetMemory(sizeHint).Span;

        private void EnsureFits(int count)
        {
            if (count > MaximumBytes - _buffer.WrittenCount)
            {
                throw new InvalidDataException("Manifest formatting exceeds its display budget.");
            }
        }
    }
}
