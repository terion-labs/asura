using Asura.Git;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views.RuntimePanels;

public sealed partial class GitRuntimePanelView
{
    private async void OnRecentRepository(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: string path } && ViewModel is { } viewModel)
        {
            await viewModel.OpenRepositoryAsync(path);
        }
    }

    private void OnRecentCommitMessage(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox { SelectedItem: string message } && ViewModel is { } viewModel)
        {
            viewModel.CommitSubject = message;
        }
    }

    /// <summary>A form whose button carries <paramref name="verb"/>.</summary>
    private Task<Dictionary<string, string>?> ShowGitFormAsync(string title, string verb, string detail, params GitWorkflowField[] fields) =>
        ShowGitFormAsync(new GitWorkflowForm(title, detail, new GitWorkflowAction(verb), fields));

    private async Task<Dictionary<string, string>?> ShowGitFormAsync(GitWorkflowForm form)
    {
        if (OwnerWindow is not { } window)
        {
            return null;
        }

        return await new GitWorkflowDialog(form).ShowDialog<Dictionary<string, string>?>(window);
    }

    private async void OnCreateRepository(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || sender is not Button { Tag: string kind })
        {
            return;
        }

        var clone = string.Equals(kind, "Clone", StringComparison.Ordinal);
        var values = await ShowGitFormAsync(
            clone ? "Clone Repository" : "Initialize Repository",
            clone ? "Clone" : "Initialize",
            $"Target: {viewModel.ConnectionDisplayName}",
            new("path", "Destination path", viewModel.RepositoryPathInput),
            new("source", clone ? "Repository URL" : "Initial branch", clone ? "" : "main"));
        if (values is null)
        {
            return;
        }

        await viewModel.CreateRepositoryAsync(values["path"], clone ? "main" : values["source"], clone ? values["source"] : null);
    }

    private async void OnNetworkOptions(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (sender is MenuItem { Tag: string action } && Enum.TryParse<GitNetworkAction>(action, out var networkAction))
        {
            await ShowNetworkOptionsAsync(networkAction);
        }
    }

    /// <summary>Fetch…, Pull… or Push… with its options, from the Remote menu or the Git menu.</summary>
    private async Task ShowNetworkOptionsAsync(GitNetworkAction networkAction)
    {
        if (ViewModel is not { } viewModel || OwnerWindow is not { } window)
        {
            return;
        }

        var action = networkAction.ToString();

        List<GitWorkflowField> fields = [new("remote", "Remote", viewModel.Remotes.FirstOrDefault()?.Name ?? "origin"),
            new("source", networkAction == GitNetworkAction.Push ? "Source branch or revision (optional)" : "Remote branch (optional)", Required: false)];
        if (networkAction == GitNetworkAction.Pull)
        {
            fields.Add(new("strategy", "Pull strategy", "FastForward", Choices: ["FastForward", "Merge", "Rebase"]));
            fields.Add(GitWorkflowField.CheckBox("autostash", "Stash local edits first and restore them afterward", false));
        }
        if (networkAction == GitNetworkAction.Fetch)
        {
            fields.Add(GitWorkflowField.CheckBox("prune", "Prune deleted remote branches", true));
        }

        if (networkAction == GitNetworkAction.Push)
        {
            fields.Add(new("destination", "Destination branch (optional)", Required: false));
            fields.Add(GitWorkflowField.CheckBox("force", "Force with lease", false));
        }
        fields.Add(GitWorkflowField.CheckBox("tags", "Include tags", false));
        var values = await ShowGitFormAsync(new GitWorkflowForm(
            action,
            $"Repository: {viewModel.RepositoryRoot}. Review the source and destination before continuing.",
            new GitWorkflowAction(action),
            fields)
        {
            // A forced push rewrites the remote branch, so it says so on its
            // button and is never the default.
            ActionFor = current => GitWorkflowDialog.IsChecked(current, "force")
                ? new GitWorkflowAction("Force Push…", IsDestructive: true)
                : new GitWorkflowAction(action),
            Validate = current => GitWorkflowDialog.IsChecked(current, "force")
                && current.GetValueOrDefault("destination") is not { Length: > 0 }
                    ? new GitWorkflowFieldError("destination", "Force with lease needs a destination branch, so the remote revision it expects can be reviewed.")
                    : null,
        });
        if (values is null)
        {
            return;
        }

        var source = values["source"].Length == 0 ? null : values["source"];
        var destinationBranch = values.GetValueOrDefault("destination") is { Length: > 0 } chosenDestination ? chosenDestination : null;
        var forceWithLease = GitWorkflowDialog.IsChecked(values, "force");
        string? lease = null;
        if (forceWithLease && destinationBranch is not null)
        {
            lease = viewModel.GetReviewedRemoteSha(values["remote"], destinationBranch) ?? "";
            var confirmed = await new ConfirmationDialog(new ConfirmationDialogOptions
            {
                Heading = $"Force push to “{values["remote"]}/{destinationBranch}”?",
                Detail = $"{source ?? "HEAD"} replaces the remote branch. Commits on the remote that are not in it are dropped from the branch.",
                Notice = lease.Length == 0
                    ? "The branch does not exist on the remote yet. Git rejects the push if it appears before then."
                    : $"Expected remote revision: {lease}. Git rejects the push if that revision changes.",
                ConfirmLabel = "Force Push",
                Intent = ConfirmationIntent.Destructive,
            }).ShowDialog<bool>(window);
            if (!confirmed)
            {
                return;
            }
        }

        await viewModel.NetworkAsync(new(networkAction, values["remote"], source,
            destinationBranch,
            values.TryGetValue("strategy", out var strategy) ? Enum.Parse<GitPullStrategy>(strategy) : GitPullStrategy.FastForward,
            Prune: GitWorkflowDialog.IsChecked(values, "prune"),
            Tags: GitWorkflowDialog.IsChecked(values, "tags"),
            ForceWithLease: forceWithLease,
            AutoStash: GitWorkflowDialog.IsChecked(values, "autostash"),
            ExpectedRemoteSha: lease));
    }

    private async void OnWorktreeTool(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || sender is not MenuItem { Tag: string action }
            || !Enum.TryParse<GitWorktreeAction>(action, out var kind))
        {
            return;
        }

        var item = (sender as MenuItem)?.DataContext as GitWorktreeItem;
        List<GitWorkflowField> fields = [new("path", "Worktree path", item?.Path ?? "")];
        if (kind == GitWorktreeAction.Add)
        {
            fields.Add(new("branch", "Starting branch or revision", "HEAD"));
            fields.Add(new("new", "New branch name (optional)", Required: false));
        }
        var values = await ShowGitFormAsync(new GitWorkflowForm(
            $"{kind} Worktree",
            "Removing a worktree requires a clean checkout. Git preserves dirty or locked worktrees.",
            new GitWorkflowAction(kind.ToString(), IsDestructive: kind == GitWorktreeAction.Remove),
            fields));
        if (values is null)
        {
            return;
        }

        await viewModel.ManageWorktreeAsync(new(kind, values["path"], values.GetValueOrDefault("branch"),
            values.GetValueOrDefault("new") is { Length: > 0 } branch ? branch : null));
    }

    private async void OnSubmoduleTool(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || sender is not MenuItem { Tag: string action }
            || !Enum.TryParse<GitSubmoduleAction>(action, out var kind))
        {
            return;
        }

        var item = (sender as MenuItem)?.DataContext as GitSubmoduleItem;
        List<GitWorkflowField> fields = [new("path", "Submodule path", item?.Path ?? "")];
        if (kind == GitSubmoduleAction.Add)
        {
            fields.Add(new("url", "Repository URL"));
        }

        var values = await ShowGitFormAsync(new GitWorkflowForm(
            $"{kind} Submodule",
            "Update and initialize operate recursively. Removal stages deletion in the parent repository.",
            new GitWorkflowAction(kind.ToString(), IsDestructive: kind == GitSubmoduleAction.Remove),
            fields));
        if (values is null)
        {
            return;
        }

        await viewModel.ManageSubmoduleAsync(new(kind, values["path"], values.GetValueOrDefault("url")));
    }

    private async void OnSelectiveStash(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var selected = viewModel.SelectedUnstagedItems.Concat(viewModel.SelectedStagedItems).Select(item => item.Path).Distinct(StringComparer.Ordinal).ToArray();
        var values = await ShowGitFormAsync("Save Stash", "Save Stash",
            selected.Length == 0 ? "Stash all working changes." : $"Stash {selected.Length} selected files.",
            new("message", "Message", Required: false),
            GitWorkflowField.CheckBox("untracked", "Include new files", true),
            GitWorkflowField.CheckBox("index", "Keep staged changes", false));
        if (values is null)
        {
            return;
        }

        await viewModel.SaveStashAsync(new(values["message"], selected,
            GitWorkflowDialog.IsChecked(values, "untracked"), GitWorkflowDialog.IsChecked(values, "index")));
    }

    private async void OnIdentitySettings(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel)
        {
            return;
        }

        var identity = await viewModel.ReadIdentityAsync();
        if (identity is null)
        {
            return;
        }

        var keys = await viewModel.ReadSigningKeysAsync();
        const string manualKey = "Use signing key field";
        var keyChoices = new[] { manualKey }.Concat(keys.Select(key => key.Fingerprint + " · " + key.Identity)).ToArray();
        var values = await ShowGitFormAsync("Repository Identity and Signing", "Save",
            "These are effective Git settings, including inherited global values. Repository overrides apply only here. A key or signing-agent failure keeps your commit draft.",
            new("mode", "Settings source", "Repository overrides", Choices: ["Repository overrides", "Inherit global settings"]),
            new("name", "Author name", identity.Name), new("email", "Author email", identity.Email),
            GitWorkflowField.CheckBox("sign", "Sign commits", identity.SignCommits),
            new("selectedKey", "Available signing keys", manualKey, Choices: keyChoices),
            new("key", "Signing key fingerprint or identifier", identity.SigningKey, Required: false));
        if (values is null)
        {
            return;
        }
        if (string.Equals(values["mode"], "Inherit global settings", StringComparison.Ordinal))
        {
            await viewModel.ResetIdentityAsync();
            return;
        }

        var selectedKey = keys.FirstOrDefault(key => string.Equals(key.Fingerprint + " · " + key.Identity, values["selectedKey"], StringComparison.Ordinal));
        await viewModel.SaveIdentityAsync(new(values["name"], values["email"], GitWorkflowDialog.IsChecked(values, "sign"), selectedKey?.Fingerprint ?? values["key"]));
    }

    private async void OnOpenNestedRepository(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || sender is not MenuItem menu)
        {
            return;
        }

        var path = menu.DataContext switch
        {
            GitWorktreeItem worktree => worktree.Path,
            GitSubmoduleItem submodule => viewModel.RepositoryRoot.TrimEnd('/') + "/" + submodule.Path,
            _ => null,
        };
        if (path is not null)
        {
            await viewModel.OpenRepositoryAsync(path);
        }
    }
}
