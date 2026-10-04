using Asura.Application;

namespace Asura.App.ViewModels;

public sealed partial class MainWindowViewModel
{
    private readonly WorkspaceMemoryRegistry? _workspaceMemories;
    public WorkspaceMemoriesViewModel? WorkspaceMemories { get; private set; }
}
