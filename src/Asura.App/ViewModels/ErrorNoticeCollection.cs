using System.Collections.ObjectModel;

namespace Asura.App.ViewModels;

/// <summary>
/// Acknowledgements are independent of current operation state. Refreshes and successful
/// retries may update that state without acknowledging a retained error. The most recent
/// failures stay available for individual dismissal within a finite display budget.
/// </summary>
public sealed class ErrorNoticeCollection : ObservableObject
{
    internal const int MaximumNotices = 100;
    internal const int MaximumMessageCharacters = 8192;
    private const int MaximumTitleCharacters = 256;
    private readonly ObservableCollection<ErrorNoticeViewModel> _items = [];

    public ErrorNoticeCollection() => Items = new ReadOnlyObservableCollection<ErrorNoticeViewModel>(_items);

    public ReadOnlyObservableCollection<ErrorNoticeViewModel> Items { get; }

    public bool HasErrors => _items.Count > 0;

    public void Report(string? message, string? title = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        message = BoundedText(message, MaximumMessageCharacters);
        title = title is null ? null : BoundedText(title, MaximumTitleCharacters);
        if (_items.Any(item => string.Equals(item.Message, message, StringComparison.Ordinal)
                && string.Equals(item.Title, title, StringComparison.Ordinal)))
        {
            return;
        }

        if (_items.Count == MaximumNotices)
        {
            _items.RemoveAt(0);
        }
        _items.Add(new ErrorNoticeViewModel(message, title, Dismiss));
        OnPropertyChanged(nameof(HasErrors));
    }

    private static string BoundedText(string text, int maximum) => text.Length <= maximum
        ? text : string.Concat(text.AsSpan(0, maximum - 1), "…");

    private void Dismiss(ErrorNoticeViewModel notice)
    {
        _items.Remove(notice);
        OnPropertyChanged(nameof(HasErrors));
    }
}

public sealed class ErrorNoticeViewModel(string message, string? title, Action<ErrorNoticeViewModel> dismiss)
{
    public string Message { get; } = message;
    public string? Title { get; } = title;
    public void Dismiss() => dismiss(this);
}
