// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

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
public class TerminalFitKerningTests : IDisposable
{
    public TerminalFitKerningTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }

    public void Dispose() => PdfSharpCore.ResetAll();

    [Fact]
    public void DecisionAdjustmentCanEnableHangingSpaceExpansion()
    {
        var baseline = Render(null);
        var adjusted = Render(0);
        adjusted.BravoX.Should().BeGreaterThan(baseline.BravoX);
        adjusted.BravoY.Should().Be(baseline.BravoY);
        adjusted.AlphaY.Should().Be(adjusted.BravoY);
        adjusted.Calls.Should().BeGreaterThan(0);
    }

    [Fact]
    public void DecisionAdjustmentAlsoControlsNearEdgeEligibility()
    {
        var baseline = Render(null, threshold: 10, nearEdge: 0.25);
        var adjusted = Render(0, threshold: 10, nearEdge: 0.25);
        adjusted.BravoX.Should().BeGreaterThan(baseline.BravoX);
        adjusted.Content.Should().Be(Render(0).Content);
    }

    [Fact]
    public void DecisionAdjustmentDoesNotChangeExpansionAmount()
    {
        Render(0).Content.Should().Be(Render(-1).Content);
    }

    [Fact]
    public void DecisionAdjustmentCanSuppressExpansion()
    {
        Render(-3).Content.Should().Be(Render(null).Content);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidValuesRetainRenderAdjustment(double value)
    {
        Render(value).Content.Should().Be(Render(null).Content);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void MissingOrInvalidThresholdDoesNotInvokeSelector(double? threshold)
    {
        var adjusted = Render(0, threshold: threshold);
        adjusted.Calls.Should().Be(0);
        adjusted.Content.Should().Be(Render(null, threshold: threshold).Content);
    }

    [Fact]
    public void JustifiedParagraphDoesNotInvokeSelector()
    {
        var adjusted = Render(0, alignment: ParagraphAlignment.Justify);
        adjusted.Calls.Should().Be(0);
        adjusted.Content.Should().Be(Render(null, alignment: ParagraphAlignment.Justify).Content);
    }

    static Result Render(double? adjustment, double? threshold = 0.25,
        ParagraphAlignment alignment = ParagraphAlignment.Left, double? nearEdge = null)
    {
        using var measure = XGraphics.CreateMeasureContext(new XSize(300, 300),
            XGraphicsUnit.Point, XPageDirection.Downwards);
        var font = new XFont("Arial", 12);
        var width = measure.MeasureString("Alpha Bravo.", font).Width - 2;
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        section.PageSetup.PageWidth = Unit.FromPoint(width + 40);
        section.PageSetup.LeftMargin = Unit.FromPoint(20);
        section.PageSetup.RightMargin = Unit.FromPoint(20);
        var paragraph = section.AddParagraph("Alpha Bravo.");
        paragraph.Format.Alignment = alignment;
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.LineEndOverhang = (_, _, _) => 8;
        renderer.DocumentRenderer.AdditiveTerminalLineEndOverhang = true;
        renderer.DocumentRenderer.RenderOnlyTrailingGlyphKerning = (_, word, _) => word == "Bravo." ? -3 : null;
        if (threshold != null)
            renderer.DocumentRenderer.HangingTerminalLineSpaceThreshold = (_, _, _) => threshold;
        if (nearEdge != null)
            renderer.DocumentRenderer.HangingTerminalLineNearEdgeThreshold = (_, _, _) => nearEdge;
        int calls = 0;
        renderer.DocumentRenderer.HangingTerminalLineFitKerning = (actual, word, actualFont) =>
        {
            actual.Should().BeSameAs(paragraph);
            word.Should().Be("Bravo.");
            actualFont.Size.Should().Be(12);
            calls++;
            return adjustment;
        };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        pdf.PageCount.Should().Be(1);
        var reader = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        var positions = new List<(string Text, double X, double Y)>();
        while (reader.MoveAndGetNext(true, out var item))
            positions.Add((item!.Text, item.X, item.Y));
        var alpha = positions.First(item => item.Text == "Alpha");
        var bravo = positions.First(item => item.Text.StartsWith("Bravo", StringComparison.Ordinal));
        return new Result(bravo.X, bravo.Y, alpha.Y, calls, PdfFileHelper.GetPageContentStream(pdf, 0));
    }

    record Result(double BravoX, double BravoY, double AlphaY, int Calls, string Content);
}
