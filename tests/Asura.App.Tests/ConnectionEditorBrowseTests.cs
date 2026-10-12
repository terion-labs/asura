using System.ComponentModel;
using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

/// <summary>
/// Browse for a terminal's repository chooses a local folder with the
/// system's Open panel and an SSH folder with Asura's own browser on the
/// host, which is the only one that can see it.
/// </summary>
public sealed class ConnectionEditorBrowseTests
{
    [Fact]
    public void A_local_repository_is_chosen_with_the_system_open_panel()
    {
        var editor = new ConnectionEditorViewModel(
            new StubConnectionRuntime(),
            gitClient: new FakeGitRepositoryClient())
        {
            Kind = ConnectionKind.Local,
        };

        Assert.True(editor.CanBrowseRepository);
        Assert.True(editor.UsesSystemFolderPicker);
    }

    [Fact]
    public void An_ssh_repository_is_browsed_on_its_host()
    {
        var editor = new ConnectionEditorViewModel(
            new StubConnectionRuntime(),
            gitClient: new FakeGitRepositoryClient())
        {
            Kind = ConnectionKind.Ssh,
            Host = "host.example",
        };

        Assert.True(editor.CanBrowseRepository);
        Assert.False(editor.UsesSystemFolderPicker);
    }

    [Fact]
    public void Changing_the_connection_type_announces_the_picker_change()
    {
        var editor = new ConnectionEditorViewModel(
            new StubConnectionRuntime(),
            gitClient: new FakeGitRepositoryClient())
        {
            Kind = ConnectionKind.Local,
        };
        var changed = new List<string?>();
        ((INotifyPropertyChanged)editor).PropertyChanged +=
            (_, e) => changed.Add(e.PropertyName);

        editor.Kind = ConnectionKind.Ssh;

        Assert.Contains(nameof(ConnectionEditorViewModel.UsesSystemFolderPicker), changed, StringComparer.Ordinal);
        Assert.False(editor.UsesSystemFolderPicker);
    }

    private sealed class StubConnectionRuntime : IConnectionRuntime
    {
        public ValueTask<ConnectionRuntimeResult<ConnectionOpenPlan>> PlanOpenAsync(
            ConnectionProfile profile,
            IProgress<ConnectionProgress>? progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public ValueTask<ConnectionRuntimeResult<ConnectionTestReport>> TestAsync(
            ConnectionProfile profile,
            IProgress<ConnectionProgress>? progress,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
