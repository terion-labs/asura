using System.Globalization;
using Asura.App.ViewModels;
using Asura.App.Views.RuntimePanels;
using Asura.Application;
using Asura.Core;
using Avalonia.Input;
using FluentIcons.Common;
using KeyModifiers = Avalonia.Input.KeyModifiers;
using StubFilePanelClient = Asura.App.Tests.FileRuntimePanelViewModelTests.StubFilePanelClient;

namespace Asura.App.Tests;

/// <summary>
/// The Files panel against the Mac's conventions: real plurals, dates in the
/// reader's culture, Finder keys, deletes that go to the Trash and come back
/// with Undo, menus in one casing with what removes data last, a status mark
/// that follows the connection, and icons by kind.
/// </summary>
public sealed class FilePanelHigConformanceTests
{
    [Theory]
    [InlineData(0, "0 items")]
    [InlineData(1, "1 item")]
    [InlineData(5, "5 items")]
    public void Counts_use_real_plurals(int count, string expected)
    {
        using var culture = new CultureScope("en-US");

        Assert.Equal(expected, FileRuntimePanelViewModel.CountOf(count, "item"));
    }

    [Fact]
    public async Task The_listing_count_never_says_item_s()
    {
        using var culture = new CultureScope("en-US");
        var client = new StubFilePanelClient();
        client.Entries.Add(Entry(client, "alpha"));
        using var panel = new FileRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Files",
            client,
            deferInitialization: true);

        await panel.StartInitialization();

        Assert.Equal("1 item", panel.Status);
        Assert.DoesNotContain("(s)", panel.Status, StringComparison.Ordinal);
        panel.Filter = "zzz";
        Assert.Equal("0 of 1 item", panel.Status);
    }

    [Fact]
    public void Modified_dates_are_written_in_the_readers_culture()
    {
        using var culture = new CultureScope("de-DE");
        var client = new StubFilePanelClient();
        var moment = new DateTimeOffset(2026, 8, 20, 21, 15, 0, TimeSpan.Zero);
        var entry = new FileEntryViewModel(Entry(client, "notes.md", modified: moment));

        Assert.Equal(
            moment.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo("de-DE")),
            entry.Modified);
        Assert.Equal("Unknown", new FileEntryViewModel(Entry(client, "x", modified: null)).Modified);
    }

    [Fact]
    public void Display_dates_follow_the_current_culture()
    {
        var moment = new DateTimeOffset(2026, 8, 2, 21, 14, 9, TimeSpan.Zero);
        using (new CultureScope("en-US"))
        {
            Assert.Equal(moment.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo("en-US")), DisplayDates.Format(moment));
            Assert.Equal(moment.ToLocalTime().ToString("T", CultureInfo.GetCultureInfo("en-US")), DisplayDates.FormatTime(moment));
        }

        using (new CultureScope("fr-FR"))
        {
            Assert.Equal(moment.ToLocalTime().ToString("g", CultureInfo.GetCultureInfo("fr-FR")), DisplayDates.Format(moment));
        }
    }

    [Theory]
    [InlineData(Key.C, KeyModifiers.Meta, FileListShortcut.Copy)]
    [InlineData(Key.X, KeyModifiers.Meta, FileListShortcut.Cut)]
    [InlineData(Key.V, KeyModifiers.Meta, FileListShortcut.Paste)]
    [InlineData(Key.C, KeyModifiers.Control, FileListShortcut.None)]
    [InlineData(Key.V, KeyModifiers.Control, FileListShortcut.None)]
    [InlineData(Key.Enter, KeyModifiers.None, FileListShortcut.Open)]
    [InlineData(Key.Down, KeyModifiers.Meta, FileListShortcut.Open)]
    [InlineData(Key.Up, KeyModifiers.Meta, FileListShortcut.OpenEnclosingFolder)]
    [InlineData(Key.Back, KeyModifiers.Meta, FileListShortcut.Delete)]
    [InlineData(Key.Back, KeyModifiers.None, FileListShortcut.None)]
    [InlineData(Key.N, KeyModifiers.Meta | KeyModifiers.Shift, FileListShortcut.NewFolder)]
    [InlineData(Key.N, KeyModifiers.Meta, FileListShortcut.None)]
    public void Mac_listings_answer_the_Finder_keys_and_never_take_Control_for_Command(
        Key key,
        KeyModifiers modifiers,
        FileListShortcut expected)
    {
        Assert.Equal(expected, FileListShortcuts.Resolve(key, modifiers, isMacOS: true));
    }

    [Theory]
    [InlineData(Key.C, KeyModifiers.Control, FileListShortcut.Copy)]
    [InlineData(Key.C, KeyModifiers.Meta, FileListShortcut.None)]
    [InlineData(Key.Delete, KeyModifiers.None, FileListShortcut.Delete)]
    [InlineData(Key.Up, KeyModifiers.Alt, FileListShortcut.OpenEnclosingFolder)]
    [InlineData(Key.N, KeyModifiers.Control | KeyModifiers.Shift, FileListShortcut.NewFolder)]
    public void Other_platforms_keep_their_control_keys(
        Key key,
        KeyModifiers modifiers,
        FileListShortcut expected)
    {
        Assert.Equal(expected, FileListShortcuts.Resolve(key, modifiers, isMacOS: false));
    }

    [Fact]
    public async Task Menus_are_title_case_and_put_what_removes_data_last_behind_a_rule()
    {
        using var panel = new FileRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Files",
            new StubFilePanelClient(),
            deferInitialization: true);

        await panel.StartInitialization();

        foreach (var menu in new[] { panel.MenuActions, panel.EntryMenuActions })
        {
            var delete = menu[^1];
            Assert.Equal(FilePanelAction.Delete, delete.Action);
            Assert.Equal(FileMenuBand.Remove, delete.Band);
            Assert.True(delete.StartsGroup);
            Assert.Equal("Delete…", delete.Label);
            Assert.True(delete.IsDestructive);
            Assert.True(menu.Select(action => action.Band).Distinct().Count() <= 4);
        }

        Assert.Contains(panel.MenuActions, action => string.Equals(action.Label, "New Folder…", StringComparison.Ordinal));
        Assert.Contains(panel.MenuActions, action => string.Equals(action.Label, "Copy Name", StringComparison.Ordinal));
        foreach (var action in Enum.GetValues<FilePanelAction>())
        {
            var label = FilePanelActionPresentation.Label(action);
            Assert.All(
                label.TrimEnd('…').Split(' ').Where(word => word is not ("to" or "With")),
                word => Assert.True(char.IsUpper(word[0]), $"“{label}” is not title case."));
        }
    }

    [Fact]
    public void Menu_gestures_are_the_platform_keys()
    {
        var modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

        Assert.Equal(new KeyGesture(Key.C, modifier), FilePanelActionPresentation.Gesture(FilePanelAction.Copy));
        Assert.Equal(new KeyGesture(Key.N, modifier | KeyModifiers.Shift), FilePanelActionPresentation.Gesture(FilePanelAction.NewFolder));
        Assert.Equal(new KeyGesture(Key.Down, modifier), FilePanelActionPresentation.Gesture(FilePanelAction.Open));
        Assert.Equal(
            OperatingSystem.IsMacOS() ? new KeyGesture(Key.Back, KeyModifiers.Meta) : new KeyGesture(Key.Delete),
            FilePanelActionPresentation.Gesture(FilePanelAction.Delete));
    }

    [Fact]
    public async Task A_local_delete_moves_to_the_Trash_without_asking_and_Undo_puts_it_back()
    {
        var client = new StubFilePanelClient();
        client.Entries.Add(Entry(client, "alpha"));
        client.Entries.Add(Entry(client, "beta"));
        var trash = new RecordingTrash();
        using var panel = new FileRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Files",
            client,
            deferInitialization: true,
            trash: trash);
        var asked = new List<FilePanelAction>();
        panel.ActionRequested += (_, action) => asked.Add(action);

        await panel.StartInitialization();
        var alpha = panel.Entries.Single(entry => string.Equals(entry.Name, "alpha", StringComparison.Ordinal));
        panel.SelectedEntry = alpha;
        panel.SetSelectedEntries([alpha.Entry]);

        Assert.True(panel.CanMoveToTrash);
        var delete = panel.EntryMenuActions.Single(action => action.Action == FilePanelAction.Delete);
        Assert.Equal("Move to Trash", delete.Label);
        Assert.False(delete.IsDestructive);

        Assert.True(await panel.MoveSelectionToTrashAsync());

        Assert.Empty(asked);
        var trashed = Assert.Single(trash.Moved);
        Assert.Equal(panel.TryGetLocalPath(alpha.Entry), trashed);
        Assert.Null(client.LastDeleteRequest);
        Assert.True(panel.CanUndoMoveToTrash);

        Assert.True(await panel.UndoMoveToTrashAsync());

        Assert.Equal([trashed], trash.PutBack);
        Assert.False(panel.CanUndoMoveToTrash);
    }

    [Fact]
    public async Task The_Delete_action_goes_to_the_Trash_from_every_place_it_is_shown()
    {
        var client = new StubFilePanelClient();
        client.Entries.Add(Entry(client, "alpha"));
        var trash = new RecordingTrash();
        using var panel = new FileRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Files",
            client,
            deferInitialization: true,
            trash: trash);
        var asked = new List<FilePanelAction>();
        panel.ActionRequested += (_, action) => asked.Add(action);

        await panel.StartInitialization();
        panel.SelectedEntry = panel.Entries.Single();
        panel.SetSelectedEntries([panel.SelectedEntry.Entry]);

        Assert.True(panel.RequestActionIfEnabled(FilePanelAction.Delete));
        await trash.Wait;

        Assert.Empty(asked);
        Assert.Single(trash.Moved);
    }

    [Fact]
    public async Task Without_a_Trash_a_delete_is_still_confirmed_by_the_window()
    {
        var client = new StubFilePanelClient();
        client.Entries.Add(Entry(client, "alpha"));
        using var panel = new FileRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Files",
            client,
            deferInitialization: true);
        var asked = new List<FilePanelAction>();
        panel.ActionRequested += (_, action) => asked.Add(action);

        await panel.StartInitialization();
        panel.SelectedEntry = panel.Entries.Single();

        Assert.False(panel.CanMoveToTrash);
        Assert.True(panel.RequestActionIfEnabled(FilePanelAction.Delete));
        Assert.Equal([FilePanelAction.Delete], asked);
    }

    [Fact]
    public async Task A_refused_move_to_the_Trash_is_reported_and_leaves_nothing_to_undo()
    {
        var client = new StubFilePanelClient();
        client.Entries.Add(Entry(client, "alpha"));
        var trash = new RecordingTrash { Refuse = true };
        using var panel = new FileRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Files",
            client,
            deferInitialization: true,
            trash: trash);

        await panel.StartInitialization();
        panel.SelectedEntry = panel.Entries.Single();

        Assert.False(await panel.MoveSelectionToTrashAsync());
        Assert.NotNull(panel.OperationIssue);
        Assert.Contains("alpha", panel.OperationIssue!.Message, StringComparison.Ordinal);
        Assert.False(panel.CanUndoMoveToTrash);
    }

    [Fact]
    public async Task The_status_mark_follows_the_connection_and_names_its_state()
    {
        var client = new StubFilePanelClient();
        client.Entries.Add(Entry(client, "alpha"));
        using var panel = new FileRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Files",
            client,
            deferInitialization: true);

        Assert.NotEqual(StatusTone.Success, panel.StatusTone);

        await panel.StartInitialization();

        Assert.Equal(StatusTone.Success, panel.StatusTone);
        Assert.Equal("Connected to Local", panel.StatusDescription);

        client.ListError = new FilePanelError(
            FilePanelErrorCode.Offline,
            "file_offline",
            "Gone",
            Retryable: true);
        await panel.RefreshAsync();

        Assert.NotEqual(StatusTone.Success, panel.StatusTone);
        Assert.NotEqual(StatusTone.Neutral, panel.StatusTone);
    }

    [Fact]
    public async Task The_search_field_says_filter_or_search_by_what_it_does()
    {
        var client = new StubFilePanelClient();
        using var panel = new FileRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Files",
            client,
            deferInitialization: true);

        await panel.StartInitialization();

        Assert.Equal("Filter", panel.FilterPlaceholder);
        Assert.Equal("Filter this folder", panel.FilterAccessibleName);
    }

    [Theory]
    [InlineData("report.csv", Symbol.DocumentTable)]
    [InlineData("libghostty.dylib", Symbol.AppGeneric)]
    [InlineData("README.md", Symbol.DocumentText)]
    [InlineData("bundle.zip", Symbol.FolderZip)]
    [InlineData("config.json", Symbol.Code)]
    [InlineData("photo.heic", Symbol.Image)]
    [InlineData("mystery", Symbol.Document)]
    public void Files_get_a_glyph_by_kind_where_the_system_has_no_icon(string name, Symbol expected)
    {
        var client = new StubFilePanelClient();

        Assert.Equal(expected, FileTypeIcons.FallbackSymbol(Entry(client, name)));
        Assert.Equal(
            Symbol.Folder,
            FileTypeIcons.FallbackSymbol(Entry(client, "src", FilePanelEntryKind.Directory)));
    }

    [Fact]
    public void An_entry_shows_its_system_icon_or_one_glyph_never_both()
    {
        var client = new StubFilePanelClient();
        var withoutIcon = new FileEntryViewModel(Entry(client, "notes.md"));
        Assert.False(withoutIcon.HasIcon);
        Assert.True(withoutIcon.ShowsFileGlyph);
        Assert.False(withoutIcon.ShowsFolderGlyph);

        var calls = 0;
        var folder = new FileEntryViewModel(
            Entry(client, "src", FilePanelEntryKind.Directory),
            _ =>
            {
                calls++;
                return null;
            });
        Assert.True(folder.ShowsFolderGlyph);
        Assert.False(folder.ShowsFileGlyph);
        _ = folder.Icon;
        _ = folder.Icon;
        Assert.Equal(1, calls);
        Assert.Equal("src", folder.ToString());
    }

    [Fact]
    public void Local_entries_resolve_to_paths_on_this_machine_and_remote_ones_do_not()
    {
        var client = new StubFilePanelClient();
        using var panel = new FileRuntimePanelViewModel(
            PanelInstanceId.New(),
            "Files",
            client,
            deferInitialization: true);

        Assert.Null(panel.TryGetLocalPath(Entry(client, "alpha")));
    }

    [Fact]
    public void The_freedesktop_Trash_records_where_an_item_came_from_and_puts_it_back()
    {
        var root = Directory.CreateTempSubdirectory("asura-trash-");
        try
        {
            var trash = new FreedesktopFileTrash(
                Path.Combine(root.FullName, "Trash"),
                () => new DateTime(2026, 10, 7, 12, 30, 0, DateTimeKind.Local));
            var folder = Directory.CreateDirectory(Path.Combine(root.FullName, "my folder"));
            var file = Path.Combine(folder.FullName, "notes.txt");
            File.WriteAllText(file, "first");

            var first = trash.MoveToTrash(file);

            Assert.False(File.Exists(file));
            Assert.Equal(Path.Combine(root.FullName, "Trash", "files", "notes.txt"), first.TrashedPath);
            var info = File.ReadAllText(Path.Combine(root.FullName, "Trash", "info", "notes.txt.trashinfo"));
            Assert.StartsWith("[Trash Info]\n", info, StringComparison.Ordinal);
            Assert.Contains("Path=" + FreedesktopFileTrash.EscapePath(file), info, StringComparison.Ordinal);
            Assert.Contains("my%20folder", info, StringComparison.Ordinal);
            Assert.Contains("DeletionDate=2026-10-07T12:30:00", info, StringComparison.Ordinal);

            File.WriteAllText(file, "second");
            var second = trash.MoveToTrash(file);
            Assert.Equal(Path.Combine(root.FullName, "Trash", "files", "notes.2.txt"), second.TrashedPath);

            trash.PutBack(second);
            Assert.Equal("second", File.ReadAllText(file));
            Assert.False(File.Exists(Path.Combine(root.FullName, "Trash", "info", "notes.2.txt.trashinfo")));

            Assert.Throws<IOException>(() => trash.PutBack(first));
            Assert.True(File.Exists(first.TrashedPath!));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void The_freedesktop_Trash_only_renames_and_leaves_an_item_it_cannot_move()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Directory.CreateTempSubdirectory("asura-trash-");
        try
        {
            var file = Path.Combine(root.FullName, "notes.txt");
            File.WriteAllText(file, "stay");

            Assert.Throws<IOException>(() => FreedesktopFileTrash.Rename(
                file,
                Path.Combine(root.FullName, "missing", "notes.txt")));
            Assert.Equal("stay", File.ReadAllText(file));

            var moved = Path.Combine(root.FullName, "moved.txt");
            FreedesktopFileTrash.Rename(file, moved);
            Assert.False(File.Exists(file));
            Assert.Equal("stay", File.ReadAllText(moved));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void The_macOS_Trash_takes_a_file_and_gives_it_back()
    {
        if (!OperatingSystem.IsMacOS() || FileTrash.ForThisMachine is not { } trash)
        {
            return;
        }

        var root = Directory.CreateTempSubdirectory("asura-trash-");
        try
        {
            var file = Path.Combine(root.FullName, $"asura-trash-test-{Guid.NewGuid():N}.txt");
            File.WriteAllText(file, "trash me");

            TrashedItem item;
            try
            {
                item = trash.MoveToTrash(file);
            }
            catch (IOException)
            {
                // A sandboxed or headless runner may have no Trash to use.
                return;
            }

            Assert.False(File.Exists(file));
            Assert.NotNull(item.TrashedPath);
            trash.PutBack(item);
            Assert.Equal("trash me", File.ReadAllText(file));
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void On_macOS_files_get_the_icon_Finder_shows()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        var root = Directory.CreateTempSubdirectory("asura-icons-");
        try
        {
            var file = Path.Combine(root.FullName, "notes.txt");
            File.WriteAllText(file, "icon");
            var client = new StubFilePanelClient();

            byte[]?[] icons =
            [
                Views.MacOsFileTypeIcons.Png(Entry(client, "notes.txt"), file),
                Views.MacOsFileTypeIcons.Png(Entry(client, "asura-icons", FilePanelEntryKind.Directory), root.FullName),
                Views.MacOsFileTypeIcons.Png(Entry(client, "config.json"), localPath: null),
                Views.MacOsFileTypeIcons.Png(Entry(client, "src", FilePanelEntryKind.Directory), localPath: null),
            ];

            Assert.All(icons, png =>
            {
                Assert.NotNull(png);
                // A PNG's IHDR chunk: the width is the big-endian word at 16.
                Assert.Equal(0x89, png[0]);
                var width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
                Assert.True(width >= FileTypeIcons.PixelSize, $"The icon is only {width} pixels wide.");
            });
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    [Fact]
    public void A_file_icon_is_remembered_by_what_it_was_asked_for_with()
    {
        var client = new StubFilePanelClient();

        // Files are asked for by content type, local or not, so one
        // extensionless file cannot pick the icon for the next.
        Assert.Equal(
            Views.MacOsFileTypeIcons.CacheKey(Entry(client, "LICENSE"), "/tmp/LICENSE"),
            Views.MacOsFileTypeIcons.CacheKey(Entry(client, "Makefile"), "/usr/local/bin/Makefile"));
        Assert.Equal(
            Views.MacOsFileTypeIcons.CacheKey(Entry(client, "a.json"), "/tmp/a.json"),
            Views.MacOsFileTypeIcons.CacheKey(Entry(client, "b.json"), localPath: null));

        // A local folder may carry its own icon, so it is kept by path.
        Assert.NotEqual(
            Views.MacOsFileTypeIcons.CacheKey(Entry(client, "One", FilePanelEntryKind.Directory), "/tmp/One"),
            Views.MacOsFileTypeIcons.CacheKey(Entry(client, "Two", FilePanelEntryKind.Directory), "/tmp/Two"),
            StringComparer.Ordinal);
    }

    private static FilePanelEntry Entry(
        StubFilePanelClient client,
        string name,
        FilePanelEntryKind kind = FilePanelEntryKind.File) =>
        Entry(client, name, DateTimeOffset.UnixEpoch, kind);

    private static FilePanelEntry Entry(
        StubFilePanelClient client,
        string name,
        DateTimeOffset? modified,
        FilePanelEntryKind kind = FilePanelEntryKind.File) =>
        new(
            client.Root.Child(new FilePanelPathSegment(name)),
            name,
            kind,
            kind == FilePanelEntryKind.Directory ? null : 10,
            modified,
            IsHidden: false);

    private sealed class RecordingTrash : IFileTrash
    {
        private readonly TaskCompletionSource _moved = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Refuse { get; init; }

        public List<string> Moved { get; } = [];

        public List<string> PutBack { get; } = [];

        public Task Wait => _moved.Task.WaitAsync(TimeSpan.FromSeconds(5));

        public string Name => "Trash";

        public TrashedItem MoveToTrash(string path)
        {
            if (Refuse)
            {
                throw new IOException("Permission denied.");
            }

            lock (Moved)
            {
                Moved.Add(path);
            }

            _moved.TrySetResult();
            return new TrashedItem(path, "/Trash/" + Path.GetFileName(path));
        }

        void IFileTrash.PutBack(TrashedItem item)
        {
            lock (PutBack)
            {
                PutBack.Add(item.OriginalPath);
            }
        }
    }
}

/// <summary>Runs a test under one culture and restores the previous one.</summary>
internal sealed class CultureScope : IDisposable
{
    private readonly CultureInfo _culture = CultureInfo.CurrentCulture;
    private readonly CultureInfo _uiCulture = CultureInfo.CurrentUICulture;

    public CultureScope(string name)
    {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(name);
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
    }

    public void Dispose()
    {
        CultureInfo.CurrentCulture = _culture;
        CultureInfo.CurrentUICulture = _uiCulture;
    }
}
