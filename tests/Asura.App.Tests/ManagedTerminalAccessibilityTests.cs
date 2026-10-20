using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaKeyModifiers = Avalonia.Input.KeyModifiers;

namespace Asura.App.Tests;

/// <summary>
/// The terminal for people who don't use a pointer or can't see the grid: the
/// automation peer, output announcements, keyboard copy mode, the find bar,
/// the context menu, Finder drops, and focus navigation out of the terminal.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class ManagedTerminalAccessibilityTests
{
    private const int Columns = 20;

    private static AvaloniaKeyModifiers CommandModifier => OperatingSystem.IsMacOS()
        ? AvaloniaKeyModifiers.Meta
        : AvaloniaKeyModifiers.Control;

    [Fact]
    public Task Automation_peer_exposes_the_screen_as_a_read_only_document_named_for_the_panel() =>
        RunHeadlessAsync(() =>
        {
            var surface = CreateSurface(new RecordingSink());
            surface.AccessibleTitle = "Build";
            surface.UpdateRenderFrame(Frame(1, cursorRow: 1, "$ make", "ok"));

            var peer = Assert.IsType<ManagedTerminalSurfaceAutomationPeer>(
                ControlAutomationPeer.CreatePeerForElement(surface));
            var value = Assert.IsAssignableFrom<IValueProvider>(peer);

            Assert.Equal(AutomationControlType.Document, peer.GetAutomationControlType());
            Assert.Equal("Terminal, Build", peer.GetName());
            Assert.Equal(AutomationLiveSetting.Off, peer.GetLiveSetting());
            Assert.True(value.IsReadOnly);
            Assert.Equal("$ make\nok", value.Value);
            Assert.Throws<InvalidOperationException>(() => value.SetValue("rm -rf /"));

            surface.UpdateSnapshot(new TerminalScreenSnapshot(
                "$ make",
                0,
                0,
                2,
                Columns,
                false,
                null,
                DateTimeOffset.UtcNow,
                WindowTitle: "make — ~/src",
                ContentRevision: 1));
            Assert.Equal("Terminal, Build, make — ~/src", peer.GetName());
            return Task.CompletedTask;
        });

    // tmux in a managed SSH session sits on the alternate screen with its
    // mouse mode off; the wheel was dropped there. It now reaches the session,
    // which scrolls tmux's history when Asura's tmux announced its keys.
    [Fact]
    public Task The_wheel_over_a_full_screen_program_without_mouse_reaches_the_session() =>
        RunHeadlessAsync(() =>
        {
            Avalonia.Media.FontManager.Current.AddFontCollection(new AsuraTerminalFontCollection());
            var sink = new RecordingSink();
            var surface = CreateSurface(sink);
            using var window = ShowFocused(surface);
            surface.UpdateSnapshot(new TerminalScreenSnapshot(
                "tmux",
                0,
                0,
                4,
                Columns,
                IsAlternateScreen: true,
                null,
                DateTimeOffset.UtcNow,
                ContentRevision: 1));

            var top = (Window)TopLevel.GetTopLevel(surface)!;
            top.MouseMove(new Point(50, 50));
            top.MouseWheel(new Point(50, 50), new Vector(0, 1));

            var scroll = Assert.Single(sink.Scrolls);
            Assert.Equal(TerminalViewportScrollDirection.Up, scroll.Direction);
            return Task.CompletedTask;
        });

    [Fact]
    public Task New_output_is_announced_politely_in_one_throttled_message() =>
        RunHeadlessAsync(() =>
        {
            var surface = CreateSurface(new RecordingSink());
            using var window = ShowFocused(surface);
            var announcements = new List<string>();
            surface.AnnouncementRequested += (_, text) => announcements.Add(text);
            surface.UpdateRenderFrame(Frame(1, cursorRow: 0, "$ ls", "", "", ""));
            _ = ControlAutomationPeer.CreatePeerForElement(surface);

            surface.UpdateRenderFrame(Frame(2, cursorRow: 3, "$ ls", "a.txt", "b.txt", "$"));
            surface.UpdateRenderFrame(Frame(3, cursorRow: 3, "$ ls", "a.txt", "b.txt", "$ ec"));
            Assert.Empty(announcements);
            surface.FlushOutputAnnouncement();

            Assert.Equal(["a.txt\nb.txt\n$"], announcements);
            Assert.Contains("b.txt", surface.AccessibleText, StringComparison.Ordinal);

            // Nothing new: no repeat announcement.
            surface.FlushOutputAnnouncement();
            Assert.Single(announcements);
            return Task.CompletedTask;
        });

    [Fact]
    public void Long_output_is_summarized_by_size_and_its_last_lines()
    {
        var lines = Enumerable.Range(1, 40).Select(index => $"line {index}").ToArray();

        var text = ManagedTerminalSurface.ComposeOutputAnnouncement(lines[^12..], 40);

        Assert.StartsWith("40 new lines, ending with: line 29\n", text, StringComparison.Ordinal);
        Assert.EndsWith("line 40", text, StringComparison.Ordinal);
        Assert.Equal("one\ntwo", ManagedTerminalSurface.ComposeOutputAnnouncement(["one", "two"], 2));
    }

    [Fact]
    public Task Copy_mode_moves_a_line_cursor_selects_with_shift_and_copies() =>
        RunHeadlessAsync(async () =>
        {
            var sink = new RecordingSink { SelectableText = "beta\ngamma" };
            var clipboard = new RecordingClipboard();
            var surface = CreateSurface(sink);
            surface.Clipboard = clipboard;
            var announcements = new List<string>();
            var exits = 0;
            surface.AnnouncementRequested += (_, text) => announcements.Add(text);
            surface.CopyModeExitRequested += (_, _) => exits++;
            surface.UpdateRenderFrame(Frame(1, cursorRow: 0, "alpha", "beta", "gamma", "$"));

            surface.IsCopyMode = true;
            Assert.Equal("Copy mode. alpha", announcements[^1]);
            Assert.Contains("copy mode", AutomationProperties.GetItemStatus(surface), StringComparison.Ordinal);

            Assert.True(await surface.HandleCopyModeKeyAsync(Key.Down, AvaloniaKeyModifiers.None));
            Assert.Equal(1, surface.CopyCursorRow);
            Assert.Equal("beta", announcements[^1]);

            Assert.True(await surface.HandleCopyModeKeyAsync(Key.Down, AvaloniaKeyModifiers.Shift));
            Assert.Equal(2, surface.CopyCursorRow);
            Assert.Equal(1, surface.CopySelectionAnchorRow);
            Assert.Equal(
                [
                    new TerminalSelectionInput(TerminalSelectionPhase.Start, 0, 1),
                    new TerminalSelectionInput(TerminalSelectionPhase.End, Columns - 1, 2),
                ],
                sink.Selections);

            Assert.True(await surface.HandleCopyModeKeyAsync(Key.C, CommandModifier));
            Assert.Equal(["beta\ngamma"], clipboard.Writes);
            Assert.Equal("Copied", announcements[^1]);

            // Ordinary keys never reach the session; command shortcuts stay the app's.
            Assert.True(await surface.HandleCopyModeKeyAsync(Key.A, AvaloniaKeyModifiers.None));
            await surface.SubmitTextInputAsync("x");
            Assert.Empty(sink.Text);
            Assert.False(await surface.HandleCopyModeKeyAsync(Key.K, CommandModifier));

            // Moving without Shift drops the keyboard selection.
            Assert.True(await surface.HandleCopyModeKeyAsync(Key.Up, AvaloniaKeyModifiers.None));
            Assert.Null(surface.CopySelectionAnchorRow);
            Assert.Equal(TerminalSelectionPhase.Clear, sink.Selections[^1].Phase);

            // Copy from a menu with nothing selected copies the cursor's line.
            var copy = await surface.ExecuteCommandAsync(BuiltInCommands.Copy);
            Assert.Equal(TerminalCommandDispatchResult.Outcome.Executed, copy.Status);
            Assert.Equal(
                [
                    new TerminalSelectionInput(TerminalSelectionPhase.Start, 0, 1),
                    new TerminalSelectionInput(TerminalSelectionPhase.End, Columns - 1, 1),
                ],
                sink.Selections[^2..]);
            Assert.Equal(2, clipboard.Writes.Count);

            Assert.True(await surface.HandleCopyModeKeyAsync(Key.Escape, AvaloniaKeyModifiers.None));
            Assert.Equal(1, exits);
        });

    [Fact]
    public Task Copy_mode_scrolls_at_the_top_edge_and_keeps_the_selection_anchored() =>
        RunHeadlessAsync(async () =>
        {
            var sink = new RecordingSink();
            sink.ScreenSnapshots.Enqueue(ScreenSnapshot(scrollbackAbove: 10, "a", "b", "c", "d"));
            sink.ScreenSnapshots.Enqueue(ScreenSnapshot(scrollbackAbove: 9, "older", "a", "b", "c"));
            var surface = CreateSurface(sink);
            var announcements = new List<string>();
            surface.AnnouncementRequested += (_, text) => announcements.Add(text);
            surface.UpdateRenderFrame(Frame(1, cursorRow: 0, "a", "b", "c", "d"));
            surface.IsCopyMode = true;

            Assert.True(await surface.HandleCopyModeKeyAsync(Key.Up, AvaloniaKeyModifiers.Shift));

            Assert.Equal(new TerminalViewportScrollInput(-1), Assert.Single(sink.Scrolls));
            Assert.Equal(0, surface.CopyCursorRow);
            Assert.Equal(1, surface.CopySelectionAnchorRow);
            Assert.Equal(
                [
                    new TerminalSelectionInput(TerminalSelectionPhase.Start, Columns - 1, 1),
                    new TerminalSelectionInput(TerminalSelectionPhase.End, 0, 0),
                ],
                sink.Selections);
            Assert.Equal("older", announcements[^1]);
        });

    [Fact]
    public Task Only_the_terminal_with_keyboard_focus_announces_its_output() =>
        RunHeadlessAsync(() =>
        {
            var focused = CreateSurface(new RecordingSink());
            var background = CreateSurface(new RecordingSink());
            using var window = ShowFocused(focused, background);
            var announcements = new List<string>();
            focused.AnnouncementRequested += (_, text) => announcements.Add($"focused: {text}");
            background.AnnouncementRequested += (_, text) => announcements.Add($"background: {text}");
            foreach (var surface in new[] { focused, background })
            {
                surface.UpdateRenderFrame(Frame(1, cursorRow: 0, "$ make", "", ""));
                _ = ControlAutomationPeer.CreatePeerForElement(surface);
                surface.UpdateRenderFrame(Frame(2, cursorRow: 2, "$ make", "building", "$"));
                surface.FlushOutputAnnouncement();
            }

            Assert.Equal(["focused: building\n$"], announcements);
            // The background terminal's text stays current for when it is read.
            Assert.Contains("building", background.AccessibleText, StringComparison.Ordinal);
            return Task.CompletedTask;
        });

    [Fact]
    public Task Find_scrolling_a_match_into_view_is_not_announced_as_output() =>
        RunHeadlessAsync(async () =>
        {
            var sink = new RecordingSink();
            var surface = CreateSurface(sink);
            using var window = ShowFocused(surface);
            var announcements = new List<string>();
            surface.AnnouncementRequested += (_, text) => announcements.Add(text);
            var bottom = new[] { "one", "two", "three", "$" };
            var scrolledBack = new[] { "error here", "zero", "one", "two" };
            surface.UpdateRenderFrame(Frame(1, cursorRow: 3, bottom));
            _ = ControlAutomationPeer.CreatePeerForElement(surface);

            // The native search scrolls to an off-screen match while it runs; a
            // frame may arrive before the search returns or just after it.
            var revision = 1L;
            sink.Finding = input => surface.UpdateRenderFrame(
                Frame(++revision, cursorRow: 3, input.Query.Length == 0 ? bottom : scrolledBack));
            Assert.True(await surface.BeginFindAsync());
            _ = await surface.UpdateFindQueryAsync("error");
            surface.UpdateRenderFrame(Frame(++revision, cursorRow: 3, scrolledBack));
            Assert.True(await surface.FindNextAsync(1));
            await surface.CloseFindAsync();
            surface.UpdateRenderFrame(Frame(++revision, cursorRow: 3, bottom));
            surface.FlushOutputAnnouncement();

            Assert.Empty(announcements);
            Assert.Equal("one\ntwo\nthree\n$", surface.AccessibleText);

            // Real output afterwards is still announced.
            surface.UpdateRenderFrame(Frame(++revision, cursorRow: 3, "two", "three", "done", "$"));
            surface.FlushOutputAnnouncement();
            Assert.Equal(["done\n$"], announcements);
        });

    [Fact]
    public Task Typing_after_scrolling_back_returns_to_the_bottom_without_an_announcement() =>
        RunHeadlessAsync(async () =>
        {
            var sink = new RecordingSink();
            var surface = CreateSurface(sink);
            using var window = ShowFocused(surface);
            var announcements = new List<string>();
            surface.AnnouncementRequested += (_, text) => announcements.Add(text);
            surface.UpdateSnapshot(ScreenSnapshot(scrollbackAbove: 5, "one", "two", "three", "$"));
            surface.UpdateRenderFrame(Frame(1, cursorRow: 3, "one", "two", "three", "$"));
            _ = ControlAutomationPeer.CreatePeerForElement(surface);

            Assert.True(await surface.ScrollViewportAsync(-2));
            surface.UpdateRenderFrame(Frame(2, cursorRow: 3, "older", "old", "one", "two"));
            await surface.SubmitTextInputAsync("l");
            surface.UpdateRenderFrame(Frame(3, cursorRow: 3, "one", "two", "three", "$ l"));
            surface.FlushOutputAnnouncement();

            Assert.Equal(["l"], sink.Text);
            Assert.Empty(announcements);
            Assert.Equal("one\ntwo\nthree\n$ l", surface.AccessibleText);
        });

    [Fact]
    public async Task Copy_mode_copies_a_pointer_selection_as_it_is()
    {
        var sink = new RecordingSink { SelectableText = "ta\ngam" };
        var clipboard = new RecordingClipboard();
        var surface = CreateSurface(sink);
        surface.Clipboard = clipboard;
        surface.UpdateSnapshot(ScreenSnapshot(scrollbackAbove: 0, "alpha", "beta", "gamma", "$"));
        surface.UpdateRenderFrame(Frame(1, cursorRow: 3, "alpha", "beta", "gamma", "$"));
        surface.IsCopyMode = true;

        var start = surface.Metrics.CellBounds(1, 2).Center;
        var end = surface.Metrics.CellBounds(2, 2).Center;
        surface.BeginLocalSelectionGesture(start);
        Assert.True(await surface.UpdateLocalSelectionGestureAsync(end));
        Assert.True(await surface.CompleteLocalSelectionGestureAsync(end));
        var pointerSelections = sink.Selections.ToArray();

        Assert.True(await surface.HandleCopyModeKeyAsync(Key.C, CommandModifier));
        var copy = await surface.ExecuteCommandAsync(BuiltInCommands.Copy);

        Assert.Equal(TerminalCommandDispatchResult.Outcome.Executed, copy.Status);
        Assert.Equal(["ta\ngam", "ta\ngam"], clipboard.Writes);
        // The cursor's line never replaced the pointer selection.
        Assert.Equal(pointerSelections, sink.Selections);
        Assert.Null(surface.CopySelectionAnchorRow);
    }

    [Fact]
    public Task Copy_mode_refuses_menu_paste_like_typing_and_drops() =>
        RunHeadlessAsync(async () =>
        {
            var sink = new RecordingSink();
            var surface = CreateSurface(sink);
            surface.Clipboard = new RecordingClipboard();
            surface.UpdateRenderFrame(Frame(1, cursorRow: 0, "alpha"));
            Assert.True(Item(surface.CreateContextMenu(), "Paste").IsEnabled);

            surface.IsCopyMode = true;
            Assert.False(Item(surface.CreateContextMenu(), "Paste").IsEnabled);
            var paste = await surface.ExecuteCommandAsync(BuiltInCommands.Paste);

            Assert.Equal(TerminalCommandDispatchResult.Outcome.Unavailable, paste.Status);
            Assert.Empty(sink.Pastes);
            Assert.Empty(sink.Text);
        });

    [Fact]
    public void Control_F6_belongs_to_the_window_and_never_reaches_the_session()
    {
        Assert.True(ManagedTerminalInput.IsApplicationFocusNavigation(Key.F6, AvaloniaKeyModifiers.Control));
        Assert.True(ManagedTerminalInput.IsApplicationFocusNavigation(
            Key.F6,
            AvaloniaKeyModifiers.Control | AvaloniaKeyModifiers.Shift));
        Assert.False(ManagedTerminalInput.IsApplicationFocusNavigation(Key.F6, AvaloniaKeyModifiers.None));
        Assert.False(ManagedTerminalInput.IsApplicationFocusNavigation(Key.F5, AvaloniaKeyModifiers.Control));
    }

    [Fact]
    public Task Control_F6_key_events_are_left_unhandled_by_the_surface() =>
        RunHeadlessAsync(() =>
        {
            var sink = new RecordingSink();
            var surface = CreateSurface(sink);
            var focusNavigation = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.F6,
                PhysicalKey = PhysicalKey.F6,
                KeyModifiers = AvaloniaKeyModifiers.Control | AvaloniaKeyModifiers.Shift,
            };
            var plainF6 = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.F6,
                PhysicalKey = PhysicalKey.F6,
            };

            surface.RaiseEvent(focusNavigation);
            surface.RaiseEvent(plainF6);

            Assert.False(focusNavigation.Handled);
            Assert.True(plainF6.Handled);
            Assert.Single(sink.PhysicalKeys);
            return Task.CompletedTask;
        });

    [Fact]
    public Task Context_menu_offers_editing_find_split_and_clear_with_the_destructive_item_last() =>
        RunHeadlessAsync(async () =>
        {
            var sink = new RecordingSink();
            var surface = CreateSurface(sink);
            surface.UpdateRenderFrame(Frame(1, cursorRow: 0, "alpha"));

            Assert.Equal(
                ["Copy", "Paste", "Select All", "-", "Find…", "-", "Clear Scrollback"],
                Headers(surface.CreateContextMenu()));
            Assert.False(Item(surface.CreateContextMenu(), "Copy").IsEnabled);

            surface.CanSplit = true;
            surface.UpdateRenderFrame(Frame(2, cursorRow: 0, selectedRow: 0, "alpha"));
            var menu = surface.CreateContextMenu();
            Assert.Equal(
                ["Copy", "Paste", "Select All", "-", "Find…", "-", "Split Right", "Split Down", "-", "Clear Scrollback"],
                Headers(menu));
            Assert.True(Item(menu, "Copy").IsEnabled);

            var splits = new List<PanelSplitOrientation>();
            surface.SplitRequested += (_, orientation) => splits.Add(orientation);
            Item(menu, "Split Right").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Item(menu, "Split Down").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal([PanelSplitOrientation.LeftRight, PanelSplitOrientation.TopBottom], splits);

            Item(menu, "Clear Scrollback").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, sink.ClearScrollbackCount);

            var clear = await surface.ExecuteCommandAsync(BuiltInCommands.ClearScrollback);
            Assert.Equal(TerminalCommandDispatchResult.Outcome.Executed, clear.Status);
        });

    [Fact]
    public Task Right_click_belongs_to_a_program_that_reads_the_mouse_unless_Shift_is_held() =>
        RunHeadlessAsync(() =>
        {
            var surface = CreateSurface(new RecordingSink());
            surface.UpdateSnapshot(ScreenSnapshot(0, mouseTracking: false, "$"));
            Assert.True(surface.OpensContextMenu(TerminalMouseButton.Right, AvaloniaKeyModifiers.None));
            Assert.False(surface.OpensContextMenu(TerminalMouseButton.Left, AvaloniaKeyModifiers.None));
            Assert.Equal(
                OperatingSystem.IsMacOS(),
                surface.OpensContextMenu(TerminalMouseButton.Left, AvaloniaKeyModifiers.Control));

            surface.UpdateSnapshot(ScreenSnapshot(0, mouseTracking: true, "htop"));
            Assert.False(surface.OpensContextMenu(TerminalMouseButton.Right, AvaloniaKeyModifiers.None));
            Assert.True(surface.OpensContextMenu(TerminalMouseButton.Right, AvaloniaKeyModifiers.Shift));
            return Task.CompletedTask;
        });

    [Fact]
    public void Dropped_paths_are_shell_escaped_like_Terminal()
    {
        Assert.Equal(
            "/Users/me/My\\ File\\ \\(1\\).txt /tmp/it\\'s\\&more /tmp/café",
            ManagedTerminalInput.FormatDroppedPaths(
                ["/Users/me/My File (1).txt", "/tmp/it's&more", "/tmp/café"],
                windowsQuoting: false));
        Assert.Equal(
            "$'/tmp/a\\nb\\\\c'",
            ManagedTerminalInput.FormatDroppedPaths(["/tmp/a\nb\\c"], windowsQuoting: false));
        Assert.Equal(
            "\"C:\\My Files\\a.txt\" C:\\plain.txt",
            ManagedTerminalInput.FormatDroppedPaths(
                ["C:\\My Files\\a.txt", "C:\\plain.txt"],
                windowsQuoting: true));
    }

    [Fact]
    public async Task Dropped_files_are_pasted_at_the_prompt_and_never_run()
    {
        var sink = new RecordingSink();
        var surface = new ManagedTerminalSurface { InputSink = sink };

        _ = await surface.InsertDroppedPathsAsync(["/tmp/a b"]);

        var paste = Assert.Single(sink.Pastes);
        Assert.Equal(
            ManagedTerminalInput.FormatDroppedPaths(["/tmp/a b"]) + " ",
            paste.Text);
        Assert.DoesNotContain('\n', paste.Text);
        Assert.Empty(sink.Text);
    }

    [Fact]
    public Task Session_host_presents_a_real_find_bar_and_screen_reader_announcer() =>
        RunHeadlessAsync(async () =>
        {
            var host = new ManagedTerminalSessionHost();
            var sink = new RecordingSink();
            var window = new Window { Width = 800, Height = 400, Content = host };
            window.Show();
            window.UpdateLayout();

            // Without a session client the host waits; drive the surface directly.
            host.Surface.InputSink = sink;
            host.Surface.SetInputReady(true);
            var overlay = host.Overlay;
            var findBar = overlay.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Name == "FindBar");
            Assert.False(findBar.IsVisible);

            Assert.True(await host.Surface.BeginFindAsync());
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.True(findBar.IsVisible);

            // The bar keeps its own band above the rows, so it never covers
            // the first line of output.
            var band = findBar.Bounds.Height + findBar.Margin.Top + findBar.Margin.Bottom;
            Assert.True(band > 0);
            Assert.Equal(band, overlay.ReservedFindBandHeight);
            Assert.Equal(band, host.Surface.Margin.Top);
            Assert.True(host.Surface.Bounds.Top >= findBar.Bounds.Bottom);
            Assert.IsType<SearchField>(overlay.FindField);
            Assert.True(overlay.FindField.IsFocused);
            Assert.Equal("Find in terminal", AutomationProperties.GetName(overlay.FindField));
            Assert.Equal(TerminalFindInput.MaximumQueryLength, overlay.FindField.MaxLength);

            overlay.FindField.Text = "error";
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new TerminalFindInput("error", 0), sink.Finds[^1]);
            Assert.Equal("Match 1 of 3", host.Surface.FindStatusMessage);

            // The first Escape clears the query, as a search field does; the
            // second closes the bar.
            var escape = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape,
            };
            overlay.FindField.RaiseEvent(escape);
            Dispatcher.UIThread.RunJobs();
            Assert.True(escape.Handled);
            Assert.True(host.Surface.IsFindVisible);
            Assert.Equal(string.Empty, overlay.FindField.Text);

            escape = new KeyEventArgs
            {
                RoutedEvent = InputElement.KeyDownEvent,
                Key = Key.Escape,
            };
            overlay.FindField.RaiseEvent(escape);
            Dispatcher.UIThread.RunJobs();
            Assert.True(escape.Handled);
            Assert.False(host.Surface.IsFindVisible);
            window.UpdateLayout();
            Assert.Equal(0, overlay.ReservedFindBandHeight);
            Assert.Equal(0, host.Surface.Margin.Top);

            var announcer = overlay.GetVisualDescendants().OfType<LiveRegionTextBlock>()
                .Single(block => AutomationProperties.GetName(block) == "Terminal output");
            Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(announcer));
            host.Surface.UpdateRenderFrame(Frame(1, cursorRow: 0, "alpha"));
            host.Surface.IsCopyMode = true;
            Assert.Equal("Copy mode. alpha", overlay.AnnouncementText);
            window.UpdateLayout();
            Assert.True(overlay.GetVisualDescendants().OfType<Border>()
                .Single(border => border.Name == "CopyModeHint").IsVisible);
            window.Close();
        });

    [Fact]
    public void Find_and_copy_mode_hints_name_their_keys_with_the_shortcut_formatter()
    {
        var mac = HostPlatform.Current == HostOperatingSystem.MacOS;
        var escape = KeySequenceDisplay.Format(new KeyStroke("ESCAPE", Asura.Core.KeyModifiers.None));
        var enter = KeySequenceDisplay.Format(new KeyStroke("ENTER", Asura.Core.KeyModifiers.None));
        var up = KeySequenceDisplay.Format(new KeyStroke("ARROWUP", Asura.Core.KeyModifiers.None));
        var down = KeySequenceDisplay.Format(new KeyStroke("ARROWDOWN", Asura.Core.KeyModifiers.None));

        Assert.Equal(escape, ManagedTerminalOverlay.EscapeKeyText);
        Assert.Equal(enter, ManagedTerminalOverlay.ReturnKeyText);
        Assert.Contains(enter, ManagedTerminalOverlay.FindNextTip, StringComparison.Ordinal);
        Assert.Contains(escape, ManagedTerminalOverlay.CloseFindTip, StringComparison.Ordinal);
        Assert.StartsWith($"Copy mode: {up} {down} move", ManagedTerminalOverlay.CopyModeHintText, StringComparison.Ordinal);
        Assert.EndsWith($"{escape} exits", ManagedTerminalOverlay.CopyModeHintText, StringComparison.Ordinal);
        if (mac)
        {
            // Keycaps, not words, and Return in text presentation so it never
            // draws as a color emoji.
            Assert.Equal("⎋", escape);
            Assert.Equal("↩\uFE0E", enter);
            Assert.Contains("⌘G", ManagedTerminalOverlay.FindNextTip, StringComparison.Ordinal);
            Assert.DoesNotContain("Esc", ManagedTerminalOverlay.CopyModeHintText, StringComparison.Ordinal);
        }
    }

    private static ManagedTerminalSurface CreateSurface(RecordingSink sink)
    {
        var surface = new ManagedTerminalSurface
        {
            InputSink = sink,
            Profile = new TerminalRenderProfileSnapshot(
                13,
                TerminalCursorStyle.Block,
                cursorBlink: false,
                10_000,
                TerminalPalette.AsuraDark,
                lineHeight: 1,
                clipboardPolicy: new TerminalClipboardPolicy(
                    TerminalClipboardAccess.Ask,
                    TerminalClipboardAccess.Allow,
                    TerminalPasteSafetyPolicy.ProtectUnsafeIncludingBracketed)),
        };
        surface.Measure(new Size(400, 200));
        surface.Arrange(new Rect(0, 0, 400, 200));
        return surface;
    }

    /// <summary>Shows the surfaces in a window and gives the first keyboard focus.</summary>
    private static ClosingWindow ShowFocused(params ManagedTerminalSurface[] surfaces)
    {
        var panel = new StackPanel();
        foreach (var surface in surfaces)
        {
            surface.Height = 200;
            panel.Children.Add(surface);
        }

        var window = new Window { Width = 400, Height = 200 * surfaces.Length, Content = panel };
        window.Show();
        window.UpdateLayout();
        Assert.True(surfaces[0].Focus());
        return new ClosingWindow(window);
    }

    private static TerminalRenderFrame Frame(
        long revision,
        int cursorRow,
        params string[] lines) => Frame(revision, cursorRow, selectedRow: -1, lines);

    private static TerminalRenderFrame Frame(
        long revision,
        int cursorRow,
        int selectedRow,
        params string[] lines)
    {
        var rows = lines
            .Select((line, row) => new TerminalRenderRow(
                row,
                [.. Enumerable.Range(0, Columns).Select(column => new TerminalRenderCell(
                    column < line.Length ? line[column].ToString() : string.Empty,
                    TerminalRenderCellWidth.Narrow,
                    TerminalCellColor.Default,
                    TerminalCellColor.Default,
                    IsSelected: row == selectedRow))]))
            .ToArray();
        return new TerminalRenderFrame(
            revision,
            rows.Length,
            Columns,
            rows,
            new TerminalRenderCursor(
                TerminalCursorVisualStyle.Block,
                IsVisible: true,
                IsBlinking: false,
                IsPasswordInput: false,
                ViewportRow: cursorRow,
                ViewportColumn: 0),
            new TerminalRenderDelta(TerminalRenderDamageKind.Full));
    }

    private static TerminalScreenSnapshot ScreenSnapshot(
        int scrollbackAbove,
        params string[] lines) => ScreenSnapshot(scrollbackAbove, mouseTracking: false, lines);

    private static TerminalScreenSnapshot ScreenSnapshot(
        int scrollbackAbove,
        bool mouseTracking,
        params string[] lines) => new(
        string.Join('\n', lines),
        0,
        0,
        lines.Length,
        Columns,
        false,
        null,
        DateTimeOffset.UtcNow,
        IsMouseTrackingEnabled: mouseTracking,
        ContentRevision: scrollbackAbove + (mouseTracking ? 1_000 : 0),
        ScrollbackLinesAbove: scrollbackAbove);

    private static string[] Headers(ContextMenu menu) =>
        [.. menu.Items.Select(item => item is MenuItem { Header: string header } ? header : "-")];

    private static MenuItem Item(ContextMenu menu, string header) =>
        menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, header));

    private static async Task RunHeadlessAsync(Func<Task> assertion)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        try
        {
            var completed = await session.Dispatch(
                async () =>
                {
                    await assertion();
                    return true;
                },
                timeout.Token);
            Assert.True(completed);
        }
        finally
        {
            await session.DisposeAsync();
        }
    }

    private sealed class RecordingSink : IManagedTerminalInputSink
    {
        public List<string> Text { get; } = [];

        public List<TerminalPhysicalKeyEvent> PhysicalKeys { get; } = [];

        public List<TerminalViewportScrollInput> Scrolls { get; } = [];

        public List<TerminalSelectionInput> Selections { get; } = [];

        public List<TerminalPasteInput> Pastes { get; } = [];

        public List<TerminalFindInput> Finds { get; } = [];

        public Queue<TerminalScreenSnapshot> ScreenSnapshots { get; } = [];

        public int ClearScrollbackCount { get; private set; }

        /// <summary>The text any selection gesture selects; Clear drops the selection.</summary>
        public string SelectableText { get; init; } = string.Empty;

        public Action<TerminalFindInput>? Finding { get; set; }

        private TerminalSelectionText SelectionText { get; set; } = new(string.Empty, false, false);

        public ValueTask SendTextAsync(string text, CancellationToken cancellationToken)
        {
            Text.Add(text);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendKeyAsync(TerminalKeyStroke keyStroke, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask SendPhysicalKeyAsync(
            TerminalPhysicalKeyEvent keyEvent,
            CancellationToken cancellationToken)
        {
            PhysicalKeys.Add(keyEvent);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendMouseAsync(TerminalMouseInput mouseInput, CancellationToken cancellationToken) =>
            ValueTask.CompletedTask;

        public ValueTask ScrollViewportAsync(
            TerminalViewportScrollInput scrollInput,
            CancellationToken cancellationToken)
        {
            Scrolls.Add(scrollInput);
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> ClearScrollbackAsync(CancellationToken cancellationToken)
        {
            ClearScrollbackCount++;
            return ValueTask.FromResult(true);
        }

        public ValueTask<TerminalFindResult?> FindAsync(
            TerminalFindInput input,
            CancellationToken cancellationToken)
        {
            Finds.Add(input);
            Finding?.Invoke(input);
            return ValueTask.FromResult<TerminalFindResult?>(input.Query.Length == 0
                ? TerminalFindResult.Empty
                : new TerminalFindResult(3, (int)(((long)input.RequestedMatchIndex % 3 + 3) % 3), false));
        }

        public ValueTask UpdateSelectionAsync(
            TerminalSelectionInput selectionInput,
            CancellationToken cancellationToken)
        {
            Selections.Add(selectionInput);
            SelectionText = selectionInput.Phase == TerminalSelectionPhase.Clear
                ? new TerminalSelectionText(string.Empty, false, false)
                : new TerminalSelectionText(SelectableText, SelectableText.Length != 0, false);
            return ValueTask.CompletedTask;
        }

        public ValueTask<TerminalSelectionText> ReadSelectionAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult(SelectionText);

        public ValueTask<TerminalPasteResult> PasteAsync(
            TerminalPasteInput pasteInput,
            CancellationToken cancellationToken)
        {
            Pastes.Add(pasteInput);
            return ValueTask.FromResult(TerminalPasteResult.Completed(bracketed: true));
        }

        public ValueTask<TerminalScreenSnapshot?> ReadScreenSnapshotAsync(
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(ScreenSnapshots.TryDequeue(out var snapshot) ? snapshot : null);
    }

    private sealed class ClosingWindow(Window window) : IDisposable
    {
        public void Dispose() => window.Close();
    }

    private sealed class RecordingClipboard : IManagedTerminalClipboard
    {
        public List<string> Writes { get; } = [];

        public ValueTask<string?> TryGetTextAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<string?>(null);

        public ValueTask SetTextAsync(string text, CancellationToken cancellationToken)
        {
            Writes.Add(text);
            return ValueTask.CompletedTask;
        }
    }
}
