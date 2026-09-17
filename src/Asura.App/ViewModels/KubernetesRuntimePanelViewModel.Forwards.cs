using System.Globalization;
using System.Text.Json;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    private string _forwardPort = string.Empty;
    private KubernetesForwardWorkspaceState? _forwardState;
    public KubernetesForwardWorkspaceState? ForwardState
    {
        get => _forwardState;
        init { _forwardState = value; value?.Forwards.CollectionChanged += OnForwardsChanged; }
    }
    private void OnForwardsChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasForwardTools)); OnPropertyChanged(nameof(HasForwards)); OnPropertyChanged(nameof(HasInspectorContent));
    }
    public bool HasForwards => ForwardState?.Forwards.Count > 0;
    public bool HasInspectorContent => HasSelection || HasForwards;
    public bool HasForwardTools => CanForward || ForwardState?.Forwards.Count > 0;
    public bool CanForward => (CanReadLogs || IsServiceForward) && ForwardState is not null
        && _session?.Features.HasFlag(Asura.Application.KubernetesSessionFeatures.PortForward) == true;
    public string ForwardPort { get => _forwardPort; set => SetProperty(ref _forwardPort, value); }
    public string DeclaredPorts
    {
        get
        {
            if (_inspection is null) { return string.Empty; }
            using var json = JsonDocument.Parse(_inspection.Json);
            if (!json.RootElement.TryGetProperty("spec", out var spec) || !spec.TryGetProperty("containers", out var containers)) { return string.Empty; }
            var ports = containers.EnumerateArray().Where(item => item.TryGetProperty("ports", out _))
                .SelectMany(item => item.GetProperty("ports").EnumerateArray())
                .Where(item => item.TryGetProperty("containerPort", out _))
                .Select(item => item.GetProperty("containerPort").GetInt32()).Distinct();
            return $"Declared ports: {string.Join(", ", ports)}";
        }
    }
    public async Task StartForwardAsync()
    {
        if (!CanForward || Profile is null || SelectedResource is not { } pod || ForwardState is null || IsBusy) { return; }
        var target = IsServiceForward ? SelectedServiceForward?.Pod : pod.Reference;
        var parsed = int.TryParse(ForwardPort, NumberStyles.None, CultureInfo.InvariantCulture, out var explicitPort);
        var port = IsServiceForward ? SelectedServiceForward?.TargetPort ?? 0 : explicitPort;
        if (target is null || !IsServiceForward && !parsed || port is < 1 or > 65535)
        { Issue = "Choose a concrete Service destination or enter a pod port between 1 and 65535."; return; }
        IsBusy = true;
        Issue = null;
        try { await ForwardState.StartAsync(Profile, target, port); }
        catch (OperationCanceledException) { }
        catch (Asura.Application.KubernetesRequestException exception) { PresentError(exception); }
        catch (IOException) { Issue = "The workspace network route could not open this forward. Check the route and retry."; }
        finally { if (!_disposed) { IsBusy = false; } }
    }
}
