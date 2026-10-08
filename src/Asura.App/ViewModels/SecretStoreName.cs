namespace Asura.App.ViewModels;

/// <summary>
/// The secret store's name for views that bind it as
/// <c>vm:SecretStoreName.Current</c>. It is <see cref="Asura.Core.PlatformTerms.SecretStore"/>;
/// new text uses <see cref="Asura.Core.PlatformTerms"/> directly.
/// </summary>
internal static class SecretStoreName
{
    public static string Current => Asura.Core.PlatformTerms.SecretStore;
}
