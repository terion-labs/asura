using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Asura.App.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    private ErrorNoticeCollection? _errorNotices;

    /// <summary>Allocated only for owners that report or display errors.</summary>
    public ErrorNoticeCollection ErrorNotices => _errorNotices ??= new();

    protected void ReportError(string? message, string? title = null)
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            ErrorNotices.Report(message, title);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetProperty<T>(
        ref T field,
        T value,
        [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
