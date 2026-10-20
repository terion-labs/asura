using System.Reflection;
using Asura.App.ViewModels;
using Asura.Application;
using Asura.Core;
using Avalonia.Headless;
using Avalonia.Threading;

namespace Asura.App.Tests;

/// <summary>
/// The Terminal and Quick Terminal pages apply each change as it is made. A
/// change made while the previous one is still being saved — a spinner held
/// down — must be the one that ends up stored and on screen.
/// </summary>
[Collection(AvaloniaUiCollection.Name)]
public sealed class SettingsAutoApplyTests
{
    private const long TerminalRevision = 52;
    private const long QuickTerminalRevision = 53;

    [Fact]
    public async Task A_quick_terminal_change_made_during_a_save_is_stored_and_kept_on_screen()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            var catalog = SlowCatalogProxy.Create(Snapshot());
            using var viewModel = MainWindowSettingsSaveFacadeTests.CreateSettingsViewModel(catalog.Catalog);
            viewModel.ShowSettings(SettingsPage.QuickTerminal);
            var editor = Assert.IsType<QuickTerminalSettingsEditorViewModel>(
                viewModel.QuickTerminalSettingsEditor);
            catalog.Proxy.HoldSaves = true;

            editor.HeightPercent = 60;
            await PumpAsync();
            Assert.Equal(1, catalog.Proxy.HeldCount);
            editor.HeightPercent = 70;
            await PumpAsync();
            Assert.Equal(1, catalog.Proxy.HeldCount);

            catalog.Proxy.HoldSaves = false;
            catalog.Proxy.ReleaseHeld();
            await PumpAsync();

            var stored = catalog.Proxy.CurrentSnapshot.QuickTerminalSettings.Single();
            Assert.Equal(0.70, stored.Value.HeightFraction, 3);
            Assert.Equal(QuickTerminalRevision + 2, stored.Revision);
            Assert.Same(editor, viewModel.QuickTerminalSettingsEditor);
            Assert.Equal(70, editor.HeightPercent);
            Assert.Equal(stored.Revision, editor.ExpectedRevision);
            Assert.Null(viewModel.OperationError);
            return true;
        }, CancellationToken.None));
    }

    [Fact]
    public async Task A_terminal_change_made_during_a_save_is_stored_and_kept_on_screen()
    {
        await using var session = HeadlessSessions.StartNew(typeof(SqlEditorHeadlessApplication));
        Assert.True(await session.Dispatch(async () =>
        {
            var catalog = SlowCatalogProxy.Create(Snapshot());
            using var viewModel = MainWindowSettingsSaveFacadeTests.CreateSettingsViewModel(catalog.Catalog);
            viewModel.ShowSettings(SettingsPage.Terminal);
            var editor = Assert.IsType<TerminalProfileEditorViewModel>(
                viewModel.TerminalSettingsEditor);
            catalog.Proxy.HoldSaves = true;

            editor.ScrollbackLines = 120_000;
            await PumpAsync();
            Assert.Equal(1, catalog.Proxy.HeldCount);
            editor.ScrollbackLines = 140_000;
            await PumpAsync();

            catalog.Proxy.HoldSaves = false;
            catalog.Proxy.ReleaseHeld();
            await PumpAsync();

            var stored = catalog.Proxy.CurrentSnapshot.TerminalProfiles.Single();
            Assert.Equal(140_000, stored.Value.ScrollbackLines);
            Assert.Equal(TerminalRevision + 2, stored.Revision);
            Assert.Same(editor, viewModel.TerminalSettingsEditor);
            Assert.Equal(140_000, editor.ScrollbackLines);
            Assert.Null(viewModel.OperationError);
            return true;
        }, CancellationToken.None));
    }

    private static async Task PumpAsync()
    {
        for (var pass = 0; pass < 5; pass++)
        {
            Dispatcher.UIThread.RunJobs();
            await Task.Yield();
        }

        Dispatcher.UIThread.RunJobs();
    }

    private static DefinitionCatalogSnapshot Snapshot() =>
        DefinitionCatalogSnapshot.Empty with
        {
            Themes = [Store(ThemePreference.Default, 51)],
            TerminalProfiles =
            [
                Store(
                    new TerminalProfile(
                        new TerminalProfileId("builtin.terminal.auto-apply"),
                        "Auto-apply terminal",
                        "JetBrains Mono",
                        14,
                        1.4,
                        TerminalCursorStyle.Block,
                        cursorBlink: true,
                        100_000,
                        TerminalPalette.AsuraDark,
                        BuiltInKeymaps.LinuxTerminalId),
                    TerminalRevision),
            ],
            QuickTerminalSettings = [Store(QuickTerminalSettings.Default, QuickTerminalRevision)],
        };

    private static StoredDefinition<T> Store<T>(T value, long revision)
        where T : IDurableDefinition =>
        new(value, revision, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch);

    /// <summary>
    /// A catalog whose saves can be held in flight. Like the real one, a save
    /// checks the revision when it lands and announces the new snapshot
    /// before it returns.
    /// </summary>
    public class SlowCatalogProxy : DispatchProxy
    {
        private readonly Queue<Action> _held = [];
        private EventHandler? _changed;

        public DefinitionCatalogSnapshot CurrentSnapshot { get; set; } = DefinitionCatalogSnapshot.Empty;

        public bool HoldSaves { get; set; }

        public int HeldCount => _held.Count;

        public static (IDefinitionCatalog Catalog, SlowCatalogProxy Proxy) Create(DefinitionCatalogSnapshot snapshot)
        {
            var catalog = DispatchProxy.Create<IDefinitionCatalog, SlowCatalogProxy>();
            var proxy = (SlowCatalogProxy)(object)catalog;
            proxy.CurrentSnapshot = snapshot;
            return (catalog, proxy);
        }

        public void ReleaseHeld()
        {
            while (_held.TryDequeue(out var complete))
            {
                complete();
            }
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            ArgumentNullException.ThrowIfNull(targetMethod);
            args ??= [];
            switch (targetMethod.Name)
            {
                case "get_Snapshot":
                    return CurrentSnapshot;
                case "add_Changed":
                    _changed += (EventHandler?)args[0];
                    return null;
                case "remove_Changed":
                    _changed -= (EventHandler?)args[0];
                    return null;
                case nameof(IDefinitionCatalog.SaveTerminalProfileAsync):
                    return Save(
                        (TerminalProfile)args[0]!,
                        (long?)args[1],
                        () => CurrentSnapshot.TerminalProfiles.Single().Revision,
                        stored => CurrentSnapshot with { TerminalProfiles = [stored] });
                case nameof(IDefinitionCatalog.SaveQuickTerminalSettingsAsync):
                    return Save(
                        (QuickTerminalSettings)args[0]!,
                        (long?)args[1],
                        () => CurrentSnapshot.QuickTerminalSettings.Single().Revision,
                        stored => CurrentSnapshot with { QuickTerminalSettings = [stored] });
                default:
                    throw new NotSupportedException(targetMethod.Name);
            }
        }

        private ValueTask<DefinitionStoreResult<StoredDefinition<T>>> Save<T>(
            T definition,
            long? expectedRevision,
            Func<long> currentRevision,
            Func<StoredDefinition<T>, DefinitionCatalogSnapshot> publish)
            where T : IDurableDefinition
        {
            var completion = new TaskCompletionSource<DefinitionStoreResult<StoredDefinition<T>>>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            void Complete()
            {
                var current = currentRevision();
                if (expectedRevision != current)
                {
                    completion.SetResult(DefinitionStoreResult<StoredDefinition<T>>.Failure(new(
                        DefinitionStoreErrorCode.RevisionConflict,
                        "The settings changed before they could be saved.",
                        current)));
                    return;
                }

                var stored = Store(definition, current + 1);
                CurrentSnapshot = publish(stored);
                _changed?.Invoke(this, EventArgs.Empty);
                completion.SetResult(DefinitionStoreResult<StoredDefinition<T>>.Success(stored));
            }

            if (HoldSaves)
            {
                _held.Enqueue(Complete);
            }
            else
            {
                Complete();
            }

            return new(completion.Task);
        }
    }
}
