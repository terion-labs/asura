using Asura.App.Controls;
using Asura.Application;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Asura.App.Views;

public sealed partial class DefinitionImportPreflightDialog : DialogWindow
{
    private readonly bool _canCommit;
    private bool _executionReviewed;
    public DefinitionImportPreflightDialog()
    {
        InitializeComponent();
    }

    public DefinitionImportPreflightDialog(DefinitionBundleImportPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        FileName = Path.GetFileName(plan.Path);
        Directory = Path.GetDirectoryName(plan.Path) ?? plan.Path;
        ModeLabel = plan.Mode == DefinitionImportMode.ReplaceExisting
            ? "Replace matching items"
            : "Stop on conflicts";
        Summary = $"{CountLabel.Format(plan.DefinitionCount, "item")} · {CountLabel.Format(plan.Conflicts.Count, "conflict")} · {CountLabel.Format(plan.Issues.Count(issue => issue.IsBlocking), "blocking issue")}";
        CommitHint = plan.CanApply
            ? plan.Mode == DefinitionImportMode.ReplaceExisting
                ? "Matching items are replaced all at once. Imports never include secret values: AI credentials are left unassigned, and AI providers and MCP servers stay off until you review them."
                : "Everything is imported at once, and items you already have stay as they are. AI credentials are left unassigned, and AI providers and MCP servers stay off until you review them."
            : "Fix the blocking issues or choose a different file. This file can’t be imported.";
        _canCommit = plan.CanApply;
        ExecutionReview = plan.ExecutionReview;
        Issues = [.. plan.Issues.Select(DefinitionImportIssueItem.From)];
        HasIssues = Issues.Count > 0;
        HasNoIssues = !HasIssues;
        InitializeComponent();
        DataContext = this;
    }

    public string FileName { get; } = "Asura setup";

    public string Directory { get; } = string.Empty;

    public string ModeLabel { get; } = string.Empty;

    public string Summary { get; } = string.Empty;

    public string CommitHint { get; } = string.Empty;

    public bool CanApply => _canCommit && (!HasExecutionReview || _executionReviewed);

    public IReadOnlyList<DefinitionExecutionReviewItem> ExecutionReview { get; } = [];

    public bool HasExecutionReview => ExecutionReview.Count > 0;

    public double IssuesMaximumHeight => HasExecutionReview ? 120 : 330;

    private void OnExecutionAcknowledged(object? sender, RoutedEventArgs e)
    {
        _executionReviewed = ExecutionAcknowledgement.IsChecked == true;
        ImportButton.IsEnabled = CanApply;
    }

    public bool HasIssues { get; }

    public bool HasNoIssues { get; } = true;

    public IReadOnlyList<DefinitionImportIssueItem> Issues { get; } = [];

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close(false);
    }

    private void OnImportClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close(CanApply);
    }
}

public sealed record DefinitionImportIssueItem(string Label, string Heading, string Message)
{
    public static DefinitionImportIssueItem From(DefinitionImportIssue issue) => new(
        issue.IsBlocking ? "Blocking" : "Notice",
        Humanize(issue.Code),
        issue.Message);

    private static string Humanize(DefinitionImportIssueCode code) => code switch
    {
        DefinitionImportIssueCode.ExistingIdentity => "Already exists",
        DefinitionImportIssueCode.MissingDependency => "Missing dependency",
        DefinitionImportIssueCode.UnsupportedKind => "Unsupported item type",
        DefinitionImportIssueCode.UnsupportedSchema => "Unsupported file version",
        DefinitionImportIssueCode.UnsafePayload => "Unsafe content rejected",
        DefinitionImportIssueCode.DuplicateIdentity => "Duplicate item",
        DefinitionImportIssueCode.InvalidPayload => "Damaged item",
        DefinitionImportIssueCode.ImportedMcpProfileDisabled =>
            "MCP server disabled for review",
        DefinitionImportIssueCode.ImportedAiProviderProfileDisabled =>
            "AI provider disabled for review",
        _ => "Damaged file",
    };
}
