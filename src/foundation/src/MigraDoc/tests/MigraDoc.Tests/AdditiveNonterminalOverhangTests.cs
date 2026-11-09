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
public class AdditiveNonterminalOverhangTests : IDisposable
{
    public AdditiveNonterminalOverhangTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }

    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CombinesAllowancesAtBreakableBoundary(bool bookmark, bool splitWord)
    {
        var baseline = Render(false, bookmark: bookmark, splitWord: splitWord);
        var adjusted = Render(true, bookmark: bookmark, splitWord: splitWord);
        baseline.SameLine.Should().BeFalse();
        adjusted.SameLine.Should().BeTrue();
        adjusted.TailY.Should().BeLessThan(adjusted.BravoY);
        adjusted.Words.Should().Equal(baseline.Words);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SingleCharacterFinalFragmentUsesCombinedAllowance(bool bookmark)
    {
        Render(false, splitWord: true, singleCharacter: true, bookmark: bookmark).SameLine.Should().BeFalse();
        var adjusted = Render(true, splitWord: true, singleCharacter: true, bookmark: bookmark);
        adjusted.SameLine.Should().BeTrue();
        adjusted.TailY.Should().BeLessThan(adjusted.BravoY);
    }

    [Fact]
    public void SingleCharacterLookaheadRespectsContractionCap()
    {
        Render(true, splitWord: true, singleCharacter: true, capContraction: true)
            .SameLine.Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidOverhangDoesNotChangeLayout(double allowance)
    {
        Render(true, allowance: allowance).Content.Should().Be(Render(false, allowance: allowance).Content);
    }

    [Fact]
    public void NonjustifiedParagraphDoesNotGainSpaceContraction()
    {
        Render(true, justify: false).Content.Should().Be(Render(false, justify: false).Content);
    }

    [Fact]
    public void NonterminalOptionDoesNotEnableTerminalAdditiveMode()
    {
        Render(true, continuation: "").Content.Should().Be(Render(false, continuation: "").Content);
    }

    [Theory]
    [InlineData("Charlie")]
    [InlineData("\u00A0Charlie")]
    [InlineData("\n")]
    [InlineData("\t")]
    public void NonbreakableAndTerminalBoundariesDoNotGainAdditiveAllowance(string continuation)
    {
        Render(true, continuation: continuation).Content.Should()
            .Be(Render(false, continuation: continuation).Content);
    }

    static Result Render(bool enabled, bool bookmark = false, bool splitWord = false,
        double allowance = 1, bool justify = true, string continuation = " Charlie",
        bool singleCharacter = false, bool capContraction = false)
    {
        using var measure = XGraphics.CreateMeasureContext(new XSize(300, 300),
            XGraphicsUnit.Point, XPageDirection.Downwards);
        var font = new XFont("Arial", 12);
        var closingWord = singleCharacter ? "Bravo" : "Bravo.";
        var width = measure.MeasureString("Alpha " + closingWord, font).Width - 1.6;
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        section.PageSetup.PageWidth = Unit.FromPoint(width + 40);
        section.PageSetup.LeftMargin = Unit.FromPoint(20);
        section.PageSetup.RightMargin = Unit.FromPoint(20);
        var paragraph = section.AddParagraph();
        paragraph.Format.Alignment = justify ? ParagraphAlignment.Justify : ParagraphAlignment.Left;
        paragraph.AddText("Alpha ");
        if (splitWord)
        {
            paragraph.AddFormattedText(singleCharacter ? "Brav" : "Bra");
            paragraph.AddFormattedText(singleCharacter ? "o" : "vo.");
        }
        else
            paragraph.AddFormattedText(closingWord);
        if (bookmark) paragraph.AddBookmark("Boundary", false);
        if (continuation.Length > 0) paragraph.AddText(continuation);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.AdditiveNonterminalLineEndOverhang = enabled;
        renderer.DocumentRenderer.JustifiedNonterminalSpaceShrinkFactorForParagraph = _ => 0.75;
        renderer.DocumentRenderer.JustifiedSpaceShrinkFactorForParagraph = _ => 0.75;
        renderer.DocumentRenderer.LineEndOverhang = (_, word, _) =>
            word is "Bravo." or "vo." or "Bravo" or "o" ? allowance : null;
        if (capContraction)
            renderer.DocumentRenderer.JustifiedNonterminalWordOverrunLimitForParagraph = _ => (0, 0);
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        pdf.PageCount.Should().Be(1);
        var reader = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        var positions = new List<(string Text, double X, double Y)>();
        while (reader.MoveAndGetNext(true, out var item))
            positions.Add((item!.Text, item.X, item.Y));
        var first = positions.First(item => item.Text == "Alpha");
        var bravo = positions.First(item => item.Text is "Bravo." or "Bra" or "Brav" or "Bravo");
        var tail = positions.FirstOrDefault(item => item.Text.Contains("Charlie", StringComparison.Ordinal));
        if (splitWord)
            positions.First(item => item.Text == (singleCharacter ? "o" : "vo.")).Y.Should().Be(bravo.Y);
        var content = PdfFileHelper.GetPageContentStream(pdf, 0);
        return new Result(first.Y == bravo.Y, bravo.Y, tail.Y,
            positions.Select(item => item.Text).ToArray(), content);
    }

    record Result(bool SameLine, double BravoY, double TailY, string[] Words, string Content);
}
