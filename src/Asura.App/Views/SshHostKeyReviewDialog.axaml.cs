using Asura.App.Controls;
using Asura.Application;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Asura.App.Views;

internal sealed record SshHostKeyReviewPrompt(SshHostKeyReview Review)
{
    public string Title => Review.RequiresExplicitReplacement
        ? "The SSH Host Key Changed"
        : "Trust This SSH Host Key?";

    public string Explanation => Review.RequiresExplicitReplacement
        ? "The server presented a different identity from the one you previously trusted. This can indicate a rebuilt server, an endpoint change, or an interception attempt."
        : "This is the first host key Asura has seen for this saved connection.";

    public string ConfirmLabel => Review.RequiresExplicitReplacement
        ? "Replace Trusted Key"
        : "Trust Host Key";

    /// <summary>
    /// Replacing a trusted key is the possible-interception case: it is styled
    /// as destructive and never answers Return.
    /// </summary>
    public bool IsReplacement => Review.RequiresExplicitReplacement;
}

public sealed partial class SshHostKeyReviewDialog : DialogWindow
{
    public SshHostKeyReviewDialog()
    {
        InitializeComponent();
    }

    public SshHostKeyReviewDialog(SshHostKeyReview review)
    {
        ArgumentNullException.ThrowIfNull(review);
        InitializeComponent();
        var prompt = new SshHostKeyReviewPrompt(review);
        DataContext = prompt;
        var confirm = this.FindControl<Button>("ConfirmButton")!;
        if (prompt.IsReplacement)
        {
            confirm.Classes.Add("DestructiveButton");
        }
        else
        {
            confirm.Classes.Add("PrimaryButton");
            confirm.IsDefault = true;
        }
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
        Close(true);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

}
