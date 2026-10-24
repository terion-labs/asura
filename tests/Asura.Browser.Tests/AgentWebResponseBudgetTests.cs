using Exclr8Cef;

namespace Asura.Browser.Tests;

public sealed class AgentWebResponseBudgetTests
{
    [Fact]
    public void Decoded_bodies_share_one_budget_across_responses()
    {
        using var budget = new AgentWebResponseBudget();
        var input = new byte[64 * 1024];
        var output = new byte[input.Length];
        for (var index = 0; index < AgentWebResponseBudget.MaximumDecodedBytes / input.Length; index++)
        {
            if (index % 16 == 0) { Assert.True(budget.BeginResponse()); }
            Assert.Equal(Cef.ResponseFilterStatus.NeedMoreData,
                budget.Copy(input, output, out var read, out var written));
            Assert.Equal(input.Length, read);
            Assert.Equal(input.Length, written);
        }
        Assert.True(budget.BeginResponse());
        output[0] = 99;
        Assert.Equal(Cef.ResponseFilterStatus.Error, budget.Copy([1], output, out var consumed, out var emitted));
        Assert.Equal(0, consumed);
        Assert.Equal(0, emitted);
        Assert.Equal(99, output[0]);
        Assert.True(budget.Exceeded);
    }

    [Fact]
    public void Partial_output_is_charged_only_as_consumed_and_preserves_content()
    {
        using var budget = new AgentWebResponseBudget();
        var output = new byte[2];
        Assert.Equal(Cef.ResponseFilterStatus.NeedMoreData,
            budget.Copy([1, 2, 3], output, out var read, out var written));
        Assert.Equal(2, read);
        Assert.Equal(2, written);
        Assert.Equal(new byte[] { 1, 2 }, output);
        Assert.Equal(Cef.ResponseFilterStatus.Done, budget.Copy([], output, out _, out _));
        Assert.False(budget.Exceeded);
    }

    [Fact]
    public void Excessive_resource_count_stops_even_empty_responses()
    {
        using var budget = new AgentWebResponseBudget();
        for (var index = 0; index <= AgentWebResponseBudget.MaximumResponses; index++)
        {
            Assert.True(budget.BeginResponse());
        }
        Assert.True(budget.Exceeded);
        Assert.Equal(Cef.ResponseFilterStatus.Error, budget.Copy([], [], out _, out _));
    }
}
