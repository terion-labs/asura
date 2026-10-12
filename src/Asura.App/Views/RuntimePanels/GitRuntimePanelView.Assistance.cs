using Asura.App.ViewModels;
using Asura.Git;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views.RuntimePanels;

public sealed partial class GitRuntimePanelView
{
    private async void OnCommitAssistance(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || OwnerWindow?.DataContext is not MainWindowViewModel { AgentChat: { } chat } main)
        {
            return;
        }

        var prompt = await viewModel.ComposeCommitAssistanceAsync();
        if (prompt is null)
        {
            return;
        }

        chat.Prompt = string.IsNullOrWhiteSpace(chat.Prompt) ? prompt : chat.Prompt + "\n\n" + prompt;
        main.IsAgentPanelVisible = true;
    }

    private void OnReviewSelection(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || OwnerWindow?.DataContext is not MainWindowViewModel { AgentChat: { } chat } main)
        {
            return;
        }

        var scope = viewModel.ReviewScope;
        if (scope is null)
        {
            return;
        }

        var prompt = $"Review {scope} in {viewModel.RepositoryRoot} on {viewModel.ConnectionDisplayName}. Report actionable findings with file paths and line references. Keep the review scoped to these exact revisions. Do not change files, commit, or push.";
        chat.Prompt = string.IsNullOrWhiteSpace(chat.Prompt) ? prompt : chat.Prompt + "\n\n" + prompt;
        main.IsAgentPanelVisible = true;
    }

    private async void OnCustomCommand(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel || OwnerWindow is not { } window)
        {
            return;
        }

        var preferences = await viewModel.ReadGitPreferencesAsync();
        var saved = preferences.CustomCommands;
        Asura.Application.GitCustomCommandDefinition? preset = null;
        if (saved.Count > 0)
        {
            var selection = await ShowGitFormAsync("Repository Commands", "Continue…", "Choose a saved command or configure a new one.",
                new GitWorkflowField("preset", "Command", "New Command", Choices: ["New Command", .. saved.Select(command => command.Name)]));
            if (selection is null)
            {
                return;
            }
            preset = saved.FirstOrDefault(command => string.Equals(command.Name, selection["preset"], StringComparison.Ordinal));
        }

        var values = await ShowGitFormAsync(new GitWorkflowForm(
            "Run Repository Command",
            $"Connection: {viewModel.ConnectionDisplayName}. Repository: {viewModel.RepositoryRoot}. Each argument is a separate line. Placeholders: {{repository}}, {{path}}, {{revision}}. Runs directly in the repository folder. Review before running.",
            new GitWorkflowAction("Run"),
            [
                new("name", "Save as (optional)", preset?.Name ?? "", Required: false),
                new("executable", "Executable", preset?.Executable ?? "git"),
                new("arguments", "Arguments, one per line", preset is null ? "status" : string.Join("\n", preset.Arguments), Required: false, Multiline: true),
                new("action", "When done", "Run", Choices: preset is null ? ["Run", "Save Without Running"] : ["Run", "Save Without Running", "Delete Saved Command"]),
            ])
        {
            // The button says which of the three the choice above it will do.
            ActionFor = current => current.GetValueOrDefault("action") switch
            {
                "Save Without Running" => new GitWorkflowAction("Save"),
                "Delete Saved Command" => new GitWorkflowAction("Delete Command", IsDestructive: true),
                _ => new GitWorkflowAction("Run"),
            },
            Validate = current => string.Equals(current["action"], "Delete Saved Command", StringComparison.Ordinal)
                ? null
                : GitPreferenceRules.ExecutableError(current["executable"]) is { } problem
                    ? new GitWorkflowFieldError("executable", problem)
                    : null,
        });
        if (values is null)
        {
            return;
        }
        if (string.Equals(values["action"], "Delete Saved Command", StringComparison.Ordinal))
        {
            await viewModel.RemoveCustomCommandAsync(preset!.Name);
            return;
        }

        var template = values["arguments"].Length == 0 ? [] : values["arguments"].Split('\n').Select(argument => argument.TrimEnd('\r')).ToArray();
        if (values["name"].Length > 0)
        {
            await viewModel.SaveCustomCommandAsync(new(values["name"], values["executable"], template));
        }
        if (string.Equals(values["action"], "Save Without Running", StringComparison.Ordinal))
        {
            return;
        }

        var arguments = template.Select(argument => argument
            .Replace("{repository}", viewModel.RepositoryRoot, StringComparison.Ordinal)
            .Replace("{path}", viewModel.SelectedChange?.Path ?? viewModel.SelectedCommitChange?.Path ?? "", StringComparison.Ordinal)
            .Replace("{revision}", viewModel.SelectedCommit?.Commit.Sha ?? "HEAD", StringComparison.Ordinal)).ToArray();
        var output = await viewModel.RunCustomCommandAsync(new(values["executable"], arguments));
        if (output is not null)
        {
            await new GitOutputDialog("Repository Command Output", output).ShowDialog(window);
        }
    }
}
