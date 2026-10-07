using System.Collections.Immutable;

namespace Asura.Core;

public enum KeymapIssueSeverity
{
    Warning,
    Error,
}

public enum KeymapIssueKind
{
    UnknownCommand,
    TerminalSequence,
    UnsupportedTerminalKey,
    ExactBinding,
    PrefixCollision,
    OverlappingContexts,
    ShadowedBinding,
    SystemShortcut,
    MenuShortcut,
}

public sealed record KeymapValidationIssue(
    KeymapIssueSeverity Severity,
    KeymapIssueKind Kind,
    int BindingIndex,
    int? OtherBindingIndex,
    string Message);

public static class KeymapConflictValidator
{
    public static ImmutableArray<KeymapValidationIssue> Validate(
        KeymapProfile profile,
        CommandRegistry registry) =>
        Validate(profile, registry, HostPlatform.Current);

    /// <summary>
    /// Checks a profile against itself and, on macOS, against the shortcuts
    /// the system and the menu bar answer before any keymap sees the key.
    /// Those two never block saving: the binding is kept, but it is reported
    /// because it will not fire.
    /// </summary>
    public static ImmutableArray<KeymapValidationIssue> Validate(
        KeymapProfile profile,
        CommandRegistry registry,
        HostOperatingSystem host)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(registry);

        var issues = ImmutableArray.CreateBuilder<KeymapValidationIssue>();
        for (var index = 0; index < profile.Bindings.Count; index++)
        {
            var binding = profile.Bindings[index];
            if (!registry.Contains(binding.CommandId))
            {
                issues.Add(new KeymapValidationIssue(
                    KeymapIssueSeverity.Warning,
                    KeymapIssueKind.UnknownCommand,
                    index,
                    null,
                    $"Command '{binding.CommandId}' is not available in this version. Its binding is preserved."));
            }

            if (profile.Layer == KeymapLayer.Terminal && binding.Sequence.Count != 1)
            {
                issues.Add(new KeymapValidationIssue(
                    KeymapIssueSeverity.Error,
                    KeymapIssueKind.TerminalSequence,
                    index,
                    null,
                    $"Terminal shortcut {Quote(binding.Sequence, host)} must contain exactly one key stroke. Multi-stroke sequences are available in application keymaps."));
            }
            else if (profile.Layer == KeymapLayer.Terminal
                && !TerminalKeyBindingRules.IsSupported(binding.Sequence[0]))
            {
                issues.Add(new KeymapValidationIssue(
                    KeymapIssueSeverity.Error,
                    KeymapIssueKind.UnsupportedTerminalKey,
                    index,
                    null,
                    $"Terminal key {Quote(KeySequence.Of(new KeyStroke(binding.Sequence[0].Key)), host)} is not supported by every desktop renderer."));
            }

            if (host == HostOperatingSystem.MacOS)
            {
                AddReservedShortcutIssue(binding, index, host, issues);
            }
        }

        for (var leftIndex = 0; leftIndex < profile.Bindings.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < profile.Bindings.Count; rightIndex++)
            {
                ValidatePair(profile.Bindings, leftIndex, rightIndex, host, issues);
            }
        }

        return issues.ToImmutable();
    }

    /// <summary>
    /// A sequence starts with its first stroke, so a reserved first stroke
    /// keeps the whole sequence from ever starting.
    /// </summary>
    private static void AddReservedShortcutIssue(
        CommandBinding binding,
        int index,
        HostOperatingSystem host,
        ImmutableArray<KeymapValidationIssue>.Builder issues)
    {
        var first = binding.Sequence[0];
        if (MacOsStandardShortcuts.TryFindSystem(first, out var system)
            && system.CommandId != binding.CommandId)
        {
            issues.Add(new KeymapValidationIssue(
                KeymapIssueSeverity.Warning,
                KeymapIssueKind.SystemShortcut,
                index,
                null,
                $"{Quote(KeySequence.Of(first), host)} is reserved by macOS for {system.Owner}, so this shortcut never reaches Asura."));
            return;
        }

        if (MacOsStandardShortcuts.TryFindMenu(first, out var menu)
            && menu.CommandId != binding.CommandId)
        {
            issues.Add(new KeymapValidationIssue(
                KeymapIssueSeverity.Warning,
                KeymapIssueKind.MenuShortcut,
                index,
                null,
                $"{Quote(KeySequence.Of(first), host)} is the menu shortcut for {menu.Owner}, so the menu runs instead of this command."));
        }
    }

    private static string Quote(KeySequence sequence, HostOperatingSystem host) =>
        $"“{ShortcutText.Format(sequence, host)}”";

    private static void ValidatePair(
        IReadOnlyList<CommandBinding> bindings,
        int leftIndex,
        int rightIndex,
        HostOperatingSystem host,
        ImmutableArray<KeymapValidationIssue>.Builder issues)
    {
        var left = bindings[leftIndex];
        var right = bindings[rightIndex];
        var relationship = GetContextRelationship(left.Contexts, right.Contexts);
        if (relationship == ContextRelationship.Disjoint)
        {
            return;
        }

        if (left.Sequence.Equals(right.Sequence))
        {
            AddExactSequenceIssue(left, right, leftIndex, rightIndex, relationship, host, issues);
            return;
        }

        if (left.Sequence.IsPrefixOf(right.Sequence) || right.Sequence.IsPrefixOf(left.Sequence))
        {
            issues.Add(new KeymapValidationIssue(
                KeymapIssueSeverity.Error,
                KeymapIssueKind.PrefixCollision,
                leftIndex,
                rightIndex,
                $"{Quote(left.Sequence, host)} and {Quote(right.Sequence, host)} cannot both resolve while their contexts overlap."));
        }
    }

    private static void AddExactSequenceIssue(
        CommandBinding left,
        CommandBinding right,
        int leftIndex,
        int rightIndex,
        ContextRelationship relationship,
        HostOperatingSystem host,
        ImmutableArray<KeymapValidationIssue>.Builder issues)
    {
        if (left.Contexts == right.Contexts)
        {
            issues.Add(new KeymapValidationIssue(
                KeymapIssueSeverity.Error,
                KeymapIssueKind.ExactBinding,
                leftIndex,
                rightIndex,
                $"{Quote(left.Sequence, host)} is assigned more than once in {left.Contexts}."));
            return;
        }

        if (relationship == ContextRelationship.SamePriorityOverlap)
        {
            issues.Add(new KeymapValidationIssue(
                KeymapIssueSeverity.Error,
                KeymapIssueKind.OverlappingContexts,
                leftIndex,
                rightIndex,
                $"{Quote(left.Sequence, host)} is ambiguous in contexts that can be active together."));
            return;
        }

        issues.Add(new KeymapValidationIssue(
            KeymapIssueSeverity.Warning,
            KeymapIssueKind.ShadowedBinding,
            leftIndex,
            rightIndex,
            $"{Quote(left.Sequence, host)} in the lower-priority context is shadowed whenever both contexts are active."));
    }

    private static ContextRelationship GetContextRelationship(CommandContext left, CommandContext right)
    {
        var hasOverlap = false;
        foreach (var leftContext in CommandContextRules.Enumerate(left))
        {
            foreach (var rightContext in CommandContextRules.Enumerate(right))
            {
                if (!CommandContextRules.CanBeActiveTogether(leftContext, rightContext))
                {
                    continue;
                }

                hasOverlap = true;
                if (CommandContextRules.ResolutionPriority(leftContext)
                    == CommandContextRules.ResolutionPriority(rightContext))
                {
                    return ContextRelationship.SamePriorityOverlap;
                }
            }
        }

        return hasOverlap ? ContextRelationship.PriorityOverlap : ContextRelationship.Disjoint;
    }

    private enum ContextRelationship
    {
        Disjoint,
        PriorityOverlap,
        SamePriorityOverlap,
    }
}
