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
public class ExactNaturalBaselineTests : IDisposable
{
    public ExactNaturalBaselineTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(9, false, false)]
    [InlineData(12, false, false)]
    [InlineData(18, false, false)]
    [InlineData(12, true, false)]
    [InlineData(12, false, true)]
    [InlineData(12, true, true)]
    public void MatchingExactHeightUsesSingleSpacingBaseline(double size, bool bold, bool italic)
    {
        var natural = Height(size, bold, italic);
        var single = Render(size, bold, italic, null);
        var exact = Render(size, bold, italic, natural);
        exact.Baselines.Should().Equal(single.Baselines);
        exact.Height.Should().BeApproximately(single.Height, 0.001);
        exact.Pitch.Should().BeApproximately(natural, 0.001);
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(0.01)]
    public void RoundedNaturalHeightKeepsNaturalDescent(double delta)
    {
        var natural = Height(12, false, false);
        var single = Render(12, false, false, null);
        var exact = Render(12, false, false, natural + delta);
        (exact.Baselines[0] - single.Baselines[0]).Should().BeApproximately(delta, 0.001);
        exact.Pitch.Should().BeApproximately(natural + delta, 0.001);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(0.1)]
    [InlineData(-3)]
    [InlineData(5)]
    public void OtherExactHeightsRetainScaledParagraphDescent(double delta)
    {
        var natural = Height(12, false, false);
        var single = Render(12, false, false, null);
        var exact = Render(12, false, false, natural + delta);
        var descent = natural - single.Baselines[0];
        exact.Baselines[0].Should().BeApproximately(
            natural + delta - descent * (natural + delta) / 12, 0.001);
        exact.Pitch.Should().BeApproximately(natural + delta, 0.001);
    }

    static double Height(double size, bool bold, bool italic) =>
        new XFont("Arial", size, (bold ? XFontStyleEx.Bold : XFontStyleEx.Regular) |
            (italic ? XFontStyleEx.Italic : XFontStyleEx.Regular)).GetHeight();

    record Result(List<double> Baselines, double Height, double Pitch);
    static Result Render(double size, bool bold, bool italic, double? exactHeight)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        var section = document.AddSection();
        var paragraph = section.AddParagraph();
        paragraph.Format.Font.Size = Unit.FromPoint(size);
        paragraph.Format.Font.Bold = bold;
        paragraph.Format.Font.Italic = italic;
        paragraph.Format.LineSpacingRule = exactHeight.HasValue ? LineSpacingRule.Exactly : LineSpacingRule.Single;
        if (exactHeight.HasValue) paragraph.Format.LineSpacing = Unit.FromPoint(exactHeight.Value);
        paragraph.AddText("First");
        paragraph.AddLineBreak();
        paragraph.AddText("Second");
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        pdf.PageCount.Should().Be(1);
        var area = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!.Single().LayoutInfo.ContentArea;
        var baselines = new List<double>();
        var text = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        while (text.MoveAndGetNext(true, out var item))
            baselines.Add(pdf.Pages[0].Height.Point - item!.Y - area.Y.Point);
        baselines.Count.Should().Be(2);
        return new Result(baselines, area.Height.Point, baselines[1] - baselines[0]);
    }
}
