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
    private async Task<Dictionary<string, string>?> ShowGitFormAsync(string title, string detail, params GitWorkflowField[] fields)
    {
        if (OwnerWindow is not { } window)
        {
            return null;
        }

        return await new GitWorkflowDialog(title, detail, fields).ShowDialog<Dictionary<string, string>?>(window);
    }

    private async void OnCreateRepository(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || sender is not Button { Tag: string kind })
        {
            return;
        }

        var clone = string.Equals(kind, "Clone", StringComparison.Ordinal);
        var values = await ShowGitFormAsync(clone ? "Clone repository" : "Initialize repository", $"Target: {viewModel.ConnectionDisplayName}",
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
        if (ViewModel is not { } viewModel || sender is not MenuItem { Tag: string action }
            || !Enum.TryParse<GitNetworkAction>(action, out var networkAction))
        {
            return;
        }

        List<GitWorkflowField> fields = [new("remote", "Remote", viewModel.Remotes.FirstOrDefault()?.Name ?? "origin"),
            new("source", networkAction == GitNetworkAction.Push ? "Source branch or revision (optional)" : "Remote branch (optional)", Required: false)];
        if (networkAction == GitNetworkAction.Pull)
        {
            fields.Add(new("strategy", "Pull strategy", "FastForward", Choices: ["FastForward", "Merge", "Rebase"]));
            fields.Add(new("autostash", "Automatically stash and restore local edits", "No", Choices: ["No", "Yes"]));
        }
        if (networkAction == GitNetworkAction.Fetch)
        {
            fields.Add(new("prune", "Prune deleted remote branches", "Yes", Choices: ["No", "Yes"]));
        }

        if (networkAction == GitNetworkAction.Push)
        {
            fields.Add(new("destination", "Destination branch (optional)", Required: false));
            fields.Add(new("force", "Push mode", "Normal", Choices: ["Normal", "Force with lease"]));
        }
        fields.Add(new("tags", "Include tags", "No", Choices: ["No", "Yes"]));
        var values = await ShowGitFormAsync(action, $"Repository: {viewModel.RepositoryRoot}. Review the source and destination before applying.", [.. fields]);
        if (values is null)
        {
            return;
        }

        var source = values["source"].Length == 0 ? null : values["source"];
        var destinationBranch = values.GetValueOrDefault("destination") is { Length: > 0 } chosenDestination ? chosenDestination : null;
        var forceWithLease = string.Equals(values.GetValueOrDefault("force"), "Force with lease", StringComparison.Ordinal);
        string? lease = null;
        if (forceWithLease)
        {
            if (destinationBranch is null)
            {
                await new GitOutputDialog("Choose a destination branch", "Force with lease needs an explicit destination so its expected remote revision can be reviewed.").ShowDialog(OwnerWindow!);
                return;
            }

            lease = viewModel.GetReviewedRemoteSha(values["remote"], destinationBranch) ?? "";
            var review = await ShowGitFormAsync("Review force push",
                $"Push {source ?? "HEAD"} to {values["remote"]}/{destinationBranch}. Expected remote revision: {(lease.Length == 0 ? "branch does not exist" : lease)}. Git rejects the push if that revision changes.");
            if (review is null)
            {
                return;
            }
        }

        await viewModel.NetworkAsync(new(networkAction, values["remote"], source,
            destinationBranch,
            values.TryGetValue("strategy", out var strategy) ? Enum.Parse<GitPullStrategy>(strategy) : GitPullStrategy.FastForward,
            Prune: string.Equals(values.GetValueOrDefault("prune"), "Yes", StringComparison.Ordinal),
            Tags: string.Equals(values["tags"], "Yes", StringComparison.Ordinal),
            ForceWithLease: forceWithLease,
            AutoStash: string.Equals(values.GetValueOrDefault("autostash"), "Yes", StringComparison.Ordinal),
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
        var values = await ShowGitFormAsync($"{kind} worktree", "Removing a worktree requires a clean checkout. Git preserves dirty or locked worktrees.", [.. fields]);
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

        var values = await ShowGitFormAsync($"{kind} submodule", "Update and initialize operate recursively. Removal stages deletion in the parent repository.", [.. fields]);
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
        var values = await ShowGitFormAsync("Save stash", selected.Length == 0 ? "Stash all working changes." : $"Stash {selected.Length} selected files.",
            new("message", "Message", Required: false), new("untracked", "Include new files", "Yes", Choices: ["No", "Yes"]),
            new("index", "Keep staged changes", "No", Choices: ["No", "Yes"]));
        if (values is null)
        {
            return;
        }

        await viewModel.SaveStashAsync(new(values["message"], selected,
            string.Equals(values["untracked"], "Yes", StringComparison.Ordinal), string.Equals(values["index"], "Yes", StringComparison.Ordinal)));
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
        var values = await ShowGitFormAsync("Repository identity and signing", "These are effective Git settings, including inherited global values. Repository overrides apply only here. A key or signing-agent failure keeps your commit draft.",
            new("mode", "Settings source", "Repository overrides", Choices: ["Repository overrides", "Inherit global settings"]),
            new("name", "Author name", identity.Name), new("email", "Author email", identity.Email),
            new("sign", "Sign commits", identity.SignCommits ? "Yes" : "No", Choices: ["No", "Yes"]),
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
        await viewModel.SaveIdentityAsync(new(values["name"], values["email"], string.Equals(values["sign"], "Yes", StringComparison.Ordinal), selectedKey?.Fingerprint ?? values["key"]));
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
