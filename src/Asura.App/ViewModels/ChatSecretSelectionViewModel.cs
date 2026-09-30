using Asura.Core;

namespace Asura.App.ViewModels;

public sealed class ChatSecretSelectionViewModel(ChatHiddenReference reference, string label) : ObservableObject
{
    private bool _include;
    public ChatHiddenReference Reference { get; } = reference;
    public string Label { get; } = label;
    public bool Include
    {
        get => _include;
        set => SetProperty(ref _include, value);
    }
}
