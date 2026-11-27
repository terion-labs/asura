using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Platform.Storage;

namespace Asura.App.Controls;

/// <summary>What a <see cref="PathField"/> names on this computer.</summary>
public enum PathFieldKind
{
    File,
    Folder,
}

/// <summary>
/// A path on this computer: the field to type it, and Choose… beside it to
/// pick it in the system's Open panel, which starts in the folder of the path
/// already entered. Every form that asks for a local file or folder wears it,
/// so none of them leaves the path to be typed from memory.
///
/// Only for paths this computer reads. A path on a remote host — an SSH
/// working directory, a repository on another machine — is not one the
/// system's panel can see.
/// </summary>
internal sealed class PathField : UserControl
{
    public static readonly StyledProperty<string?> PathProperty =
        AvaloniaProperty.Register<PathField, string?>(
            nameof(Path),
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<PathFieldKind> KindProperty =
        AvaloniaProperty.Register<PathField, PathFieldKind>(nameof(Kind));

    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        AvaloniaProperty.Register<PathField, string?>(nameof(PlaceholderText));

    /// <summary>The Open panel's title; "Choose File" or "Choose Folder" when unset.</summary>
    public static readonly StyledProperty<string?> PickerTitleProperty =
        AvaloniaProperty.Register<PathField, string?>(nameof(PickerTitle));

    private readonly TextBox _input;
    private readonly Button _choose;

    static PathField()
    {
        AutomationProperties.NameProperty.Changed.AddClassHandler<PathField>((field, _) => field.Name_Changed());
        KindProperty.Changed.AddClassHandler<PathField>((field, _) => field.Name_Changed());
    }

    public PathField()
    {
        _input = new TextBox();
        _input[!!TextBox.TextProperty] = this[!!PathProperty];
        _input[!TextBox.PlaceholderTextProperty] = this[!PlaceholderTextProperty];
        _choose = new Button
        {
            Content = "Choose…",
            VerticalAlignment = VerticalAlignment.Stretch,
            Classes = { "SecondaryButton" },
        };
        Grid.SetColumn(_choose, 1);
        _choose.Click += OnChooseClick;
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Children = { _input, _choose },
        };
        row.Bind(Grid.ColumnSpacingProperty, row.GetResourceObservable("ShellSpaceSm"));
        Content = row;
        Name_Changed();
    }

    public string? Path
    {
        get => GetValue(PathProperty);
        set => SetValue(PathProperty, value);
    }

    public PathFieldKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    public string? PickerTitle
    {
        get => GetValue(PickerTitleProperty);
        set => SetValue(PickerTitleProperty, value);
    }

    /// <summary>
    /// The folder the Open panel starts in: the entered folder itself, or the
    /// folder holding the entered file, with a leading ~ read as home. Null
    /// when nothing entered names a folder that exists.
    /// </summary>
    internal static string? StartFolder(string? entered, PathFieldKind kind)
    {
        if (string.IsNullOrWhiteSpace(entered))
        {
            return null;
        }

        var path = entered.Trim();
        if (string.Equals(path, "~", StringComparison.Ordinal) || path.StartsWith("~/", StringComparison.Ordinal))
        {
            path = System.IO.Path.Join(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                path.Length > 1 ? path[2..] : string.Empty);
        }

        if (!System.IO.Path.IsPathFullyQualified(path))
        {
            return null;
        }

        if (kind == PathFieldKind.Folder && Directory.Exists(path))
        {
            return path;
        }

        return System.IO.Path.GetDirectoryName(path) is { Length: > 0 } folder && Directory.Exists(folder)
            ? folder
            : null;
    }

    // The field and its button carry the field's name, so a screen reader
    // reads "Kubeconfig file" and "Choose Kubeconfig file" rather than two
    // unnamed controls.
    private void Name_Changed()
    {
        var name = AutomationProperties.GetName(this);
        AutomationProperties.SetName(_input, name);
        AutomationProperties.SetName(
            _choose,
            string.IsNullOrWhiteSpace(name)
                ? Kind == PathFieldKind.Folder ? "Choose a folder" : "Choose a file"
                : $"Choose {name}");
    }

    private async void OnChooseClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        if (TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
        {
            return;
        }

        var start = StartFolder(Path, Kind) is { } folder
            ? await storage.TryGetFolderFromPathAsync(folder)
            : null;
        string? chosen;
        if (Kind == PathFieldKind.Folder)
        {
            if (!storage.CanPickFolder)
            {
                return;
            }

            var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = PickerTitle ?? "Choose Folder",
                AllowMultiple = false,
                SuggestedStartLocation = start,
            });
            chosen = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        }
        else
        {
            if (!storage.CanOpen)
            {
                return;
            }

            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = PickerTitle ?? "Choose File",
                AllowMultiple = false,
                SuggestedStartLocation = start,
            });
            chosen = files.Count > 0 ? files[0].TryGetLocalPath() : null;
        }

        if (chosen is not null)
        {
            Path = chosen;
        }
    }
}
