using Asura.App.Controls;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using FluentIcons.Common;

namespace Asura.App.Views;

/// <summary>One way to open a folder, as a row of <see cref="OpenFolderAsDialog"/>.</summary>
public sealed record OpenFolderViewRow(
    OpenFolderView View,
    string Title,
    string Detail,
    Symbol Symbol,
    bool IsAvailable);

/// <summary>
/// Asks what a folder handed to Asura opens as: a terminal there, the Files
/// panel showing it, or the Git panel for the repository it is in. Answers
/// with the view, or null when cancelled.
/// </summary>
public sealed partial class OpenFolderAsDialog : DialogWindow
{
    public OpenFolderAsDialog()
    {
        InitializeComponent();
    }

    public OpenFolderAsDialog(string folder, string? repositoryRoot)
        : this()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        var name = Path.GetFileName(folder);
        Shell.Subtitle = folder;
        Title = Shell.Title = string.IsNullOrEmpty(name) ? "Open Folder As" : $"Open “{name}” As";
        Rows =
        [
            new(OpenFolderView.Terminal, "Terminal", "A shell in this folder", Symbol.WindowConsole, IsAvailable: true),
            new(OpenFolderView.Files, "Files", "Browse this folder", Symbol.Folder, IsAvailable: true),
            repositoryRoot is null
                ? new(OpenFolderView.Git, "Git", "Not in a Git repository", Symbol.Branch, IsAvailable: false)
                : new(OpenFolderView.Git, "Git", $"The repository at {repositoryRoot}", Symbol.Branch, IsAvailable: true),
        ];
        ViewList.ItemsSource = Rows;
        ViewList.SelectionChanged += (_, _) =>
            OpenButton.IsEnabled = ViewList.SelectedItem is OpenFolderViewRow { IsAvailable: true };
        ViewList.SelectedIndex = 0;
        Opened += (_, _) => ViewList.ContainerFromIndex(0)?.Focus();
    }

    /// <summary>The choices, in order.</summary>
    internal IReadOnlyList<OpenFolderViewRow> Rows { get; } = [];

    private void OnOpenClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (ViewList.SelectedItem is OpenFolderViewRow { IsAvailable: true } row)
        {
            Close(row.View);
        }
    }

    private void OnViewDoubleTapped(object? sender, TappedEventArgs e)
    {
        _ = sender;
        _ = e;
        OnOpenClick(null, new RoutedEventArgs());
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close(null);
    }
}
