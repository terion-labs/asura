using Asura.App.Controls;
using Asura.App.Views.Components;
using SkiaSharp;
using ShimFontStyleSlant = ShimSkiaSharp.SKFontStyleSlant;
using ShimFontStyleWeight = ShimSkiaSharp.SKFontStyleWeight;
using ShimFontStyleWidth = ShimSkiaSharp.SKFontStyleWidth;
using ShimTypeface = ShimSkiaSharp.SKTypeface;

namespace Asura.App.Tests;

/// <summary>
/// Mermaid diagrams draw their code text in the app's bundled data face. The
/// SVG renderer looks families up in the system font manager, where the
/// bundled JetBrains Mono does not exist: asked for by name, it came back as
/// Times.
/// </summary>
public sealed class DatabaseMermaidTypefaceTests
{
    [Fact]
    public void The_diagram_mono_family_is_the_bundled_data_face()
    {
        Assert.Equal(AsuraTerminalFontCollection.FamilyName, MermaidDiagramTypefaces.MonoFamily);

        var regular = MermaidDiagramTypefaces.Resolve(MermaidDiagramTypefaces.MonoFamily);
        Assert.NotNull(regular);
        Assert.Equal("JetBrains Mono", regular.FamilyName);
        Assert.True(regular.IsFixedPitch);

        var bold = MermaidDiagramTypefaces.Resolve(MermaidDiagramTypefaces.MonoFamily, SKFontStyleWeight.Bold);
        Assert.NotNull(bold);
        Assert.True(bold.FontStyle.Weight >= (int)SKFontStyleWeight.Bold);

        Assert.Null(MermaidDiagramTypefaces.Resolve("Inter"));
    }

    [Fact]
    public void A_loaded_diagram_resolves_the_mono_family_to_the_bundled_face()
    {
        const string svg = """
            <svg xmlns="http://www.w3.org/2000/svg" width="200" height="40">
              <text x="0" y="20" font-family="JetBrains Mono" font-size="13">users.id</text>
            </svg>
            """;

        using var source = MermaidDiagramTypefaces.LoadSvg(svg);
        Assert.NotNull(source.Picture);

        var requested = ShimTypeface.FromFamilyName(
            MermaidDiagramTypefaces.MonoFamily,
            ShimFontStyleWeight.Normal,
            ShimFontStyleWidth.Normal,
            ShimFontStyleSlant.Upright);
        var resolved = source.Svg!.SkiaModel.ToSKTypeface(requested);

        Assert.Same(MermaidDiagramTypefaces.Resolve(MermaidDiagramTypefaces.MonoFamily), resolved);
    }
}
