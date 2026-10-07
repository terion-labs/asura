using System.Reflection;
using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;

namespace Asura.App.Tests;

public sealed class TerminalSettingsViewModelTests
{
    [Fact]
    public void Applying_the_same_catalog_preserves_both_editor_instances()
    {
        var fixture = CreateCatalog(Snapshot());
        using var viewModel = new TerminalSettingsViewModel(fixture.Catalog);
        var terminal = Assert.IsType<TerminalProfileEditorViewModel>(viewModel.TerminalEditor);
        var quickTerminal = Assert.IsType<QuickTerminalSettingsEditorViewModel>(
            viewModel.QuickTerminalEditor);

        viewModel.ApplyCatalog(fixture.Proxy.CurrentSnapshot);

        Assert.Same(terminal, viewModel.TerminalEditor);
        Assert.Same(quickTerminal, viewModel.QuickTerminalEditor);
    }

    [Fact]
    public void A_new_revision_replaces_only_the_changed_editor()
    {
        var snapshot = Snapshot();
        var fixture = CreateCatalog(snapshot);
        using var viewModel = new TerminalSettingsViewModel(fixture.Catalog);
        var terminal = Assert.IsType<TerminalProfileEditorViewModel>(viewModel.TerminalEditor);
        var quickTerminal = Assert.IsType<QuickTerminalSettingsEditorViewModel>(
            viewModel.QuickTerminalEditor);
        var revisedTerminal = snapshot.TerminalProfiles.Single() with { Revision = TerminalRevision + 1 };

        viewModel.ApplyCatalog(snapshot with { TerminalProfiles = [revisedTerminal] });

        Assert.NotSame(terminal, viewModel.TerminalEditor);
        Assert.Equal(TerminalRevision + 1, viewModel.TerminalEditor?.ExpectedRevision);
        Assert.Same(quickTerminal, viewModel.QuickTerminalEditor);
    }

    [Fact]
    public void Forced_discard_rebuilds_dirty_fields_even_at_the_same_revision()
    {
        var fixture = CreateCatalog(Snapshot());
        using var viewModel = new TerminalSettingsViewModel(fixture.Catalog);
        var dirty = Assert.IsType<TerminalProfileEditorViewModel>(viewModel.TerminalEditor);
        dirty.FontFamily = "Unsaved preview";

        viewModel.DiscardTerminalDraft();

        var restored = Assert.IsType<TerminalProfileEditorViewModel>(viewModel.TerminalEditor);
        Assert.NotSame(dirty, restored);
        Assert.Equal("JetBrains Mono", restored.FontFamily);
        Assert.Equal(TerminalRevision, restored.ExpectedRevision);
    }

    [Fact]
    public async Task Terminal_save_forwards_the_editor_revision_and_conflict_keeps_the_draft()
    {
        var fixture = CreateCatalog(Snapshot());
        fixture.Proxy.RejectTerminalSave = true;
        using var viewModel = new TerminalSettingsViewModel(fixture.Catalog);
        var editor = Assert.IsType<TerminalProfileEditorViewModel>(viewModel.TerminalEditor);
        editor.FontFamily = "Unsaved conflict draft";

        var result = await viewModel.SaveTerminalProfileAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DefinitionStoreErrorCode.RevisionConflict, result.Error?.Code);
        Assert.Equal(TerminalRevision, fixture.Proxy.LastExpectedTerminalRevision);
        Assert.Same(editor, viewModel.TerminalEditor);
        Assert.Equal("Unsaved conflict draft", editor.FontFamily);
    }

    [Fact]
    public async Task Unchanged_terminal_save_does_not_write_to_the_catalog()
    {
        var fixture = CreateCatalog(Snapshot());
        using var viewModel = new TerminalSettingsViewModel(fixture.Catalog);

        var result = await viewModel.SaveTerminalProfileAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(TerminalRevision, result.Value?.Revision);
        Assert.Null(fixture.Proxy.LastExpectedTerminalRevision);
    }

    [Fact]
    public async Task Quick_terminal_save_forwards_the_editor_revision_and_conflict_keeps_the_draft()
    {
        var fixture = CreateCatalog(Snapshot());
        fixture.Proxy.RejectQuickTerminalSave = true;
        using var viewModel = new TerminalSettingsViewModel(fixture.Catalog);
        var editor = Assert.IsType<QuickTerminalSettingsEditorViewModel>(
            viewModel.QuickTerminalEditor);
        editor.HeightPercent = 73;

        var result = await viewModel.SaveQuickTerminalSettingsAsync(CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(DefinitionStoreErrorCode.RevisionConflict, result.Error?.Code);
        Assert.Equal(QuickTerminalRevision, fixture.Proxy.LastExpectedQuickTerminalRevision);
        Assert.Same(editor, viewModel.QuickTerminalEditor);
        Assert.Equal(73, editor.HeightPercent);
    }

    [Fact]
    public async Task A_quick_terminal_edit_made_while_its_save_is_in_flight_survives_the_refresh()
    {
        var fixture = CreateCatalog(Snapshot());
        fixture.Proxy.HoldSaves = true;
        using var viewModel = new TerminalSettingsViewModel(fixture.Catalog);
        var editor = Assert.IsType<QuickTerminalSettingsEditorViewModel>(
            viewModel.QuickTerminalEditor);
        editor.HeightPercent = 60;

        var save = viewModel.SaveQuickTerminalSettingsAsync(CancellationToken.None).AsTask();
        editor.HeightPercent = 70;
        // The catalog publishes the stored value before the save returns.
        fixture.Proxy.PublishHeldSave();
        viewModel.ApplyCatalog(fixture.Proxy.CurrentSnapshot);
        fixture.Proxy.ReleaseHeldSave();

        Assert.True((await save).IsSuccess);
        Assert.Same(editor, viewModel.QuickTerminalEditor);
        Assert.Equal(70, editor.HeightPercent);
        Assert.Equal(QuickTerminalRevision + 1, editor.ExpectedRevision);

        fixture.Proxy.HoldSaves = false;
        var second = await viewModel.SaveQuickTerminalSettingsAsync(CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(QuickTerminalRevision + 1, fixture.Proxy.LastExpectedQuickTerminalRevision);
        Assert.Equal(0.70, second.Value!.Value.HeightFraction, 3);
    }

    [Fact]
    public async Task A_terminal_edit_made_while_its_save_is_in_flight_survives_the_refresh()
    {
        var fixture = CreateCatalog(Snapshot());
        fixture.Proxy.HoldSaves = true;
        using var viewModel = new TerminalSettingsViewModel(fixture.Catalog);
        var editor = Assert.IsType<TerminalProfileEditorViewModel>(viewModel.TerminalEditor);
        editor.FontSize = 15;

        var save = viewModel.SaveTerminalProfileAsync(CancellationToken.None).AsTask();
        editor.FontSize = 16;
        // Here the save returns first and the refresh follows it.
        fixture.Proxy.PublishHeldSave();
        fixture.Proxy.ReleaseHeldSave();
        Assert.True((await save).IsSuccess);
        viewModel.ApplyCatalog(fixture.Proxy.CurrentSnapshot);

        Assert.Same(editor, viewModel.TerminalEditor);
        Assert.Equal(16, editor.FontSize);
        Assert.Equal(TerminalRevision + 1, editor.ExpectedRevision);

        fixture.Proxy.HoldSaves = false;
        var second = await viewModel.SaveTerminalProfileAsync(CancellationToken.None);

        Assert.True(second.IsSuccess);
        Assert.Equal(TerminalRevision + 1, fixture.Proxy.LastExpectedTerminalRevision);
        Assert.Equal(16, second.Value!.Value.FontSize);
    }

    [Fact]
    public async Task A_refresh_holding_another_value_still_replaces_the_editor()
    {
        var snapshot = Snapshot();
        var fixture = CreateCatalog(snapshot);
        fixture.Proxy.HoldSaves = true;
        using var viewModel = new TerminalSettingsViewModel(fixture.Catalog);
        var editor = Assert.IsType<QuickTerminalSettingsEditorViewModel>(
            viewModel.QuickTerminalEditor);
        editor.HeightPercent = 60;
        _ = viewModel.SaveQuickTerminalSettingsAsync(CancellationToken.None).AsTask();

        // Something else stored a different value meanwhile.
        var elsewhere = new QuickTerminalSettingsEditorViewModel(
            QuickTerminalSettings.Default,
            QuickTerminalRevision)
        {
            HeightPercent = 40,
        }.CreateSaveRequest().Settings;
        viewModel.ApplyCatalog(snapshot with
        {
            QuickTerminalSettings = [Store(elsewhere, QuickTerminalRevision + 1)],
        });

        var replaced = Assert.IsType<QuickTerminalSettingsEditorViewModel>(
            viewModel.QuickTerminalEditor);
        Assert.NotSame(editor, replaced);
        Assert.Equal(40, replaced.HeightPercent);
    }

    [Fact]
    public void Registration_result_is_forwarded_to_the_live_quick_terminal_editor()
    {
        var fixture = CreateCatalog(Snapshot());
        using var viewModel = new TerminalSettingsViewModel(fixture.Catalog);
        var configured = new KeyStroke("K", KeyModifiers.Meta);
        var failure = new GlobalHotkeyRegistrationResult.Failure(new(
            GlobalHotkeyRegistrationErrorCode.Conflict,
            "global_hotkey_conflict",
            "Another application owns the shortcut."));

        viewModel.ApplyQuickTerminalRegistration(configured, failure);

        var editor = Assert.IsType<QuickTerminalSettingsEditorViewModel>(
            viewModel.QuickTerminalEditor);
        Assert.Contains("Another application", editor.RegistrationStatus);
        Assert.Contains("no global hotkey", editor.RegistrationStatus);
    }

    [Fact]
    public void Disposing_the_owner_releases_both_editor_references()
    {
        var fixture = CreateCatalog(Snapshot());
        var viewModel = new TerminalSettingsViewModel(fixture.Catalog);
        Assert.NotNull(viewModel.TerminalEditor);
        Assert.NotNull(viewModel.QuickTerminalEditor);

        viewModel.Dispose();
        viewModel.Dispose();

        Assert.Null(viewModel.TerminalEditor);
        Assert.Null(viewModel.QuickTerminalEditor);
    }

    private const long TerminalRevision = 42;
    private const long QuickTerminalRevision = 43;

    private static DefinitionCatalogSnapshot Snapshot() =>
        DefinitionCatalogSnapshot.Empty with
        {
            TerminalProfiles = [Store(DefaultTerminalProfile(), TerminalRevision)],
            QuickTerminalSettings =
                [Store(QuickTerminalSettings.Default, QuickTerminalRevision)],
            Keymaps = [Store(BuiltInKeymaps.LinuxTerminal, 2)],
        };

    private static TerminalProfile DefaultTerminalProfile() =>
        new(
            new TerminalProfileId("builtin.terminal.settings-owner"),
            "Settings terminal",
            "JetBrains Mono",
            14,
            1.4,
            TerminalCursorStyle.Block,
            cursorBlink: true,
            100_000,
            TerminalPalette.AsuraDark,
            BuiltInKeymaps.LinuxTerminalId);

    private static StoredDefinition<T> Store<T>(T value, long revision)
        where T : IDurableDefinition =>
        new(value, revision, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    private static CatalogFixture CreateCatalog(DefinitionCatalogSnapshot snapshot)
    {
        var catalog = DispatchProxy.Create<IDefinitionCatalog, RecordingCatalogProxy>();
        var proxy = (RecordingCatalogProxy)(object)catalog;
        proxy.CurrentSnapshot = snapshot;
        return new(catalog, proxy);
    }

    private sealed record CatalogFixture(
        IDefinitionCatalog Catalog,
        RecordingCatalogProxy Proxy);

    public class RecordingCatalogProxy : DispatchProxy
    {
        public DefinitionCatalogSnapshot CurrentSnapshot { get; set; } =
            DefinitionCatalogSnapshot.Empty;

        public bool RejectTerminalSave { get; set; }

        public bool RejectQuickTerminalSave { get; set; }

        public long? LastExpectedTerminalRevision { get; private set; }

        public long? LastExpectedQuickTerminalRevision { get; private set; }

        /// <summary>Keeps each save in flight until the test releases it.</summary>
        public bool HoldSaves { get; set; }

        private (Action Publish, Action Release)? _held;

        public void PublishHeldSave() => _held!.Value.Publish();

        public void ReleaseHeldSave() => _held!.Value.Release();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            args ??= [];
            return targetMethod.Name switch
            {
                "get_Snapshot" => CurrentSnapshot,
                nameof(IDefinitionCatalog.SaveTerminalProfileAsync) => SaveTerminal(
                    (TerminalProfile)args[0]!,
                    (long?)args[1]),
                nameof(IDefinitionCatalog.SaveQuickTerminalSettingsAsync) => SaveQuickTerminal(
                    (QuickTerminalSettings)args[0]!,
                    (long?)args[1]),
                "add_Changed" or "remove_Changed" => null,
                _ => throw new NotSupportedException(targetMethod.Name),
            };
        }

        private ValueTask<DefinitionStoreResult<StoredDefinition<TerminalProfile>>> SaveTerminal(
            TerminalProfile definition,
            long? expectedRevision)
        {
            LastExpectedTerminalRevision = expectedRevision;
            return HoldSaves
                ? Hold(definition, expectedRevision, stored => CurrentSnapshot with { TerminalProfiles = [stored] })
                : Complete(definition, expectedRevision, RejectTerminalSave);
        }

        private ValueTask<DefinitionStoreResult<StoredDefinition<QuickTerminalSettings>>>
            SaveQuickTerminal(
                QuickTerminalSettings definition,
                long? expectedRevision)
        {
            LastExpectedQuickTerminalRevision = expectedRevision;
            return HoldSaves
                ? Hold(definition, expectedRevision, stored => CurrentSnapshot with { QuickTerminalSettings = [stored] })
                : Complete(definition, expectedRevision, RejectQuickTerminalSave);
        }

        private ValueTask<DefinitionStoreResult<StoredDefinition<T>>> Hold<T>(
            T definition,
            long? expectedRevision,
            Func<StoredDefinition<T>, DefinitionCatalogSnapshot> publish)
            where T : IDurableDefinition
        {
            var stored = Store(definition, (expectedRevision ?? 0) + 1);
            var completion = new TaskCompletionSource<DefinitionStoreResult<StoredDefinition<T>>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _held = (
                () => CurrentSnapshot = publish(stored),
                () => completion.SetResult(DefinitionStoreResult<StoredDefinition<T>>.Success(stored)));
            return new(completion.Task);
        }

        private static ValueTask<DefinitionStoreResult<StoredDefinition<T>>> Complete<T>(
            T definition,
            long? expectedRevision,
            bool reject)
            where T : IDurableDefinition =>
            ValueTask.FromResult(reject
                ? DefinitionStoreResult<StoredDefinition<T>>.Failure(new(
                    DefinitionStoreErrorCode.RevisionConflict,
                    "The settings changed before they could be saved.",
                    (expectedRevision ?? 0) + 1))
                : DefinitionStoreResult<StoredDefinition<T>>.Success(
                    Store(definition, (expectedRevision ?? 0) + 1)));
    }
}
