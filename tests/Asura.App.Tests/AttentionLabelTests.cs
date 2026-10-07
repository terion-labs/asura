using System.Globalization;
using Asura.App.Converters;
using Asura.App.ViewModels;

namespace Asura.App.Tests;

public sealed class AttentionLabelTests
{
    [Fact]
    public void Count_folds_the_number_needing_attention_into_the_name()
    {
        Assert.Equal("Workspaces", AttentionLabel.Count("Workspaces", 0));
        Assert.Equal("Workspaces, 1 needs attention", AttentionLabel.Count("Workspaces", 1));
        Assert.Equal("Workspaces, 3 need attention", AttentionLabel.Count("Workspaces", 3));
    }

    [Fact]
    public void Compose_adds_attention_then_any_further_state()
    {
        Assert.Equal("Terminal", AttentionLabel.Compose("Terminal", hasAttention: false));
        Assert.Equal("Terminal, needs attention", AttentionLabel.Compose("Terminal", hasAttention: true));
        Assert.Equal(
            "Terminal, needs attention, Run command",
            AttentionLabel.Compose("Terminal", hasAttention: true, " Run command "));
    }

    [Fact]
    public void Converter_formats_the_name_with_its_parameter()
    {
        var name = AttentionLabelConverter.Instance.Convert(
            ["Terminal", true, string.Empty],
            typeof(string),
            "Restore {0}",
            CultureInfo.InvariantCulture);

        Assert.Equal("Restore Terminal, needs attention", name);
    }
}
