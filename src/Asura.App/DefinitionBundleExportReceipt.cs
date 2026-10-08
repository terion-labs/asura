namespace Asura.App;

public sealed record DefinitionBundleExportReceipt(
    string Path,
    int DefinitionCount,
    DateTimeOffset ExportedAt,
    int ReconnectRequiredDatabasePanelCount = 0)
{
    public string? Warning => ReconnectRequiredDatabasePanelCount == 0 ? null
        : (ReconnectRequiredDatabasePanelCount == 1
              ? "1 older database panel in this export needs its connection chosen again"
              : $"{ReconnectRequiredDatabasePanelCount:N0} older database panels in this export need their connections chosen again")
          + " because their saved connections contain credentials. Your saved connections on this computer are unchanged.";
}
