using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.App.Views.Components;
using Asura.App.Views.RuntimePanels;
using Asura.Application;
using Asura.Core;
using Asura.Git;

namespace Asura.App.Tests;

/// <summary>
/// The Git panel's and its dialogs' fixes from the Human Interface Guidelines
/// review that live in plain code: forms that check what is typed where it is
/// typed, pop-ups in place of retyped names, labels in the words people use,
/// and colours from the chart palette rather than the accent.
/// </summary>
public sealed class GitHigConformanceTests
{
    [Theory]
    [InlineData("1", "20", "src/app.cs", null, null)]
    [InlineData("0", "20", "src/app.cs", "start", "Enter a line number of 1 or more.")]
    [InlineData("x", "20", "src/app.cs", "start", "Enter a line number of 1 or more.")]
    [InlineData("5", "", "src/app.cs", "end", "Enter a line number of 1 or more.")]
    [InlineData("20", "5", "src/app.cs", "end", "Enter a last line at or after the first line.")]
    [InlineData("1", "20", "  ", "path", "Enter the path of a file in the repository.")]
    [InlineData("1", "20", "/etc/passwd", "path", "Enter the file’s path inside the repository, such as src/app.cs.")]
    [InlineData("1", "20", "../outside.cs", "path", "Enter the file’s path inside the repository, such as src/app.cs.")]
    public void Line_history_checks_its_range_and_file_beside_the_field_that_is_wrong(
        string start, string end, string path, string? field, string? message)
    {
        var error = GitRuntimePanelViewModel.ValidateLineRange(start, end, path);

        Assert.Equal(field, error?.Field);
        Assert.Equal(message, error?.Message);
    }

    [Theory]
    [InlineData("https://github.com/ada/engine.git", "engine")]
    [InlineData("git@github.com:ada/engine.git", "engine")]
    [InlineData("ssh://git@example.com/ada/engine/", "engine")]
    [InlineData("/Users/ada/src/engine", "engine")]
    [InlineData("  ", null)]
    public void A_clone_is_named_after_the_last_segment_of_its_url(string url, string? name) =>
        Assert.Equal(name, GitRuntimePanelView.RepositoryFolderName(url));

    [Fact]
    public void Choosing_a_folder_that_has_files_for_a_clone_makes_a_folder_named_after_the_repository_inside_it()
    {
        var parent = Directory.CreateTempSubdirectory("asura-clone-");
        var empty = parent.CreateSubdirectory("empty");
        try
        {
            File.WriteAllText(Path.Combine(parent.FullName, "notes.txt"), "");
            var name = GitRuntimePanelView.RepositoryFolderName("https://github.com/ada/engine.git");

            Assert.Equal(Path.Combine(parent.FullName, "engine"), GitRuntimePanelView.ChosenDestination(parent.FullName, name));
            Assert.Equal(empty.FullName, GitRuntimePanelView.ChosenDestination(empty.FullName, name));
            Assert.Equal(parent.FullName, GitRuntimePanelView.ChosenDestination(parent.FullName, null));

            // A typed folder that has files is caught in the form, with the
            // folder the clone could use instead.
            var error = GitRuntimePanelView.ValidateCloneDestination(parent.FullName, name);
            Assert.Equal("path", error?.Key);
            Assert.Contains(Path.Combine(parent.FullName, "engine"), error?.Message, StringComparison.Ordinal);
            Assert.Null(GitRuntimePanelView.ValidateCloneDestination(empty.FullName, name));
            Assert.Null(GitRuntimePanelView.ValidateCloneDestination(Path.Combine(parent.FullName, "engine"), name));
        }
        finally
        {
            parent.Delete(recursive: true);
        }
    }

    [Fact]
    public void Line_history_from_the_tools_menu_asks_for_lines_and_file_as_separate_checked_fields()
    {
        var fields = GitRuntimePanelView.TaskFields(GitRepositoryTask.RangeHistory, "src/app.cs", "HEAD", []);

        Assert.Equal(["start", "end", "path"], fields.Select(field => field.Key), StringComparer.Ordinal);
        Assert.Equal("src/app.cs", fields[2].Value);
        var validate = Assert.IsType<Func<IReadOnlyDictionary<string, string>, GitWorkflowFieldError?>>(
            GitRuntimePanelView.TaskValidation(GitRepositoryTask.RangeHistory));
        Assert.Equal("end", validate(new Dictionary<string, string>(StringComparer.Ordinal) { ["start"] = "9", ["end"] = "3", ["path"] = "a.cs" })?.Key);
        Assert.Null(validate(new Dictionary<string, string>(StringComparer.Ordinal) { ["start"] = "3", ["end"] = "9", ["path"] = "a.cs" }));
        Assert.Null(GitRuntimePanelView.TaskValidation(GitRepositoryTask.Signature));
        Assert.Equal("3,9:a.cs", GitRuntimePanelViewModel.LineRangeArgument(3, 9, " a.cs "));
    }

    [Fact]
    public void A_remote_is_chosen_from_the_repository_remotes_and_typed_only_when_there_are_none()
    {
        var chosen = GitRuntimePanelView.TaskFields(GitRepositoryTask.PruneRemote, "", "HEAD", ["origin", "upstream"]).Single();
        Assert.Equal(["origin", "upstream"], chosen.Choices!);
        Assert.Equal("origin", chosen.Value);

        var typed = GitWorkflowField.ChoiceOrText("remote", "Remote", [], "origin");
        Assert.Null(typed.Choices);
        Assert.Equal("origin", typed.Value);
    }

    [Fact]
    public void Only_the_current_branch_says_it_is_current_to_VoiceOver()
    {
        var current = new GitRefTreeNodeViewModel("main", "main", false, (_, _) => { },
            new GitRefItemViewModel(new GitRefItem("refs/heads/main", "main", GitRefKind.LocalBranch, "a", IsCurrent: true)));
        var other = new GitRefTreeNodeViewModel("feature", "codex/feature", false, (_, _) => { },
            new GitRefItemViewModel(new GitRefItem("refs/heads/codex/feature", "codex/feature", GitRefKind.LocalBranch, "a", IsCurrent: false)));
        var folder = new GitRefTreeNodeViewModel("codex", "codex", true, (_, _) => { });

        Assert.Equal("Current branch", current.AccessibleStatus);
        Assert.Equal("main", current.AccessibleName);
        Assert.Null(other.AccessibleStatus);
        Assert.Equal("codex/feature", other.AccessibleName);
        Assert.Null(folder.AccessibleStatus);
        Assert.Equal("codex", folder.AccessibleName);
    }

    [Fact]
    public void Rebase_steps_edit_a_message_only_when_the_rebase_uses_it_and_cannot_move_past_the_ends()
    {
        var row = new GitRebaseRowViewModel(new GitRebaseEntry("abc", "Subject"));
        Assert.False(row.UsesMessage);
        row.Action = GitRebaseAction.Reword;
        Assert.True(row.UsesMessage);
        // The rebase runs without an editor and applies a typed message only
        // to reword, so squash does not offer one it would throw away.
        row.Action = GitRebaseAction.Squash;
        Assert.False(row.UsesMessage);
        row.Action = GitRebaseAction.Fixup;
        Assert.False(row.UsesMessage);

        row.SetPosition(0, 3);
        Assert.False(row.CanMoveEarlier);
        Assert.True(row.CanMoveLater);
        row.SetPosition(2, 3);
        Assert.True(row.CanMoveEarlier);
        Assert.False(row.CanMoveLater);
        row.SetPosition(0, 1);
        Assert.False(row.CanMoveEarlier);
        Assert.False(row.CanMoveLater);
        Assert.Equal("Move “Subject” earlier", row.MoveEarlierLabel);
    }

    [Fact]
    public void Statistics_list_contributors_as_count_and_author_rows_and_total_size_in_byte_units()
    {
        var rows = GitStatisticsDialog.ParseContributors("   42\tAda Lovelace <ada@example.com>\n     7\tGrace Hopper <grace@example.com>\nnot a row\n");

        Assert.Equal(
            [new GitContributorRow(42, "Ada Lovelace <ada@example.com>"), new GitContributorRow(7, "Grace Hopper <grace@example.com>")],
            rows);
        Assert.Empty(GitStatisticsDialog.ParseContributors(null));
        Assert.Contains("Ada Lovelace", rows[0].AccessibleName, StringComparison.Ordinal);
        Assert.Contains("42 commits", rows[0].AccessibleName, StringComparison.Ordinal);

        var summary = GitStatisticsDialog.SummaryText(new GitRepositoryStatistics(
            [new GitDirectorySize("src", 15_000, 3), new GitDirectorySize("docs", 5_000, 2)], ""));
        Assert.EndsWith(" · " + ByteSize.Format(20_000), summary, StringComparison.Ordinal);
        Assert.DoesNotContain("bytes", summary, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Jpeg", "JPEG")]
    [InlineData("Tiff", "TIFF")]
    [InlineData("Png", "PNG")]
    [InlineData("Webp", "WebP")]
    [InlineData("Gif", "GIF")]
    [InlineData("Heif", "HEIF")]
    [InlineData("Jpegxl", "JPEG XL")]
    [InlineData(null, null)]
    public void Image_formats_are_named_as_people_write_them(string? decoderName, string? label) =>
        Assert.Equal(label, GitRuntimePanelViewModel.ImageFormatLabel(decoderName));

    [Fact]
    public void Hosting_providers_keep_their_own_spelling_and_account_actions_wait_for_an_account()
    {
        Assert.Equal("GitHub", GitHostingBrowserViewModel.ProviderName(GitHostingProvider.GitHub));
        Assert.Equal("GitLab", GitHostingBrowserViewModel.ProviderName(GitHostingProvider.GitLab));

        using var browser = new GitHostingBrowserViewModel(new FakeGitRepositoryClient(), BuiltInConnections.Local);
        Assert.False(browser.CanRemoveAccount);
        Assert.True(browser.IsListEmpty);
        Assert.Equal("Sign in to a hosting account to list its repositories.", browser.EmptyListText);

        browser.SelectedAccount = new GitHostingAccount(
            GitHostingProvider.GitHub, new Uri("https://api.github.com/"), "ada", new SecretRef("hosting-ada"));
        Assert.True(browser.CanRemoveAccount);
        Assert.Equal("Choose Load Repositories to list this account’s repositories.", browser.EmptyListText);
        browser.Starred = true;
        Assert.Equal("Choose Load Repositories to list this account’s starred repositories.", browser.EmptyListText);
    }

    [Fact]
    public async Task Hosting_clone_waits_for_a_chosen_repository_and_for_a_load_to_finish()
    {
        var client = new FakeGitRepositoryClient { HostedRepositoriesGate = new() };
        using var browser = new GitHostingBrowserViewModel(client, BuiltInConnections.Local);
        var repository = new GitHostedRepository("ada/engine", "https://example.com/ada/engine.git",
            "git@example.com:ada/engine.git", "https://example.com/ada/engine", IsPrivate: false);
        Assert.False(browser.CanClone);

        browser.SelectedRepository = repository;
        Assert.True(browser.CanClone);

        var load = browser.LoadRepositoriesAsync(reset: true);
        Assert.True(browser.IsBusy);
        Assert.False(browser.CanClone);

        client.HostedRepositoriesGate.SetResult(new([repository], 1, HasMore: false));
        await load;
        Assert.False(browser.IsBusy);
        Assert.True(browser.CanClone);
    }

    [Fact]
    public void An_LFS_lock_runs_its_details_together_and_keeps_its_id_to_the_tooltip()
    {
        var item = new GitLfsLockItemViewModel(new GitLfsLock("1234", "assets/logo.psd", "Ada", GitLfsLockOwnership.CurrentUser, null));

        Assert.Equal("Ada · Your lock", item.DetailLine);
        Assert.Equal("Lock ID 1234", item.IdTip);
        Assert.DoesNotContain("1234", item.DetailLine, StringComparison.Ordinal);
    }

    [Fact]
    public void The_repository_picker_names_the_place_it_browses_in_a_sentence()
    {
        var picker = new GitRepositoryPickerViewModel(new FakeGitRepositoryClient(), BuiltInConnections.Local, "/repo");

        Assert.StartsWith(OperatingSystem.IsMacOS() ? "Browsing this Mac." : "Browsing this computer.", picker.Subtitle, StringComparison.Ordinal);
        Assert.DoesNotContain("Local", picker.Subtitle, StringComparison.Ordinal);
    }

    [Fact]
    public void Commit_graph_lanes_take_the_chart_palette_not_the_accent()
    {
        Assert.All(GitCommitGraphRowView.LaneBrushKeys, key => Assert.StartsWith("ShellChartSeries", key, StringComparison.Ordinal));
        Assert.DoesNotContain("ShellAccentBrush", GitCommitGraphRowView.LaneBrushKeys, StringComparer.Ordinal);
    }
}
