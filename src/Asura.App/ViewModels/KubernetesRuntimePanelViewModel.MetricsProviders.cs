using System.Text.Json;
using Asura.Application;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    private IReadOnlyList<KubernetesMetricsProviderOption> _prometheusProviders = [];
    private KubernetesMetricsProviderOption? _selectedPrometheusProvider;
    private string _metricsProviderStatus = "Looking for an in-cluster Prometheus service.";
    private Task? _providerDiscovery;
    public IReadOnlyList<KubernetesMetricsProviderOption> PrometheusProviders
    {
        get => _prometheusProviders;
        private set
        {
            if (SetProperty(ref _prometheusProviders, value)) { OnPropertyChanged(nameof(HasMetricsProviderChoices)); }
        }
    }
    public bool HasMetricsProviderChoices => PrometheusProviders.Count > 0;
    public bool HasSelectedMetricsProvider => SelectedPrometheusProvider is not null;
    public string MetricsProviderStatus { get => _metricsProviderStatus; private set => SetProperty(ref _metricsProviderStatus, value); }
    public Task ProviderSelectionLoading { get; private set; } = Task.CompletedTask;
    public KubernetesMetricsProviderOption? SelectedPrometheusProvider
    {
        get => _selectedPrometheusProvider;
        set
        {
            if (value is not null && !PrometheusProviders.Contains(value)) { return; }
            if (!SetProperty(ref _selectedPrometheusProvider, value)) { return; }
            OnPropertyChanged(nameof(HasSelectedMetricsProvider));
            MetricsProviderStatus = value is null ? "Choose a Prometheus service to load history." : $"Prometheus · {value.DisplayName} · Kubernetes API proxy";
            ProviderSelectionLoading = RefreshProviderSelectionAsync();
        }
    }
    private async Task RefreshProviderSelectionAsync()
    {
        await RefreshResourceUsageAsync();
        await LoadSelectedHistoryAsync();
    }
    public Task DiscoverMetricsProvidersAsync() => _providerDiscovery ??= DiscoverMetricsProvidersCoreAsync();
    private async Task DiscoverMetricsProvidersCoreAsync()
    {
        if (_session is null || _disposed) { return; }
        var cancellationToken = _lifetime.Token;
        var candidates = new List<KubernetesMetricsProviderOption>();
        string? continuation = null;
        try
        {
            for (int pageNumber = 0; pageNumber < 4; pageNumber++)
            {
                var page = await _session.ListAsync(new(new("", "v1", "services", "Service", true, ["list"]), Limit: 500, ContinueToken: continuation), cancellationToken);
                foreach (var service in page.Items) { candidates.AddRange(FindMetricsProviders(service)); }
                continuation = page.ContinueToken;
                if (string.IsNullOrEmpty(continuation)) { break; }
            }
            if (_disposed) { return; }
            PrometheusProviders = [.. candidates.Distinct().OrderByDescending(item => item.Preferred).ThenBy(item => item.DisplayName, StringComparer.Ordinal)];
            if (PrometheusProviders.Count == 1 && string.IsNullOrEmpty(continuation))
            {
                // Do not invoke the public setter here: discovery can be awaited by the usage refresh itself.
                _selectedPrometheusProvider = PrometheusProviders[0];
                OnPropertyChanged(nameof(SelectedPrometheusProvider));
                OnPropertyChanged(nameof(HasSelectedMetricsProvider));
                MetricsProviderStatus = $"Prometheus · {_selectedPrometheusProvider.DisplayName} · Kubernetes API proxy";
            }
            else { MetricsProviderStatus = PrometheusProviders.Count == 0 ? "No eligible Prometheus service found." : "Choose a Prometheus service; multiple providers or a partial service inventory were found."; }
        }
        catch (OperationCanceledException) { if (!_disposed) { MetricsProviderStatus = "Metrics provider discovery canceled or timed out."; } }
        catch (KubernetesRequestException exception) { if (!_disposed) { MetricsProviderStatus = exception.Code == KubernetesErrorCode.Forbidden ? "Service discovery is forbidden. Ask for service list and proxy access to enable history." : "Prometheus service discovery is unavailable."; } }
    }
    internal static IReadOnlyList<KubernetesMetricsProviderOption> FindMetricsProviders(KubernetesResourceDocument service)
    {
        using var json = JsonDocument.Parse(service.Json);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object) { return []; }
        bool preferred = service.Reference.Name is "prometheus-operated" or "prometheus" or "prometheus-server";
        bool labelled = root.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty("labels", out var labels)
            && labels.ValueKind == JsonValueKind.Object
            && labels.EnumerateObject().Any(label => (label.Name is "app" or "app.kubernetes.io/name") && label.Value.ValueKind == JsonValueKind.String && string.Equals(label.Value.GetString(), "prometheus", StringComparison.Ordinal));
        if ((!preferred && !labelled) || service.Reference.Namespace is null || !root.TryGetProperty("spec", out var spec)
            || spec.ValueKind != JsonValueKind.Object || !spec.TryGetProperty("ports", out var ports) || ports.ValueKind != JsonValueKind.Array) { return []; }
        if (spec.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
            && string.Equals(type.GetString(), "ExternalName", StringComparison.Ordinal)) { return []; }
        var options = new List<KubernetesMetricsProviderOption>();
        foreach (var port in ports.EnumerateArray())
        {
            if (port.ValueKind != JsonValueKind.Object) { continue; }
            if (port.TryGetProperty("protocol", out var protocol) && (protocol.ValueKind != JsonValueKind.String || !string.Equals(protocol.GetString(), "TCP", StringComparison.Ordinal))) { continue; }
            if (!port.TryGetProperty("port", out var number) || number.ValueKind != JsonValueKind.Number || !number.TryGetInt32(out int value) || value is < 1 or > 65535) { continue; }
            string name = port.TryGetProperty("name", out var portName) && portName.ValueKind == JsonValueKind.String ? portName.GetString() ?? "" : "";
            if (value != 9090 && name is not ("http" or "https" or "web" or "http-web")) { continue; }
            options.Add(new(new(service.Reference.Namespace, service.Reference.Name, value, string.Equals(name, "https", StringComparison.Ordinal)), preferred));
        }
        return options;
    }
}

public sealed record KubernetesMetricsProviderOption(KubernetesPrometheusService Service, bool Preferred)
{
    public string DisplayName => $"{Service.Namespace}/{Service.Service}:{Service.Port}";
}
