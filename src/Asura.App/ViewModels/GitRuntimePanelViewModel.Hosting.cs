namespace Asura.App.ViewModels;

public sealed partial class GitRuntimePanelViewModel
{
    public GitHostingBrowserViewModel CreateHostingBrowser() => new(_client, _connection);
}
