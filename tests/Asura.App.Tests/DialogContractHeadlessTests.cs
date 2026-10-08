using Asura.App.Controls;
using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Application;
using Asura.Core;
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
            inspector.Close();
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
