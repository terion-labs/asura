using System.Windows.Input;
using Asura.Application;
using Asura.Core;

namespace Asura.App.ViewModels;

/// <summary>Reviews configuration in the chosen backend without executing its credential helper.</summary>
public sealed class KubernetesConnectionEditorViewModel : ObservableObject
{
    private readonly KubernetesConnectionProfile? _existing;
    private readonly Func<KubernetesConnectionProfile, CancellationToken, ValueTask<KubernetesConfigurationReview>>? _review;
    private readonly AsyncActionCommand _reviewCommand;
    private string _name;
    private string _path;
    private string _context;
    private string _namespace;
    private string? _trustedFingerprint;
    private IReadOnlyList<KubernetesContextReview> _contexts = [];
    private KubernetesContextReview? _selectedContext;
    private string _reviewStatus = "Review the file to choose a context and inspect its authentication.";
    private bool _busy;

    public KubernetesConnectionEditorViewModel(KubernetesConnectionProfile? existing = null,
        Func<KubernetesConnectionProfile, CancellationToken, ValueTask<KubernetesConfigurationReview>>? review = null)
    {
        _existing = existing;
        _review = review;
        _name = existing?.Name ?? string.Empty;
        _path = existing?.KubeconfigPath ?? (existing?.ManagedKubeconfigSecret is null ? "~/.kube/config" : string.Empty);
        _context = existing?.ContextName ?? string.Empty;
        _namespace = existing?.DefaultNamespace ?? "default";
        _trustedFingerprint = existing?.TrustedExecFingerprint;
        _reviewCommand = new(ReviewAsync, () => _review is not null && !IsReviewing);
    }

    public bool IsEditing => _existing is not null;
    public KubernetesConnectionProfileId? ExistingId => _existing?.Id;
    public string Name { get => _name; set => SetProperty(ref _name, value); }
    public string KubeconfigPath
    {
        get => _path;
        set { if (SetProperty(ref _path, value)) { ClearReview(); } }
    }
    public string ContextName
    {
        get => _context;
        set { if (SetProperty(ref _context, value)) { _trustedFingerprint = null; if (!string.Equals(_selectedContext?.ContextName, value, StringComparison.Ordinal)) { SelectedContext = null; } OnPropertyChanged(nameof(TrustCredentialCommand)); } }
    }
    public string Namespace { get => _namespace; set => SetProperty(ref _namespace, value); }
    public ICommand ReviewCommand => _reviewCommand;
    public bool IsReviewing { get => _busy; private set { SetProperty(ref _busy, value); _reviewCommand.RaiseCanExecuteChanged(); } }
    public string ReviewStatus { get => _reviewStatus; private set => SetProperty(ref _reviewStatus, value); }
    public IReadOnlyList<KubernetesContextReview> Contexts { get => _contexts; private set => SetProperty(ref _contexts, value); }
    public KubernetesContextReview? SelectedContext
    {
        get => _selectedContext;
        set
        {
            if (!SetProperty(ref _selectedContext, value)) { return; }
            ContextName = value?.ContextName ?? ContextName;
            Namespace = value?.DefaultNamespace ?? Namespace;
            OnPropertyChanged(nameof(HasCredentialCommand)); OnPropertyChanged(nameof(CredentialCommandDescription));
            OnPropertyChanged(nameof(TrustCredentialCommand)); OnPropertyChanged(nameof(ContextDescription));
        }
    }
    public string ContextDescription => SelectedContext is { } context
        ? $"{context.ApiServer} · {context.Authentication}{(context.InsecureTls ? " · TLS verification disabled in configuration" : "")}" : string.Empty;
    public bool HasCredentialCommand => SelectedContext?.ExecFingerprint is not null;
    public string CredentialCommandDescription => SelectedContext is { ExecFingerprint: not null } context
        ? $"{context.ExecCommand} {string.Join(' ', context.ExecArguments)}\nEnvironment names: {string.Join(", ", context.ExecEnvironmentNames)}\nFingerprint: {context.ExecFingerprint}" : string.Empty;
    public bool TrustCredentialCommand
    {
        get => SelectedContext?.ExecFingerprint is { } fingerprint && string.Equals(_trustedFingerprint, fingerprint, StringComparison.Ordinal);
        set { _trustedFingerprint = value ? SelectedContext?.ExecFingerprint : null; OnPropertyChanged(); }
    }

    public async Task ReviewAsync()
    {
        if (_review is null || IsReviewing) { return; }
        var path = KubeconfigPath;
        IsReviewing = true;
        try
        {
            var result = await _review(BuildProfile(), CancellationToken.None);
            if (!string.Equals(path, KubeconfigPath, StringComparison.Ordinal)) { return; }
            Contexts = result.Contexts;
            SelectedContext = Contexts.FirstOrDefault(item => string.Equals(item.ContextName, ContextName, StringComparison.Ordinal));
            ReviewStatus = $"{Contexts.Count} contexts found. Review does not run credential commands or connect.";
        }
        catch (KubernetesRequestException exception) { ReviewStatus = exception.Message; }
        catch (NotSupportedException) { ReviewStatus = "Configuration review is unavailable in this execution environment."; }
        finally { IsReviewing = false; }
    }

    public KubernetesConnectionProfile CreateProfile()
    {
        var profile = BuildProfile();
        if (!profile.Validate().IsValid)
        {
            throw new ArgumentException("Enter a name, kubeconfig path, explicit context and valid namespace.");
        }
        return profile;
    }

    private KubernetesConnectionProfile BuildProfile() => new(_existing?.Id ?? KubernetesConnectionProfileId.New(),
        KubernetesConnectionProfile.CurrentSchemaVersion, Name.Trim(), string.IsNullOrWhiteSpace(KubeconfigPath) ? null : KubeconfigPath.Trim(),
        ContextName.Trim(), Namespace.Trim(), _existing?.TunnelConnectionId,
        string.IsNullOrWhiteSpace(KubeconfigPath) ? _existing?.ManagedKubeconfigSecret : null, trustedExecFingerprint: _trustedFingerprint);

    private void ClearReview()
    {
        _trustedFingerprint = null;
        Contexts = [];
        SelectedContext = null;
        ReviewStatus = "Configuration changed. Review it before trusting a credential command.";
    }
}
