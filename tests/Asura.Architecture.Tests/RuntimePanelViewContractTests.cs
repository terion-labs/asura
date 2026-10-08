using System.Xml.Linq;
using Asura.Testing;

namespace Asura.Architecture.Tests;

public sealed class RuntimePanelViewContractTests
{
    private static readonly ApplicationViewCatalog ApplicationViews =
        ApplicationViewCatalog.Load();

    [Fact]
    public void Docker_files_embed_the_file_viewer_instead_of_declaring_another_browser()
    {
        var root = Assert.IsType<XElement>(
            XDocument.Load(RuntimePanelPath("DockerRuntimePanelView", ".axaml")).Root);
        var sharedViewer = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "FileRuntimePanelView", StringComparison.Ordinal));

        Assert.Equal("True", AttributeValue(sharedViewer, "IsEmbedded"));
        Assert.DoesNotContain(
            root.DescendantsAndSelf().Attributes(),
            attribute => attribute.Value.Contains("Docker files and folders", StringComparison.Ordinal)
                || attribute.Value.Contains("DockerFile", StringComparison.Ordinal));
        Assert.False(File.Exists(Path.Combine(
            ApplicationViews.RepositoryRoot,
            "src",
            "Asura.App",
            "ViewModels",
            "DockerFileBrowserViewModel.cs")));
    }

    [Fact]
    public void Docker_json_uses_the_shared_highlighted_code_preview()
    {
        var root = Assert.IsType<XElement>(
            XDocument.Load(RuntimePanelPath("DockerRuntimePanelView", ".axaml")).Root);
        var preview = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "CodePreviewView"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Text"), "{Binding Inspection.Json}", StringComparison.Ordinal));

        Assert.Equal("inspection.json", AttributeValue(preview, "FileName"));
        Assert.Equal("False", AttributeValue(preview, "WordWrap"));
        Assert.DoesNotContain(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "TextBox"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Text"), "{Binding Inspection.Json}", StringComparison.Ordinal));
    }

    [Fact]
    public void Docker_image_navigation_uses_archive_icons()
    {
        var root = Assert.IsType<XElement>(
            XDocument.Load(RuntimePanelPath("DockerRuntimePanelView", ".axaml")).Root);
        var imageButtons = root.Descendants()
            .Where(element => element.Name.LocalName is "ListBoxItem" or "RadioButton")
            .Where(element =>
                element.Descendants().Any(descendant => string.Equals(descendant.Name.LocalName, "TextBlock"
, StringComparison.Ordinal) && string.Equals(AttributeValue(descendant, "Text"), "Images", StringComparison.Ordinal))
                || string.Equals(AttributeValue(element, "ToolTip.Tip"), "Images", StringComparison.Ordinal))
            .ToArray();

        Assert.NotEmpty(imageButtons);
        Assert.All(
            imageButtons,
            button => Assert.Contains(
                button.Descendants(),
                element => string.Equals(element.Name.LocalName, "SymbolIcon"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Symbol"), "Archive", StringComparison.Ordinal)));
    }

    [Fact]
    public void Docker_logs_use_a_virtualized_paged_surface_with_remote_controls()
    {
        var root = Assert.IsType<XElement>(
            XDocument.Load(RuntimePanelPath("DockerRuntimePanelView", ".axaml")).Root);
        var logList = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "ListBox"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Name"), "LogList", StringComparison.Ordinal));

        Assert.Contains(
            logList.Descendants(),
            element => string.Equals(element.Name.LocalName, "VirtualizingStackPanel", StringComparison.Ordinal));
        // The log search is a search field that runs on Return, not a text
        // box beside an accent Search button.
        var search = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "SearchField"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "PlaceholderText"), "Search all container logs", StringComparison.Ordinal));
        Assert.Equal("OnLogSearchKeyDown", AttributeValue(search, "KeyDown"));
        Assert.DoesNotContain(
            root.Descendants(),
            element => AttributeValue(element, "Click") is "OnLogSearchClick" or "OnClearLogSearchClick");
        // Following the log is a toolbar state, so a toggle button: a switch
        // is for a setting in a window's body.
        Assert.DoesNotContain(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "ToggleSwitch", StringComparison.Ordinal));
        var follow = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "ToggleButton"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "AutomationProperties.Name"), "Follow container logs", StringComparison.Ordinal));
        Assert.Equal("{Binding FollowLogs, Mode=TwoWay}", AttributeValue(follow, "IsChecked"));
        Assert.Contains(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Button"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "AutomationProperties.Name")
, "Download all container logs", StringComparison.Ordinal));
    }

    [Fact]
    public void Docker_container_actions_use_accessible_icons_and_name_the_terminal_action_shell()
    {
        var root = Assert.IsType<XElement>(
            XDocument.Load(RuntimePanelPath("DockerRuntimePanelView", ".axaml")).Root);
        var actions = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "StackPanel"
, StringComparison.Ordinal) && HasClass(element, "DockerContainerActions"));
        var expectedActions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{Binding StartCommand}"] = "Play",
            ["{Binding StopCommand}"] = "Stop",
            ["{Binding RestartCommand}"] = "ArrowClockwise",
            ["{Binding PauseCommand}"] = "Pause",
            ["{Binding ResumeCommand}"] = "PauseOff",
        };
        var expectedEnabledBindings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["{Binding StartCommand}"] = "{Binding CanStartSelectedContainer}",
            ["{Binding StopCommand}"] = "{Binding CanStopSelectedContainer}",
            ["{Binding RestartCommand}"] = "{Binding CanRestartSelectedContainer}",
            ["{Binding PauseCommand}"] = "{Binding CanPauseSelectedContainer}",
            ["{Binding ResumeCommand}"] = "{Binding CanResumeSelectedContainer}",
        };

        foreach (var (command, symbol) in expectedActions)
        {
            var button = Assert.Single(
                actions.Descendants(),
                element => string.Equals(element.Name.LocalName, "Button"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Command"), command, StringComparison.Ordinal));
            Assert.True(HasClass(button, "IconButton"));
            Assert.NotNull(AttributeValue(button, "ToolTip.Tip"));
            Assert.NotNull(AttributeValue(button, "AutomationProperties.Name"));
            Assert.Equal(
                expectedEnabledBindings[command],
                AttributeValue(button, "IsEnabled"));
            Assert.Contains(
                button.Descendants(),
                element => string.Equals(element.Name.LocalName, "SymbolIcon"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Symbol"), symbol, StringComparison.Ordinal));
        }

        var actionToggles = actions.Elements()
            .Where(element => string.Equals(element.Name.LocalName, "Grid"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Width"), "32"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Height"), "32", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, actionToggles.Length);
        var startStopToggle = Assert.Single(
            actionToggles,
            element => element.Descendants().Any(button => string.Equals(AttributeValue(button, "Command"), "{Binding StartCommand}", StringComparison.Ordinal)));
        Assert.Equal(2, startStopToggle.Elements().Count(element => string.Equals(element.Name.LocalName, "Button", StringComparison.Ordinal)));
        Assert.Contains(
            startStopToggle.Elements(),
            element => string.Equals(AttributeValue(element, "Command"), "{Binding StartCommand}"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "IsVisible")
, "{Binding SelectedContainerIsStopped}", StringComparison.Ordinal));
        Assert.Contains(
            startStopToggle.Elements(),
            element => string.Equals(AttributeValue(element, "Command"), "{Binding StopCommand}"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "IsVisible")
, "{Binding SelectedContainerIsActive}", StringComparison.Ordinal));

        var pauseToggle = Assert.Single(
            actionToggles,
            element => element.Descendants().Any(button => string.Equals(AttributeValue(button, "Command"), "{Binding PauseCommand}", StringComparison.Ordinal)));
        Assert.Equal(2, pauseToggle.Elements().Count(element => string.Equals(element.Name.LocalName, "Button", StringComparison.Ordinal)));
        Assert.Contains(
            pauseToggle.Elements(),
            element => string.Equals(AttributeValue(element, "Command"), "{Binding PauseCommand}"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "IsVisible")
, "{Binding !SelectedResource.IsPaused}", StringComparison.Ordinal));
        Assert.Contains(
            pauseToggle.Elements(),
            element => string.Equals(AttributeValue(element, "Command"), "{Binding ResumeCommand}"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "IsVisible")
, "{Binding SelectedResource.IsPaused}", StringComparison.Ordinal));

        // The lifecycle actions are one toolbar group — a capsule around the
        // icons, as a Mac toolbar groups related items — not separately
        // bordered squares, each with its own surface.
        var group = Assert.IsType<XElement>(actions.Parent);
        Assert.Equal("Border", group.Name.LocalName);
        Assert.True(HasClass(group, "ToolbarGroup"));
        Assert.Equal("{Binding SelectedResource.IsContainer}", AttributeValue(group, "IsVisible"));
        Assert.All(
            actions.Descendants().Where(element => string.Equals(element.Name.LocalName, "Button", StringComparison.Ordinal)),
            button => Assert.True(HasClass(button, "ToolbarGroupAction")));
        Assert.DoesNotContain(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Style", StringComparison.Ordinal)
                && AttributeValue(element, "Selector")?.Contains("DockerToolbarAction", StringComparison.Ordinal) == true);

        // "Shell" is named once, as a view. The toolbar keeps only the move of
        // that shell into a tab of its own, as a plain icon shown with the
        // shell — it was an accent "Shell" button beside a "Shell" tab.
        var shell = Assert.Single(
            actions.Elements(),
            element => string.Equals(element.Name.LocalName, "Button"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Click"), "OnOpenShellClick", StringComparison.Ordinal));
        Assert.True(HasClass(shell, "IconButton"));
        Assert.False(HasClass(shell, "PrimaryButton"));
        Assert.Equal("{Binding IsShellDetail}", AttributeValue(shell, "IsVisible"));
        Assert.Equal("Open in New Tab", AttributeValue(shell, "ToolTip.Tip"));
        Assert.Contains(
            shell.Descendants(),
            element => string.Equals(element.Name.LocalName, "SymbolIcon"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Symbol"), "Open", StringComparison.Ordinal));
        Assert.DoesNotContain(
            shell.Descendants(),
            element => string.Equals(element.Name.LocalName, "TextBlock", StringComparison.Ordinal));
        Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "TextBlock"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Text"), "Shell", StringComparison.Ordinal));
        Assert.DoesNotContain(
            root.Descendants(),
            element => HasClass(element, "PrimaryButton"));
    }

    [Fact]
    public void Docker_volume_size_loading_is_visible_and_accessible()
    {
        var root = Assert.IsType<XElement>(
            XDocument.Load(RuntimePanelPath("DockerRuntimePanelView", ".axaml")).Root);
        var indicator = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "ProgressBar"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "IsVisible")
, "{Binding ShowResourceProgress}", StringComparison.Ordinal));

        Assert.True(HasClass(indicator, "WorkingStripe"));
        Assert.Equal("Bottom", AttributeValue(indicator, "VerticalAlignment"));
        Assert.Equal(
            "Loading Docker resources",
            AttributeValue(indicator, "AutomationProperties.Name"));
        Assert.DoesNotContain(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "ProgressBar"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "IsVisible"), "{Binding ShowLoading}", StringComparison.Ordinal));
    }

    /// <summary>
    /// The selection's views are one segmented control of five stable
    /// segments, each a radio button so the current one is announced as
    /// selected, and dimmed rather than removed when it does not apply. Where
    /// they do not fit they become a pop-up button whose menu checks the
    /// current view, rather than a menu of buttons marking it with a class.
    /// </summary>
    [Fact]
    public void Docker_detail_views_are_a_stable_segmented_control_with_a_checked_pop_up()
    {
        var root = Assert.IsType<XElement>(
            XDocument.Load(RuntimePanelPath("DockerRuntimePanelView", ".axaml")).Root);
        var segments = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "SegmentedControl"
, StringComparison.Ordinal) && HasClass(element, "DockerDetailTabs"));
        Assert.Equal("{Binding DetailIndex, Mode=OneWay}", AttributeValue(segments, "SelectedIndex"));
        Assert.Equal("OnDetailSegmentSelectionChanged", AttributeValue(segments, "SelectionChanged"));
        Assert.Equal("Resource view", AttributeValue(segments, "AutomationProperties.Name"));
        var tabs = segments.Elements()
            .Where(element => string.Equals(element.Name.LocalName, "RadioButton", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(
            ["Info", "Logs", "Shell", "Files", "JSON"],
            tabs.Select(tab => AttributeValue(tab, "AutomationProperties.Name")),
            StringComparer.Ordinal);
        Assert.InRange(tabs.Length, 2, 5);
        Assert.All(
            tabs,
            tab =>
            {
                Assert.Null(AttributeValue(tab, "IsVisible"));
                Assert.Null(AttributeValue(tab, "Click"));
                Assert.Contains(
                    tab.Descendants(),
                    element => string.Equals(element.Name.LocalName, "SymbolIcon", StringComparison.Ordinal));
                Assert.Contains(
                    tab.Descendants(),
                    element => string.Equals(element.Name.LocalName, "TextBlock"
, StringComparison.Ordinal) && HasClass(element, "DockerTabLabel"));
            });
        Assert.Equal(
            [null, "{Binding CanShowLogs}", "{Binding CanShowShell}", "{Binding CanShowFiles}", "{Binding CanShowJson}"],
            tabs.Select(tab => AttributeValue(tab, "IsEnabled")),
            StringComparer.Ordinal);
        Assert.DoesNotContain(
            root.Descendants(),
            element => (AttributeValue(element, "Classes.selected") is not null
                    && !HasClass(element, "DockerResourceRow"))
                || AttributeValue(element, "Classes.active") is not null);

        var menuButton = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Button"
, StringComparison.Ordinal) && HasClass(element, "DockerDetailMenuButton"));
        Assert.Equal("Resource view", AttributeValue(menuButton, "AutomationProperties.Name"));
        Assert.Contains(
            menuButton.Descendants(),
            element => string.Equals(element.Name.LocalName, "TextBlock"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Text"), "{Binding DetailTitle}", StringComparison.Ordinal));
        var menu = Assert.Single(
            menuButton.Descendants(),
            element => string.Equals(element.Name.LocalName, "MenuFlyout", StringComparison.Ordinal));
        var menuItems = menu.Elements()
            .Where(element => string.Equals(element.Name.LocalName, "MenuItem", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(
            ["Info", "Logs", "Shell", "Files", "JSON"],
            menuItems.Select(item => AttributeValue(item, "Header")),
            StringComparer.Ordinal);
        Assert.All(menuItems, item =>
        {
            Assert.Equal("Radio", AttributeValue(item, "ToggleType"));
            Assert.NotNull(AttributeValue(item, "IsChecked"));
            Assert.NotNull(AttributeValue(item, "Click"));
            Assert.Null(AttributeValue(item, "IsVisible"));
        });

        var hideTabsStyle = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Style"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Selector")
, "Grid.compactDetails controls|SegmentedControl.DockerDetailTabs", StringComparison.Ordinal));
        Assert.Contains(
            hideTabsStyle.Elements(),
            element => string.Equals(element.Name.LocalName, "Setter"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Property"), "IsVisible"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Value"), "False", StringComparison.Ordinal));
        var showMenuStyle = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Style"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Selector")
, "Grid.compactDetails Button.DockerDetailMenuButton.hasSelection", StringComparison.Ordinal));
        Assert.Contains(
            showMenuStyle.Elements(),
            element => string.Equals(element.Name.LocalName, "Setter"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Property"), "IsVisible"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Value"), "True", StringComparison.Ordinal));

        // The narrow resource-type switcher is the same control, not a row of
        // icon buttons wearing a class.
        var compactSections = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "SegmentedControl"
, StringComparison.Ordinal) && HasClass(element, "DockerCompactSections"));
        Assert.Equal("{Binding SectionIndex, Mode=TwoWay}", AttributeValue(compactSections, "SelectedIndex"));
        Assert.Equal(4, compactSections.Elements().Count(element => string.Equals(element.Name.LocalName, "RadioButton", StringComparison.Ordinal)));
    }

    [Fact]
    public void Docker_family_rail_is_wide_and_aligns_count_chips_to_its_right_edge()
    {
        var root = Assert.IsType<XElement>(
            XDocument.Load(RuntimePanelPath("DockerRuntimePanelView", ".axaml")).Root);
        var railStyle = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Style"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Selector"), "Border.DockerFamilyRail", StringComparison.Ordinal));
        var width = Assert.Single(
            railStyle.Elements(),
            element => string.Equals(element.Name.LocalName, "Setter"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Property"), "Width", StringComparison.Ordinal));
        Assert.Equal("180", AttributeValue(width, "Value"));

        var navigationStyle = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Style"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Selector"), "ListBox.DockerFamilyList ListBoxItem.DockerNav", StringComparison.Ordinal));
        var horizontalAlignment = Assert.Single(
            navigationStyle.Elements(),
            element => string.Equals(element.Name.LocalName, "Setter"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Property"), "HorizontalAlignment", StringComparison.Ordinal));
        Assert.Equal("Stretch", AttributeValue(horizontalAlignment, "Value"));

        // The resource types are the rows of a list, so the current one is a
        // selected row a screen reader can find.
        var navigationList = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "ListBox"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Name"), "DockerFamilyNavigation", StringComparison.Ordinal));
        Assert.Equal("{Binding SectionIndex, Mode=TwoWay}", AttributeValue(navigationList, "SelectedIndex"));
        Assert.Equal("Single,AlwaysSelected", AttributeValue(navigationList, "SelectionMode"));
        var navigationButtons = navigationList.Elements()
            .Where(element => string.Equals(element.Name.LocalName, "ListBoxItem"
, StringComparison.Ordinal) && HasClass(element, "DockerNav"))
            .ToArray();
        Assert.Equal(4, navigationButtons.Length);
        Assert.All(navigationButtons, item => Assert.NotNull(AttributeValue(item, "AutomationProperties.Name")));
        Assert.All(
            navigationButtons,
            button =>
            {
                var count = Assert.Single(
                    button.Descendants(),
                    element => string.Equals(element.Name.LocalName, "StatusChip", StringComparison.Ordinal));
                Assert.Null(AttributeValue(count, "Width"));
                Assert.Equal("28", AttributeValue(count, "MinWidth"));
                Assert.Equal("Right", AttributeValue(count, "HorizontalAlignment"));
                Assert.All(
                    button.Descendants().Where(element => element.Name.LocalName is
                        "SymbolIcon" or "TextBlock" or "StatusChip"),
                    element => Assert.Equal(
                        "Center",
                        AttributeValue(element, "VerticalAlignment")));
            });
    }

    [Fact]
    public void Docker_typography_uses_scaled_line_heights_and_inspection_values_wrap()
    {
        var root = Assert.IsType<XElement>(
            XDocument.Load(RuntimePanelPath("DockerRuntimePanelView", ".axaml")).Root);

        var explicitlySizedText = root.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "TextBlock", StringComparison.Ordinal))
            .Select(element => new
            {
                Element = element,
                FontSize = AttributeValue(element, "FontSize") ?? string.Empty,
            })
            .Where(item => item.FontSize.StartsWith(
                "{DynamicResource ShellFontSize",
                StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(explicitlySizedText);
        Assert.All(
            explicitlySizedText,
            item => Assert.Equal(
                item.FontSize.Replace("ShellFontSize", "ShellLineHeight", StringComparison.Ordinal),
                AttributeValue(item.Element, "LineHeight")));

        var propertyValue = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "TextBlock"
, StringComparison.Ordinal) && HasClass(element, "DockerPropertyValue"));
        Assert.Equal("Wrap", AttributeValue(propertyValue, "TextWrapping"));
        Assert.Equal(
            "{DynamicResource ShellLineHeight12}",
            AttributeValue(propertyValue, "LineHeight"));
        Assert.Null(AttributeValue(propertyValue, "TextTrimming"));
    }

    [Fact]
    public void Docker_vertical_collections_use_adaptive_full_width_item_containers()
    {
        var docker = Assert.IsType<XElement>(
            XDocument.Load(RuntimePanelPath("DockerRuntimePanelView", ".axaml")).Root);
        var verticalCollections = docker.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "ItemsControl", StringComparison.Ordinal))
            .Where(element => AttributeValue(element, "ItemsSource") is
                "{Binding ContainerStacks}"
                or "{Binding Containers}"
                or "{Binding Inspection.Properties}")
            .ToArray();

        Assert.Equal(3, verticalCollections.Length);
        Assert.All(verticalCollections, collection =>
            Assert.True(HasClass(collection, "StretchItems")));

        var stackHeaderBackground = Assert.Single(
            docker.Descendants(),
            element => string.Equals(element.Name.LocalName, "Style"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Selector"), "Border.DockerStackHeaderSurface", StringComparison.Ordinal));
        Assert.Contains(
            stackHeaderBackground.Elements(),
            element => string.Equals(element.Name.LocalName, "Setter"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Property"), "Background"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Value")
, "{DynamicResource ShellSurfaceHoverBrush}", StringComparison.Ordinal));
        // A group heading is neutral. The accent outline every header wore is
        // gone; the accent marks what is selected or pressed, nothing else.
        Assert.DoesNotContain(
            stackHeaderBackground.Elements(),
            element => string.Equals(element.Name.LocalName, "Setter"
, StringComparison.Ordinal) && AttributeValue(element, "Value")?.Contains("Accent", StringComparison.Ordinal) == true);
        Assert.Contains(
            stackHeaderBackground.Elements(),
            element => string.Equals(element.Name.LocalName, "Setter"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Property"), "BorderThickness"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Value"), "0", StringComparison.Ordinal));

        var stackHeader = Assert.Single(
            docker.Descendants(),
            element => string.Equals(element.Name.LocalName, "Grid"
, StringComparison.Ordinal) && HasClass(element, "DockerStackHeader"));
        Assert.Equal(
            "{controls:Inset Horizontal=Sm, Vertical=Xs}",
            AttributeValue(stackHeader, "Margin"));
        Assert.Equal("Auto,*,Auto", AttributeValue(stackHeader, "ColumnDefinitions"));
        var stackToggle = Assert.Single(
            stackHeader.Elements(),
            element => string.Equals(element.Name.LocalName, "Button"
, StringComparison.Ordinal) && HasClass(element, "DockerStackToggle"));
        Assert.Equal("0", AttributeValue(stackToggle, "Grid.Column"));
        var stackActionsHost = Assert.Single(
            stackHeader.Elements(),
            element => string.Equals(element.Name.LocalName, "StackPanel"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Grid.Column"), "2", StringComparison.Ordinal));
        Assert.Equal("Right", AttributeValue(stackActionsHost, "HorizontalAlignment"));
        Assert.DoesNotContain(
            stackHeader.Elements(),
            element => string.Equals(element.Name.LocalName, "Border"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Width"), "2", StringComparison.Ordinal));
        var resourceRowTemplate = Assert.Single(
            docker.Descendants(),
            element => string.Equals(element.Name.LocalName, "DataTemplate"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Key"), "DockerResourceItemTemplate", StringComparison.Ordinal));
        var resourceRow = Assert.Single(
            resourceRowTemplate.Descendants(),
            element => string.Equals(element.Name.LocalName, "ListItem"
, StringComparison.Ordinal) && HasClass(element, "DockerResourceListItem"));
        Assert.Equal("{DynamicResource ShellListRowPadding}", AttributeValue(resourceRow, "ContentPadding"));
        Assert.Equal("{Binding Title}", AttributeValue(resourceRow, "Title"));
        Assert.Equal("{Binding Subtitle}", AttributeValue(resourceRow, "Detail"));
        Assert.Equal("{Binding Tertiary}", AttributeValue(resourceRow, "Metadata"));

        var containerResourceRow = Assert.Single(
            docker.Descendants(),
            element => string.Equals(element.Name.LocalName, "Button"
, StringComparison.Ordinal) && HasClass(element, "DockerResourceRow"));
        Assert.Equal(
            "{StaticResource DockerResourceItemTemplate}",
            AttributeValue(containerResourceRow, "ContentTemplate"));
        var flatResourceList = Assert.Single(
            docker.Descendants(),
            element => string.Equals(element.Name.LocalName, "ListBox"
, StringComparison.Ordinal) && HasClass(element, "DockerResources"));
        Assert.Equal(
            "{StaticResource DockerResourceItemTemplate}",
            AttributeValue(flatResourceList, "ItemTemplate"));
        Assert.DoesNotContain(
            docker.Descendants(),
            element => HasClass(element, "DockerStackRowContent"));

        // Each of a stack's buttons names the stack it acts on, in its tooltip
        // and to a screen reader, and is a full-size target rather than the
        // compact one.
        var stackActions = stackActionsHost.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "Button", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(5, stackActions.Length);
        Assert.Equal(
            [
                "{Binding Name, StringFormat='Start {0}'}",
                "{Binding Name, StringFormat='Stop {0}'}",
                "{Binding Name, StringFormat='Restart {0}'}",
                "{Binding Name, StringFormat='Pause {0}'}",
                "{Binding Name, StringFormat='Resume {0}'}",
            ],
            stackActions.Select(button => AttributeValue(button, "AutomationProperties.Name")), StringComparer.Ordinal);
        Assert.All(stackActions, button =>
        {
            Assert.StartsWith("{Binding Name, StringFormat=", AttributeValue(button, "ToolTip.Tip"), StringComparison.Ordinal);
            Assert.True(HasClass(button, "IconButton"));
            Assert.False(HasClass(button, "Compact"));
        });
        Assert.Equal("{Binding ToggleLabel}", AttributeValue(stackToggle, "AutomationProperties.Name"));
        var stackActionToggles = stackActionsHost.Elements()
            .Where(element => string.Equals(element.Name.LocalName, "Grid"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Width"), "28"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Height"), "28", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(2, stackActionToggles.Length);
        var stackStartStopToggle = Assert.Single(
            stackActionToggles,
            element => element.Descendants().Any(button => string.Equals(AttributeValue(button, "Click"), "OnStartStackClick", StringComparison.Ordinal)));
        Assert.Contains(
            stackStartStopToggle.Elements(),
            element => string.Equals(AttributeValue(element, "Click"), "OnStartStackClick"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "IsVisible"), "{Binding !CanStop}", StringComparison.Ordinal));
        Assert.Contains(
            stackStartStopToggle.Elements(),
            element => string.Equals(AttributeValue(element, "Click"), "OnStopStackClick"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "IsVisible"), "{Binding CanStop}", StringComparison.Ordinal));
        var stackPauseResumeToggle = Assert.Single(
            stackActionToggles,
            element => element.Descendants().Any(button => string.Equals(AttributeValue(button, "Click"), "OnPauseStackClick", StringComparison.Ordinal)));
        Assert.Contains(
            stackPauseResumeToggle.Elements(),
            element => string.Equals(AttributeValue(element, "Click"), "OnPauseStackClick"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "IsVisible"), "{Binding CanPause}", StringComparison.Ordinal));
        Assert.Contains(
            stackPauseResumeToggle.Elements(),
            element => string.Equals(AttributeValue(element, "Click"), "OnResumeStackClick"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "IsVisible"), "{Binding !CanPause}", StringComparison.Ordinal));

        var statusTiles = docker.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "IdentityTile"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "StatusToneBrush.Tint"), "{Binding StatusTone}", StringComparison.Ordinal))
            .ToArray();
        Assert.Single(statusTiles);
        Assert.DoesNotContain(
            docker.Descendants(),
            element => string.Equals(element.Name.LocalName, "Ellipse"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "StatusToneBrush.Fill"), "{Binding StatusTone}", StringComparison.Ordinal));

        var resourceRowStyle = Assert.Single(
            docker.Descendants(),
            element => string.Equals(element.Name.LocalName, "Style"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Selector"), "Button.DockerResourceRow", StringComparison.Ordinal));
        Assert.Contains(
            resourceRowStyle.Elements(),
            element => string.Equals(element.Name.LocalName, "Setter"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Property"), "Margin"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Value")
, "{controls:Inset Horizontal=Sm}", StringComparison.Ordinal));
        Assert.Contains(
            resourceRowStyle.Elements(),
            element => string.Equals(element.Name.LocalName, "Setter"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Property"), "HorizontalAlignment"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Value"), "Stretch", StringComparison.Ordinal));

        var designSystem = Assert.IsType<XElement>(
            XDocument.Load(Path.Combine(
                ApplicationViews.RepositoryRoot,
                "src",
                "Asura.App",
                "Styles",
                "DesignSystem.axaml")).Root);
        var generatedItemStyle = Assert.Single(
            designSystem.Descendants(),
            element => string.Equals(element.Name.LocalName, "Style"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Selector")
, "ItemsControl.StretchItems > ContentPresenter", StringComparison.Ordinal));
        Assert.Contains(
            generatedItemStyle.Elements(),
            element => string.Equals(element.Name.LocalName, "Setter"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Property"), "HorizontalAlignment"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Value"), "Stretch", StringComparison.Ordinal));
    }

    [Fact]
    public void List_selection_surfaces_keep_one_consistent_side_gutter()
    {
        var theme = Assert.IsType<XElement>(
            XDocument.Load(Path.Combine(
                ApplicationViews.RepositoryRoot,
                "src",
                "Asura.App",
                "Styles",
                "AsuraTheme.axaml")).Root);

        foreach (var selector in new[] { "ListBoxItem", "Button.ListRow" })
        {
            var style = Assert.Single(
                theme.Descendants(),
                element => string.Equals(element.Name.LocalName, "Style"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Selector"), selector, StringComparison.Ordinal));
            Assert.Contains(
                style.Elements(),
                element => string.Equals(element.Name.LocalName, "Setter"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Property"), "Margin"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Value")
, "{controls:Inset Horizontal=Xs}", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// Every panel wears the same chrome, and it comes from the component rather
    /// than from the view.
    ///
    /// It used to be written out in each of these eight views, and had drifted the
    /// way duplicated markup does. The browser declared its split buttons with no
    /// <c>Grid.Column</c>, so they rendered stacked on top of its status dot where
    /// nobody could press them — the panel looked as though it simply had no split
    /// controls. Its two split icons were also swapped relative to the other seven,
    /// and two views set a header height the other six did not.
    ///
    /// None of that is reachable from a view any more, which is what this asserts.
    /// </summary>
    [Theory]
    [InlineData("TerminalRuntimePanelView")]
    [InlineData("BrowserRuntimePanelView")]
    [InlineData("FileRuntimePanelView")]
    [InlineData("StatisticsRuntimePanelView")]
    [InlineData("ProcessMonitorRuntimePanelView")]
    [InlineData("DatabaseRuntimePanelView")]
    [InlineData("RedisRuntimePanelView")]
    [InlineData("UnavailableRuntimePanelView")]
    [InlineData("PanelPlaceholderView")]
    public void Panels_take_their_chrome_from_the_component_rather_than_drawing_it(
        string viewName)
    {
        var root = Assert.IsType<XElement>(
            XDocument.Load(RuntimePanelPath(viewName, ".axaml")).Root);
        var chrome = Assert.Single(
            root.Elements(),
            element => string.Equals(element.Name.LocalName, "PanelChrome", StringComparison.Ordinal));

        Assert.Equal("OnCloseClick", AttributeValue(chrome, "CloseRequested"));
        Assert.Equal("OnSplitRequested", AttributeValue(chrome, "SplitRequested"));
        Assert.Equal("{Binding IsActive}", AttributeValue(chrome, "IsActive"));
        Assert.Equal(
            "{Binding IsAgentActive}",
            AttributeValue(chrome, "IsAgentActive"));
        Assert.Equal("{Binding IsZoomed}", AttributeValue(chrome, "IsZoomed"));
        Assert.NotNull(AttributeValue(chrome, "Title"));

        foreach (var owned in new[] { "PanelDockHandle", "SurfaceCard" })
        {
            Assert.DoesNotContain(
                root.Elements(),
                element => string.Equals(element.Name.LocalName, owned, StringComparison.Ordinal));
        }

        Assert.DoesNotContain(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Border"
, StringComparison.Ordinal) && HasClass(element, "PanelHeader"));

        // Nor the other end of it. Two panels drew the same footer by hand —
        // surface fill, a hairline along the top, muted nine-point text — which
        // is how the header started before it was one component. A footer is a
        // slot now, and the browser needs it to be exactly the strip the
        // component draws: its page covers everything else the panel has.
        //
        // Along the bottom of the panel itself, which a strip inside some pane of
        // it is not — the file panel's preview toggles sit on the same hairline
        // and are nobody's footer.
        Assert.DoesNotContain(
            chrome.Elements().SelectMany(content => content.Elements()),
            element => string.Equals(element.Name.LocalName, "Border"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "BorderThickness"), "0,1,0,0"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Background")
, "{DynamicResource ShellSurfaceBrush}", StringComparison.Ordinal));
        Assert.DoesNotContain(
            root.Descendants(),
            element => AttributeValue(element, "AutomationProperties.Name")
                is "Split this panel left and right"
                or "Split this panel top and bottom");
    }

    /// <summary>
    /// And the component draws it: the card, the title as Dock's drag surface so
    /// panels need no second tab strip, and split/split/close in that order.
    /// </summary>
    [Fact]
    public void The_panel_component_owns_the_card_the_drag_handle_and_the_shared_actions()
    {
        var theme = Assert.Single(
            DesignSystem().Descendants(),
            element => string.Equals(element.Name.LocalName, "ControlTheme"
, StringComparison.Ordinal) && string.Equals(
                    AttributeValue(element, "TargetType"),
                    "controls:PanelChrome",
                    StringComparison.Ordinal));

        var card = Assert.Single(
            theme.Descendants(),
            element => string.Equals(element.Name.LocalName, "SurfaceCard", StringComparison.Ordinal));
        Assert.Equal("Panel", AttributeValue(card, "Tone"));
        Assert.Equal("True", AttributeValue(card, "ClipsContent"));
        Assert.Equal(
            "{TemplateBinding IsActive}",
            AttributeValue(card, "Classes.active"));

        // Title and passive status regions are drag surfaces. They need no hand-set data
        // context: a handle declared in a template cannot know where it will be
        // used, so PanelDockHandle finds its dockable by walking to the ancestor
        // that holds one.
        var handles = theme.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "PanelDockHandle", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(
            ["PART_StatusDragHandle", "PART_TitleDragHandle", "PART_FooterDragHandle"],
            handles.Select(element => AttributeValue(element, "Name")),
            StringComparer.Ordinal);
        Assert.All(handles, handle => Assert.Null(AttributeValue(handle, "DataContext")));
        foreach (var (index, slot) in new[] { (0, "Status"), (2, "Footer") })
        {
            var presenter = Assert.Single(handles[index].Elements());
            Assert.Equal("ContentPresenter", presenter.Name.LocalName);
            Assert.Equal($"{{TemplateBinding {slot}}}", AttributeValue(presenter, "Content"));
            Assert.Equal("{TemplateBinding DataContext}", AttributeValue(presenter, "DataContext"));
        }

        var handleSource = File.ReadAllText(Path.Combine(
            ApplicationViews.RepositoryRoot,
            "src",
            "Asura.App",
            "Controls",
            "PanelDockHandle.cs"));
        // It points itself at that dockable. Dock reads the drag source's own
        // data context to decide what is being dragged, so a handle whose context
        // is something else is not a drag surface at all. Moving the handle into
        // the component dropped the binding each view used to set by hand, and
        // nothing failed — panels just stopped being draggable.
        Assert.Contains(
            "Bind(DataContextProperty, host.GetObservable(DataContextProperty))",
            handleSource,
            StringComparison.Ordinal);

        var actions = theme
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "Button", StringComparison.Ordinal))
            .Select(element => AttributeValue(element, "Name") ?? string.Empty)
            .ToArray();
        Assert.Equal(
            [
                "PART_Collapse",
                "PART_Expand",
                "PART_Float",
                "PART_Dock",
                "PART_SplitLeftRight",
                "PART_SplitTopBottom",
                "PART_Overflow",
                "PART_Close",
            ],
            actions);

        // Float and dock are one control in two states, so exactly one of them is
        // ever offered. Floating used to be a double-click on the title — nothing
        // announced it, and nothing undid it.
        var floatOut = theme.Descendants().Single(element => string.Equals(AttributeValue(element, "Name"), "PART_Float", StringComparison.Ordinal));
        var dockBack = theme.Descendants().Single(element => string.Equals(AttributeValue(element, "Name"), "PART_Dock", StringComparison.Ordinal));
        Assert.Equal("{TemplateBinding CanFloat}", AttributeValue(floatOut, "IsVisible"));
        Assert.Equal("{TemplateBinding IsFloating}", AttributeValue(dockBack, "IsVisible"));

        // A panel says what it is doing along its bottom, and for one holding an
        // operating-system view that strip is the only part of the panel the
        // shell still draws — such a view is composited above every Avalonia
        // pixel. Anything the browser panel needs seen or pressed lives there,
        // including the corner a floating panel is resized from, so the footer
        // is load-bearing rather than decorative.
        var footer = Assert.Single(
            theme.Descendants(),
            element => string.Equals(element.Name.LocalName, "Border"
, StringComparison.Ordinal) && HasClass(element, "PanelFooter"));
        Assert.Equal("{TemplateBinding IsFooterVisible}", AttributeValue(footer, "IsVisible"));

        var browser = XDocument.Load(RuntimePanelPath("BrowserRuntimePanelView", ".axaml"));
        Assert.Contains(
            browser.Descendants(),
            element => string.Equals(element.Name.LocalName, "PanelChrome.Footer", StringComparison.Ordinal));

        // The browser drew the two splits the other way round for as long as it
        // drew them itself.
        var splits = theme
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "SymbolIcon", StringComparison.Ordinal))
            .Select(element => AttributeValue(element, "Symbol") ?? string.Empty)
            .ToArray();
        Assert.Equal(
            [
                "Subtract",
                "{TemplateBinding ExpansionIcon}",
                "WindowMultiple",
                "WindowMultipleOff",
                "SplitVertical",
                "SplitHorizontal",
                "MoreHorizontal",
                "Dismiss",
            ],
            splits);

        Assert.DoesNotContain(
            theme.Descendants(),
            element => string.Equals(element.Name.LocalName, "SymbolIcon"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Symbol"), "Bot", StringComparison.Ordinal));

        var agentGlow = Assert.Single(
            theme.Descendants(),
            element => string.Equals(element.Name.LocalName, "Border"
, StringComparison.Ordinal) && HasClass(element, "PanelAgentGlow"));
        Assert.Equal(
            "{TemplateBinding IsAgentActive}",
            AttributeValue(agentGlow, "IsVisible"));
        Assert.Equal(
            "{DynamicResource ShellAgentPanelGlowShadow}",
            AttributeValue(agentGlow, "BoxShadow"));
        Assert.Equal(
            "{DynamicResource ShellAgentPanelGlowBrush}",
            AttributeValue(agentGlow, "Background"));
        Assert.Null(AttributeValue(agentGlow, "Margin"));
        Assert.Equal(
            "{Binding CornerRadius, ElementName=PART_PanelSurface}",
            AttributeValue(agentGlow, "CornerRadius"));

        var chrome = File.ReadAllText(Path.Combine(
            ApplicationViews.RepositoryRoot,
            "src",
            "Asura.App",
            "Controls",
            "PanelChrome.cs"));
        // Raised as the chrome, not as the button inside its template: the shell
        // resolves which panel to act on from the sender's data context, and the
        // chrome's is the panel.
        Assert.Contains(
            "CloseRequested?.Invoke(this, e);",
            chrome,
            StringComparison.Ordinal);
        Assert.Contains(
            "SplitRequested?.Invoke(this, PanelSplitOrientation.LeftRight);",
            chrome,
            StringComparison.Ordinal);
        // Float is asked for, not done. A panel floats inside the shell's window
        // — a panel holding an operating-system view cannot change window without
        // that view being destroyed — so which panels are floating is the
        // workspace's to know, and the chrome only says which one asked.
        Assert.Contains(
            "RaiseEvent(new RoutedEventArgs(FloatToggleRequestedEvent, this));",
            chrome,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsFloating = FloatingPanelLayer.For(this) is not null;",
            chrome,
            StringComparison.Ordinal);
        Assert.DoesNotContain("FloatDockable", chrome, StringComparison.Ordinal);

        // Nothing floats on a gesture nobody can see.
        Assert.DoesNotContain("DoubleTapped", handleSource, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "TerminalRuntimePanelViewModel",
        "TerminalRuntimePanelView")]
    [InlineData(
        "BrowserRuntimePanelViewModel",
        "BrowserRuntimePanelView")]
    [InlineData(
        "FileRuntimePanelViewModel",
        "FileRuntimePanelView")]
    [InlineData(
        "StatisticsRuntimePanelViewModel",
        "StatisticsRuntimePanelView")]
    [InlineData(
        "ProcessMonitorRuntimePanelViewModel",
        "ProcessMonitorRuntimePanelView")]
    [InlineData(
        "DatabaseRuntimePanelViewModel",
        "DatabaseRuntimePanelView")]
    [InlineData(
        "RedisRuntimePanelViewModel",
        "RedisRuntimePanelView")]
    [InlineData(
        "UnavailableRuntimePanelViewModel",
        "UnavailableRuntimePanelView")]
    public void Main_window_templates_delegate_runtime_interaction_to_named_panel_views(
        string viewModel,
        string panelView)
    {
        var mainWindow = LoadView("MainWindow");
        var template = Assert.Single(
            mainWindow.Descendants(),
            element => string.Equals(element.Name.LocalName, "DataTemplate"
, StringComparison.Ordinal) && string.Equals(
                    AttributeValue(element, "DataType"),
                    $"vm:{viewModel}",
                    StringComparison.Ordinal));
        var component = Assert.Single(template.Elements());

        Assert.Equal(panelView, component.Name.LocalName);
        Assert.Equal(
            "OnRuntimePanelGotFocus",
            AttributeValue(component, "GotFocus"));
        Assert.Equal(
            "OnRuntimePanelPointerPressed",
            AttributeValue(component, "PointerPressed"));
        Assert.Equal(
            "OnCloseRuntimePanelClick",
            AttributeValue(component, "CloseRequested"));
        Assert.DoesNotContain(
            template.Descendants(),
            element => string.Equals(element.Name.LocalName, "SurfaceCard", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(
        "TerminalRuntimePanelView",
        null,
        "Close panel")]
    [InlineData(
        "BrowserRuntimePanelView",
        null,
        "Close Browser panel")]
    [InlineData(
        "FileRuntimePanelView",
        null,
        "Close Files panel")]
    [InlineData(
        "StatisticsRuntimePanelView",
        "Statistics panel",
        "Close Statistics panel")]
    [InlineData(
        "ProcessMonitorRuntimePanelView",
        "Processes panel",
        "Close Processes panel")]
    [InlineData(
        "DatabaseRuntimePanelView",
        "Database panel",
        "Close Database panel")]
    [InlineData(
        "RedisRuntimePanelView",
        "Redis panel",
        "Close Redis panel")]
    [InlineData(
        "UnavailableRuntimePanelView",
        null,
        "Close panel")]
    public void Runtime_panel_views_preserve_focus_layout_and_close_contracts(
        string panelView,
        string? accessibleName,
        string closeName)
    {
        var document = LoadRuntimePanelView(panelView);
        var root = Assert.IsType<XElement>(document.Root);

        Assert.Equal("UserControl", root.Name.LocalName);
        Assert.True(HasClass(root, "RuntimePanelFocusTarget"));
        Assert.Equal("True", AttributeValue(root, "Focusable"));
        Assert.Equal("Stretch", AttributeValue(root, "HorizontalContentAlignment"));
        Assert.Equal("Stretch", AttributeValue(root, "VerticalContentAlignment"));
        Assert.Equal(
            "{Binding IsVisibleInLayout}",
            AttributeValue(root, "IsVisible"));
        Assert.Equal(
            accessibleName,
            AttributeValue(root, "AutomationProperties.Name"));

        // A panel's chrome is the shared component, which draws the card on the
        // panel surface — the same one the workspaces sidebar uses. What closing
        // this particular panel is called is the one part of it the panel says.
        var chrome = Assert.Single(
            root.Elements(),
            element => string.Equals(element.Name.LocalName, "PanelChrome", StringComparison.Ordinal));
        Assert.Equal(closeName, AttributeValue(chrome, "CloseLabel") ?? "Close panel");
        Assert.Equal("OnCloseClick", AttributeValue(chrome, "CloseRequested"));

        var codeBehind = File.ReadAllText(RuntimePanelPath(panelView, ".axaml.cs"));
        Assert.Contains(
            "public event EventHandler<RoutedEventArgs>? CloseRequested;",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains(
            "CloseRequested?.Invoke(sender, e);",
            codeBehind,
            StringComparison.Ordinal);
        if (string.Equals(
                panelView,
                "FileRuntimePanelView",
                StringComparison.Ordinal))
        {
            // The file view creates and owns its transient HTML renderer visual,
            // so detaching the visual must release that renderer lifetime.
            Assert.Contains("ReleaseHtmlPreview();", codeBehind, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("Dispose(", codeBehind, StringComparison.Ordinal);
        }
        Assert.DoesNotContain("CancellationTokenSource", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Redis_panel_uses_the_database_toolbar_and_flat_workspace_geometry()
    {
        var root = Assert.IsType<XElement>(LoadRuntimePanelView("RedisRuntimePanelView").Root);
        // One connection button that says what it will do, never a Connect
        // and a Disconnect enabled side by side.
        var connection = Assert.Single(
            root.Descendants(),
            element => string.Equals(AttributeValue(element, "Command"), "{Binding ToggleConnectionCommand}", StringComparison.Ordinal));
        Assert.Equal("{Binding ConnectionActionLabel}", AttributeValue(connection, "Content"));
        Assert.Equal("{Binding ConnectionActionDescription}", AttributeValue(connection, "AutomationProperties.Name"));
        Assert.DoesNotContain(
            root.Descendants(),
            element => AttributeValue(element, "Command") is "{Binding ConnectCommand}" or "{Binding DisconnectCommand}");
        Assert.Equal("26", AttributeValue(connection, "Height"));
        Assert.Equal("{controls:Inset Horizontal=Md}", AttributeValue(connection, "Padding"));
        Assert.Equal("Center", AttributeValue(connection, "VerticalAlignment"));
        Assert.Equal("Center", AttributeValue(connection, "VerticalContentAlignment"));
        Assert.True(HasClass(connection, "SecondaryButton"));

        // Runtime errors live in the shared panel chrome, independently of refreshed content.
        Assert.Single(root.Descendants(), element => element.Name.LocalName == "PanelChrome");
        var notices = Assert.Single(DesignSystem().Descendants(),
            element => element.Name.LocalName == "ErrorNoticesView");
        Assert.Equal(
            "{CompiledBinding $parent[controls:PanelChrome].((vm:ObservableObject)DataContext).ErrorNotices}",
            AttributeValue(notices, "DataContext"));
        var error = Assert.Single(LoadView("Components/ErrorNoticesView").Descendants(),
            element => element.Name.LocalName == "Callout");
        Assert.Equal("Danger", AttributeValue(error, "Tone"));
        Assert.Equal("{Binding Message}", AttributeValue(error, "Text"));
        Assert.Contains(error.Descendants(), element => element.Name.LocalName == "Button"
            && AttributeValue(element, "Click") == "OnDismissClick");

        var browser = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Grid"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "IsVisible"), "{Binding ShowBrowser}", StringComparison.Ordinal));
        Assert.Equal("320,5,*", AttributeValue(browser, "ColumnDefinitions"));
        Assert.Contains(
            browser.Elements(),
            element => string.Equals(element.Name.LocalName, "GridSplitter"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "AutomationProperties.Name")
, "Resize the Redis keys list", StringComparison.Ordinal));
        Assert.DoesNotContain(
            browser.Elements(),
            element => string.Equals(element.Name.LocalName, "SurfaceCard"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Tone"), "Panel", StringComparison.Ordinal));
    }

    /// <summary>
    /// Redis is a database panel, so it states what it is showing and who it is
    /// talking to in the same footer band the database workspace carries, and
    /// its value tables are the shell's one table rather than stock grids.
    /// </summary>
    [Fact]
    public void Redis_panel_shares_the_database_footer_band_and_table()
    {
        var root = Assert.IsType<XElement>(LoadRuntimePanelView("RedisRuntimePanelView").Root);

        Assert.Contains(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "PanelChrome.Footer", StringComparison.Ordinal));

        var grids = root
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "DataGrid", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(grids);
        foreach (var grid in grids)
        {
            Assert.True(
                HasClass(grid, "DatabaseGrid"),
                "Every Redis value table must wear the shared DatabaseGrid chrome.");
        }

        // A row of a list is the kit's row. Hand-rolled grids are how two
        // lists in the same product stopped agreeing on their own geometry.
        foreach (var list in root.Descendants().Where(element => string.Equals(element.Name.LocalName, "ListBox", StringComparison.Ordinal)))
        {
            Assert.True(
                HasClass(list, "Rows"),
                "A Redis list is a list of rows and takes its chrome from the theme.");
            var template = Assert.Single(
                list.Descendants(),
                element => string.Equals(element.Name.LocalName, "DataTemplate", StringComparison.Ordinal));
            Assert.Equal("ListItem", Assert.Single(template.Elements()).Name.LocalName);
        }

        // The create sheet is a form, so it names every input with the kit's
        // field: a placeholder leaves the moment it is used. Sitting in the
        // field is the structure; that it actually names the control for a
        // screen reader is checked against the control's automation peer in
        // RedisRuntimePanelViewHeadlessTests. The two other
        // shapes are deliberately not held to this — a repeated row is named
        // once by the group above it, and the panel's dense bars (the value
        // editor's footer, the Pub/Sub column) state their options inline the
        // way the log toolbar does.
        // The sheet is a region of the panel, not a popup: a flyout is a window
        // of its own and can be drawn outside the frame it belongs to.
        Assert.DoesNotContain(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Flyout", StringComparison.Ordinal));
        var sheet = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "SurfaceCard"
, StringComparison.Ordinal) && element.Descendants().Any(child => string.Equals(AttributeValue(child, "AutomationProperties.Name"), "Create Redis key", StringComparison.Ordinal)));

        foreach (var input in sheet
                     .Descendants()
                     .Where(element => element.Name.LocalName is "TextBox" or "ComboBox")
                     .Where(element => AttributeValue(element, "PlaceholderText") is null))
        {
            Assert.True(
                input.Ancestors().Any(parent => string.Equals(parent.Name.LocalName, "LabeledField", StringComparison.Ordinal)),
                $"A {input.Name.LocalName} in the create-key form carries no field label.");
        }

        // Both forms describe themselves from the selected type rather than
        // hard-coding one type's words.
        foreach (var binding in new[]
                 {
                     "{Binding NewKeyForm.ValueLabel}",
                     "{Binding MutationForm.ValueLabel}",
                     "{Binding MutationForm.ActionLabel}",
                 })
        {
            Assert.Contains(
                root.Descendants(),
                element => element.Attributes().Any(attribute => string.Equals(attribute.Value, binding, StringComparison.Ordinal)));
        }
    }

    [Fact]
    public void Shell_focuses_the_single_runtime_panel_focus_target()
    {
        var focusNavigator = File.ReadAllText(Path.Combine(
            ApplicationViews.RepositoryRoot,
            "src",
            "Asura.App",
            "Views",
            "ShellFocusNavigator.cs"));

        Assert.Contains(
            "control.Classes.Contains(\"RuntimePanelFocusTarget\")",
            focusNavigator,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "control.Classes.Contains(\"PanelCard\")",
            focusNavigator,
            StringComparison.Ordinal);

        var mainWindow = LoadView("MainWindow");
        Assert.DoesNotContain(
            mainWindow.Descendants(),
            element => HasClass(element, "RuntimePanelFocusTarget"));
    }

    [Fact]
    public void Empty_panel_reuses_the_new_item_catalog()
    {
        var document = LoadRuntimePanelView("PanelPlaceholderView");
        var root = Assert.IsType<XElement>(document.Root);

        var chooser = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "LauncherView", StringComparison.Ordinal));
        // The cell has its own close button in its header, so the catalog does
        // not offer a second one.
        Assert.Equal("False", AttributeValue(chooser, "ShowCloseAction"));
        Assert.Equal(
            "{Binding $parent[Window].((vm:MainWindowViewModel)DataContext)}",
            AttributeValue(chooser, "DataContext"));
        Assert.Equal(
            "OnConnectionLaunchRequested",
            AttributeValue(chooser, "ConnectionLaunchRequested"));
        Assert.Equal(
            "OnOpenScreenClick",
            AttributeValue(chooser, "OpenScreenRequested"));
        Assert.Equal(
            "OnChooseTerminalClick",
            AttributeValue(chooser, "NewLocalTerminalRequested"));
        Assert.Equal(
            "OnChooseBrowserClick",
            AttributeValue(chooser, "NewBrowserRequested"));
        Assert.Equal(
            "OnChooseFileViewerClick",
            AttributeValue(chooser, "NewFileViewerRequested"));
        Assert.Equal(
            "OnChooseStatisticsClick",
            AttributeValue(chooser, "NewStatisticsRequested"));
        Assert.Equal(
            "OnChooseProcessMonitorClick",
            AttributeValue(chooser, "NewProcessMonitorRequested"));

        Assert.DoesNotContain(
            root.Descendants(),
            element => HasClass(element, "InlinePanelChooser"));
    }

    [Fact]
    public void Terminal_panel_view_preserves_managed_host_and_typed_shell_interactions()
    {
        var mainWindow = LoadView("MainWindow");
        var template = Assert.Single(
            mainWindow.Descendants(),
            element => string.Equals(element.Name.LocalName, "DataTemplate"
, StringComparison.Ordinal) && string.Equals(
                    AttributeValue(element, "DataType"),
                    "vm:TerminalRuntimePanelViewModel",
                    StringComparison.Ordinal));
        var component = Assert.Single(template.Elements());

        Assert.Equal(
            "OnCancelConnectionReconnectClick",
            AttributeValue(component, "CancelReconnectRequested"));
        Assert.Equal(
            "OnTerminalConnectionSelected",
            AttributeValue(component, "ConnectionSelected"));
        Assert.Equal(
            "OnTerminalNewConnectionRequested",
            AttributeValue(component, "NewConnectionRequested"));
        Assert.Equal(
            "OnRetryConnectionPanelClick",
            AttributeValue(component, "RetryConnectionRequested"));
        Assert.Equal(
            "OnTerminalSessionInitializationFailed",
            AttributeValue(component, "SessionInitializationFailed"));
        Assert.Equal(
            "OnTerminalSessionSnapshotChanged",
            AttributeValue(component, "SessionSnapshotChanged"));
        Assert.Equal(
            "OnTrustConnectionHostKeyClick",
            AttributeValue(component, "TrustHostKeyRequested"));

        var document = LoadRuntimePanelView("TerminalRuntimePanelView");
        var root = Assert.IsType<XElement>(document.Root);
        var connectionSelector = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "PanelConnectionSelectorView", StringComparison.Ordinal));
        Assert.Equal(
            "{CompiledBinding $parent[Window].((vm:IPanelConnectionOptionsHost)DataContext).PanelConnectionOptions}",
            AttributeValue(connectionSelector, "Options"));
        Assert.Equal(
            "{Binding ConnectionDisplayName}",
            AttributeValue(connectionSelector, "SelectedLabel"));
        Assert.Equal(
            "OnConnectionSelected",
            AttributeValue(connectionSelector, "ConnectionSelected"));
        Assert.Equal(
            "OnNewConnectionRequested",
            AttributeValue(connectionSelector, "NewConnectionRequested"));
        Assert.Equal(
            "Terminal",
            AttributeValue(
                Assert.Single(
                    root.Elements(),
                    element => string.Equals(element.Name.LocalName, "PanelChrome", StringComparison.Ordinal)),
                "Title"));
        var terminal = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "TerminalPresentationHost", StringComparison.Ordinal));

        Assert.Equal("RuntimeTerminal", AttributeValue(terminal, "Name"));
        Assert.Equal("{DynamicResource ShellHairline}", AttributeValue(terminal, "Margin"));
        Assert.Equal("{CompiledBinding ClientId}", AttributeValue(terminal, "ClientId"));
        Assert.Equal(
            "{CompiledBinding SessionClient}",
            AttributeValue(terminal, "SessionClient"));
        Assert.Equal(
            "{CompiledBinding SessionRequest}",
            AttributeValue(terminal, "SessionRequest"));
        Assert.Equal(
            "OnSessionSnapshotChanged",
            AttributeValue(terminal, "SessionSnapshotChanged"));
        Assert.Equal(
            "OnSessionInitializationFailed",
            AttributeValue(terminal, "SessionInitializationFailed"));
        Assert.DoesNotContain(
            root.DescendantsAndSelf(),
            element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName.Contains("Transform", StringComparison.Ordinal)));

        // The header mark says the session's real state in words and, with
        // Differentiate Without Color, by shape.
        var status = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "PanelChrome.Status", StringComparison.Ordinal));
        var mark = Assert.Single(status.Elements());
        Assert.Equal("StatusMark", mark.Name.LocalName);
        Assert.Equal(
            "Polite",
            AttributeValue(mark, "AutomationProperties.LiveSetting"));
        Assert.Equal(
            "{Binding StatusTone, ElementName=RuntimeTerminal}",
            AttributeValue(mark, "Tone"));
        Assert.StartsWith(
            "{Binding StatusText, ElementName=RuntimeTerminal",
            AttributeValue(mark, "Description"),
            StringComparison.Ordinal);
        Assert.Equal(
            "{DynamicResource ShellTerminalSurfaceOpacity}",
            AttributeValue(terminal, "BackgroundOpacity"));

        var codeBehind = File.ReadAllText(
            RuntimePanelPath("TerminalRuntimePanelView", ".axaml.cs"));
        Assert.Contains(
            "SessionInitializationFailed?.Invoke(sender, e);",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains(
            "SessionSnapshotChanged?.Invoke(sender, e);",
            codeBehind,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Dispose(", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("CancellationTokenSource", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Panel_connection_selector_filters_saved_connections_and_reports_intent()
    {
        var path = Path.Combine(
            ApplicationViews.RepositoryRoot,
            "src",
            "Asura.App",
            "Views",
            "Components",
            "PanelConnectionSelectorView.axaml");
        var root = Assert.IsType<XElement>(XDocument.Load(path).Root);
        var filter = FindUniqueAccessibleElement(root, "Filter saved connections");
        Assert.Equal("OnFilterTextChanged", AttributeValue(filter, "TextChanged"));
        Assert.Equal("Filter connections", AttributeValue(filter, "PlaceholderText"));
        Assert.Contains(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "ItemsControl"
, StringComparison.Ordinal) && string.Equals(
                    AttributeValue(element, "ItemsSource"),
                    "{Binding FilteredOptions, ElementName=Root}",
                    StringComparison.Ordinal));
        var create = FindUniqueAccessibleElement(
            root,
            "Create and connect a new connection");
        Assert.Equal("OnNewConnectionClick", AttributeValue(create, "Click"));

        var codeBehind = File.ReadAllText($"{path}.cs");
        Assert.Contains("connection.Name.Contains", codeBehind, StringComparison.Ordinal);
        Assert.Contains("connection.Kind.Contains", codeBehind, StringComparison.Ordinal);
        Assert.Contains("connection.Detail.Contains", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ISessionHostClient", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDialog", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Browser_panel_view_preserves_native_host_and_typed_shell_interactions()
    {
        var mainWindow = LoadView("MainWindow");
        var template = Assert.Single(
            mainWindow.Descendants(),
            element => string.Equals(element.Name.LocalName, "DataTemplate"
, StringComparison.Ordinal) && string.Equals(
                    AttributeValue(element, "DataType"),
                    "vm:BrowserRuntimePanelViewModel",
                    StringComparison.Ordinal));
        var component = Assert.Single(template.Elements());

        Assert.Equal(
            "OnBrowserAddressKeyDown",
            AttributeValue(component, "AddressKeyDown"));
        Assert.Equal(
            "OnBrowserBackClick",
            AttributeValue(component, "BackRequested"));
        Assert.Equal(
            "OnBrowserStateChanged",
            AttributeValue(component, "BrowserStateChanged"));
        Assert.Equal(
            "OnBrowserDeveloperToolsClick",
            AttributeValue(component, "DeveloperToolsRequested"));
        Assert.Equal(
            "OnBrowserForwardClick",
            AttributeValue(component, "ForwardRequested"));
        Assert.Equal(
            "OnBrowserReloadClick",
            AttributeValue(component, "ReloadRequested"));
        Assert.Equal(
            "OnBrowserStopClick",
            AttributeValue(component, "StopRequested"));

        var document = LoadRuntimePanelView("BrowserRuntimePanelView");
        var root = Assert.IsType<XElement>(document.Root);
        var browser = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "BrowserPresentationHost", StringComparison.Ordinal));

        Assert.Equal("RuntimeBrowser", AttributeValue(browser, "Name"));
        Assert.Equal("{DynamicResource ShellHairline}", AttributeValue(browser, "Margin"));
        Assert.Equal("{Binding ClientId}", AttributeValue(browser, "ClientId"));
        Assert.Equal(
            "{Binding RendererView}",
            AttributeValue(browser, "RendererView"));
        Assert.Equal(
            "{Binding SessionClient}",
            AttributeValue(browser, "SessionClient"));
        Assert.Equal(
            "{Binding SessionRequest}",
            AttributeValue(browser, "SessionRequest"));
        Assert.Equal(
            "OnBrowserStateChanged",
            AttributeValue(browser, "BrowserStateChanged"));
        var developerTools = FindUniqueAccessibleElement(
            root,
            "Open browser developer tools");
        Assert.Equal(
            "OnDeveloperToolsClick",
            AttributeValue(developerTools, "Click"));
        Assert.Equal(
            "{Binding IsLive, ElementName=RuntimeBrowser}",
            AttributeValue(developerTools, "IsEnabled"));
        var findInPage = FindUniqueAccessibleElement(root, "Find in page");
        Assert.Equal("OnFindClick", AttributeValue(findInPage, "Click"));
        Assert.Equal(
            "{Binding IsLive, ElementName=RuntimeBrowser}",
            AttributeValue(findInPage, "IsEnabled"));
        var findText = FindUniqueAccessibleElement(root, "Find text in page");
        Assert.Equal("SearchField", findText.Name.LocalName);
        Assert.Equal("OnFindBoxKeyDown", AttributeValue(findText, "KeyDown"));
        var browserStatus = Assert.Single(Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "PanelChrome.Status", StringComparison.Ordinal))
            .Elements());
        Assert.Equal("StatusMark", browserStatus.Name.LocalName);
        Assert.Equal(
            "{Binding StatusTone, ElementName=RuntimeBrowser}",
            AttributeValue(browserStatus, "Tone"));
        Assert.StartsWith(
            "{Binding StatusText, ElementName=RuntimeBrowser",
            AttributeValue(browserStatus, "Description"),
            StringComparison.Ordinal);
        Assert.Equal("OnFindTextChanged", AttributeValue(findText, "TextChanged"));
        var eventNotice = FindUniqueAccessibleElement(root, "Browser event notice");
        Assert.Equal(
            "Polite",
            AttributeValue(eventNotice, "AutomationProperties.LiveSetting"));
        Assert.Equal(
            "{Binding IsProductNoticeVisible, ElementName=RuntimeBrowser}",
            AttributeValue(eventNotice, "IsVisible"));
        var dismissNotice = FindUniqueAccessibleElement(
            root,
            "Dismiss browser event notice");
        Assert.Equal(
            "OnDismissProductNoticeClick",
            AttributeValue(dismissNotice, "Click"));
        Assert.DoesNotContain(
            root.DescendantsAndSelf(),
            element => element.Attributes().Any(attribute =>
                attribute.Name.LocalName.Contains("Transform", StringComparison.Ordinal)));

        var address = FindUniqueAccessibleElement(root, "Browser address");
        Assert.Equal("OnAddressKeyDown", AttributeValue(address, "KeyDown"));
        // Bound by element name, not by data context. The row's data context is the
        // panel, because the shell resolves which panel to act on from the sender's
        // context — re-pointing the row at the browser host broke Close silently.
        Assert.Equal(
            "{Binding AddressText, ElementName=RuntimeBrowser, Mode=TwoWay}",
            AttributeValue(address, "Text"));
        Assert.Equal("about:blank", AttributeValue(address, "PlaceholderText"));
        Assert.Equal(
            "OnAddressGotFocus",
            AttributeValue(address, "GotFocus"));
        Assert.Equal(
            "OnAddressLostFocus",
            AttributeValue(address, "LostFocus"));

        var status = FindUniqueAccessibleElement(root, "Browser session status");
        Assert.Equal(
            "Polite",
            AttributeValue(status, "AutomationProperties.LiveSetting"));

        var codeBehind = File.ReadAllText(
            RuntimePanelPath("BrowserRuntimePanelView", ".axaml.cs"));
        // Raised with the presentation host, not the sender. The shell resolves
        // browser actions from it, and resolves Close from the panel's data
        // context — one context cannot answer both, so the view names the host.
        Assert.Contains(
            "AddressKeyDown?.Invoke(RuntimeBrowser, e);",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains(
            "addressBox.PlaceholderText = null;",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains(
            "addressBox.PlaceholderText = BlankAddressPlaceholder;",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains(
            "BrowserStateChanged?.Invoke(sender, e);",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains("RoutingStrategies.Tunnel", codeBehind, StringComparison.Ordinal);
        Assert.Contains("OperatingSystem.IsMacOS()", codeBehind, StringComparison.Ordinal);
        Assert.Contains("BrowserFindDirection.Previous", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("async ", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("Dispose(", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("CancellationTokenSource", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void File_panel_view_preserves_content_states_and_typed_shell_interactions()
    {
        var mainWindow = LoadView("MainWindow");
        var template = Assert.Single(
            mainWindow.Descendants(),
            element => string.Equals(element.Name.LocalName, "DataTemplate"
, StringComparison.Ordinal) && string.Equals(
                    AttributeValue(element, "DataType"),
                    "vm:FileRuntimePanelViewModel",
                    StringComparison.Ordinal));
        var component = Assert.Single(template.Elements());
        var shellInteractions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // The file actions arrive as one event that names which was asked
            // for, rather than one event each. They are a list the panel builds
            // from what the connection can do, and a list cannot be turned into
            // a fixed set of events without going back to hand-written markup
            // per action — which is what left the toolbar and its overflow menu
            // disagreeing about what was possible.
            ["ActionRequested"] = "OnFileActionRequested",
            ["DismissOperationIssueRequested"] = "OnDismissFileOperationIssueClick",
            ["EntryDoubleTapped"] = "OnFileEntryDoubleTapped",
            ["EntrySelectionChanged"] = "OnFileEntrySelectionChanged",
            ["EntryTransferDropRequested"] = "OnFileEntryTransferDropRequested",
            ["EntryTransferKeyRequested"] = "OnFileEntryTransferKeyRequested",
            ["LocationKeyDown"] = "OnFileLocationKeyDown",
            ["NavigateUpRequested"] = "OnFileNavigateUpClick",
            ["RefreshRequested"] = "OnFileRefreshClick",
        };

        foreach (var (interaction, handler) in shellInteractions)
        {
            Assert.Equal(handler, AttributeValue(component, interaction));
        }

        var document = LoadRuntimePanelView("FileRuntimePanelView");
        var root = Assert.IsType<XElement>(document.Root);
        Assert.Contains(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Grid"
, StringComparison.Ordinal) && string.Equals(
                    AttributeValue(element, "RowDefinitions"),
                    "Auto,*",
                    StringComparison.Ordinal));
        Assert.Contains(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Border"
, StringComparison.Ordinal) && string.Equals(
                    AttributeValue(element, "Grid.Row"),
                    "0",
                    StringComparison.Ordinal)
                && string.Equals(
                    AttributeValue(element, "Padding"),
                    "{controls:Inset Left=Sm, Top=Xs, Right=Sm, Bottom=Sm}",
                    StringComparison.Ordinal)
                && string.Equals(
                    AttributeValue(element, "Background"),
                    "{DynamicResource ShellSurfaceBrush}",
                    StringComparison.Ordinal));
        var connectionSelector = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "PanelConnectionSelectorView", StringComparison.Ordinal));
        Assert.Equal(
            "{Binding $parent[Window].((vm:MainWindowViewModel)DataContext).FileConnectionOptions}",
            AttributeValue(connectionSelector, "Options"));
        Assert.Equal(
            "{Binding ConnectionDisplayName}",
            AttributeValue(connectionSelector, "SelectedLabel"));
        Assert.DoesNotContain(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "ComboBox"
, StringComparison.Ordinal) && string.Equals(
                    AttributeValue(element, "AutomationProperties.Name"),
                    "File provider profile",
                    StringComparison.Ordinal));
        var sortHeaders = new Dictionary<string, (string Handler, string StateBinding)>(
            StringComparer.Ordinal)
        {
            ["Sort files by name"] = ("OnSortNameClick", "{Binding IsSortingByName}"),
            ["Sort files by size"] = ("OnSortSizeClick", "{Binding IsSortingBySize}"),
            ["Sort files by modified date"] = (
                "OnSortModifiedClick",
                "{Binding IsSortingByModified}"),
        };
        foreach (var (accessibleName, sortHeader) in sortHeaders)
        {
            var button = Assert.Single(
                root.Descendants(),
                element => string.Equals(element.Name.LocalName, "Button"
, StringComparison.Ordinal) && string.Equals(
                        AttributeValue(element, "AutomationProperties.Name"),
                        accessibleName,
                        StringComparison.Ordinal));
            Assert.Equal(sortHeader.Handler, AttributeValue(button, "Click"));
            Assert.True(HasClass(button, "FileColumnHeader"));
            Assert.Equal(
                sortHeader.StateBinding,
                AttributeValue(button, "Classes.sorted"));
            Assert.Equal(
                "{Binding IsSortDescending}",
                AttributeValue(button, "Classes.descending"));
            Assert.Contains(
                button.Descendants(),
                element => string.Equals(element.Name.LocalName, "SymbolIcon"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Symbol"), "ArrowUp", StringComparison.Ordinal));
            Assert.Contains(
                button.Descendants(),
                element => string.Equals(element.Name.LocalName, "SymbolIcon"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Symbol"), "ArrowDown", StringComparison.Ordinal));
        }
        var headerStyle = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Style"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Selector"), "Button.FileColumnHeader", StringComparison.Ordinal));
        Assert.Contains(
            headerStyle.Elements(),
            setter => string.Equals(AttributeValue(setter, "Property"), "HorizontalAlignment"
, StringComparison.Ordinal) && string.Equals(AttributeValue(setter, "Value"), "Stretch", StringComparison.Ordinal));
        Assert.Contains(
            headerStyle.Elements(),
            setter => string.Equals(AttributeValue(setter, "Property"), "CornerRadius"
, StringComparison.Ordinal) && string.Equals(AttributeValue(setter, "Value"), "0", StringComparison.Ordinal));
        var headerHoverStyle = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Style"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Selector")
, "Button.FileColumnHeader:pointerover /template/ ContentPresenter", StringComparison.Ordinal));
        Assert.Contains(
            headerHoverStyle.Elements(),
            setter => string.Equals(AttributeValue(setter, "Property"), "Background"
, StringComparison.Ordinal) && string.Equals(AttributeValue(setter, "Value")
, "{DynamicResource ShellSurfaceHoverBrush}", StringComparison.Ordinal));
        Assert.Equal(
            3,
            root.Descendants().Count(element => string.Equals(element.Name.LocalName, "ListBox"
, StringComparison.Ordinal) && string.Equals(
                    AttributeValue(element, "ItemsSource"),
                    "{Binding Entries}",
                    StringComparison.Ordinal)));
        var fileNameLabels = root.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "TextBlock"
, StringComparison.Ordinal) && string.Equals(
                    AttributeValue(element, "Text"),
                    "{Binding Name}",
                    StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(3, fileNameLabels.Length);
        Assert.All(fileNameLabels, label =>
        {
            // A long name loses its middle, as Finder truncates, so the
            // extension and the end that tells two names apart stay visible.
            Assert.Equal("MiddleTruncated", AttributeValue(label, "Classes"));
            Assert.Null(AttributeValue(label, "TextTrimming"));
            Assert.Equal("{Binding Name}", AttributeValue(label, "ToolTip.Tip"));
        });
        Assert.Contains(
            fileNameLabels,
            label => string.Equals(label.Parent?.Name.LocalName, "Grid"
, StringComparison.Ordinal) && string.Equals(
                    AttributeValue(label, "Grid.Column"),
                    "1",
                    StringComparison.Ordinal));
        var viewOptionsButton = FindUniqueAccessibleElement(
            root,
            "Open file sort and view options");
        var viewOptionsFlyout = Assert.Single(
            viewOptionsButton.Descendants(),
            element => string.Equals(element.Name.LocalName, "Flyout", StringComparison.Ordinal));
        Assert.Equal(
            3,
            viewOptionsFlyout.Descendants().Count(element => string.Equals(element.Name.LocalName, "ComboBox", StringComparison.Ordinal)));
        Assert.DoesNotContain(
            root.Descendants().Where(element => string.Equals(element.Name.LocalName, "ComboBox", StringComparison.Ordinal)),
            element => !element.Ancestors().Contains(viewOptionsFlyout));
        Assert.DoesNotContain(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "TextBlock"
, StringComparison.Ordinal) && string.Equals(
                    AttributeValue(element, "Text"),
                    "{Binding KindLabel}",
                    StringComparison.Ordinal));
        Assert.Contains(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Border"
, StringComparison.Ordinal) && HasClass(element, "FileToolbarGroup"));
        Assert.Contains(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Button"
, StringComparison.Ordinal) && HasClass(element, "FileToolbarAction"));
        var previewToggle = FindUniqueAccessibleElement(root, "Show or hide file preview");
        Assert.Equal("OnTogglePreviewClick", AttributeValue(previewToggle, "Click"));
        Assert.True(HasClass(previewToggle, "InsetToggle"));
        Assert.Equal(
            "{Binding IsPreviewVisible}",
            AttributeValue(previewToggle, "Classes.active"));
        var previewSplitter = FindUniqueAccessibleElement(root, "Resize file preview");
        Assert.Equal(
            "{Binding IsPreviewVisible}",
            AttributeValue(previewSplitter, "IsVisible"));
        Assert.Equal("Columns", AttributeValue(previewSplitter, "ResizeDirection"));
        Assert.Equal(
            "PreviousAndNext",
            AttributeValue(previewSplitter, "ResizeBehavior"));
        foreach (var columnSplitterName in new[]
                 {
                     "Resize file name and size columns",
                     "Resize file size and modified columns",
                 })
        {
            var columnSplitter = FindUniqueAccessibleElement(root, columnSplitterName);
            Assert.Equal("Columns", AttributeValue(columnSplitter, "ResizeDirection"));
            Assert.Equal(
                "PreviousAndNext",
                AttributeValue(columnSplitter, "ResizeBehavior"));
        }

        // No action is written out by hand. The strip and the overflow menu are
        // the same list of actions shown twice, so an action a connection
        // cannot perform leaves both at once and neither can be left behind.
        var actionStrip = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "ItemsControl"
, StringComparison.Ordinal) && string.Equals(
                    AttributeValue(element, "ItemsSource"),
                    "{Binding ToolbarActions}",
                    StringComparison.Ordinal));
        Assert.Contains(
            actionStrip.Descendants(),
            element => string.Equals(element.Name.LocalName, "Button"
, StringComparison.Ordinal) && string.Equals(
                    AttributeValue(element, "Command"),
                    "{Binding Command}",
                    StringComparison.Ordinal));

        // One right-click menu over all three layouts and over the space below
        // the last row, filled when it opens from where the press landed.
        // Three menus in the markup would be three places to add the next
        // action to, which is the shape this replaced.
        var contextMenu = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "ContextMenu", StringComparison.Ordinal));
        Assert.Equal("OnFileContextMenuOpening", AttributeValue(contextMenu, "Opening"));
        foreach (var menu in new[] { contextMenu, OverflowMenu(root) })
        {
            Assert.Contains(
                menu.Descendants(),
                element => string.Equals(element.Name.LocalName, "Setter"
, StringComparison.Ordinal) && string.Equals(
                        AttributeValue(element, "Property"),
                        "Command",
                        StringComparison.Ordinal));
            Assert.Contains(
                menu.Descendants(),
                element => string.Equals(element.Name.LocalName, "Setter"
, StringComparison.Ordinal) && string.Equals(
                        AttributeValue(element, "Property"),
                        "IsEnabled",
                        StringComparison.Ordinal));
        }

        // And the toolbar answers to the panel's own width, not the window's:
        // two of these can share one window at different widths.
        var containerQuery = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "ContainerQuery", StringComparison.Ordinal));
        Assert.Equal("filePanel", AttributeValue(containerQuery, "Name"));
        Assert.Equal("Width", AttributeValue(root, "Container.Sizing"));

        var itemCount = FindUniqueAccessibleElement(root, "Files item count");
        Assert.Equal("{Binding Status}", AttributeValue(itemCount, "Value"));
        Assert.Equal("{Binding ShortStatus}", AttributeValue(itemCount, "CompactValue"));
        Assert.Equal(
            "{Binding HasListingSummary}",
            AttributeValue(itemCount, "IsVisible"));
        Assert.Equal(
            1,
            root.Descendants().Count(element => string.Equals(
                AttributeValue(element, "Text"),
                "{Binding LocationText}",
                StringComparison.Ordinal)));
        Assert.DoesNotContain(
            root.Descendants(),
            element => string.Equals(
                AttributeValue(element, "AutomationProperties.Name"),
                "Load more files",
                StringComparison.Ordinal));
        Assert.True(
            root.Descendants().Count(element => string.Equals(element.Name.LocalName, "VirtualizingStackPanel", StringComparison.Ordinal)) >= 2,
            "The complete details and list views must virtualize their rows.");
        Assert.DoesNotContain(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "TextBlock"
, StringComparison.Ordinal) && string.Equals(
                    AttributeValue(element, "Text"),
                    "BOUNDED PREVIEW",
                    StringComparison.Ordinal));
        Assert.DoesNotContain(
            root.Descendants(),
            element => string.Equals(
                AttributeValue(element, "BorderThickness"),
                "0,1,0,1",
                StringComparison.Ordinal));
        var transferDropTarget = FindUniqueAccessibleElement(
            root,
            "File transfer drop target");
        Assert.Equal("Rectangle", transferDropTarget.Name.LocalName);
        Assert.Equal("False", AttributeValue(transferDropTarget, "IsVisible"));
        Assert.Equal("4,3", AttributeValue(transferDropTarget, "StrokeDashArray"));
        Assert.Equal(
            "{DynamicResource ShellCardRadius}",
            AttributeValue(transferDropTarget, "RadiusX"));
        Assert.Equal(
            "{DynamicResource ShellCardRadius}",
            AttributeValue(transferDropTarget, "RadiusY"));
        var folderDropStyle = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Style"
, StringComparison.Ordinal) && string.Equals(
                    AttributeValue(element, "Selector"),
                    "ListBoxItem.transferDropTarget /template/ ContentPresenter",
                    StringComparison.Ordinal));
        Assert.Contains(
            folderDropStyle.Elements(),
            element => string.Equals(element.Name.LocalName, "Setter"
, StringComparison.Ordinal) && string.Equals(
                    AttributeValue(element, "Property"),
                    "Background",
                    StringComparison.Ordinal));
        Assert.DoesNotContain(
            folderDropStyle.Elements(),
            element => string.Equals(element.Name.LocalName, "Setter"
, StringComparison.Ordinal) && AttributeValue(element, "Property") is
                    "BorderBrush" or "BorderThickness");

        var application = XDocument.Load(Path.Combine(
            ApplicationViews.RepositoryRoot,
            "src",
            "Asura.App",
            "App.axaml"));
        var scalarCardRadius = Assert.Single(
            application.Descendants(),
            element => string.Equals(
                AttributeValue(element, "Key"),
                "ShellCardRadius",
                StringComparison.Ordinal));
        Assert.Equal("Double", scalarCardRadius.Name.LocalName);

        Assert.Equal(
            "Polite",
            AttributeValue(
                FindUniqueAccessibleElement(root, "Files loading"),
                "AutomationProperties.LiveSetting"));
        var retainedError = Assert.Single(LoadView("Components/ErrorNoticesView").Descendants(),
            element => element.Name.LocalName == "Callout");
        Assert.Equal("Assertive", AttributeValue(retainedError, "AutomationProperties.LiveSetting"));
        Assert.Equal("{Binding Message}", AttributeValue(retainedError, "AutomationProperties.Name"));
        Assert.Contains(
            root.Descendants(),
            element => string.Equals(
                AttributeValue(element, "IsVisible"),
                "{Binding ShowErrorState}",
                StringComparison.Ordinal));
        Assert.Contains(
            root.Descendants(),
            element => string.Equals(
                AttributeValue(element, "IsVisible"),
                "{Binding ShowEmptyLocationState}",
                StringComparison.Ordinal));
        Assert.Contains(
            root.Descendants(),
            element => string.Equals(
                AttributeValue(element, "IsVisible"),
                "{Binding ShowSearchNoResultsState}",
                StringComparison.Ordinal));

        var codeBehind = File.ReadAllText(
            RuntimePanelPath("FileRuntimePanelView", ".axaml.cs"));
        foreach (var interaction in shellInteractions.Keys.Except(
                     [
                         "ActionRequested",
                         "EntryTransferDropRequested",
                         "EntryTransferKeyRequested",
                     ],
                     StringComparer.Ordinal))
        {
            Assert.Contains(
                $"{interaction}?.Invoke(sender, e);",
                codeBehind,
                StringComparison.Ordinal);
        }

        // The action carries which action it is, so it is raised with an
        // argument the view builds rather than one it was handed.
        Assert.Contains(
            "ActionRequested?.Invoke(this, new FilePanelActionEventArgs(action));",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains(
            "EntryTransferKeyRequested?.Invoke(",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains(
            "EntryTransferDropRequested?.Invoke(",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains(
            "private void OnFilePointerMoved",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains(
            "new ActiveFileDrag(",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains(
            "window.ShowDragGhost(",
            codeBehind,
            StringComparison.Ordinal);
        Assert.True(
            codeBehind.IndexOf(
                "candidate.Pointer.Capture(this);",
                StringComparison.Ordinal)
            < codeBehind.IndexOf(
                "_activeFileDrag = activeDrag;",
                StringComparison.Ordinal));
        Assert.Contains(
            "ResolveInternalFileDropTarget(",
            codeBehind,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "DragDrop.DoDragDropAsync(",
            codeBehind,
            StringComparison.Ordinal);

        // A drag between panels stays the shell's own captured drag. Only
        // once the pointer leaves the window with local files is it handed to
        // the system, so Finder can take the files.
        var dragOut = File.ReadAllText(
            RuntimePanelPath("FileRuntimePanelView", ".DragOut.cs"));
        Assert.Contains("DragDrop.DoDragDropAsync(", dragOut, StringComparison.Ordinal);
        Assert.Contains("IsOutside(window,", dragOut, StringComparison.Ordinal);
        Assert.Contains("DataTransferItem.CreateFile(", dragOut, StringComparison.Ordinal);
        Assert.Contains("TryHandOffToSystemDrag(e, active, window)", codeBehind, StringComparison.Ordinal);
        Assert.Contains(
            "AddHandler(DragDrop.DragLeaveEvent, OnFileDragLeave)",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains(
            "ResolveFileDropTarget(e.Source, e.DataTransfer)",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains(
            "DestinationFolder",
            codeBehind,
            StringComparison.Ordinal);
        Assert.DoesNotContain("private async ", codeBehind, StringComparison.Ordinal);
        Assert.Contains(
            "protected override void OnDetachedFromVisualTree(",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains("ReleaseHtmlPreview();", codeBehind, StringComparison.Ordinal);
        Assert.Contains("preview.Dispose();", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("CancellationTokenSource", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDialog", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("StorageProvider", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", codeBehind, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "StatisticsRuntimePanelView",
        "Statistics state",
        "Refresh statistics",
        "Statistics loading",
        "Statistics unavailable")]
    [InlineData(
        "ProcessMonitorRuntimePanelView",
        "Processes state",
        "Refresh processes",
        "Processes loading",
        "Processes unavailable")]
    public void Monitoring_panel_views_preserve_commands_and_live_state_announcements(
        string panelView,
        string statusName,
        string refreshName,
        string loadingName,
        string unavailableName)
    {
        var document = LoadRuntimePanelView(panelView);
        var root = Assert.IsType<XElement>(document.Root);

        var status = FindUniqueAccessibleElement(root, statusName);
        Assert.Equal("Polite", AttributeValue(status, "AutomationProperties.LiveSetting"));

        var refresh = FindUniqueAccessibleElement(root, refreshName);
        Assert.Equal("Button", refresh.Name.LocalName);
        Assert.Equal(
            "{Binding RefreshCommand}",
            AttributeValue(refresh, "Command"));

        var loading = FindUniqueAccessibleElement(root, loadingName);
        Assert.Equal("Polite", AttributeValue(loading, "AutomationProperties.LiveSetting"));

        var unavailable = FindUniqueAccessibleElement(root, unavailableName);
        Assert.Equal(
            "Assertive",
            AttributeValue(unavailable, "AutomationProperties.LiveSetting"));
    }

    /// <summary>
    /// The process list is a Mac table: the shared data-grid theme, sortable
    /// column headers that ask the host for the order (it bounds the sample by
    /// that order, so the grid must not reorder rows itself), several rows at
    /// once, and a menu on the rows. It used to be hand-built from buttons
    /// and text blocks.
    /// </summary>
    [Fact]
    public void Process_monitor_is_a_sortable_extended_selection_table()
    {
        var document = LoadRuntimePanelView("ProcessMonitorRuntimePanelView");
        var root = Assert.IsType<XElement>(document.Root);
        var grid = FindUniqueAccessibleElement(root, "Process list");

        Assert.Equal("DataGrid", grid.Name.LocalName);
        Assert.True(HasClass(grid, "DatabaseGrid"));
        Assert.Equal("Extended", AttributeValue(grid, "SelectionMode"));
        Assert.Equal("True", AttributeValue(grid, "CanUserSortColumns"));
        Assert.Equal("OnProcessSorting", AttributeValue(grid, "Sorting"));
        Assert.Equal("{Binding Processes}", AttributeValue(grid, "ItemsSource"));
        Assert.Contains(
            grid.Descendants(),
            element => string.Equals(element.Name.LocalName, "ContextMenu", StringComparison.Ordinal));
        Assert.DoesNotContain(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "ListBox", StringComparison.Ordinal));

        var sortable = grid.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "DataGridTextColumn", StringComparison.Ordinal)
                && string.Equals(AttributeValue(element, "CanUserSort"), "True", StringComparison.Ordinal))
            .Select(element => AttributeValue(element, "Tag") ?? string.Empty)
            .ToArray();
        Assert.Equal(["ProcessId", "Name", "Cpu", "Memory"], sortable);

        var filter = FindUniqueAccessibleElement(root, "Filter processes");
        Assert.Equal("SearchField", filter.Name.LocalName);

        var codeBehind = File.ReadAllText(
            RuntimePanelPath("ProcessMonitorRuntimePanelView", ".axaml.cs"));
        Assert.Contains("e.Handled = true;", codeBehind, StringComparison.Ordinal);
        Assert.Contains("panel.ChangeSort(sort);", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("private async ", codeBehind, StringComparison.Ordinal);
    }

    [Fact]
    public void Statistics_panel_presents_host_metrics_with_bounded_history_charts()
    {
        var document = LoadRuntimePanelView("StatisticsRuntimePanelView");
        var root = Assert.IsType<XElement>(document.Root);
        var charts = root
            .Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "TimeSeriesChart", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(4, charts.Length);
        Assert.Contains(
            charts,
            chart => string.Equals(AttributeValue(chart, "Values"), "{Binding CpuHistory}"
, StringComparison.Ordinal) && string.Equals(AttributeValue(chart, "Maximum"), "100", StringComparison.Ordinal));
        Assert.Contains(
            charts,
            chart => string.Equals(AttributeValue(chart, "Values"), "{Binding MemoryHistory}", StringComparison.Ordinal));
        Assert.Contains(
            charts,
            chart => string.Equals(AttributeValue(chart, "Values"), "{Binding NetworkReceivedHistory}", StringComparison.Ordinal));
        Assert.Contains(
            charts,
            chart => string.Equals(AttributeValue(chart, "Values"), "{Binding NetworkSentHistory}", StringComparison.Ordinal));

        var visibleCopy = string.Join(
            " ",
            root.Descendants()
                .Select(element => AttributeValue(element, "Text"))
                .OfType<string>());
        Assert.DoesNotContain("OBSERVED", visibleCopy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ASURA CPU", visibleCopy, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ASURA MEMORY", visibleCopy, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CPU usage", visibleCopy, StringComparison.Ordinal);
        Assert.Contains("Process memory", visibleCopy, StringComparison.Ordinal);
        Assert.Contains("Network receive", visibleCopy, StringComparison.Ordinal);
        Assert.Contains("Network send", visibleCopy, StringComparison.Ordinal);
        Assert.Contains("Running processes", visibleCopy, StringComparison.Ordinal);
    }

    /// <summary>
    /// The Files panel follows the Mac's list conventions: every listing jumps
    /// to a typed name, the filter is the shared search field, the header mark
    /// follows the connection instead of a fixed green dot, the "more" button
    /// is a horizontal ellipsis, preview options are checkboxes, and icons sit
    /// on the icon ramp rather than literal sizes.
    /// </summary>
    [Fact]
    public void File_panel_lists_search_status_and_options_follow_mac_conventions()
    {
        var root = Assert.IsType<XElement>(LoadRuntimePanelView("FileRuntimePanelView").Root);
        var listings = root.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "ListBox", StringComparison.Ordinal)
                && string.Equals(AttributeValue(element, "ItemsSource"), "{Binding Entries}", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(3, listings.Length);
        Assert.All(listings, list => Assert.Equal("True", AttributeValue(list, "IsTextSearchEnabled")));

        var filter = Assert.Single(
            root.Descendants(),
            element => string.Equals(AttributeValue(element, "Name"), "FileFilterBox", StringComparison.Ordinal));
        Assert.Equal("SearchField", filter.Name.LocalName);
        Assert.Equal("{Binding FilterPlaceholder}", AttributeValue(filter, "PlaceholderText"));

        var status = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "PanelChrome.Status", StringComparison.Ordinal));
        var mark = Assert.Single(status.Elements());
        Assert.Equal("StatusMark", mark.Name.LocalName);
        Assert.Equal("{Binding StatusTone}", AttributeValue(mark, "Tone"));
        Assert.Equal("{Binding StatusDescription}", AttributeValue(mark, "Description"));

        Assert.DoesNotContain(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "ToggleSwitch", StringComparison.Ordinal));
        Assert.DoesNotContain(
            root.Descendants(),
            element => string.Equals(AttributeValue(element, "Symbol"), "MoreVertical", StringComparison.Ordinal));
        Assert.Contains(
            root.Descendants(),
            element => string.Equals(AttributeValue(element, "Symbol"), "MoreCircle", StringComparison.Ordinal));
        Assert.All(
            root.Descendants().Where(element => string.Equals(element.Name.LocalName, "SymbolIcon", StringComparison.Ordinal)
                && AttributeValue(element, "FontSize") is not null),
            icon => Assert.StartsWith(
                "{DynamicResource ShellIconSize",
                AttributeValue(icon, "FontSize"),
                StringComparison.Ordinal));
    }

    /// <summary>
    /// Docker, Kubernetes and Processes follow the same conventions: lists
    /// jump to a typed name, toolbar icons sit together in toolbar groups,
    /// icons are on the icon ramp, no text is set below ten points, and the
    /// button that opens the connection editor says so with an ellipsis.
    /// </summary>
    [Theory]
    [InlineData("DockerRuntimePanelView")]
    [InlineData("KubernetesRuntimePanelView")]
    [InlineData("KubernetesInspectorOverviewView")]
    [InlineData("ProcessMonitorRuntimePanelView")]
    public void Container_and_process_panels_use_the_shared_ramps_and_toolbar_groups(string panelView)
    {
        var root = Assert.IsType<XElement>(LoadRuntimePanelView(panelView).Root);

        Assert.All(
            root.Descendants().Where(element => string.Equals(element.Name.LocalName, "SymbolIcon", StringComparison.Ordinal)
                && AttributeValue(element, "FontSize") is not null),
            icon => Assert.Matches(
                @"^\{(DynamicResource ShellIconSize(Xs|Sm|Md|Lg|Xl)|controls:TextScaled \d+)\}$",
                AttributeValue(icon, "FontSize")));
        Assert.DoesNotContain(
            root.DescendantsAndSelf().SelectMany(element => element.Attributes()),
            attribute => attribute.Value.Contains("ShellFontSize8", StringComparison.Ordinal)
                || attribute.Value.Contains("ShellFontSize9", StringComparison.Ordinal)
                || attribute.Value.Contains("ShellPillFontSize", StringComparison.Ordinal));
        Assert.All(
            root.Descendants().Where(element => string.Equals(element.Name.LocalName, "Button", StringComparison.Ordinal)
                && HasClass(element, "ToolbarGroupAction")),
            button => Assert.True(
                button.Ancestors().Any(ancestor => string.Equals(ancestor.Name.LocalName, "Border", StringComparison.Ordinal)
                    && HasClass(ancestor, "ToolbarGroup")),
                $"{AttributeValue(button, "AutomationProperties.Name")} sits outside a toolbar group."));
    }

    [Fact]
    public void Container_lists_answer_to_typing_and_the_connection_editor_is_named_with_an_ellipsis()
    {
        var docker = Assert.IsType<XElement>(LoadRuntimePanelView("DockerRuntimePanelView").Root);
        var resources = Assert.Single(
            docker.Descendants(),
            element => string.Equals(AttributeValue(element, "Name"), "FlatResourceHost", StringComparison.Ordinal));
        Assert.Equal("True", AttributeValue(resources, "IsTextSearchEnabled"));
        Assert.StartsWith("{Binding Title", AttributeValue(resources, "TextSearch.TextBinding"), StringComparison.Ordinal);

        var kubernetes = Assert.IsType<XElement>(LoadRuntimePanelView("KubernetesRuntimePanelView").Root);
        foreach (var name in new[] { "NamespaceList", "HelmReleaseList" })
        {
            var list = Assert.Single(
                kubernetes.Descendants(),
                element => string.Equals(AttributeValue(element, "Name"), name, StringComparison.Ordinal));
            Assert.Equal("True", AttributeValue(list, "IsTextSearchEnabled"));
        }

        Assert.Contains(
            kubernetes.Descendants(),
            element => string.Equals(AttributeValue(element, "Content"), "Add Kubernetes Connection…", StringComparison.Ordinal));
        Assert.DoesNotContain(
            kubernetes.Descendants(),
            element => string.Equals(AttributeValue(element, "Content"), "Add Kubernetes connection", StringComparison.Ordinal));
    }

    /// <summary>
    /// Each chart states its unit so it can label the top of its scale, and
    /// the failure that replaces the charts is opaque and hidden until the
    /// panel says otherwise, so it never floats over a chart.
    /// </summary>
    [Fact]
    public void Statistics_charts_state_their_units_and_the_failure_replaces_them()
    {
        var root = Assert.IsType<XElement>(LoadRuntimePanelView("StatisticsRuntimePanelView").Root);
        var charts = root.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "TimeSeriesChart", StringComparison.Ordinal))
            .ToArray();

        Assert.All(charts, chart =>
        {
            Assert.NotNull(AttributeValue(chart, "Unit"));
            Assert.Equal("{Binding SampleInterval}", AttributeValue(chart, "SampleInterval"));
            Assert.NotNull(AttributeValue(chart, "LabelBrush"));
        });
        Assert.DoesNotContain(
            root.Descendants().Select(element => AttributeValue(element, "Text")),
            text => text?.Contains("auto scale", StringComparison.Ordinal) == true);

        var unavailable = FindUniqueAccessibleElement(root, "Statistics unavailable");
        Assert.Equal("{Binding ShowTerminalError, FallbackValue=False}", AttributeValue(unavailable, "IsVisible"));
        Assert.Equal("{DynamicResource ShellSurfaceBrush}", AttributeValue(unavailable, "Background"));
    }

    private static XElement OverflowMenu(XElement root) => Assert.Single(
        root.Descendants(),
        element => string.Equals(element.Name.LocalName, "MenuFlyout", StringComparison.Ordinal));

    private static XElement FindUniqueAccessibleElement(
        XElement root,
        string accessibleName) =>
        Assert.Single(
            root.Descendants(),
            element => string.Equals(
                AttributeValue(element, "AutomationProperties.Name"),
                accessibleName,
                StringComparison.Ordinal));

    private static XDocument DesignSystem() =>
        XDocument.Load(Path.Combine(
            ApplicationViews.RepositoryRoot,
            "src",
            "Asura.App",
            "Styles",
            "DesignSystem.axaml"));

    private static XDocument LoadView(string view) =>
        XDocument.Load(Path.Combine(
            ApplicationViews.RepositoryRoot,
            "src",
            "Asura.App",
            "Views",
            $"{view}.axaml"));

    private static XDocument LoadRuntimePanelView(string panelView) =>
        XDocument.Load(RuntimePanelPath(panelView, ".axaml"));

    private static string RuntimePanelPath(string panelView, string extension) =>
        Path.Combine(
            ApplicationViews.RepositoryRoot,
            "src",
            "Asura.App",
            "Views",
            "RuntimePanels",
            $"{panelView}{extension}");

    private static string? AttributeValue(XElement element, string name) =>
        element.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, name, StringComparison.Ordinal))
            ?.Value;

    private static bool HasClass(XElement element, string className) =>
        (AttributeValue(element, "Classes") ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains(className, StringComparer.Ordinal);
}
