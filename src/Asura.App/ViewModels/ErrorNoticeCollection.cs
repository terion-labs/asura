using System.Collections.ObjectModel;

namespace Asura.App.ViewModels;

/// <summary>
/// Acknowledgements are independent of current operation state. Refreshes and successful
/// retries may update that state, but only the user removes an error from this collection.
/// </summary>
public sealed class ErrorNoticeCollection : ObservableObject
{
    private readonly ObservableCollection<ErrorNoticeViewModel> _items = [];

    public ErrorNoticeCollection() => Items = new ReadOnlyObservableCollection<ErrorNoticeViewModel>(_items);

    public ReadOnlyObservableCollection<ErrorNoticeViewModel> Items { get; }

    public bool HasErrors => _items.Count > 0;

    public void Report(string? message, string? title = null)
    {
        if (string.IsNullOrWhiteSpace(message)
            || _items.Any(item => string.Equals(item.Message, message, StringComparison.Ordinal)
                && string.Equals(item.Title, title, StringComparison.Ordinal)))
        {
            return;
        }

        _items.Add(new ErrorNoticeViewModel(message, title, Dismiss));
        OnPropertyChanged(nameof(HasErrors));
    }

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
