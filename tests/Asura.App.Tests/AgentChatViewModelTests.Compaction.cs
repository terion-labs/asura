using Asura.App.ViewModels;
using Asura.Application;

namespace Asura.App.Tests;

public sealed partial class AgentChatViewModelTests
{
    [Fact]
    public async Task Manual_compaction_preserves_the_draft_and_prevents_duplicate_clicks()
    {
        var provider = Provider("provider", "Provider", order: 0);
        using var runtime = new StubGovernedRuntime
        {
            Snapshot = Snapshot(providerId: provider.Id) with { CanCompact = true, ContextTokensUsed = 40_000 },
            PendingCompaction = new(TaskCreationOptions.RunContinuationsAsynchronously),
        };
        using var profiles = new StubProfileRuntime { Profiles = [provider] };
        using var viewModel = new AgentChatViewModel(runtime, profiles, ImmediateUiThreadDispatcher.Instance)
        {
            Prompt = "An unsent follow-up",
        };

        Assert.True(viewModel.CompactCommand.CanExecute(null));
        var compaction = viewModel.CompactAsync(CancellationToken.None);
        Assert.True(viewModel.IsCompacting);
        Assert.False(viewModel.CompactCommand.CanExecute(null));
        await viewModel.CompactAsync(CancellationToken.None);
        Assert.Equal(1, runtime.CompactCount);
        Assert.Equal("An unsent follow-up", viewModel.Prompt);

        runtime.Snapshot = runtime.Snapshot with { CanCompact = false, ContextTokensUsed = 4_000 };
        runtime.PendingCompaction.SetResult(new(true, "completed", "Context compacted."));
        await compaction;

        Assert.False(viewModel.IsCompacting);
        Assert.False(viewModel.CanCompact);
        Assert.Equal(4_000, viewModel.ContextUsedTokens);
        Assert.Equal("An unsent follow-up", viewModel.Prompt);
        Assert.Equal("Context compacted.", viewModel.CompactionStatus);
    }
}
