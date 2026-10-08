namespace Asura.App.Views;

/// <summary>
/// Why a file Asura was asked to write couldn't be saved, said as the
/// person can act on it. The exception's own text names types and paths, not
/// causes, so it is never shown.
/// </summary>
internal static class SaveFailure
{
    public static string Describe(Exception exception) => exception switch
    {
        UnauthorizedAccessException => "Asura doesn’t have permission to save there. Choose another folder.",
        NotSupportedException => "That location can’t be saved to. Choose a folder on this computer.",
        _ => "The file couldn’t be written. Check that the disk has room, then try again.",
    };
}
