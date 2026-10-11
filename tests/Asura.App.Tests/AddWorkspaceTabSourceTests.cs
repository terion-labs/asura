using Asura.App.ViewModels;
using Asura.App.Views;
using Asura.Core;
using FluentIcons.Common;

namespace Asura.App.Tests;

public sealed class AddWorkspaceTabSourceTests
{
    /// <summary>
    /// Each kind of connection has its own glyph in the Add Tab list, so SSH,
    /// WSL, Docker, and this computer don't all look like the same server.
    /// </summary>
    [Fact]
    public void Each_connection_kind_has_its_own_glyph()
    {
        var symbols = Enum.GetValues<ConnectionKind>()
            .Select(kind => TabSourceRow.ConnectionSymbol(kind))
            .ToArray();

        Assert.Equal(symbols.Length, symbols.Distinct().Count());
        Assert.Equal(Symbol.Server, TabSourceRow.ConnectionSymbol(ConnectionKind.Ssh));
    }

    /// <summary>
    /// A connection row carries no badge — its kind is its detail line — and
    /// filtering still finds it by that kind.
    /// </summary>
    [Fact]
    public void A_connection_row_has_no_badge_and_is_found_by_its_kind()
    {
        var row = new TabSourceRow(
            "production-api",
            "SSH",
            TabSourceRow.ConnectionSymbol(ConnectionKind.Ssh),
            Badge: null,
            new WorkspaceTabSource.Connection(new ConnectionId("production-api")));

        Assert.Null(row.Badge);
        Assert.True(row.Matches("ssh"));
        Assert.False(row.Matches("docker"));
    }
}
