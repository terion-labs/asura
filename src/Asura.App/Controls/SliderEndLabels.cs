using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace Asura.App.Controls;

/// <summary>
/// A slider with what its two ends mean written under them, as System
/// Settings draws one in a grouped form: "Small" under the left end and
/// "Large" under the right. The slider is the content; the words are its
/// help text too, so a screen reader hears the range as well as the value.
/// </summary>
internal sealed class SliderEndLabels : ContentControl
{
    public static readonly StyledProperty<string?> StartLabelProperty =
        AvaloniaProperty.Register<SliderEndLabels, string?>(nameof(StartLabel));

    public static readonly StyledProperty<string?> EndLabelProperty =
        AvaloniaProperty.Register<SliderEndLabels, string?>(nameof(EndLabel));

    static SliderEndLabels()
    {
        StartLabelProperty.Changed.AddClassHandler<SliderEndLabels>((labels, _) => labels.Describe());
        EndLabelProperty.Changed.AddClassHandler<SliderEndLabels>((labels, _) => labels.Describe());
        ContentProperty.Changed.AddClassHandler<SliderEndLabels>((labels, _) => labels.Describe());
    }

    public string? StartLabel
    {
        get => GetValue(StartLabelProperty);
        set => SetValue(StartLabelProperty, value);
    }

    public string? EndLabel
    {
        get => GetValue(EndLabelProperty);
        set => SetValue(EndLabelProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(SliderEndLabels);

    /// <summary>
    /// The range, said once: "From Small to Large". A help text the view set
    /// on the slider itself is kept.
    /// </summary>
    private void Describe()
    {
        if (Content is not Slider slider
            || string.IsNullOrWhiteSpace(StartLabel)
            || string.IsNullOrWhiteSpace(EndLabel)
            || !string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(slider)))
        {
            return;
        }

        AutomationProperties.SetHelpText(slider, $"From {StartLabel} to {EndLabel}");
    }
}
