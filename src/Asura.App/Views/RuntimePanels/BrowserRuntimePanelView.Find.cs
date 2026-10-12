using Asura.Application;

namespace Asura.App.Views.RuntimePanels;

/// <summary>
/// Edit ▸ Find opens the page's find bar; Find Next and Find Previous step
/// through its matches.
/// </summary>
public sealed partial class BrowserRuntimePanelView : IFindTarget
{
    bool IFindTarget.CanBeginFind => IsEffectivelyVisible;

    bool IFindTarget.CanFindNext => RuntimeBrowser.IsFindVisible;

    Task IFindTarget.BeginFindAsync(string? searchText)
    {
        OpenFind();
        if (searchText is not null)
        {
            FindBox.Text = searchText;
            FindBox.SelectAll();
        }

        return Task.CompletedTask;
    }

    Task IFindTarget.FindNextAsync(FindDirection direction)
    {
        RuntimeBrowser.FindNext(direction == FindDirection.Previous
            ? BrowserFindDirection.Previous
            : BrowserFindDirection.Next);
        return Task.CompletedTask;
    }
}
