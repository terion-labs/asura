using Asura.App.ViewModels;
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
        GitWorkflowField destination = new("path", "Destination path", viewModel.RepositoryPathInput)
        {
            // A clone usually goes in the folder that holds other repositories,
            // so a chosen folder that already has files gets a new folder
            // inside it named after the repository, as git clone itself does.
            ChooseFolder = clone
                ? FolderChooser("Choose a Folder for the Clone", values => RepositoryFolderName(values.GetValueOrDefault("source") ?? ""))
                : FolderChooser("Choose a Folder to Initialize"),
        };

        // The URL comes first when cloning: it names the folder the clone goes in.
        var values = await ShowGitFormAsync(new GitWorkflowForm(
            clone ? "Clone Repository" : "Initialize Repository",
            $"Connection: {viewModel.ConnectionDisplayName}",
            new GitWorkflowAction(clone ? "Clone" : "Initialize"),
            clone
                ? [new("source", "Repository URL"), destination]
                : [destination, new("source", "Initial branch", "main")])
        {
            Validate = clone && viewModel.UsesSystemFolderPicker
                ? values => ValidateCloneDestination(values["path"], RepositoryFolderName(values["source"]))
                : null,
        });
        if (values is null)
        {
            return;
        }

        await viewModel.CreateRepositoryAsync(values["path"], clone ? "main" : values["source"], clone ? values["source"] : null);
    }

    /// <summary>
    /// The folder a clone of <paramref name="repositoryUrl"/> is named after:
    /// the URL's last segment without ".git", as git clone names it.
    /// </summary>
    internal static string? RepositoryFolderName(string repositoryUrl)
    {
        var trimmed = repositoryUrl.Trim().TrimEnd('/', '\\');
        var name = trimmed[(trimmed.LastIndexOfAny(['/', '\\', ':']) + 1)..];
        if (name.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        return name.Length == 0 ? null : name;
    }

    /// <summary>
    /// Git cannot clone into a folder on this Mac that already has files in it,
    /// so the form says so under the field, with the folder it could use
    /// instead, rather than the clone failing after the form has closed.
    /// </summary>
    internal static GitWorkflowFieldError? ValidateCloneDestination(string path, string? folderName)
    {
        if (!Directory.Exists(path) || !Directory.EnumerateFileSystemEntries(path).Any())
        {
            return null;
        }

        return folderName is not null && !string.Equals(Path.GetFileName(path.TrimEnd('/', '\\')), folderName, StringComparison.Ordinal)
            ? new("path", $"This folder isn’t empty. To clone into a new folder inside it, use {Path.Combine(path, folderName)}.")
            : new("path", "This folder isn’t empty. Choose an empty folder or a new folder name.");
    }

    /// <summary>
    /// The folder chosen on this Mac when it is empty, otherwise a new folder
    /// named <paramref name="folderName"/> inside it; the chosen folder itself
    /// when there is no name to give one.
    /// </summary>
    internal static string ChosenDestination(string chosenFolder, string? folderName) =>
        folderName is null ? chosenFolder : WorktreeDestination(chosenFolder, folderName);

    /// <summary>
    /// The folder chosen over SSH with a new folder named
    /// <paramref name="folderName"/> inside it, always: the connection's folder
    /// browser lists folders, not files, so it cannot tell an empty folder from
    /// one that holds files, and the browser's subtitle says the folder is made.
    /// </summary>
    internal static string RemoteChosenDestination(string chosenFolder, string? folderName) =>
        folderName is null ? chosenFolder : $"{chosenFolder.TrimEnd('/')}/{folderName}";

    /// <summary>
    /// The Choose… beside a destination field. A folder on this Mac comes from
    /// the system's Open panel, with its sidebar, recents and search; one over
    /// SSH from Asura's folder browser, which sees the connection's files. With
    /// <paramref name="folderName"/>, a chosen folder on this Mac that already
    /// holds files gets a new folder inside it, named from the form's values,
    /// as a worktree does; over SSH the new folder is always made, and the
    /// browser says so (<see cref="RemoteChosenDestination"/>).
    /// </summary>
    private Func<Window, string, IReadOnlyDictionary<string, string>, Task<string?>> FolderChooser(
        string title, Func<IReadOnlyDictionary<string, string>, string?>? folderName = null) =>
        async (owner, current, values) =>
        {
            if (ViewModel is not { } viewModel)
            {
                return null;
            }

            var name = folderName?.Invoke(values);
            if (SystemFolderPicker(owner) is { } storage && viewModel.UsesSystemFolderPicker)
            {
                var start = Directory.Exists(current) ? current : Path.GetDirectoryName(current.TrimEnd('/', '\\'));
                var local = await PickLocalFolderAsync(storage, title, start);
                return local is null ? null : ChosenDestination(local, name);
            }

            var remote = await new GitRepositoryPickerDialog(viewModel.CreateRepositoryPicker(current), title, "Choose", name)
                .ShowDialog<string?>(owner);
            return remote is null ? null : RemoteChosenDestination(remote, name);
        };

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

        List<GitWorkflowField> fields = [GitWorkflowField.ChoiceOrText("remote", "Remote", RemoteNames(viewModel), "origin"),
            new("source", networkAction == GitNetworkAction.Push ? "Source branch or revision (optional)" : "Remote branch (optional)", Required: false)];
        if (networkAction == GitNetworkAction.Pull)
        {
            fields.Add(new("strategy", "Pull strategy", PullStrategyLabel(GitPullStrategy.FastForward),
                Choices: [.. Enum.GetValues<GitPullStrategy>().Select(PullStrategyLabel)]));
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
                Heading = $"Force Push to “{values["remote"]}/{destinationBranch}”?",
                Detail = $"{source ?? "HEAD"} replaces the remote branch. Commits on the remote that aren’t in it are dropped from the branch.",
                Notice = lease.Length == 0
                    ? "The branch doesn’t exist on the remote yet. Git rejects the push if it appears before then."
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
            values.TryGetValue("strategy", out var strategy)
                ? Enum.GetValues<GitPullStrategy>().Single(choice => string.Equals(PullStrategyLabel(choice), strategy, StringComparison.Ordinal))
                : GitPullStrategy.FastForward,
            Prune: GitWorkflowDialog.IsChecked(values, "prune"),
            Tags: GitWorkflowDialog.IsChecked(values, "tags"),
            ForceWithLease: forceWithLease,
            AutoStash: GitWorkflowDialog.IsChecked(values, "autostash"),
            ExpectedRemoteSha: lease));
    }

    /// <summary>What the pull strategy pop-up shows for each strategy, in place of its enum name.</summary>
    private static string PullStrategyLabel(GitPullStrategy strategy) => strategy switch
    {
        GitPullStrategy.FastForward => "Fast-forward only",
        GitPullStrategy.Merge => "Merge",
        GitPullStrategy.Rebase => "Rebase",
        _ => throw new ArgumentOutOfRangeException(nameof(strategy), strategy, null),
    };

    /// <summary>
    /// Lock, Unlock or Remove a linked worktree. From a worktree's own row the
    /// row says which; from the Branch menu the form offers the repository's
    /// linked worktrees to choose from rather than a path to type. Adding one
    /// has its own command, <see cref="OnAddWorktree"/>.
    /// </summary>
    private async void OnWorktreeTool(object? sender, RoutedEventArgs e)
    {
        _ = e;
        if (ViewModel is not { } viewModel || OwnerWindow is not { } window || sender is not MenuItem { Tag: string action } menu
            || !Enum.TryParse<GitWorktreeAction>(action, out var kind) || kind is GitWorktreeAction.Add or GitWorktreeAction.Prune)
        {
            return;
        }

        string[] linked = menu.DataContext is GitWorktreeItem item
            ? [item.Path]
            : [.. viewModel.Worktrees.Where(worktree => !worktree.IsMain).Select(worktree => worktree.Path)];
        if (linked.Length == 0)
        {
            await Confirmations.Problem("No Linked Worktrees",
                "This repository has only its main worktree, which Git doesn’t lock or remove. Add a worktree first.").ShowDialog(window);
            return;
        }

        var verb = WorktreeVerb(kind);
        var values = await ShowGitFormAsync(new GitWorkflowForm(
            $"{verb} Worktree",
            kind == GitWorktreeAction.Remove
                ? "Git removes only a clean worktree. It keeps a worktree with local edits or a lock."
                : "A locked worktree stays registered even when its folder is on a drive that isn’t connected.",
            new GitWorkflowAction(kind == GitWorktreeAction.Remove ? "Remove Worktree" : verb, IsDestructive: kind == GitWorktreeAction.Remove),
            [new("path", "Worktree", linked[0], Choices: linked)]));
        if (values is null)
        {
            return;
        }

        await viewModel.ManageWorktreeAsync(new(kind, values["path"]));
    }

    private static string WorktreeVerb(GitWorktreeAction action) => action switch
    {
        GitWorktreeAction.Lock => "Lock",
        GitWorktreeAction.Unlock => "Unlock",
        GitWorktreeAction.Remove => "Remove",
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, null),
    };

    /// <summary>The names of the repository's remotes, for a pop-up in place of a typed name.</summary>
    private static string[] RemoteNames(GitRuntimePanelViewModel viewModel) =>
        [.. viewModel.Remotes.Select(remote => remote.Name)];

    private async void OnSubmoduleTool(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || sender is not MenuItem { Tag: string action }
            || !Enum.TryParse<GitSubmoduleAction>(action, out var kind))
        {
            return;
        }

        // A submodule the repository already records is chosen from its list;
        // only a new one is typed.
        var item = (sender as MenuItem)?.DataContext as GitSubmoduleItem;
        List<GitWorkflowField> fields = kind == GitSubmoduleAction.Add
            ? [new("path", "Submodule path"), new("url", "Repository URL")]
            : [GitWorkflowField.ChoiceOrText("path", "Submodule",
                item is not null ? [item.Path] : [.. viewModel.Submodules.Select(submodule => submodule.Path)], "")];

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
            selected.Length switch
            {
                0 => "Stash all working changes.",
                1 => "Stash 1 selected file.",
                var count => $"Stash {count:N0} selected files.",
            },
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
        const string manualKey = "Enter a key below";
        var keyChoices = new[] { manualKey }.Concat(keys.Select(key => key.Fingerprint + " · " + key.Identity)).ToArray();
        var values = await ShowGitFormAsync("Repository Identity and Signing", "Save",
            "These are the Git settings in effect, including global values. Repository overrides apply only to this repository. If signing fails, your commit message is kept.",
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
