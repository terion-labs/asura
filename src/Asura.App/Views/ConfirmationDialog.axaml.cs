using Asura.App.Controls;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using FluentIcons.Avalonia;

namespace Asura.App.Views;

/// <summary>
/// What answering yes does, which decides how the alert dresses. There is no
/// default: a question that looks like a deletion when it is not one teaches
/// people to stop reading the deletions, so every caller says which it asks.
/// </summary>
internal enum ConfirmationIntent
{
    /// <summary>
    /// A routine action that needs a yes — merge, push, trust, authorize. The
    /// app's icon, an accent confirm button, and Return answers it.
    /// </summary>
    Confirm,

    /// <summary>
    /// Something that loses data or work and cannot be undone. A caution mark
    /// on the icon and a destructive confirm button that is deliberately not
    /// the default, so Return cannot destroy anything.
    /// </summary>
    Destructive,

    /// <summary>A notice with nothing to decide: one OK, which Return presses.</summary>
    Acknowledge,
}

/// <summary>Which of an alert's buttons answered it.</summary>
internal enum ConfirmationAnswer
{
    Cancel,
    Confirm,

    /// <summary>The leading-edge third button, such as Don't Save.</summary>
    Alternate,
}

/// <summary>
/// Everything a confirmation can vary in. The window owns everything else.
/// </summary>
internal sealed record ConfirmationDialogOptions
{
    /// <summary>
    /// The question, in bold. It is also the window's title — the alert has no
    /// separate title to repeat it.
    /// </summary>
    public required string Heading { get; init; }

    public required string Detail { get; init; }

    /// <summary>The blast radius: what the action does and does not touch.</summary>
    public string? Notice { get; init; }

    public required string ConfirmLabel { get; init; }

    public string CancelLabel { get; init; } = "Cancel";

    /// <summary>
    /// A third answer on the leading edge, apart from Cancel and the confirm
    /// button: Don't Save beside Save. Null for a two-button alert.
    /// </summary>
    public string? AlternateLabel { get; init; }

    /// <summary>An error acknowledgement has nothing to cancel.</summary>
    public bool ShowsCancel { get; init; } = true;

    public required ConfirmationIntent Intent { get; init; }

    public string? ConfirmAutomationName { get; init; }

    public string? CancelAutomationName { get; init; }

    /// <summary>
    /// A "Don't ask again" checkbox under the buttons' question. Only a
    /// confirmation backed by a setting that can turn it back on offers one.
    /// </summary>
    public string? SuppressionLabel { get; init; }
}

/// <summary>
/// The one confirmation window. Callers state what is being asked via
/// <see cref="ConfirmationDialogOptions"/> and await <c>ShowDialog&lt;bool&gt;</c>;
/// true means the action was confirmed.
/// </summary>
public sealed partial class ConfirmationDialog : DialogWindow
{
    public ConfirmationDialog()
        : this(new ConfirmationDialogOptions
        {
            Heading = "Are You Sure?",
            Detail = "This action needs a second look before it runs.",
            ConfirmLabel = "Confirm",
            Intent = ConfirmationIntent.Confirm,
        })
    {
    }

    internal ConfirmationDialog(ConfirmationDialogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Intent = options.Intent;
        Heading = options.Heading.Trim();
        Detail = options.Detail.Trim();
        Notice = string.IsNullOrWhiteSpace(options.Notice) ? null : options.Notice.Trim();
        ConfirmLabel = options.ConfirmLabel.Trim();
        CancelLabel = options.CancelLabel.Trim();
        ShowsCancel = options.ShowsCancel;
        AlternateLabel = string.IsNullOrWhiteSpace(options.AlternateLabel)
            ? null
            : options.AlternateLabel.Trim();
        SuppressionLabel = string.IsNullOrWhiteSpace(options.SuppressionLabel)
            ? null
            : options.SuppressionLabel.Trim();
        InitializeComponent();
        DataContext = this;
        Title = Heading;

        var confirm = this.FindControl<Button>("ConfirmButton")!;
        var cancel = this.FindControl<Button>("CancelButton")!;
        if (Intent == ConfirmationIntent.Destructive)
        {
            // Pressed on purpose or not at all: no default, and Escape still
            // reaches Cancel.
            confirm.Classes.Add("DestructiveButton");
        }
        else
        {
            confirm.Classes.Add("PrimaryButton");
            confirm.IsDefault = true;
        }

        PresentIcon();
        ArrangeButtons(confirm, cancel);
        AutomationProperties.SetName(confirm, options.ConfirmAutomationName ?? ConfirmLabel);
        AutomationProperties.SetName(cancel, options.CancelAutomationName ?? CancelLabel);
    }

    internal ConfirmationIntent Intent { get; }

    public string Heading { get; }

    public string Detail { get; }

    public string? Notice { get; }

    public bool HasNotice => Notice is not null;

    public string ConfirmLabel { get; }

    public string CancelLabel { get; }

    public bool ShowsCancel { get; }

    public string? AlternateLabel { get; }

    public bool HasAlternate => AlternateLabel is not null;

    /// <summary>The button that closed the alert.</summary>
    internal ConfirmationAnswer Answer { get; private set; }

    public string? SuppressionLabel { get; }

    public bool HasSuppression => SuppressionLabel is not null;

    /// <summary>Whether "Don't ask again" was ticked when the dialog closed.</summary>
    public bool IsSuppressionChecked { get; set; }

    /// <summary>
    /// Shows the confirmation over <paramref name="owner"/>, or on its own when
    /// there is no window left to own it — quitting from the Dock with only
    /// background sessions running still has to ask.
    /// </summary>
    internal Task<bool> ShowAsync(Window? owner)
    {
        if (owner is not null)
        {
            return ShowDialog<bool>(owner);
        }

        var answer = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        Closed += (_, _) => answer.TrySetResult(_confirmed);
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Show();
        Activate();
        return answer.Task;
    }

    private bool _confirmed;

    /// <summary>
    /// Asks a three-way question — Save, Don't Save, Cancel — over
    /// <paramref name="owner"/> and says which was chosen.
    /// </summary>
    internal async Task<ConfirmationAnswer> AskAsync(Window owner)
    {
        _ = await ShowDialog<bool>(owner);
        return Answer;
    }

    /// <summary>
    /// The app's icon, as the host's alerts carry it; a destructive question
    /// adds the caution badge. Where the platform has no icon to read, the
    /// state's own glyph stands in.
    /// </summary>
    private void PresentIcon()
    {
        var destructive = Intent == ConfirmationIntent.Destructive;
        var caution = StateOverlayPresentation.For(StateOverlayKind.DestructiveAction).Glyph;
        this.FindControl<SymbolIcon>("CautionGlyph")!.Symbol = caution;
        var icon = MacOsApplicationIconImage.Read();
        if (icon is not null)
        {
            this.FindControl<Image>("ApplicationIcon")!.Source = icon;
            this.FindControl<Border>("CautionBadge")!.IsVisible = destructive;
            return;
        }

        var fallback = this.FindControl<SymbolIcon>("FallbackGlyph")!;
        fallback.IsVisible = true;
        if (destructive)
        {
            fallback.Symbol = caution;
            fallback.Foreground = this.FindResource("ShellWarningBrush") as Avalonia.Media.IBrush;
        }
    }

    /// <summary>
    /// The buttons span the alert's width. Two short answers share one row
    /// with the default on the trailing side; three, or any too long for half
    /// the row, stack with the default on top and Cancel at the bottom; one —
    /// an acknowledgement — takes the whole row.
    /// </summary>
    private void ArrangeButtons(Button confirm, Button cancel)
    {
        const int longestSideBySideLabel = 14;
        var row = this.FindControl<Grid>("ButtonRow")!;
        var alternate = this.FindControl<Button>("AlternateButton")!;
        (Button Button, string Label)[] shown = [.. new[]
            {
                (confirm, ConfirmLabel),
                (alternate, AlternateLabel ?? string.Empty),
                (cancel, CancelLabel),
            }
            .Where(item => item.Item1 == confirm
                || (item.Item1 == alternate && HasAlternate)
                || (item.Item1 == cancel && ShowsCancel))];
        if (shown.Length == 3
            || (shown.Length == 2 && shown.Any(item => item.Label.Length > longestSideBySideLabel)))
        {
            row.RowDefinitions = new RowDefinitions(string.Join(',', shown.Select(_ => "Auto")));
            for (var index = 0; index < shown.Length; index++)
            {
                Grid.SetRow(shown[index].Button, index);
            }

            return;
        }

        row.ColumnDefinitions = shown.Length == 2
            ? new ColumnDefinitions("*,*")
            : new ColumnDefinitions("*");
        Grid.SetColumn(confirm, shown.Length - 1);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Close(false);
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        _confirmed = true;
        Answer = ConfirmationAnswer.Confirm;
        Close(true);
    }

    private void OnAlternateClick(object? sender, RoutedEventArgs e)
    {
        _ = sender;
        _ = e;
        Answer = ConfirmationAnswer.Alternate;
        Close(false);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
