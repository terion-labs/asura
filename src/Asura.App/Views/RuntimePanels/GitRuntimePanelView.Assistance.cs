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
            var selection = await ShowGitFormAsync("Repository commands", "Choose a saved command or configure a new one.",
                new GitWorkflowField("preset", "Command", "New command", Choices: ["New command", .. saved.Select(command => command.Name)]));
            if (selection is null)
            {
                return;
            }
            preset = saved.FirstOrDefault(command => string.Equals(command.Name, selection["preset"], StringComparison.Ordinal));
        }

        var values = await ShowGitFormAsync("Run repository command",
            $"Target: {viewModel.ConnectionDisplayName}. Repository: {viewModel.RepositoryRoot}. Each argument is a separate line. Placeholders: {{repository}}, {{path}}, {{revision}}. Runs directly in the repository directory. Review before running.",
            new("name", "Save as (optional)", preset?.Name ?? "", Required: false),
            new("executable", "Executable", preset?.Executable ?? "git"),
            new("arguments", "Arguments, one per line", preset is null ? "status" : string.Join("\n", preset.Arguments), Required: false, Multiline: true),
            new("action", "Action", "Run", Choices: preset is null ? ["Run", "Save without running"] : ["Run", "Save without running", "Delete saved command"]));
        if (values is null)
        {
            return;
        }
        if (string.Equals(values["action"], "Delete saved command", StringComparison.Ordinal))
        {
            await viewModel.RemoveCustomCommandAsync(preset!.Name);
            return;
        }

        if (values["executable"].StartsWith('-') || values["executable"].IndexOfAny(['\0', '\n', '\r']) >= 0)
        {
            await new GitOutputDialog("Invalid executable", "Choose a command name or target path without control characters.").ShowDialog(window);
            return;
        }
        var template = values["arguments"].Length == 0 ? [] : values["arguments"].Split('\n').Select(argument => argument.TrimEnd('\r')).ToArray();
        if (values["name"].Length > 0)
        {
            await viewModel.SaveCustomCommandAsync(new(values["name"], values["executable"], template));
        }
        if (string.Equals(values["action"], "Save without running", StringComparison.Ordinal))
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
            await new GitOutputDialog("Repository command result", output).ShowDialog(window);
        }
    }
}
