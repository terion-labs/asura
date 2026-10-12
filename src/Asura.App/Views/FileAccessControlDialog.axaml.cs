using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.Application;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views;

/// <summary>
/// Who can read or change one item. Closes with the change to send, or with
/// nothing — closing without changing anything is not a write.
/// </summary>
public sealed partial class FileAccessControlDialog : DialogWindow
{
    public FileAccessControlDialog()
    {
        InitializeComponent();
    }

    public FileAccessControlDialog(FileAccessControlEditorViewModel editor)
        : this()
    {
        ArgumentNullException.ThrowIfNull(editor);
        DataContext = editor;
        // The header names what is being changed; the window is titled with
        // the same words.
        this.FindControl<DialogShell>("Shell")!.Title = $"Permissions for “{editor.ItemName}”";
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close(null);
    }

    private void OnApplyClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close((DataContext as FileAccessControlEditorViewModel)?.BuildRequest());
    }
}
