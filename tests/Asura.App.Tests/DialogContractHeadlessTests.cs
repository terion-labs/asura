using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.App.Views.RuntimePanels;
using Asura.Application;
using Asura.Core;
using Asura.Git;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Asura.App.Tests;

/// <summary>
/// The dialog contract as people meet it: Escape and ⌘. press Cancel, a
/// search field takes the first Escape, a disabled Cancel refuses the key,
/// routine confirmations answer Return and destructive ones do not, and a
/// form keeps its values and says what is wrong under the field.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class DialogContractHeadlessTests
{
    [Fact]
    public async Task Escape_presses_the_dialogs_cancel_button()
    {
        await RunAsync(async () =>
        {
            var dialog = new GitStashPushDialog();
            var closed = false;
            dialog.Closed += (_, _) => closed = true;
            dialog.Show();
            await Idle();

            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await Idle();

            Assert.True(closed);
        });
    }

    [Fact]
    public async Task Remove_last_returns_focus_to_the_shortcut_capture_area()
    {
        await RunAsync(async () =>
        {
            Assert.True(ShortcutText.TryParseStroke("Ctrl+B", out var first));
            Assert.True(ShortcutText.TryParseStroke("X", out var second));
            var dialog = new ShortcutRecorderDialog(new KeySequence([first, second]));
            dialog.Show();
            await Idle();
            var remove = dialog.FindControl<Button>("RemoveLastButton")!;
            remove.Focus();

            remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Idle();

            // The status line asks for another key press, so the capture area must take it.
            Assert.True(dialog.FindControl<SurfaceCard>("CaptureArea")!.IsKeyboardFocusWithin);
            Assert.Equal("1 of 8 strokes", dialog.FindControl<TextBlock>("StrokeCountText")!.Text);
            dialog.Close();
        });
    }

    [Fact]
    public async Task Return_saves_the_label_until_a_replacement_value_is_typed()
    {
        await RunAsync(async () =>
        {
            var editor = new SecretEditorViewModel(new SecretMetadataViewModel(
                new SecretRef("secret-test"),
                "Password",
                SecretKind.Password.ToString(),
                "Connection · connection-test",
                "Today",
                "Never",
                new SecretScope(SecretScopeKind.Connection, "connection-test"),
                "Used by: Production",
                1));
            var dialog = new SecretEditorDialog(editor);
            dialog.Show();
            await Idle();
            var saveLabel = dialog.FindControl<Button>("SaveLabelButton")!;
            var rotate = dialog.FindControl<Button>("RotateValueButton")!;

            Assert.True(saveLabel.IsDefault);
            Assert.False(rotate.IsDefault);
            Assert.False(rotate.IsEnabled);

            editor.ReplacementValue = "new value";
            await Idle();

            Assert.False(saveLabel.IsDefault);
            Assert.True(rotate.IsDefault);
            Assert.True(rotate.IsEnabled);
            dialog.Close();
        });
    }

    [Fact]
    public async Task Command_period_cancels_on_macos()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        await RunAsync(async () =>
        {
            var dialog = new GitStashPushDialog();
            var closed = false;
            dialog.Closed += (_, _) => closed = true;
            dialog.Show();
            await Idle();

            dialog.KeyPress(Key.OemPeriod, RawInputModifiers.Meta, PhysicalKey.Period, ".");
            await Idle();

            Assert.True(closed);
        });
    }

    [Fact]
    public async Task Command_w_closes_an_inspector_on_macos()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        await RunAsync(async () =>
        {
            var inspector = new SavedConnectionsDialog();
            var closed = false;
            inspector.Closed += (_, _) => closed = true;
            inspector.Show();
            await Idle();

            inspector.KeyPress(Key.W, RawInputModifiers.Meta, PhysicalKey.W, "w");
            await Idle();

            Assert.True(closed);
        });
    }

    [Fact]
    public async Task Command_w_leaves_a_sheet_to_its_buttons()
    {
        await RunAsync(async () =>
        {
            var sheet = new GitStashPushDialog();
            var closed = false;
            sheet.Closed += (_, _) => closed = true;
            sheet.Show();
            await Idle();

            sheet.KeyPress(Key.W, RawInputModifiers.Meta, PhysicalKey.W, "w");
            await Idle();

            Assert.False(closed);
            sheet.Close();
        });
    }

    [Fact]
    public async Task A_sheet_hangs_below_the_owners_drawn_title_band()
    {
        await RunAsync(async () =>
        {
            var band = new Border { Height = 72, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
            Avalonia.Controls.Chrome.WindowDecorationProperties.SetElementRole(band, WindowDecorationsElementRole.TitleBar);
            var owner = new Window { Width = 800, Height = 600, Content = new Panel { Children = { band } } };
            owner.Show();
            await Idle();

            Assert.True(DialogWindow.TitleBandHeight(owner) >= 72);

            band.IsVisible = false;
            await Idle();
            Assert.True(DialogWindow.TitleBandHeight(owner) < 72);
            owner.Close();
        });
    }

    [Fact]
    public async Task A_search_field_with_text_takes_the_first_escape()
    {
        await RunAsync(async () =>
        {
            var dialog = new AddWorkspaceTabDialog();
            var closed = false;
            dialog.Closed += (_, _) => closed = true;
            dialog.Show();
            await Idle();
            var search = dialog.GetVisualDescendants().OfType<SearchField>().Single();
            search.Text = "prod";
            search.Focus();
            await Idle();

            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await Idle();

            Assert.False(closed);
            Assert.True(string.IsNullOrEmpty(search.Text));

            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await Idle();

            Assert.True(closed);
        });
    }

    [Fact]
    public async Task A_disabled_cancel_refuses_escape_too()
    {
        await RunAsync(async () =>
        {
            var dialog = new GitStashPushDialog();
            var closed = false;
            dialog.Closed += (_, _) => closed = true;
            dialog.Show();
            await Idle();
            var cancel = dialog.GetVisualDescendants().OfType<Button>().Single(button => button.IsCancel);
            cancel.IsEnabled = false;

            dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
            await Idle();

            Assert.False(closed);
            dialog.Close();
        });
    }

    [Fact]
    public async Task Saved_connections_are_edited_from_the_selection_not_a_button_per_row()
    {
        await RunAsync(async () =>
        {
            using var launcher = new LauncherViewModel(() => []);
            launcher.Connections.Add(new LauncherConnectionViewModel(
                new ConnectionId("one"), 1, "Build server", "SSH", "ops@build", "Ready", true, []));
            launcher.Connections.Add(new LauncherConnectionViewModel(
                new ConnectionId("two"), 1, "Staging", "SSH", "ops@staging", "Ready", true, []));
            var inspector = new SavedConnectionsDialog { DataContext = launcher };
            LauncherConnectionViewModel? edited = null;
            inspector.EditRequested += (_, connection) => edited = connection;
            inspector.Show();
            await Idle();

            var buttons = inspector.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Content is "Edit…")
                .ToArray();
            var edit = Assert.Single(buttons);
            Assert.False(edit.IsEnabled);

            var list = inspector.FindControl<ListBox>("ConnectionList")!;
            list.SelectedIndex = 1;
            await Idle();
            Assert.True(edit.IsEnabled);

            Assert.True(list.ContainerFromIndex(1)!.Focus());
            await Idle();
            inspector.KeyPress(Key.Return, RawInputModifiers.None, PhysicalKey.Enter, "\r");
            await Idle();
            Assert.Equal("Staging", edited?.Name);

            edited = null;
            edit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("Staging", edited?.Name);

            // The count above the list is a real plural.
            static bool ShowsText(Window window, string text) =>
                window.GetVisualDescendants().OfType<TextBlock>().Any(block => block.Text == text);
            Assert.True(ShowsText(inspector, "2 saved connections"));
            inspector.Close();

            using var single = new LauncherViewModel(() => []);
            single.Connections.Add(new LauncherConnectionViewModel(
                new ConnectionId("one"), 1, "Build server", "SSH", "ops@build", "Ready", true, []));
            var singleInspector = new SavedConnectionsDialog { DataContext = single };
            singleInspector.Show();
            await Idle();
            Assert.True(ShowsText(singleInspector, "1 saved connection"));
            singleInspector.Close();
        });
    }

    [Fact]
    public async Task Sheets_cannot_be_minimized_and_inspectors_can()
    {
        await RunAsync(() =>
        {
            var sheet = new GitStashPushDialog();
            Assert.False(sheet.CanMinimize);
            Assert.False(sheet.CanMaximize);
            Assert.False(sheet.ShowInTaskbar);

            var inspector = new SavedConnectionsDialog();
            Assert.Equal(DialogPresentation.Inspector, inspector.Presentation);
            Assert.True(inspector.CanMinimize);
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData("Confirm", true, "PrimaryButton")]
    [InlineData("Acknowledge", true, "PrimaryButton")]
    [InlineData("Destructive", false, "DestructiveButton")]
    public async Task Confirmation_dresses_its_button_for_its_intent(
        string intentName,
        bool answersReturn,
        string role)
    {
        var intent = Enum.Parse<ConfirmationIntent>(intentName);
        await RunAsync(() =>
        {
            var dialog = new ConfirmationDialog(new ConfirmationDialogOptions
            {
                Heading = "Merge “feature” into “main”?",
                Detail = "The merge commits into the current branch.",
                ConfirmLabel = "Merge",
                Intent = intent,
            });
            var confirm = dialog.FindControl<Button>("ConfirmButton")!;

            Assert.Equal(answersReturn, confirm.IsDefault);
            Assert.Contains(role, confirm.Classes, StringComparer.Ordinal);
            // The question is the window's title; it is not repeated above itself.
            Assert.Equal("Merge “feature” into “main”?", dialog.Title);
            Assert.True(dialog.FindControl<Button>("CancelButton")!.IsCancel);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Confirmation_buttons_span_the_alert_and_stack_when_there_are_three()
    {
        await RunAsync(() =>
        {
            var pair = new ConfirmationDialog(new ConfirmationDialogOptions
            {
                Heading = "Merge?",
                Detail = "The merge commits into the current branch.",
                ConfirmLabel = "Merge",
                Intent = ConfirmationIntent.Confirm,
            });
            var pairConfirm = pair.FindControl<Button>("ConfirmButton")!;
            var pairCancel = pair.FindControl<Button>("CancelButton")!;
            // Side by side, the default on the trailing side.
            Assert.Equal(1, Grid.GetColumn(pairConfirm));
            Assert.Equal(0, Grid.GetColumn(pairCancel));
            Assert.Equal(Grid.GetRow(pairConfirm), Grid.GetRow(pairCancel));

            var save = new ConfirmationDialog(new ConfirmationDialogOptions
            {
                Heading = "Save changes?",
                Detail = "Your changes are lost if you don't save them.",
                ConfirmLabel = "Save",
                AlternateLabel = "Don't Save",
                Intent = ConfirmationIntent.Confirm,
            });
            // Stacked: the default on top, Cancel at the bottom.
            Assert.Equal(0, Grid.GetRow(save.FindControl<Button>("ConfirmButton")!));
            Assert.Equal(1, Grid.GetRow(save.FindControl<Button>("AlternateButton")!));
            Assert.Equal(2, Grid.GetRow(save.FindControl<Button>("CancelButton")!));
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_changed_host_key_is_never_replaced_by_return(bool changed)
    {
        await RunAsync(async () =>
        {
            var presented = new SshHostKeyIdentity("ssh-ed25519", $"SHA256:{new string('A', 43)}");
            var review = new SshHostKeyReview(
                SshHostKeyReviewId.New(),
                new ConnectionId("host"),
                "host.example:22",
                changed ? SshHostKeyDisposition.Changed : SshHostKeyDisposition.Unknown,
                presented,
                changed ? new SshHostKeyIdentity("ssh-ed25519", $"SHA256:{new string('B', 43)}") : null,
                DateTimeOffset.UtcNow.AddMinutes(5));
            Assert.Equal(changed, review.RequiresExplicitReplacement);
            var dialog = new SshHostKeyReviewDialog(review);
            var closed = false;
            dialog.Closed += (_, _) => closed = true;
            dialog.Show();
            await Idle();
            var confirm = dialog.FindControl<Button>("ConfirmButton")!;

            Assert.Equal(changed ? "Replace Trusted Key" : "Trust Host Key", confirm.Content);
            Assert.Equal(!changed, confirm.IsDefault);
            Assert.Contains(changed ? "DestructiveButton" : "PrimaryButton", confirm.Classes, StringComparer.Ordinal);
            Assert.DoesNotContain(changed ? "PrimaryButton" : "DestructiveButton", confirm.Classes, StringComparer.Ordinal);

            dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            await Idle();

            // A first-seen key may be trusted from the keyboard; a changed one
            // has to be clicked.
            Assert.Equal(!changed, closed);
            if (!closed)
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public async Task Routine_git_confirmations_are_not_dressed_as_deletions()
    {
        await RunAsync(() =>
        {
            Assert.Equal(ConfirmationIntent.Confirm, Confirmations.GitMergeBranch("feature", "main").Intent);
            Assert.Equal(ConfirmationIntent.Confirm, Confirmations.GitPushBranch("main", "origin").Intent);
            Assert.Equal(ConfirmationIntent.Confirm, Confirmations.GitTrustRepository("/srv/repo").Intent);
            Assert.Equal(ConfirmationIntent.Destructive, Confirmations.GitDeleteBranch("old").Intent);
            Assert.Equal(ConfirmationIntent.Destructive, Confirmations.GitDropStash("wip").Intent);
            var error = Confirmations.OperationError("The session did not stop.");
            Assert.Equal(ConfirmationIntent.Acknowledge, error.Intent);
            Assert.Equal("OK", error.ConfirmLabel);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Git_form_says_its_verb_and_draws_options_as_checkboxes()
    {
        await RunAsync(async () =>
        {
            var dialog = new GitWorkflowDialog(new GitWorkflowForm(
                "Push",
                "Review the destination.",
                new GitWorkflowAction("Push"),
                [new("destination", "Destination branch", Required: false), GitWorkflowField.CheckBox("force", "Force with lease", false)])
            {
                ActionFor = values => GitWorkflowDialog.IsChecked(values, "force")
                    ? new GitWorkflowAction("Force Push…", IsDestructive: true)
                    : new GitWorkflowAction("Push"),
            });
            dialog.Show();
            await Idle();
            var apply = dialog.FindControl<Button>("ApplyButton")!;
            var force = dialog.GetVisualDescendants().OfType<CheckBox>().Single();

            Assert.Equal("Push", apply.Content);
            Assert.True(apply.IsDefault);
            Assert.DoesNotContain(dialog.GetVisualDescendants(), visual => visual is ComboBox);

            force.IsChecked = true;
            await Idle();

            Assert.Equal("Force Push…", apply.Content);
            Assert.False(apply.IsDefault);
            Assert.Contains("DestructiveButton", apply.Classes, StringComparer.Ordinal);
            dialog.Close();
        });
    }

    [Fact]
    public async Task Git_form_keeps_its_values_and_shows_the_error_under_the_field()
    {
        await RunAsync(async () =>
        {
            var dialog = new GitWorkflowDialog(new GitWorkflowForm(
                "Line History",
                "Trace these lines.",
                new GitWorkflowAction("Show History"),
                [new("start", "First line", "abc")])
            {
                Validate = values => int.TryParse(values["start"], System.Globalization.CultureInfo.InvariantCulture, out _)
                    ? null
                    : new GitWorkflowFieldError("start", "Enter a line number of 1 or more."),
            });
            var closed = false;
            dialog.Closed += (_, _) => closed = true;
            dialog.Show();
            await Idle();

            dialog.FindControl<Button>("ApplyButton")!
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Idle();

            Assert.False(closed);
            var field = dialog.GetVisualDescendants().OfType<LabeledField>().Single();
            Assert.Equal("Enter a line number of 1 or more.", field.Error);
            Assert.Equal("abc", dialog.GetVisualDescendants().OfType<TextBox>().Single().Text);
            dialog.Close();
        });
    }

    [Fact]
    public async Task A_long_access_list_scrolls_inside_a_window_that_fits_the_screen()
    {
        await RunAsync(async () =>
        {
            // An object store allows a hundred grants; the window sizes to its
            // table but stops at its maximum, and the table scrolls instead.
            var grants = Enumerable.Range(0, 100)
                .Select(index => new FilePanelAccessGrant(
                    new FilePanelGrantee(FilePanelGranteeKind.User, $"account-{index}"),
                    FilePanelAccessRight.Read))
                .ToArray();
            var editor = new FileAccessControlEditorViewModel(
                "report.csv",
                "bucket",
                new FilePanelAccessControl(
                    new FilePanelLocation(
                        "files.remote",
                        "bucket",
                        new FilePanelAddress.Hierarchical(
                            FilePanelPath.Root.Append(new FilePanelPathSegment("report.csv")))),
                    grants: grants),
                canEdit: true);
            var dialog = new FileAccessControlDialog(editor);
            dialog.Show();
            await Idle();
            dialog.UpdateLayout();

            Assert.True(dialog.Bounds.Height <= dialog.MaxHeight, $"The window is {dialog.Bounds.Height} high.");
            var table = dialog.GetVisualDescendants()
                .OfType<ScrollViewer>()
                .Single(viewer => viewer.Content is ItemsControl);
            Assert.True(table.Extent.Height > table.Viewport.Height);
            dialog.Close();
        });
    }

    [Fact]
    public async Task Git_form_path_fields_choose_a_folder_and_remotes_are_a_pop_up()
    {
        await RunAsync(async () =>
        {
            string? startedFrom = null;
            string? urlWhenChoosing = null;
            var dialog = new GitWorkflowDialog(new GitWorkflowForm(
                "Clone Repository",
                "Connection: Local",
                new GitWorkflowAction("Clone"),
                [
                    new("path", "Destination path", "/Users/ada/src")
                    {
                        ChooseFolder = (_, current, values) =>
                        {
                            startedFrom = current;
                            urlWhenChoosing = values["source"];
                            return Task.FromResult<string?>("/Users/ada/clones");
                        },
                    },
                    new("source", "Repository URL", "https://example.com/ada/engine.git"),
                    GitWorkflowField.ChoiceOrText("remote", "Remote", ["origin", "upstream"], "origin"),
                ]));
            dialog.Show();
            await Idle();

            // The prefilled first field takes the keyboard with its text
            // selected, so typing replaces it.
            var path = dialog.GetVisualDescendants().OfType<TextBox>().First();
            Assert.True(path.IsFocused);
            Assert.Equal(0, path.SelectionStart);
            Assert.Equal("/Users/ada/src".Length, path.SelectionEnd);

            var choose = dialog.GetVisualDescendants().OfType<Button>().Single(button => Equals(button.Content, "Choose…"));
            choose.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Idle();
            Assert.Equal("/Users/ada/src", startedFrom);
            // The chooser sees the URL typed so far, to name the clone's folder.
            Assert.Equal("https://example.com/ada/engine.git", urlWhenChoosing);
            Assert.Equal("/Users/ada/clones", path.Text);

            var remote = dialog.GetVisualDescendants().OfType<ComboBox>().Single();
            Assert.Equal("origin", remote.SelectedItem);
            Assert.Equal(2, remote.ItemCount);
            dialog.Close();
        });
    }

    [Fact]
    public async Task Submodule_footer_keeps_its_primary_trailing_and_empty_wells_say_what_goes_in_them()
    {
        await RunAsync(async () =>
        {
            var dialog = new GitSubmoduleDialog { Width = 560 };
            dialog.Show();
            await Idle();

            var row = dialog.FindControl<Grid>("FooterRow")!;
            var open = dialog.FindControl<Button>("OpenButton")!;
            var actions = dialog.FindControl<Button>("ActionsButton")!;
            Assert.True(open.IsDefault);
            Assert.IsType<MenuFlyout>(actions.Flyout);
            Assert.DoesNotContain(dialog.GetVisualDescendants(), visual => visual is WrapPanel);
            // One row: Actions on the leading edge, the primary on the
            // trailing one, both on the same line.
            var openCenter = open.TranslatePoint(new Point(0, open.Bounds.Height / 2), row)!.Value;
            var actionsCenter = actions.TranslatePoint(new Point(0, actions.Bounds.Height / 2), row)!.Value;
            Assert.Equal(actionsCenter.Y, openCenter.Y, 0.5);
            Assert.True(actionsCenter.X < openCenter.X);
            Assert.True(row.Bounds.Height < open.Bounds.Height * 2, "The footer wrapped onto a second row.");
            AssertFooterSpansTheInsets(dialog, leading: actions, trailing: open);
            Assert.True(dialog.FindControl<TextBlock>("ChangesEmpty")!.IsVisible);
            Assert.True(dialog.FindControl<TextBlock>("PatchEmpty")!.IsVisible);
            dialog.Close();
        });
    }

    [Fact]
    public async Task Conflict_footer_keeps_delete_on_the_leading_inset_and_the_default_on_the_trailing_one()
    {
        await RunAsync(async () =>
        {
            // Wide enough for the headless font's wider glyphs to fit the
            // footer's five buttons on one row.
            var dialog = new GitConflictDialog { Width = 1400 };
            dialog.Show();
            await Idle();

            var delete = dialog.FindControl<Button>("DeleteButton")!;
            var save = dialog.FindControl<Button>("SaveButton")!;
            Assert.True(save.IsDefault);
            Assert.False(delete.IsDefault);
            AssertFooterSpansTheInsets(dialog, leading: delete, trailing: save);
            dialog.Close();
        });
    }

    [Fact]
    public async Task Conflict_footer_moves_the_resolutions_to_a_second_line_when_the_window_is_narrow()
    {
        await RunAsync(async () =>
        {
            var dialog = new GitConflictDialog();
            dialog.Width = dialog.MinWidth;
            dialog.Show();
            await Idle();
            await Idle();

            var row = dialog.FindControl<Grid>("FooterRow")!;
            var delete = dialog.FindControl<Button>("DeleteButton")!;
            var resolutions = dialog.FindControl<StackPanel>("ResolutionButtons")!;
            var deleteBounds = new Rect(delete.TranslatePoint(default, row)!.Value, delete.Bounds.Size);
            var resolutionBounds = new Rect(resolutions.TranslatePoint(default, row)!.Value, resolutions.Bounds.Size);
            Assert.False(deleteBounds.Intersects(resolutionBounds), "Delete File… overlaps the resolution buttons.");
            Assert.True(resolutionBounds.Right <= row.Bounds.Width + 0.5, "The resolution buttons run past the footer.");
            Assert.Equal(0, deleteBounds.X, 0.5);

            // Wide again, both share one line.
            dialog.Width = 1400;
            await Idle();
            await Idle();
            var deleteCenter = delete.TranslatePoint(new Point(0, delete.Bounds.Height / 2), row)!.Value;
            var saveButton = dialog.FindControl<Button>("SaveButton")!;
            var saveCenter = saveButton.TranslatePoint(new Point(0, saveButton.Bounds.Height / 2), row)!.Value;
            Assert.Equal(deleteCenter.Y, saveCenter.Y, 0.5);
            dialog.Close();
        });
    }

    [Fact]
    public async Task Hosting_starred_only_reloads_the_starred_list_when_ticked()
    {
        await RunAsync(async () =>
        {
            var repository = new GitHostedRepository("ada/engine", "https://example.com/ada/engine.git",
                "git@example.com:ada/engine.git", "https://example.com/ada/engine", IsPrivate: false);
            var client = new FakeGitRepositoryClient { HostedRepositories = [repository] };
            // The dialog disposes its browser when it closes.
            var browser = new GitHostingBrowserViewModel(client, BuiltInConnections.Local)
            {
                SelectedAccount = new GitHostingAccount(
                    GitHostingProvider.GitHub, new Uri("https://api.github.com/"), "ada", new SecretRef("hosting-ada")),
            };
            var dialog = new GitHostingDialog(browser);
            dialog.Show();
            await Idle();

            dialog.FindControl<CheckBox>("StarredCheckBox")!.IsChecked = true;
            await Idle();
            await Idle();

            Assert.Equal([true], client.HostedRepositoryReadsStarred);
            Assert.True(browser.Starred);
            Assert.False(browser.IsBusy);
            Assert.Equal([repository], browser.Repositories);
            Assert.False(browser.IsListEmpty);
            dialog.Close();
        });
    }

    [Fact]
    public async Task Choosing_a_clone_folder_over_SSH_says_a_folder_named_after_the_repository_is_made()
    {
        await RunAsync(async () =>
        {
            var picker = new GitRepositoryPickerViewModel(new FakeGitRepositoryClient(), BuiltInConnections.Local, "/home/qa");
            var dialog = new GitRepositoryPickerDialog(picker, "Choose a Folder for the Clone", "Choose", "engine");
            dialog.Show();
            await Idle();

            Assert.EndsWith("A new folder named “engine” is made inside the folder you choose.", picker.Subtitle, StringComparison.Ordinal);
            Assert.Equal("/srv/empty/engine", GitRuntimePanelView.RemoteChosenDestination("/srv/empty/", "engine"));
            Assert.Equal("/srv/empty", GitRuntimePanelView.RemoteChosenDestination("/srv/empty", null));
            dialog.Close();
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Repository_picker_chooses_a_repository_on_double_click_only_when_opening_one(bool choosingDestination)
    {
        await RunAsync(async () =>
        {
            var client = new FakeGitRepositoryClient();
            var picker = new GitRepositoryPickerViewModel(client, BuiltInConnections.Local, "/home/qa");
            var dialog = choosingDestination
                ? new GitRepositoryPickerDialog(picker, "Choose a Folder for the Clone", "Choose")
                : new GitRepositoryPickerDialog(picker);
            var closed = false;
            dialog.Closed += (_, _) => closed = true;
            dialog.Show();
            await Idle();

            var folders = dialog.GetVisualDescendants().OfType<ListBox>().Single();
            picker.SelectedDirectory = picker.Directories.Single(entry => entry.IsRepository);
            folders.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent, null!));
            await Idle();

            if (choosingDestination)
            {
                // Choosing where a clone goes: a repository is a folder to
                // step into, not an answer, and the subtitle is not about
                // repositories.
                Assert.False(closed);
                Assert.Equal("/home/qa/asura", client.ListedDirectories[^1]);
                Assert.DoesNotContain("repository", picker.Subtitle, StringComparison.OrdinalIgnoreCase);
                dialog.Close();
            }
            else
            {
                Assert.True(closed);
                Assert.Contains("repository", picker.Subtitle, StringComparison.OrdinalIgnoreCase);
            }
        });
    }

    /// <summary>
    /// A footer with a command on the leading edge starts where the content
    /// starts and ends at the same trailing inset as every other dialog's
    /// buttons.
    /// </summary>
    private static void AssertFooterSpansTheInsets(DialogWindow dialog, Button leading, Button trailing)
    {
        var shell = dialog.FindControl<DialogShell>("Shell")!;
        var inset = shell.TryFindResource("ShellSpaceLg", out var value) && value is double space ? space : 16;
        var leadingEdge = leading.TranslatePoint(default, shell)!.Value.X;
        var trailingEdge = trailing.TranslatePoint(new Point(trailing.Bounds.Width, 0), shell)!.Value.X;
        Assert.Equal(inset, leadingEdge, 0.5);
        Assert.Equal(shell.Bounds.Width - inset, trailingEdge, 0.5);
    }

    private static async Task RunAsync(Func<Task> test)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var session = HeadlessUnitTestSession.StartNew(typeof(SqlEditorHeadlessApplication));
        await session.Dispatch(async () =>
        {
            await test();
            return true;
        }, timeout.Token);
    }

    private static Task Idle() =>
        Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background).GetTask();
}
