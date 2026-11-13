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
public class WordSpaceContractionCapTests : IDisposable
{
    public WordSpaceContractionCapTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void CapUsesWholeWordAcrossFormattingRuns(bool split, bool mixedFont, bool final)
    {
        SameLine(Render(.031, split: split, mixedFont: mixedFont, final: final)).Should().BeTrue();
        SameLine(Render(.02, split: split, mixedFont: mixedFont, final: final)).Should().BeFalse();
    }

    [Fact]
    public void ValidNewCapReplacesLegacyWordOnlyCap()
    {
        SameLine(Render(null, legacy: 0)).Should().BeFalse();
        SameLine(Render(.031, legacy: 0)).Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidOrNullFractionRetainsLegacyCap(double? fraction)
    {
        Render(fraction, legacy: 0).Words.Should().Equal(Render(null, legacy: 0).Words);
        Render(fraction).Words.Should().Equal(Render(null).Words);
    }

    [Fact]
    public void LineSelectionOverridesParagraphAndNullFallsBack()
    {
        SameLine(Render(.031, line: _ => .02)).Should().BeFalse();
        SameLine(Render(.02, line: _ => .031)).Should().BeTrue();
        SameLine(Render(.031, line: _ => null)).Should().BeTrue();
        SameLine(Render(.031, line: _ => double.NaN, legacy: 0)).Should().BeFalse();
    }

    [Theory]
    [InlineData(2.0, 0.0)]
    [InlineData(1.0, 3.0)]
    public void CapUsesActualPrecedingBlankAdvance(double scale, double spacing)
        => SameLine(Render(.031, blankScale: scale, blankSpacing: spacing)).Should().BeTrue();

    [Fact]
    public void LineSelectionCountsPendingBlankAndOtherEligibleBlanks()
    {
        var counts = new List<int>();
        var result = Render(null, twoBlanks: true, line: count => { counts.Add(count); return count == 2 ? .031 : .0001; });
        SameLine(result).Should().BeTrue();
        counts.Should().Contain(2);
    }

    [Fact]
    public void IgnoredLeadingAndDuplicateBlanksDoNotIncreaseCount()
    {
        SameLine(Render(null, ignoredBlanks: true, line: count => count == 1 ? .031 : .0001)).Should().BeTrue();
    }

    [Fact]
    public void ZeroWidthBlankDoesNotIncreaseCount()
    {
        SameLine(Render(null, twoBlanks: true, blankSpacing: -Width(" "),
            line: count => count == 1 ? .031 : .0001)).Should().BeTrue();
    }

    [Fact]
    public void TabStartsAFreshContractionInterval()
    {
        var result = Render(null, tabs: true, line: count => count == 1 ? .031 : .0001);
        SameLine(result).Should().BeTrue();
    }

    [Fact]
    public void HyphenContinuationDoesNotBorrowAnAbsentBlank()
    {
        SameLine(Render(.031, hyphen: true)).Should().BeTrue();
        SameLine(Render(.029, hyphen: true)).Should().BeFalse();
    }

    [Theory]
    [InlineData(ParagraphAlignment.Left)]
    [InlineData(ParagraphAlignment.Center)]
    [InlineData(ParagraphAlignment.Right)]
    public void OtherAlignmentsDoNotUseCap(ParagraphAlignment alignment)
    {
        var calls = 0;
        var selected = Render(.031, alignment: alignment, line: _ => { calls++; return .031; });
        calls.Should().Be(0);
        selected.Words.Should().Equal(Render(null, alignment: alignment).Words);
    }

    [Fact]
    public void CapDoesNotEnableContraction()
    {
        var calls = 0;
        SameLine(Render(.031, enableShrink: false, line: _ => { calls++; return .031; })).Should().BeFalse();
        calls.Should().Be(0);
    }

    [Fact]
    public void SelectorsAreDisabledByDefault()
    {
        var renderer = new DocumentRenderer(new Document());
        renderer.JustifiedWordAndSpaceOverrunFractionForParagraph.Should().BeNull();
        renderer.JustifiedWordAndSpaceOverrunFractionForLine.Should().BeNull();
    }

    record Word(string Text, double X, double Y);
    record Result(List<Word> Words);
    static bool SameLine(Result result) => Math.Abs(result.Words.Last(w => w.Text is "Bravo" or "vo").Y - result.Words.Single(w => w.Text == "Alpha").Y) < .001;
    static double Width(string text, double size = 12)
    {
        using var gfx = XGraphics.CreateMeasureContext(new XSize(300, 300), XGraphicsUnit.Point, XPageDirection.Downwards);
        return gfx.MeasureString(text, new XFont("Arial", size)).Width;
    }
    static Result Render(double? fraction, bool split = false, bool mixedFont = false, bool final = false,
        double? legacy = null, Func<int, double?>? line = null, double blankScale = 1, double blankSpacing = 0,
        bool twoBlanks = false, bool ignoredBlanks = false, bool tabs = false, bool hyphen = false, bool enableShrink = true,
        ParagraphAlignment alignment = ParagraphAlignment.Justify)
    {
        var doc = new Document();
        doc.Styles.Normal.Font.Name = "Arial";
        doc.Styles.Normal.Font.Size = 12;
        var wordWidth = split ? Width("Bra") + Width("vo", mixedFont ? 16 : 12) : Width("Bravo");
        var blankWidth = Width(" ") * blankScale + blankSpacing;
        var preceding = Width(twoBlanks ? "Alpha Echo" : "Alpha") + blankWidth + (hyphen ? Width("Micro-") : 0);
        var missing = .03 * (wordWidth + (hyphen ? 0 : blankWidth));
        var bodyWidth = (tabs ? 120 : 0) + preceding + wordWidth - missing;
        var section = doc.AddSection();
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(20);
        section.PageSetup.PageWidth = Unit.FromPoint(40 + bodyWidth);
        var paragraph = section.AddParagraph();
        paragraph.Format.Alignment = alignment;
        if (tabs)
        {
            paragraph.Format.AddTabStop(Unit.FromPoint(120), TabAlignment.Left);
            paragraph.AddText("A B C D E");
            paragraph.AddTab();
        }
        if (ignoredBlanks) paragraph.AddText("  ");
        paragraph.AddText(twoBlanks ? "Alpha Echo" : "Alpha");
        paragraph.AddText(" ").Tag = "blank";
        if (ignoredBlanks) paragraph.AddText("  ");
        if (hyphen) paragraph.AddText("Micro-");
        if (split)
        {
            paragraph.AddFormattedText("Bra").Font.Size = Unit.FromPoint(12);
            paragraph.AddFormattedText("vo").Font.Size = Unit.FromPoint(mixedFont ? 16 : 12);
        }
        else paragraph.AddText("Bravo");
        if (!final) paragraph.AddText(" Delta");
        var renderer = new PdfDocumentRenderer { Document = doc };
        var dr = renderer.DocumentRenderer;
        if (enableShrink)
        {
            dr.JustifiedSpaceShrinkFactorForParagraph = _ => .5;
            dr.JustifiedNonterminalSpaceShrinkFactorForParagraph = _ => .5;
        }
        dr.JustifiedWordAndSpaceOverrunFractionForParagraph = source => { source.Should().BeSameAs(paragraph); return fraction; };
        if (line != null) dr.JustifiedWordAndSpaceOverrunFractionForLine = (source, count) => { source.Should().BeSameAs(paragraph); return line(count); };
        if (legacy.HasValue)
        {
            dr.JustifiedTerminalWordOverrunLimitForParagraph = _ => (legacy.Value, 0);
            dr.JustifiedNonterminalWordOverrunLimitForParagraph = _ => (legacy.Value, 0);
        }
        dr.TextHorizontalScale = tag => Equals(tag, "blank") ? blankScale : null;
        dr.TextTrailingAdvance = tag => Equals(tag, "blank") ? blankSpacing : null;
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var enumerator = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        var words = new List<Word>();
        while (enumerator.MoveAndGetNext(true, out var text)) words.Add(new Word(text!.Text, text.X, text.Y));
        return new Result(words);
    }
}
