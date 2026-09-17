using System.Text.Json;
using Asura.Application;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    private IReadOnlyList<KubernetesMetricsProviderOption> _prometheusProviders = [];
    private KubernetesMetricsProviderOption? _selectedPrometheusProvider;
    private string _metricsProviderStatus = "Looking for an in-cluster metrics service.";
    private Task? _providerDiscovery;
    private bool _providerDiscoverySucceeded;
    private bool _metricsProviderChosenByUser;
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
            if (value is null || !PrometheusProviders.Contains(value)) { return; }
            if (!SetProperty(ref _selectedPrometheusProvider, value)) { return; }
            _metricsProviderChosenByUser = true;
            OnPropertyChanged(nameof(HasSelectedMetricsProvider));
            MetricsProviderStatus = $"{value.Product} · {value.DisplayName}";
            ProviderSelectionLoading = RefreshProviderSelectionAsync();
        }
    }
    private async Task RefreshProviderSelectionAsync()
    {
        await RefreshResourceUsageAsync();
        await LoadSelectedHistoryAsync();
    }
    public Task DiscoverMetricsProvidersAsync()
    {
        if (_providerDiscovery is { IsCompleted: false }) { return _providerDiscovery; }
        if (_providerDiscoverySucceeded) { return Task.CompletedTask; }
        return _providerDiscovery = DiscoverMetricsProvidersCoreAsync();
    }

    private void SelectAutomaticMetricsProvider(KubernetesMetricsProviderOption provider)
    {
        // Discovery and usage loading await each other. Publish the choice without
        // entering the public setter, which starts a fresh usage/history request.
        SetProperty(ref _selectedPrometheusProvider, provider, nameof(SelectedPrometheusProvider));
        OnPropertyChanged(nameof(HasSelectedMetricsProvider));
        MetricsProviderStatus = $"{provider.Product} · {provider.DisplayName}";
    }
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
            _providerDiscoverySucceeded = PrometheusProviders.Count > 0;
            if (PrometheusProviders.Count > 0)
            {
                // Operator-managed clusters commonly expose several Services for
                // the same Prometheus. Usage loading verifies the preferred one
                // and tries the others if it cannot supply measurements.
                SelectAutomaticMetricsProvider(PrometheusProviders[0]);
            }
            else
            {
                SetProperty(ref _selectedPrometheusProvider, null, nameof(SelectedPrometheusProvider));
                OnPropertyChanged(nameof(HasSelectedMetricsProvider));
                MetricsProviderStatus = "No Prometheus or VictoriaMetrics query service found.";
            }
        }
        catch (OperationCanceledException) { if (!_disposed) { MetricsProviderStatus = "Metrics provider discovery canceled or timed out."; } }
        catch (KubernetesRequestException exception) { if (!_disposed) { MetricsProviderStatus = exception.Code == KubernetesErrorCode.Forbidden ? "Metrics discovery requires service list and proxy access." : "Metrics service discovery is unavailable. Refresh to retry."; } }
    }
    internal static IReadOnlyList<KubernetesMetricsProviderOption> FindMetricsProviders(KubernetesResourceDocument service)
    {
        using var json = JsonDocument.Parse(service.Json);
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object) { return []; }
        bool preferred = service.Reference.Name is "prometheus-operated" or "prometheus" or "prometheus-server";
        string[] apps = root.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object && metadata.TryGetProperty("labels", out var labels)
            && labels.ValueKind == JsonValueKind.Object
            ? [.. labels.EnumerateObject().Where(label => (label.Name is "app" or "app.kubernetes.io/name") && label.Value.ValueKind == JsonValueKind.String).Select(label => label.Value.GetString()!)] : [];
        bool vmselect = apps.Contains("vmselect", StringComparer.Ordinal);
        bool vmsingle = apps.Contains("vmsingle", StringComparer.Ordinal) || apps.Contains("victoria-metrics-single", StringComparer.Ordinal);
        bool labelled = apps.Any(app => app is "prometheus" || app.EndsWith("-prometheus", StringComparison.Ordinal));
        if ((!preferred && !labelled && !vmselect && !vmsingle) || service.Reference.Namespace is null || !root.TryGetProperty("spec", out var spec)
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
            int defaultPort = vmselect ? 8481 : vmsingle ? 8428 : 9090;
            if (value != defaultPort && name is not ("http" or "https" or "web" or "http-web")) { continue; }
            options.Add(new(new(service.Reference.Namespace, service.Reference.Name, value, string.Equals(name, "https", StringComparison.Ordinal),
                VictoriaMetricsTenant: vmselect ? "0" : null), preferred, vmselect || vmsingle ? "VictoriaMetrics" : "Prometheus"));
        }
        return options;
    }
}

public sealed record KubernetesMetricsProviderOption(KubernetesPrometheusService Service, bool Preferred, string Product = "Prometheus")
{
    public string DisplayName => $"{Service.Namespace}/{Service.Service}:{Service.Port}"
        + (Service.VictoriaMetricsTenant is { } tenant ? $" · tenant {tenant}" : "");
}
