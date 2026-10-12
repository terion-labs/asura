using Avalonia.Markup.Xaml;

namespace Asura.App.Views;

/// <summary>
/// The workspace editor's window: "Edit Workspace — Name", or "New
/// Workspace" for one not saved yet.
/// </summary>
public sealed partial class WorkspaceEditorWindow : EditorWindow
{
    public WorkspaceEditorWindow()
    {
        InitializeComponent();
    }

    internal static string TitleFor(string name, bool isNew) =>
        isNew
            ? "New Workspace"
            : string.IsNullOrWhiteSpace(name) ? "Edit Workspace" : $"Edit Workspace — {name.Trim()}";

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
