using System.Globalization;
using System.Text.Json;
using Asura.Application;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    private IReadOnlyList<KubernetesServiceForwardChoice> _serviceForwardChoices = [];
    private KubernetesServiceForwardChoice? _selectedServiceForward;
    public bool IsServiceForward => SelectedResource is { Kind: "Service", Reference.Resource: "services" };
    public bool IsPodForward => CanReadLogs;
    public IReadOnlyList<KubernetesServiceForwardChoice> ServiceForwardChoices { get => _serviceForwardChoices; private set => SetProperty(ref _serviceForwardChoices, value); }
    public KubernetesServiceForwardChoice? SelectedServiceForward { get => _selectedServiceForward; set => SetProperty(ref _selectedServiceForward, value); }
    public async Task ResolveServiceForwardAsync()
    {
        if (!CanForward || !IsServiceForward || _inspection is not { } service || _session is null || IsBusy) { return; }
        IsBusy = true;
        Issue = null;
        ServiceForwardChoices = [];
        SelectedServiceForward = null;
        try
        {
            using var json = JsonDocument.Parse(service.Json);
            if (!json.RootElement.TryGetProperty("spec", out var spec) || !spec.TryGetProperty("selector", out var selector)
                || selector.ValueKind != JsonValueKind.Object || !selector.EnumerateObject().Any())
            { Issue = "This Service has no pod selector. Select its backing pod explicitly to forward a port."; return; }
            if (!spec.TryGetProperty("ports", out var ports)) { Issue = "This Service declares no ports."; return; }
            var labelSelector = string.Join(',', selector.EnumerateObject().OrderBy(item => item.Name, StringComparer.Ordinal)
                .Select(item => $"{item.Name}={item.Value.GetString()}"));
            var pods = await _session.ListAsync(new(new("", "v1", "pods", "Pod", true, ["list"]), service.Reference.Namespace, labelSelector), _lifetime.Token);
            if (_disposed || _inspection?.Reference != service.Reference) { return; }
            ServiceForwardChoices = [.. pods.Items.SelectMany(pod => ResolvePodPorts(pod, ports))];
            Status = $"{ServiceForwardChoices.Count} ready pod/port choices{(pods.ContinueToken is not null || pods.IsTruncated ? " · First page only" : "")}. Choose the exact destination.";
            if (ServiceForwardChoices.Count == 0) { Issue = "No ready pod exposes a matching TCP target port for this Service."; }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (KubernetesRequestException exception) { PresentError(exception); }
        finally { if (!_disposed) { IsBusy = false; } }
    }
    internal static IReadOnlyList<KubernetesServiceForwardChoice> ResolvePodPorts(KubernetesResourceDocument pod, JsonElement ports)
    {
        using var json = JsonDocument.Parse(pod.Json);
        var root = json.RootElement;
        if (!root.TryGetProperty("status", out var status) || !status.TryGetProperty("conditions", out var conditions)
            || !conditions.EnumerateArray().Any(item => item.TryGetProperty("type", out var type) && string.Equals(type.GetString(), "Ready", StringComparison.Ordinal)
                && item.TryGetProperty("status", out var state) && string.Equals(state.GetString(), "True", StringComparison.Ordinal))
            || root.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("deletionTimestamp", out var deleting) && deleting.ValueKind != JsonValueKind.Null)
        { return []; }
        List<KubernetesServiceForwardChoice> choices = [];
        foreach (var port in ports.EnumerateArray())
        {
            if (port.TryGetProperty("protocol", out var protocol) && !string.Equals(protocol.GetString(), "TCP", StringComparison.Ordinal)) { continue; }
            var servicePort = port.GetProperty("port").GetInt32();
            var target = port.TryGetProperty("targetPort", out var configured) ? configured : port.GetProperty("port");
            if (target.ValueKind == JsonValueKind.Number)
            { choices.Add(new(pod.Reference, servicePort, target.GetInt32())); continue; }
            if (!root.TryGetProperty("spec", out var spec) || !spec.TryGetProperty("containers", out var containers)) { continue; }
            foreach (var containerPort in containers.EnumerateArray().Where(item => item.TryGetProperty("ports", out _)).SelectMany(item => item.GetProperty("ports").EnumerateArray()))
            {
                if (containerPort.TryGetProperty("name", out var name) && string.Equals(name.GetString(), target.GetString(), StringComparison.Ordinal)
                    && (!containerPort.TryGetProperty("protocol", out var targetProtocol) || string.Equals(targetProtocol.GetString(), "TCP", StringComparison.Ordinal)))
                { choices.Add(new(pod.Reference, servicePort, containerPort.GetProperty("containerPort").GetInt32())); }
            }
        }
        return [.. choices.Distinct()];
    }
}

public sealed record KubernetesServiceForwardChoice(KubernetesResourceReference Pod, int ServicePort, int TargetPort)
{
    public string Label => $"{Pod.Name} · Service {ServicePort.ToString(CultureInfo.InvariantCulture)} → Pod {TargetPort.ToString(CultureInfo.InvariantCulture)}";
}
