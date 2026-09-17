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
            using var output = new MemoryStream();
            using (var writer = new Utf8JsonWriter(output, new JsonWriterOptions { Indented = true }))
            {
                document.RootElement.WriteTo(writer);
            }
            return Encoding.UTF8.GetString(output.GetBuffer(), 0, checked((int)output.Length));
        }
        catch (JsonException) { return source; }
    }
}
