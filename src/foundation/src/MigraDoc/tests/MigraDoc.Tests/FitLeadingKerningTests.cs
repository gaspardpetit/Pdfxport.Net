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
public class FitLeadingKerningTests : IDisposable
{
    public FitLeadingKerningTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }

    public void Dispose() => PdfSharpCore.ResetAll();

    [Fact]
    public void FormattingAdjustmentCanFitWordWithoutPaintShift()
    {
        Render(null, overrun: 0.7).SameLine.Should().BeFalse();
        var selected = Render(-1.4, overrun: 0.7);
        selected.SameLine.Should().BeTrue();
        selected.WordX.Should().BeApproximately(selected.NaturalWordX, 0.001);
        selected.FitCalls.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(-1.4)]
    [InlineData(2)]
    public void FormattingAdjustmentDoesNotDirectlyMovePaintedWord(double value)
    {
        Render(value).Content.Should().Be(Render(null, install: false).Content);
    }

    [Fact]
    public void PositiveFormattingAdjustmentCanWrapWord()
    {
        Render(null, overrun: -1).SameLine.Should().BeTrue();
        var selected = Render(2, overrun: -1);
        selected.SameLine.Should().BeFalse();
        selected.WordX.Should().BeApproximately(20, 0.001);
    }

    [Fact]
    public void NullFallsBackToLegacyAdjustment()
    {
        Render(null, legacy: -1.4, overrun: 0.7).Content.Should()
            .Be(Render(null, legacy: -1.4, overrun: 0.7, install: false).Content);
    }

    [Theory]
    [InlineData(-0.5)]
    [InlineData(0)]
    public void TaggedSelectionTakesPrecedence(double tagged)
    {
        var selected = Render(-1.4, tagged: tagged, overrun: 0.7);
        selected.FitCalls.Should().Be(0);
        selected.SameLine.Should().BeFalse();
        selected.Content.Should().Be(Render(null, tagged: tagged, overrun: 0.7, install: false).Content);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ZeroOrNonfiniteFitSelectionSuppressesLegacyDuringFormatting(double value)
    {
        Render(null, legacy: -1.4, overrun: 0.7, install: false).SameLine.Should().BeTrue();
        var selected = Render(value, legacy: -1.4, overrun: 0.7);
        selected.SameLine.Should().BeFalse();
        selected.WordX.Should().BeApproximately(20, 0.001);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Alpha")]
    [InlineData("Alpha\u00A0")]
    public void IneligibleBoundaryDoesNotInvokeSelector(string prefix)
    {
        var selected = Render(-1.4, prefix: prefix);
        selected.FitCalls.Should().Be(0);
        selected.Content.Should().Be(Render(null, prefix: prefix, install: false).Content);
    }

    static Result Render(double? fit, double? legacy = null, double? tagged = null,
        double? overrun = null, bool install = true, string prefix = "Alpha ")
    {
        using var measure = XGraphics.CreateMeasureContext(new XSize(300, 300),
            XGraphicsUnit.Point, XPageDirection.Downwards);
        var font = new XFont("Arial", 12);
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        section.PageSetup.LeftMargin = Unit.FromPoint(20);
        section.PageSetup.RightMargin = Unit.FromPoint(20);
        if (overrun != null)
            section.PageSetup.PageWidth = Unit.FromPoint(measure.MeasureString(prefix + "Bravo", font).Width - overrun.Value + 40);
        var paragraph = section.AddParagraph();
        if (prefix.Length > 0) paragraph.AddText(prefix);
        paragraph.AddFormattedText("Bravo");
        var renderer = new PdfDocumentRenderer { Document = document };
        int fitCalls = 0;
        if (install)
            renderer.DocumentRenderer.FitOnlyLeadingKerningAfterBlank = (actual, word, actualFont) =>
            {
                if (word != "Bravo") return null;
                actual.Should().BeSameAs(paragraph);
                actualFont.Size.Should().Be(12);
                fitCalls++;
                return fit;
            };
        if (legacy != null)
            renderer.DocumentRenderer.LeadingKerningAfterBlank = (_, word, _) => word == "Bravo" ? legacy : null;
        if (tagged != null)
            renderer.DocumentRenderer.LeadingKerningAfterBlankWithTextTag = (_, _, word, _) => word == "Bravo" ? tagged : null;
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        pdf.PageCount.Should().Be(1);
        var reader = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        var positions = new List<(string Text, double X, double Y)>();
        while (reader.MoveAndGetNext(true, out var item))
            positions.Add((item!.Text, item.X, item.Y));
        var wordPosition = positions.First(item => item.Text == "Bravo");
        var first = positions.First();
        return new Result(first.Y == wordPosition.Y, wordPosition.X,
            20 + measure.MeasureString(prefix, font).Width, fitCalls, PdfFileHelper.GetPageContentStream(pdf, 0));
    }

    record Result(bool SameLine, double WordX, double NaturalWordX, int FitCalls, string Content);
}
