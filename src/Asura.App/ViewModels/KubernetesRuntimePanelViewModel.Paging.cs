using Asura.Application;

namespace Asura.App.ViewModels;

public sealed partial class KubernetesRuntimePanelViewModel
{
    public async Task LoadMoreAsync()
    {
        if (_disposed || IsBusy || !HasMore || _session is null || _loadedRequest is null) { return; }
        var generation = _generation;
        IsBusy = true;
        Issue = null;
        try
        {
            var page = await _session.ListAsync(_loadedRequest with { ContinueToken = _continuation }, _lifetime.Token);
            if (_disposed || generation != _generation) { return; }
            _allResources = [.. _allResources.Concat(page.Items).DistinctBy(item => item.Reference.Uid, StringComparer.Ordinal).Take(10000)];
            _continuation = page.ContinueToken;
            ApplyFilter();
            Status = $"{_allResources.Count} resources{(_allResources.Count >= 10000 ? " · Narrow the namespace or resource type to see more" : "")}";
            OnPropertyChanged(nameof(HasMore));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (KubernetesRequestException exception) { PresentError(exception); }
        finally { if (!_disposed) { IsBusy = false; if (generation == _generation) { StartWatching(); } } }
    }
}
