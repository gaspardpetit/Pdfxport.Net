// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.
using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class TextPaintScaleTests : IDisposable
{
    public TextPaintScaleTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(.5, 1)]
    [InlineData(1.5, 1)]
    [InlineData(.5, 2)]
    [InlineData(2, .5)]
    public void PaintScalePreservesLayoutGlyphOrigins(double paint, double layout)
    {
        var selected = Render(paint, layout);
        var original = Render(null, layout);
        selected.FollowingX.Should().BeApproximately(original.FollowingX, .001);
        selected.FollowingY.Should().BeApproximately(original.FollowingY, .001);
        selected.Matrices.Should().HaveCount(5);
        for (var index = 0; index < 5; index++)
        {
            selected.Matrices[index].Scale.Should().BeApproximately(paint, .000001);
            selected.Matrices[index].X.Should().BeApproximately(20 + Width("Alpha ") + Width("Bravo"[..index]) * layout, .001);
        }
    }

    [Theory]
    [InlineData(.5, 1, false)]
    [InlineData(2, -1, true)]
    public void PaintScaleDoesNotChangeWrapping(double paint, double overrun, bool fits)
    {
        var selected = Render(paint, overrun: overrun, suffix: false);
        (Math.Abs(selected.WordY - selected.FirstY) < .001).Should().Be(fits);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidSelectionFallsBackToLayoutScale(double paint)
        => Render(paint, layout: 1.5).Content.Should().Be(Render(null, layout: 1.5).Content);

    [Theory]
    [InlineData(1)]
    [InlineData(.5)]
    [InlineData(2)]
    public void EqualPaintAndLayoutScalesPreserveOutput(double scale)
        => Render(scale, scale).Content.Should().Be(Render(null, scale).Content);

    [Theory]
    [InlineData(.5)]
    [InlineData(1.5)]
    public void PointAdjustmentsRetainTheirLayoutAndPaintOrigins(double paint)
    {
        var selected = Render(paint, layout: 2, trailing: 2, kerning: -1);
        var original = Render(null, layout: 2, trailing: 2, kerning: -1);
        selected.FollowingX.Should().BeApproximately(original.FollowingX, .001);
        selected.Matrices[^1].X.Should().BeApproximately(20 + Width("Alpha ") + 2 * Width("Brav") - 1, .001);
    }

    [Fact]
    public void PaintScaleDoesNotSplitSurrogatePair()
    {
        var selected = Render(.5, word: "A\U0001F600B");
        selected.Matrices.Should().HaveCount(3);
        selected.Matrices[2].X.Should().BeApproximately(20 + Width("Alpha ") + Width("A\U0001F600"), .001);
        selected.FollowingX.Should().BeApproximately(Render(null, word: "A\U0001F600B").FollowingX, .001);
    }

    static Result Render(double? paint, double layout = 1, double? overrun = null,
        bool suffix = true, double? trailing = null, double? kerning = null, string word = "Bravo")
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        section.PageSetup.LeftMargin = Unit.FromPoint(20);
        section.PageSetup.RightMargin = Unit.FromPoint(20);
        if (overrun != null)
            section.PageSetup.PageWidth = Unit.FromPoint(40 + Width("Alpha ") + Width(word) * layout - overrun.Value);
        var paragraph = section.AddParagraph();
        paragraph.AddText("Alpha ");
        var tag = new object();
        paragraph.Elements.Add(new Text(word) { Tag = tag });
        if (suffix) paragraph.AddText(" Charlie");
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.TextHorizontalScale = actual => ReferenceEquals(actual, tag) ? layout : null;
        if (paint != null)
            renderer.DocumentRenderer.TextPaintHorizontalScale = (actual, font) =>
            {
                font.Size.Should().Be(12);
                return ReferenceEquals(actual, tag) ? paint : null;
            };
        if (trailing != null)
            renderer.DocumentRenderer.TextTrailingAdvance = actual => ReferenceEquals(actual, tag) ? trailing : null;
        if (kerning != null)
            renderer.DocumentRenderer.TrailingGlyphKerningWithTextTag = (_, actual, _, _) => ReferenceEquals(actual, tag) ? kerning : null;
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var reader = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        var positions = new List<(string Text, double X, double Y)>();
        while (reader.MoveAndGetNext(true, out var item)) positions.Add((item!.Text, item.X, item.Y));
        var first = positions.First(item => item.Text == "Alpha");
        var wordPosition = positions.First(item => item.Text != "Alpha");
        var following = positions.FirstOrDefault(item => item.Text == "Charlie");
        var content = PdfFileHelper.GetPageContentStream(pdf, 0);
        var matrices = Regex.Matches(content, @"(?m)^([\d.]+) 0 0 1 ([\d.]+) 0 cm")
            .Select(match => (Scale: double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                X: double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))).ToList();
        return new Result(first.Y, wordPosition.Y, following.X, following.Y, matrices, content);
    }

    static double Width(string text)
    {
        using var gfx = XGraphics.CreateMeasureContext(new XSize(300, 300), XGraphicsUnit.Point, XPageDirection.Downwards);
        return gfx.MeasureString(text, new XFont("Arial", 12)).Width;
    }
    record Result(double FirstY, double WordY, double FollowingX, double FollowingY,
        List<(double Scale, double X)> Matrices, string Content);
}
