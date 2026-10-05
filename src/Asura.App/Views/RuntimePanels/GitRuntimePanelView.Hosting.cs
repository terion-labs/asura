using Asura.Git;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views.RuntimePanels;

public sealed partial class GitRuntimePanelView
{
    private async void OnHostingAccounts(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || OwnerWindow is not { } window)
        {
            return;
        }

        var selection = await new GitHostingDialog(viewModel.CreateHostingBrowser()).ShowDialog<GitHostingCloneSelection?>(window);
        if (selection is null)
        {
            return;
        }

        var repository = selection.Repository;
        var values = await ShowGitFormAsync("Clone " + repository.Name, $"Target: {viewModel.ConnectionDisplayName}",
            new("protocol", "Protocol", "HTTPS", Choices: ["HTTPS", "SSH"]), new("path", "Destination path", viewModel.RepositoryPathInput));
        if (values is not null)
        {
            await viewModel.CreateRepositoryAsync(values["path"], "main", string.Equals(values["protocol"], "SSH", StringComparison.Ordinal) ? repository.SshUrl : repository.CloneUrl, selection.Account);
        }
    }

    private async void OnHostingShortcut(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || OwnerWindow is not { } window || sender is not MenuItem { Tag: string action })
        {
            return;
        }
        var remote = viewModel.Remotes.FirstOrDefault();
        if (remote is null)
        {
            return;
        }
        var url = remote.FetchUrl;
        if (url.StartsWith("git@", StringComparison.Ordinal) && url.IndexOf(':', StringComparison.Ordinal) is var colon && colon > 0)
        {
            url = "https://" + url[4..colon] + "/" + url[(colon + 1)..];
        }
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed) || parsed.Scheme is not ("https" or "ssh"))
        {
            return;
        }
        var repository = new UriBuilder(parsed) { Scheme = "https", Port = -1, UserName = "", Password = "", Query = "", Fragment = "" };
        repository.Path = repository.Path.EndsWith(".git", StringComparison.Ordinal) ? repository.Path[..^4] : repository.Path;
        var gitlab = parsed.Host.Contains("gitlab", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(action, "PullRequest", StringComparison.Ordinal))
        {
            var values = await ShowGitFormAsync("Open pull request", "The hosting site opens with these branches. Review and submit the pull request there.",
                new("base", "Target branch", "main"), new("head", "Source branch", viewModel.CurrentBranchName ?? ""));
            if (values is null)
            {
                return;
            }
            var baseBranch = Uri.EscapeDataString(values["base"]);
            var headBranch = Uri.EscapeDataString(values["head"]);
            repository.Path = repository.Path.TrimEnd('/') + (gitlab ? "/-/merge_requests/new" : "/compare/" + baseBranch + "..." + headBranch);
            repository.Query = gitlab ? "merge_request[target_branch]=" + baseBranch + "&merge_request[source_branch]=" + headBranch : "expand=1";
        }
        else if (string.Equals(action, "Notifications", StringComparison.Ordinal))
        {
            repository.Path = gitlab ? "/dashboard/todos" : "/notifications";
        }
        else if (string.Equals(action, "Commit", StringComparison.Ordinal) && viewModel.SelectedCommit is { } commit)
        {
            repository.Path = repository.Path.TrimEnd('/') + (gitlab ? "/-/commit/" : "/commit/") + Uri.EscapeDataString(commit.Commit.Sha);
        }
        else if (string.Equals(action, "Branch", StringComparison.Ordinal))
        {
            var branch = RefItem(sender)?.Name ?? viewModel.CurrentBranchName;
            if (branch is not null)
            {
                repository.Path = repository.Path.TrimEnd('/') + (gitlab ? "/-/tree/" : "/tree/") + Uri.EscapeDataString(branch);
            }
        }
        await window.Launcher.LaunchUriAsync(repository.Uri);
    }
}
