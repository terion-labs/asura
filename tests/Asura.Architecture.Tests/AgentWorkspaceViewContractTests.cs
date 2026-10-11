using System.Xml.Linq;
using Asura.Testing;

namespace Asura.Architecture.Tests;

public sealed class AgentWorkspaceViewContractTests
{
    private static readonly ApplicationViewCatalog ApplicationViews =
        ApplicationViewCatalog.Load();

    private static readonly IReadOnlyDictionary<string, string> Interactions =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["AgentQuestionResponseKeyDownRequested"] =
                "OnAgentQuestionResponseKeyDown",
            ["ApproveAgentActionRequested"] = "OnApproveAgentActionClick",
            ["CancelAgentActionRequested"] = "OnCancelAgentActionClick",
            ["CancelAgentChatRequested"] = "OnCancelAgentChatClick",
            ["ClearAgentChatRequested"] = "OnClearAgentChatClick",
            ["DeclineAgentQuestionRequested"] = "OnDeclineAgentQuestionClick",
            ["DenyAgentActionRequested"] = "OnDenyAgentActionClick",
            ["DisableAgentYoloRequested"] = "OnDisableAgentYoloClick",
            ["EnableAgentCapabilityAskRequested"] =
                "OnEnableAgentCapabilityAskClick",
            ["EnableAgentYoloRequested"] = "OnEnableAgentYoloClick",
            ["KeepAgentCapabilityOffRequested"] =
                "OnKeepAgentCapabilityOffClick",
            ["LoadOlderAgentAuditRequested"] = "OnLoadOlderAgentAuditClick",
            ["StartNewAgentConversationRequested"] = "OnStartNewConversationClick",
            ["OpenAgentConversationRequested"] = "OnOpenConversationClick",
            ["DeleteAgentConversationRequested"] = "OnDeleteConversationClick",
            ["CopyAgentMessageRequested"] = "OnCopyAgentMessageClick",
            ["ForkAgentConversationRequested"] = "OnForkAgentConversationClick",
            ["ToggleAgentModelFavoriteRequested"] =
                "OnToggleFavoriteModelClick",
            ["RefreshAgentModelsRequested"] = "OnRefreshModelsClick",
            ["RefreshAgentAuditRequested"] = "OnRefreshAgentAuditClick",
            ["SendAgentChatRequested"] = "OnSendAgentChatClick",
            ["ShowAgentSettingsRequested"] = "OnShowAgentSettingsClick",
            ["SubmitAgentQuestionRequested"] = "OnSubmitAgentQuestionClick",
        };

    [Fact]
    public void Agent_workspace_owns_the_complete_pixel_stable_agent_surface()
    {
        var document = LoadView();
        var root = Assert.IsType<XElement>(document.Root);

        Assert.Equal("UserControl", root.Name.LocalName);
        Assert.Equal("Stretch", AttributeValue(root, "HorizontalContentAlignment"));
        Assert.Equal("Stretch", AttributeValue(root, "VerticalContentAlignment"));
        Assert.Null(AttributeValue(root, "DataContext"));

        // The root is a wrapper hosting the surface and, over its bottom-left
        // corner, the floating-mode resize grip.
        var host = Assert.Single(
            root.Elements(),
            element => string.Equals(element.Name.LocalName, "Panel", StringComparison.Ordinal));
        var panel = Assert.Single(
            host.Elements(),
            element => string.Equals(element.Name.LocalName, "Border"
, StringComparison.Ordinal) && HasClass(element, "AgentPanel"));
        var layout = Assert.Single(
            panel.Elements(),
            element => string.Equals(element.Name.LocalName, "Grid", StringComparison.Ordinal));
        Assert.Equal("Auto,*,Auto,Auto", AttributeValue(layout, "RowDefinitions"));

        // The ordinary floating surface uses the corner grip. Edge-attached
        // hosts reuse it as a full inner-edge handle, including while docked.
        var resizeGrip = Assert.Single(
            host.Elements(),
            element => string.Equals(element.Name.LocalName, "Thumb"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Name"), "FloatingResizeHandle", StringComparison.Ordinal));
        Assert.Equal("FloatingResizeHandle", AttributeValue(resizeGrip, "Name"));
        Assert.Equal("OnFloatingResizeDragDelta", AttributeValue(resizeGrip, "DragDelta"));
        Assert.Null(AttributeValue(resizeGrip, "HorizontalAlignment"));
        Assert.Null(AttributeValue(resizeGrip, "VerticalAlignment"));
        var defaultResizeStyle = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Style"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Selector")
, "views|AgentWorkspaceView Thumb#FloatingResizeHandle", StringComparison.Ordinal));
        Assert.Contains(
            defaultResizeStyle.Elements(),
            element => string.Equals(AttributeValue(element, "Property"), "HorizontalAlignment"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Value"), "Left", StringComparison.Ordinal));
        Assert.Contains(
            defaultResizeStyle.Elements(),
            element => string.Equals(AttributeValue(element, "Property"), "VerticalAlignment"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Value"), "Bottom", StringComparison.Ordinal));
        var edgeResizeStyle = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Style"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Selector")
, "views|AgentWorkspaceView.edgeResizable Thumb#FloatingResizeHandle", StringComparison.Ordinal));
        Assert.Contains(
            edgeResizeStyle.Elements(),
            element => string.Equals(AttributeValue(element, "Property"), "IsVisible"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Value"), "True", StringComparison.Ordinal));
        Assert.Contains(
            root.Descendants(),
            element => string.Equals(AttributeValue(element, "Name"), "CornerResizeGlyph", StringComparison.Ordinal));
        var heightResizeGrip = Assert.Single(
            host.Elements(),
            element => string.Equals(element.Name.LocalName, "Thumb"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Name"), "FloatingHeightResizeHandle", StringComparison.Ordinal));
        Assert.Equal(
            "OnFloatingHeightResizeDragDelta",
            AttributeValue(heightResizeGrip, "DragDelta"));

        foreach (var controlName in NamedControls)
        {
            Assert.Single(
                root.Descendants(),
                element => string.Equals(
                    AttributeValue(element, "Name"),
                    controlName,
                    StringComparison.Ordinal));
        }

        var transcript = FindNamedElement(root, "AgentChatTranscript");
        Assert.Equal(
            "OnAgentChatTranscriptScrollChanged",
            AttributeValue(transcript, "ScrollChanged"));
        Assert.Contains(
            "AI agent activity",
            AttributeValue(transcript, "AutomationProperties.Name"),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            root.Descendants(),
            element => string.Equals(AttributeValue(element, "IsVisible")
, "{Binding AgentChat.CanStartConversation}", StringComparison.Ordinal));
        Assert.DoesNotContain(
            root.Descendants(),
            element => string.Equals(
                AttributeValue(element, "Text"),
                "{Binding AgentChat.CapabilityNotice}",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            root.Descendants(),
            element => string.Equals(
                AttributeValue(element, "Name"),
                "AgentContextDetailsButton",
                StringComparison.Ordinal));

        // One label for the one action, with the ellipsis of a button that
        // opens a window: "Configure AI", "Open AI settings" and "Provider
        // settings" all opened AI settings.
        var aiSettingsButtons = root.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "Button", StringComparison.Ordinal)
                && string.Equals(AttributeValue(element, "Click"), "OnShowAgentSettingsClick", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(3, aiSettingsButtons.Length);
        Assert.All(aiSettingsButtons, button =>
        {
            Assert.Equal("AI Settings…", AttributeValue(button, "Content"));
            Assert.Equal("AI Settings…", AttributeValue(button, "AutomationProperties.Name"));
        });
        // The empty states lead with it; after a failed turn it sits beside
        // the recovery buttons at their size, as a secondary button.
        Assert.Equal(
            ["PrimaryButton", "PrimaryButton", "SecondaryButton"],
            aiSettingsButtons.Select(button => AttributeValue(button, "Classes")),
            StringComparer.Ordinal);
        Assert.Contains(
            aiSettingsButtons,
            button => !string.IsNullOrWhiteSpace(AttributeValue(button, "AutomationProperties.HelpText")));

        var footerStatus = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "TextBlock"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Text"), "{Binding AgentChat.Status}", StringComparison.Ordinal));
        Assert.Equal("Center", AttributeValue(footerStatus, "HorizontalAlignment"));
        Assert.Equal("Center", AttributeValue(footerStatus, "TextAlignment"));
        var statusRow = Assert.Single(
            footerStatus.Ancestors(),
            element => string.Equals(element.Name.LocalName, "Grid"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Grid.Row"), "2", StringComparison.Ordinal));
        Assert.NotNull(statusRow);

        var clearRetainedSession = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Button"
, StringComparison.Ordinal) && string.Equals(
                    AttributeValue(element, "Content"),
                    "Clear Agent Session",
                    StringComparison.Ordinal));
        // Clearing throws the retained session away, so it says so in red.
        Assert.Equal("DestructiveButton", AttributeValue(clearRetainedSession, "Classes"));
        Assert.Equal(
            "{Binding AgentChat.CanClear}",
            AttributeValue(clearRetainedSession, "IsVisible"));
        Assert.False(string.IsNullOrWhiteSpace(
            AttributeValue(
                clearRetainedSession,
                "AutomationProperties.Name")));

        var prompt = FindNamedElement(root, "AgentChatPromptInput");
        var composerStack = Assert.Single(
            prompt.Ancestors(),
            element => string.Equals(element.Name.LocalName, "StackPanel"
, StringComparison.Ordinal) && string.Equals(
                    AttributeValue(element, "Grid.Row"),
                    "3",
                    StringComparison.Ordinal));
        var composer = Assert.Single(
            prompt.Ancestors(),
            element => string.Equals(element.Name.LocalName, "Border"
, StringComparison.Ordinal) && composerStack.Descendants().Contains(element));
        Assert.Equal(
            "{Binding AgentChat.ShowComposer, FallbackValue=False}",
            AttributeValue(composer, "IsVisible"));

        var contextUsage = Assert.Single(
            composer.Descendants(),
            element => string.Equals(element.Name.LocalName, "Button"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "AutomationProperties.Name")
, "{Binding AgentChat.ContextWindowAccessibleName}", StringComparison.Ordinal));
        Assert.Null(AttributeValue(contextUsage, "Content"));
        // Sized by the composer controls' text-scaled style, in the toolbar
        // beside Send rather than in the raw-secrets row.
        Assert.Null(AttributeValue(contextUsage, "Width"));
        Assert.Null(AttributeValue(contextUsage, "MinHeight"));
        Assert.Equal("3", AttributeValue(contextUsage, "Grid.Column"));
        Assert.Equal("AgentComposerToolbar", AttributeValue(contextUsage.Parent!, "Name"));
        Assert.Equal(
            "{Binding AgentChat.ContextWindowAccessibleName}",
            AttributeValue(contextUsage, "ToolTip.Tip"));
        Assert.Contains("ComposerControl", AttributeValue(contextUsage, "Classes"), StringComparison.Ordinal);
        var contextDonut = Assert.Single(
            contextUsage.Elements(),
            element => string.Equals(element.Name.LocalName, "ContextWindowDonut", StringComparison.Ordinal));
        Assert.Equal(
            "{Binding AgentChat.ContextWindowPercent}",
            AttributeValue(contextDonut, "Percentage"));
        var composerToolbar = FindNamedElement(root, "AgentComposerToolbar");
        Assert.Equal(
            "Auto,*,*,Auto,Auto,Auto",
            AttributeValue(composerToolbar, "ColumnDefinitions"));
        Assert.Equal("0", AttributeValue(composerToolbar, "ColumnSpacing"));
        var accessMode = Assert.Single(
            composerToolbar.Elements(),
            element => string.Equals(element.Name.LocalName, "Button"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "AutomationProperties.Name")
, "Choose how AI actions are approved", StringComparison.Ordinal));
        Assert.Equal("0", AttributeValue(accessMode, "MinWidth"));
        Assert.Contains(
            accessMode.Descendants(),
            element => string.Equals(element.Name.LocalName, "TextBlock"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "TextTrimming")
, "CharacterEllipsis", StringComparison.Ordinal));
        var modelPicker = FindNamedElement(root, "AgentModelPickerButton");
        Assert.Equal("0", AttributeValue(modelPicker, "MinWidth"));
        Assert.Contains(
            modelPicker.Descendants(),
            element => string.Equals(element.Name.LocalName, "TextBlock"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "TextTrimming")
, "CharacterEllipsis", StringComparison.Ordinal));

        var stop = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Button"
, StringComparison.Ordinal) && string.Equals(
                    AttributeValue(element, "Click"),
                    "OnCancelAgentChatClick",
                    StringComparison.Ordinal));
        Assert.Equal(
            "{Binding AgentChat.ShowStopAction, FallbackValue=False}",
            AttributeValue(stop, "IsVisible"));
        Assert.Same(composerToolbar, stop.Parent);
        Assert.Equal("5", AttributeValue(stop, "Grid.Column"));

        var committedReasoning = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "StackPanel"
, StringComparison.Ordinal) && string.Equals(
                    AttributeValue(element, "AutomationProperties.Name"),
                    "AI reasoning summary",
                    StringComparison.Ordinal));
        var reasoningDisclosure = Assert.Single(
            committedReasoning.Descendants(),
            element => string.Equals(element.Name.LocalName, "ToggleButton"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Name")
, "CommittedReasoningDisclosure", StringComparison.Ordinal));
        Assert.Equal("False", AttributeValue(reasoningDisclosure, "IsChecked"));
        Assert.Contains(
            committedReasoning.Descendants(),
            element => string.Equals(element.Name.LocalName, "Border"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "BorderThickness"), "1,0,0,0"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "IsVisible")
, "{Binding IsChecked, ElementName=CommittedReasoningDisclosure}", StringComparison.Ordinal));
        Assert.Contains(
            committedReasoning.Descendants(),
            element => string.Equals(element.Name.LocalName, "MarkdownPreviewView"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Text")
, "{Binding ReasoningSummaryDisplay}", StringComparison.Ordinal));
        Assert.Contains(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "MarkdownPreviewView"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Text")
, "{Binding AgentChat.ProvisionalReasoningStageDisplay}", StringComparison.Ordinal));
        var reasoningLoader = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "ProgressBar"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "AutomationProperties.Name")
, "Reasoning in progress", StringComparison.Ordinal));
        Assert.Equal("1", AttributeValue(reasoningLoader, "Grid.Row"));
        Assert.Equal("2", AttributeValue(reasoningLoader, "Grid.ColumnSpan"));
        Assert.Equal("0", AttributeValue(reasoningLoader, "MinWidth"));
        Assert.Equal("Stretch", AttributeValue(reasoningLoader, "HorizontalAlignment"));
        Assert.Equal(
            "{Binding AgentChat.ShowProvisionalReasoningLoader, FallbackValue=False}",
            AttributeValue(reasoningLoader, "IsVisible"));
        var provisionalReasoningBody = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Border"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "IsVisible")
, "{Binding IsChecked, ElementName=ProvisionalReasoningDisclosure}", StringComparison.Ordinal));
        Assert.Equal(
            "{controls:Inset Top=Xs, Left=Sm, Bottom=Xs}",
            AttributeValue(provisionalReasoningBody, "Margin"));
        Assert.Equal(
            "{controls:Inset Left=Md}",
            AttributeValue(provisionalReasoningBody, "Padding"));
        var reasoningHover = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Style"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Selector")
, "ToggleButton.ReasoningTraceDisclosure:pointerover", StringComparison.Ordinal));
        Assert.Contains(
            reasoningHover.Elements(),
            element => string.Equals(AttributeValue(element, "Property"), "Background"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Value"), "Transparent", StringComparison.Ordinal));
        var messages = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "ItemsControl"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Name")
, "AgentChatMessages", StringComparison.Ordinal));
        Assert.Null(AttributeValue(messages, "ItemsSource"));
        Assert.Contains(
            messages.Descendants(),
            element => string.Equals(element.Name.LocalName, "Setter"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Property")
, "HorizontalContentAlignment"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Value"), "Stretch", StringComparison.Ordinal));
        Assert.DoesNotContain(
            messages.Descendants(),
            element => string.Equals(
                element.Name.LocalName,
                "VirtualizingStackPanel",
                StringComparison.Ordinal));
        Assert.Contains(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "MarkdownPreviewView"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Text"), "{Binding Content}", StringComparison.Ordinal));
        Assert.DoesNotContain(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "TextBlock"
, StringComparison.Ordinal) && (string.Equals(AttributeValue(element, "Text"), "{Binding Content}"
, StringComparison.Ordinal) || string.Equals(AttributeValue(element, "Text")
, "{Binding ReasoningSummaryDisplay}", StringComparison.Ordinal)));
        var copyButtons = root.Descendants()
            .Where(element => string.Equals(element.Name.LocalName, "Button"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Click"), "OnCopyAgentMessageClick", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(copyButtons);
        Assert.All(
            copyButtons,
            button => Assert.Equal(
                "{Binding HasMessageText}",
                AttributeValue(button, "IsVisible")));
        Assert.Contains(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Button"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Click"), "OnForkAgentConversationClick"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "IsVisible"), "{Binding CanFork}", StringComparison.Ordinal));
    }

    [Fact]
    public void Agent_toolbar_icon_pulses_slowly_only_while_a_run_is_active()
    {
        var root = Assert.IsType<XElement>(LoadView().Root);
        var pulseIcon = FindNamedElement(root, "AgentActivityPulseIcon");

        Assert.Equal("0", AttributeValue(pulseIcon, "Opacity"));
        Assert.Equal(
            "{Binding AgentChat.IsBusy}",
            AttributeValue(pulseIcon, "Classes.running"));
        Assert.Equal(
            "{DynamicResource ShellAccentBrush}",
            AttributeValue(pulseIcon, "Foreground"));

        var pulseStyle = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Style"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Selector")
, "Window:not(.motion-disabled) icons|SymbolIcon.AgentActivityPulse.running", StringComparison.Ordinal));
        var animation = Assert.Single(
            pulseStyle.Descendants(),
            element => string.Equals(element.Name.LocalName, "Animation", StringComparison.Ordinal));
        Assert.Equal("0:0:5", AttributeValue(animation, "Duration"));
        Assert.Equal("INFINITE", AttributeValue(animation, "IterationCount"));
        Assert.Equal(
            ["0%", "50%", "100%"],
            animation.Elements()
                .Where(element => string.Equals(element.Name.LocalName, "KeyFrame", StringComparison.Ordinal))
                .Select(element => AttributeValue(element, "Cue")), StringComparer.Ordinal);
    }

    [Fact]
    public void Model_picker_keeps_filter_and_speed_separate_while_reasoning_stays_in_composer()
    {
        var root = Assert.IsType<XElement>(LoadView().Root);
        var picker = FindNamedElement(root, "AgentModelPickerButton");
        var flyout = Assert.Single(
            picker.Descendants(),
            element => string.Equals(element.Name.LocalName, "Flyout", StringComparison.Ordinal));
        Assert.Equal("AgentModelMenu", AttributeValue(flyout, "FlyoutPresenterClasses"));
        Assert.Equal("OnModelMenuOpened", AttributeValue(flyout, "Opened"));

        var layout = Assert.Single(
            flyout.Elements(),
            element => string.Equals(element.Name.LocalName, "Grid", StringComparison.Ordinal));
        Assert.Equal("Auto,*,Auto", AttributeValue(layout, "RowDefinitions"));

        // A menu that filters: the shared search field above a list with a
        // selection role, where Return chooses.
        Assert.DoesNotContain(
            layout.Descendants(),
            element => string.Equals(element.Name.LocalName, "TextBox", StringComparison.Ordinal));
        var filter = Assert.Single(
            layout.Descendants(),
            element => string.Equals(element.Name.LocalName, "SearchField"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "PlaceholderText"), "Filter models", StringComparison.Ordinal));
        Assert.Equal(
            "{Binding AgentChat.ModelSearch, Mode=TwoWay}",
            AttributeValue(filter, "Text"));
        Assert.Equal("OnModelFilterKeyDown", AttributeValue(filter, "KeyDown"));

        var modelList = Assert.Single(
            layout.Descendants(),
            element => string.Equals(element.Name.LocalName, "ListBox"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "ItemsSource")
, "{Binding AgentChat.FilteredModels}", StringComparison.Ordinal));
        Assert.Equal("AgentModelList", AttributeValue(modelList, "Name"));
        Assert.Equal("Single", AttributeValue(modelList, "SelectionMode"));
        Assert.Equal("OnModelListKeyDown", AttributeValue(modelList, "KeyDown"));
        Assert.Equal(
            "1",
            AttributeValue(modelList.Ancestors().First(element => string.Equals(element.Name.LocalName, "Grid", StringComparison.Ordinal)), "Grid.Row"));
        Assert.DoesNotContain(
            layout.Descendants(),
            element => HasClass(element, "FlyoutMenuItem"));
        var mark = Assert.Single(
            modelList.Descendants(),
            element => string.Equals(AttributeValue(element, "Name"), "CurrentModelMark", StringComparison.Ordinal));
        Assert.Equal("Checkmark", AttributeValue(mark, "Symbol"));
        Assert.Equal("{Binding IsCurrent}", AttributeValue(mark, "IsVisible"));
        Assert.Contains(
            modelList.Descendants(),
            element => string.Equals(AttributeValue(element, "Tag"), "{Binding Model}", StringComparison.Ordinal));
        Assert.Contains(
            modelList.Descendants(),
            element => string.Equals(element.Name.LocalName, "TextBlock"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Text"), "{Binding ProviderName}", StringComparison.Ordinal));
        Assert.DoesNotContain(
            modelList.Descendants(),
            element => string.Equals(element.Name.LocalName, "TextBlock"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Text"), "{Binding Id}", StringComparison.Ordinal));
        var favorite = Assert.Single(
            modelList.Descendants(),
            element => string.Equals(element.Name.LocalName, "Button"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Click")
, "OnToggleFavoriteModelClick", StringComparison.Ordinal));
        Assert.Equal("{Binding}", AttributeValue(favorite, "Tag"));
        Assert.Equal(
            "{Binding FavoriteAccessibleName}",
            AttributeValue(favorite, "AutomationProperties.Name"));

        // The chosen model reaches the host from the row that carries it.
        var codeBehind = ApplicationViews.FindUniqueCodeBehindSourceContaining(
            "public sealed partial class AgentWorkspaceView");
        Assert.Contains("SelectAgentModelRequested?.Invoke(carrier,", codeBehind, StringComparison.Ordinal);

        var footer = Assert.Single(
            layout.Elements(),
            element => string.Equals(element.Name.LocalName, "Border"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Grid.Row"), "2", StringComparison.Ordinal));
        Assert.Equal("0,1,0,0", AttributeValue(footer, "BorderThickness"));
        var reasoning = Assert.Single(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "ComboBox"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "AutomationProperties.Name")
, "AI reasoning effort", StringComparison.Ordinal));
        Assert.Equal("2", AttributeValue(reasoning, "Grid.Column"));
        Assert.DoesNotContain(reasoning, picker.Descendants());
        Assert.Equal("{Binding AgentChat.HasMultipleReasoningEfforts}", AttributeValue(reasoning, "IsVisible"));
        var serviceTier = Assert.Single(
            footer.Descendants(),
            element => string.Equals(element.Name.LocalName, "ComboBox"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "AutomationProperties.Name")
, "AI service tier", StringComparison.Ordinal));
        Assert.Equal("1", AttributeValue(serviceTier, "Grid.Column"));
        Assert.Equal(
            "{Binding AgentChat.HasServiceTiers}",
            AttributeValue(
                serviceTier.Ancestors().First(element => string.Equals(element.Name.LocalName, "Grid", StringComparison.Ordinal)),
                "IsVisible"));
    }

    [Fact]
    public void Conversation_picker_uses_the_same_filter_list_footer_structure()
    {
        var root = Assert.IsType<XElement>(LoadView().Root);
        var picker = FindNamedElement(root, "AgentConversationHistoryButton");
        var flyout = Assert.Single(
            picker.Descendants(),
            element => string.Equals(element.Name.LocalName, "Flyout", StringComparison.Ordinal));
        Assert.Equal(
            "AgentConversationMenu",
            AttributeValue(flyout, "FlyoutPresenterClasses"));

        var layout = Assert.Single(
            flyout.Elements(),
            element => string.Equals(element.Name.LocalName, "Grid", StringComparison.Ordinal));
        Assert.Equal("Auto,*,Auto", AttributeValue(layout, "RowDefinitions"));

        var filter = Assert.Single(
            layout.Descendants(),
            element => string.Equals(element.Name.LocalName, "SearchField"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "PlaceholderText")
, "Filter conversations", StringComparison.Ordinal));
        Assert.Equal(
            "{Binding AgentChat.ConversationSearch, Mode=TwoWay}",
            AttributeValue(filter, "Text"));

        var conversations = Assert.Single(
            layout.Descendants(),
            element => string.Equals(element.Name.LocalName, "ItemsControl"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "ItemsSource")
, "{Binding AgentChat.FilteredConversations}", StringComparison.Ordinal));
        Assert.Contains(
            conversations.Descendants(),
            element => string.Equals(element.Name.LocalName, "TextBlock"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Text"), "{Binding Details}", StringComparison.Ordinal));

        var footer = Assert.Single(
            layout.Elements(),
            element => string.Equals(element.Name.LocalName, "Border"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Grid.Row"), "2", StringComparison.Ordinal));
        Assert.Equal("0,1,0,0", AttributeValue(footer, "BorderThickness"));
        Assert.Contains(
            footer.Descendants(),
            element => string.Equals(element.Name.LocalName, "Button"
, StringComparison.Ordinal) && string.Equals(AttributeValue(element, "Click")
, "OnStartNewConversationClick", StringComparison.Ordinal));
    }

    [Fact]
    public void Full_access_is_a_normal_run_scoped_agent_option()
    {
        var root = Assert.IsType<XElement>(LoadView().Root);
        var access = FindNamedElement(root, "AgentAccessModeButton");
        var menu = Assert.Single(
            access.Descendants(),
            element => string.Equals(element.Name.LocalName, "MenuFlyout", StringComparison.Ordinal));
        var modes = menu.Elements()
            .Where(element => string.Equals(element.Name.LocalName, "MenuItem", StringComparison.Ordinal))
            .ToArray();
        Assert.Equal(
            ["Ask for Approval", "Full Access"],
            modes.Select(element => AttributeValue(element, "Header")),
            StringComparer.Ordinal);
        Assert.All(modes, element =>
        {
            Assert.Equal("Radio", AttributeValue(element, "ToggleType"));
            Assert.Equal("AgentApprovalMode", AttributeValue(element, "GroupName"));
        });
        Assert.Equal(
            "{Binding AgentChat.IsAskForApprovalSelected, Mode=OneWay, FallbackValue=False}",
            AttributeValue(modes[0], "IsChecked"));
        Assert.Equal(
            "{Binding AgentChat.IsFullAccessSelected, Mode=OneWay, FallbackValue=False}",
            AttributeValue(modes[1], "IsChecked"));
        var fullAccess = modes[1];

        Assert.Null(AttributeValue(fullAccess, "IsEnabled"));
        Assert.DoesNotContain(
            root.Descendants(),
            element => (AttributeValue(element, "Text") ?? string.Empty)
                .Contains("exact terminal scope", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Agent_surface_announces_only_through_its_one_crash_safe_status_region()
    {
        var root = ApplicationViews.RepositoryRoot;
        var agentViews = new[]
            {
                Path.Combine(
                    root,
                    "src",
                    "Asura.App",
                    "Views",
                    "AgentWorkspaceView.axaml"),
            }
            .Concat(Directory.GetFiles(
                Path.Combine(
                    root,
                    "src",
                    "Asura.App",
                    "Views",
                    "Components"),
                "Agent*View.axaml",
                SearchOption.TopDirectoryOnly));

        // Avalonia 12.0.5's macOS bridge terminates the process on a nil live
        // announcement from streaming or detached controls, so the agent
        // surface itself has no live region.
        Assert.DoesNotContain(
            agentViews.Select(XDocument.Load).SelectMany(document => document.Descendants()),
            element => AttributeValue(element, "AutomationProperties.LiveSetting") is not null);

        // Its one status region is the LiveRegionTextBlock, which never
        // announces empty text, fed discrete events rather than the stream.
        // It sits in the workspace's root grid, outside the agent overlay, so
        // a hidden agent panel is still heard.
        var workspace = XDocument.Load(Path.Combine(root, "src", "Asura.App", "Views", "WorkspaceView.axaml"));
        var region = Assert.Single(
            workspace.Descendants(),
            element => string.Equals(AttributeValue(element, "Name"), "AgentAnnouncementRegion", StringComparison.Ordinal));
        Assert.Equal(
            XName.Get("LiveRegionTextBlock", "using:Asura.App.Controls"),
            region.Name);
        Assert.Equal(
            "{Binding AgentChat.Announcement, FallbackValue=''}",
            AttributeValue(region, "Text"));
        Assert.Equal("Polite", AttributeValue(region, "AutomationProperties.LiveSetting"));
        var rootGrid = Assert.IsType<XElement>(workspace.Root)
            .Elements()
            .Single(element => string.Equals(element.Name.LocalName, "Grid", StringComparison.Ordinal));
        Assert.Same(rootGrid, region.Parent);
    }

    [Fact]
    public void Send_raw_secrets_is_a_checkbox_that_explains_itself_in_view()
    {
        var root = Assert.IsType<XElement>(LoadView().Root);
        Assert.DoesNotContain(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "ToggleSwitch", StringComparison.Ordinal));

        var option = FindNamedElement(root, "AgentSendRawSecrets");
        Assert.Equal("CheckBox", option.Name.LocalName);
        Assert.Equal("Send raw secrets", AttributeValue(option, "Content"));
        Assert.Equal(
            "{Binding AgentChat.SendRawSecrets, Mode=TwoWay}",
            AttributeValue(option, "IsChecked"));
        Assert.Null(AttributeValue(option, "ToolTip.Tip"));

        var help = FindNamedElement(root, "AgentSendRawSecretsHelp");
        Assert.Equal("TextBlock", help.Name.LocalName);
        Assert.Equal("{Binding AgentChat.SendRawSecretsHelp}", AttributeValue(help, "Text"));
        Assert.Equal("Wrap", AttributeValue(help, "TextWrapping"));
        Assert.Same(option.Parent, help.Parent);
    }

    [Fact]
    public void Failed_run_recovery_is_secondary_and_names_what_it_clears()
    {
        var root = Assert.IsType<XElement>(LoadView().Root);
        var failedState = FindNamedElement(root, "AgentFailedTurnState");
        var clear = FindNamedElement(failedState, "AgentClearFailedRunButton");
        var delete = FindNamedElement(failedState, "AgentDeleteFailedConversationButton");

        // Clearing a run is routine; deleting the saved conversation with it
        // is a separate, destructive button rather than a relabeled one.
        Assert.Equal("Clear Failed Run", AttributeValue(clear, "Content"));
        Assert.Equal("SecondaryButton", AttributeValue(clear, "Classes"));
        Assert.Equal("OnClearAgentChatClick", AttributeValue(clear, "Click"));
        Assert.Equal("Delete Conversation…", AttributeValue(delete, "Content"));
        Assert.Equal("DestructiveButton", AttributeValue(delete, "Classes"));
        Assert.Equal("OnClearAgentChatClick", AttributeValue(delete, "Click"));
        Assert.DoesNotContain(
            failedState.Descendants(),
            element => HasClass(element, "PrimaryButton"));
    }

    [Fact]
    public void Composer_accepts_dropped_files_and_marks_itself_as_the_target()
    {
        var root = Assert.IsType<XElement>(LoadView().Root);
        var composer = FindNamedElement(root, "AgentComposer");

        Assert.Equal("True", AttributeValue(composer, "DragDrop.AllowDrop"));
        Assert.Equal("OnAgentComposerDragOver", AttributeValue(composer, "DragDrop.DragEnter"));
        Assert.Equal("OnAgentComposerDragOver", AttributeValue(composer, "DragDrop.DragOver"));
        Assert.Equal("OnAgentComposerDragLeave", AttributeValue(composer, "DragDrop.DragLeave"));
        Assert.Equal("OnAgentComposerDrop", AttributeValue(composer, "DragDrop.Drop"));
        Assert.Contains(
            root.Descendants(),
            element => string.Equals(element.Name.LocalName, "Style", StringComparison.Ordinal)
                && string.Equals(
                    AttributeValue(element, "Selector"),
                    "Border#AgentComposer.dropTarget",
                    StringComparison.Ordinal));
    }

    [Fact]
    public void Context_usage_is_a_gauge_with_its_value_not_a_progress_bar()
    {
        var root = Assert.IsType<XElement>(LoadView().Root);
        var gauge = FindNamedElement(root, "AgentContextWindowGauge");
        var flyout = Assert.Single(
            gauge.Ancestors(),
            element => string.Equals(element.Name.LocalName, "Flyout", StringComparison.Ordinal));

        Assert.Equal("ContextWindowDonut", gauge.Name.LocalName);
        Assert.Equal(
            "{Binding AgentChat.ContextWindowPercent}",
            AttributeValue(gauge, "Percentage"));
        Assert.Equal("{Binding AgentChat.ContextWindowAccessibleName}", AttributeValue(gauge, "AutomationProperties.Name"));
        Assert.Contains(
            flyout.Descendants(),
            element => string.Equals(
                AttributeValue(element, "Text"),
                "{Binding AgentChat.ContextWindowPercentLabel}",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            flyout.Descendants(),
            element => string.Equals(element.Name.LocalName, "ProgressBar", StringComparison.Ordinal));
    }

    [Fact]
    public void Run_card_names_the_panel_and_keeps_identifiers_in_details()
    {
        var root = Assert.IsType<XElement>(LoadView().Root);
        var heading = Assert.Single(
            root.Descendants(),
            element => (AttributeValue(element, "Text") ?? string.Empty)
                .Contains("Running in", StringComparison.Ordinal));
        Assert.Equal(
            "{Binding AgentChat.ActiveTool.TargetDisplayTitle, StringFormat={}Running in {0}}",
            AttributeValue(heading, "Text"));

        var details = FindNamedElement(root, "AgentToolActivityDetails");
        foreach (var identifier in new[] { "ActiveTool.ToolName", "ActiveTool.TargetTitle," })
        {
            var element = Assert.Single(
                root.Descendants(),
                element => (AttributeValue(element, "Text") ?? string.Empty)
                    .Contains(identifier, StringComparison.Ordinal));
            Assert.Contains(details, element.Ancestors());
        }
    }

    [Fact]
    public void Decision_cards_are_named_so_the_view_can_focus_them()
    {
        var root = Assert.IsType<XElement>(LoadView().Root);

        Assert.Equal("AgentApprovalCardView", FindNamedElement(root, "AgentApprovalCard").Name.LocalName);
        Assert.Equal("AgentQuestionCardView", FindNamedElement(root, "AgentQuestionCard").Name.LocalName);
        Assert.Equal(
            "AgentCapabilityRequestCardView",
            FindNamedElement(root, "AgentCapabilityRequestCard").Name.LocalName);
    }

    [Fact]
    public void Agent_workspace_forwards_original_input_without_owning_policy()
    {
        var codeBehind = ApplicationViews.FindUniqueCodeBehindSourceContaining(
            "public sealed partial class AgentWorkspaceView");
        var transcriptCode = ApplicationViews.FindUniqueCodeBehindSourceContaining(
            "private const int AgentTranscriptPageSize");

        foreach (var interaction in Interactions.Keys)
        {
            Assert.Contains($" {interaction};", codeBehind, StringComparison.Ordinal);
            Assert.Contains(
                $"{interaction}?.Invoke(sender, e);",
                codeBehind,
                StringComparison.Ordinal);
        }

        Assert.Contains(
            "event EventHandler<KeyEventArgs>? AgentQuestionResponseKeyDownRequested;",
            codeBehind,
            StringComparison.Ordinal);
        Assert.Contains(
            "private void OnAgentChatTranscriptScrollChanged(",
            transcriptCode,
            StringComparison.Ordinal);
        Assert.Contains(
            "RequestAgentChatScrollToEnd(force: false);",
            transcriptCode,
            StringComparison.Ordinal);

        Assert.DoesNotContain("async ", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("CancellationTokenSource", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowDialog", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("StorageProvider", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("MainWindowViewModel", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_floatingWidth", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_floatingHeight", codeBehind, StringComparison.Ordinal);
        Assert.Contains("_dockedWidth", codeBehind, StringComparison.Ordinal);
        Assert.Contains(
            "PreserveSizeAcrossPresentationChanges();",
            codeBehind,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Agent_workspace_markup_routes_every_interaction_to_its_local_relay()
    {
        var root = Assert.IsType<XElement>(LoadView().Root);

        foreach (var (interaction, handler) in Interactions)
        {
            Assert.Contains(
                root.Descendants(),
                element => string.Equals(
                    AttributeValue(element, InteractionAttribute(interaction)),
                    handler,
                    StringComparison.Ordinal));
        }
    }

    private static readonly string[] NamedControls =
    [
        "AgentChatTranscript",
        "AgentComposerToolbar",
        "AgentCurrentProgress",
        "AgentRunAudit",
        "AgentChatPromptInput",
    ];

    private static string InteractionAttribute(string interaction) =>
        interaction switch
        {
            "AgentQuestionResponseKeyDownRequested" => "ResponseKeyDownRequested",
            "ApproveAgentActionRequested" => "ApproveRequested",
            "DeclineAgentQuestionRequested" => "DeclineRequested",
            "DenyAgentActionRequested" => "DenyRequested",
            "EnableAgentCapabilityAskRequested" => "EnableAskRequested",
            "KeepAgentCapabilityOffRequested" => "KeepOffRequested",
            "SubmitAgentQuestionRequested" => "SubmitRequested",
            _ => "Click",
        };

    private static XElement FindNamedElement(XElement root, string name) =>
        Assert.Single(
            root.Descendants(),
            element => string.Equals(
                AttributeValue(element, "Name"),
                name,
                StringComparison.Ordinal));

    private static bool HasClass(XElement element, string className) =>
        (AttributeValue(element, "Classes") ?? string.Empty)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Contains(className, StringComparer.Ordinal);

    private static XDocument LoadView() =>
        XDocument.Load(Path.Combine(
            ApplicationViews.RepositoryRoot,
            "src",
            "Asura.App",
            "Views",
            "AgentWorkspaceView.axaml"));

    private static string? AttributeValue(XElement element, string name) =>
        element.Attributes()
            .FirstOrDefault(attribute => string.Equals(attribute.Name.LocalName, name, StringComparison.Ordinal))
            ?.Value;
}
