// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Fields;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class NonterminalSpaceContractionTests : IDisposable
{
    public NonterminalSpaceContractionTests()
    {
        PdfSharpCore.ResetAll();
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptInFitsNonfinalWordAndPaintsContractedSpaces(bool split)
    {
        var normal = Render(null, split: split);
        var contracted = Render(0.5, split: split);
        var last = split ? "vo" : "Bravo";
        normal.Words.Single(w => w.Text == last).Y.Should().NotBe(normal.Words[0].Y);
        contracted.Words.Single(w => w.Text == last).Y.Should().BeApproximately(contracted.Words[0].Y, 0.001);
        contracted.Words.Single(w => w.Text == "Delta").Y.Should().NotBe(contracted.Words[0].Y);
        var end = contracted.Words.Single(w => w.Text == last);
        (end.X + Width(last)).Should().BeApproximately(20 + contracted.BodyWidth, 0.02);
        var start = contracted.Words.Single(w => w.Text == (split ? "Bra" : "Bravo"));
        (start.X - contracted.Words[0].X - Width("Alpha")).Should()
            .BeApproximately(Width(" ") * 0.75, 0.02);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(0.9)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidOrInsufficientBudgetPreservesWrapping(double factor)
        => Render(factor).Words.Should().Equal(Render(null).Words);

    [Theory]
    [InlineData(ParagraphAlignment.Left)]
    [InlineData(ParagraphAlignment.Center)]
    [InlineData(ParagraphAlignment.Right)]
    public void OtherAlignmentsAreUnaffected(ParagraphAlignment alignment)
        => Render(0.5, alignment: alignment).Words.Should().Equal(Render(null, alignment: alignment).Words);

    [Fact]
    public void FinalAndNonfinalPoliciesAreIndependent()
    {
        Render(null, finalFactor: 0.5).Words.Should().Equal(Render(null).Words);
        Render(0.5, tail: false).Words.Should().Equal(Render(null, tail: false).Words);
        var final = Render(null, finalFactor: 0.5, tail: false);
        final.Words.Single(w => w.Text == "Bravo").Y.Should().BeApproximately(final.Words[0].Y, 0.001);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    [InlineData(false, 8)]
    [InlineData(true, 8)]
    [InlineData(false, 7)]
    [InlineData(true, 7)]
    [InlineData(false, 10)]
    [InlineData(true, 10)]
    [InlineData(false, 11)]
    [InlineData(true, 11)]
    [InlineData(false, 12)]
    [InlineData(true, 12)]
    public void FinalLineSuffixUsesFinalPolicy(bool split, int suffix)
    {
        var normal = Render(null, split: split, tail: false, suffix: suffix);
        Render(0.5, split: split, tail: false, suffix: suffix).Words.Should().Equal(normal.Words);
        var last = split ? "vo" : "Bravo";
        normal.Words.Single(w => w.Text == last).Y.Should().NotBe(normal.Words[0].Y);

        var final = Render(null, split: split, finalFactor: 0.5, tail: false, suffix: suffix);
        var end = final.Words.Single(w => w.Text == last);
        end.Y.Should().BeApproximately(final.Words[0].Y, 0.001);
        (end.X + Width(last)).Should().BeApproximately(20 + final.BodyWidth, 0.02);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VisibleContentAfterBookmarksStillUsesNonfinalPolicy(bool split)
    {
        var result = Render(0.5, split: split, suffix: 0);
        var last = split ? "vo" : "Bravo";
        result.Words.Single(w => w.Text == last).Y.Should().BeApproximately(result.Words[0].Y, 0.001);
        result.Words.Single(w => w.Text == "Delta").Y.Should().NotBe(result.Words[0].Y);
    }

    [Theory]
    [InlineData(false, 6)]
    [InlineData(true, 6)]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    [InlineData(false, 10)]
    [InlineData(true, 10)]
    [InlineData(false, 13)]
    [InlineData(true, 13)]
    public void BreakWithFollowingLineUsesNonfinalPolicy(bool split, int suffix)
    {
        var result = Render(0.5, split: split, tail: suffix is 5 or 10, suffix: suffix);
        var last = split ? "vo" : "Bravo";
        var end = result.Words.Single(w => w.Text == last);
        end.Y.Should().BeApproximately(result.Words[0].Y, 0.001);
        (end.X + Width(last)).Should().BeApproximately(20 + result.BodyWidth, 0.02);
    }

    [Theory]
    [InlineData(false, 1, false)]
    [InlineData(true, 1, false)]
    [InlineData(false, 2, false)]
    [InlineData(true, 2, false)]
    [InlineData(false, 1, true)]
    [InlineData(true, 1, true)]
    [InlineData(false, 2, true)]
    [InlineData(true, 2, true)]
    public void TabsLimitBudgetToFollowingSpaces(bool split, int tabs, bool final)
    {
        var normal = Render(null, split: split, tail: !final, tabs: tabs, missingSpaceFraction: 0.75);
        var limited = Render(final ? null : 0.5, split: split, finalFactor: final ? 0.5 : null,
            tail: !final, tabs: tabs, missingSpaceFraction: 0.75);
        limited.Words.Should().Equal(normal.Words, "earlier tab segments cannot supply contraction budget");
        var last = split ? "vo" : "Bravo";
        normal.Words.Single(w => w.Text == last).Y.Should()
            .NotBe(normal.Words.Single(w => w.Text == "Alpha").Y);

        var allowed = Render(final ? null : 0.5, split: split, finalFactor: final ? 0.5 : null,
            tail: !final, tabs: tabs);
        var alpha = allowed.Words.Single(w => w.Text == "Alpha");
        var end = allowed.Words.Single(w => w.Text == last);
        end.Y.Should().BeApproximately(alpha.Y, 0.001);
        (end.X + Width(last)).Should().BeApproximately(20 + allowed.BodyWidth, 0.02);
        var start = allowed.Words.Single(w => w.Text == (split ? "Bra" : "Bravo"));
        (start.X - alpha.X - Width("Alpha")).Should().BeApproximately(Width(" ") * 0.75, 0.02);
        allowed.Words.TakeWhile(w => w.Text != "Alpha").Should()
            .Equal(normal.Words.TakeWhile(w => w.Text != "Alpha"));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void BudgetUsesActualSpacesInCurrentInterval(bool split, bool final, bool wrap)
    {
        var wordSize = wrap ? 12 : 48;
        var normal = Render(null, split: split, tail: !final, wordSize: wordSize,
            wrap: wrap, missingSpaceFraction: 1.25);
        var limited = Render(final ? null : 0.5, split: split, finalFactor: final ? 0.5 : null,
            tail: !final, wordSize: wordSize, wrap: wrap, missingSpaceFraction: 1.25);
        limited.Words.Should().Equal(normal.Words, "neither earlier intervals nor the incoming font supply space width");
        var last = split ? "vo" : "Bravo";
        normal.Words.Single(w => w.Text == last).Y.Should()
            .NotBe(normal.Words.Single(w => w.Text == "Alpha").Y);

        var allowed = Render(final ? null : 0.5, split: split, finalFactor: final ? 0.5 : null,
            tail: !final, wordSize: wordSize, wrap: wrap);
        var alpha = allowed.Words.Single(w => w.Text == "Alpha");
        var end = allowed.Words.Single(w => w.Text == last);
        end.Y.Should().BeApproximately(alpha.Y, 0.001);
        (end.X + Width(last, wordSize)).Should().BeApproximately(20 + allowed.BodyWidth, 0.02);
        var start = allowed.Words.Single(w => w.Text == (split ? "Bra" : "Bravo"));
        (start.X - alpha.X - Width("Alpha")).Should().BeApproximately(Width(" ") * 0.75, 0.02);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EarlierIntervalContractsEvenWhenParagraphEndsInNextInterval(bool split)
    {
        var normal = Render(null, split: split, finalAcrossIntervals: true);
        var last = split ? "vo" : "Bravo";
        normal.Words.Single(w => w.Text == last).Y.Should().NotBe(normal.Words[0].Y);
        var allowed = Render(0.5, split: split, finalAcrossIntervals: true);
        var end = allowed.Words.Single(w => w.Text == last);
        end.Y.Should().BeApproximately(allowed.Words[0].Y, 0.001);
        var leftEdge = 20 + Width("Alpha Bravo") - Width(" ") * 0.25;
        (end.X + Width(last)).Should().BeApproximately(leftEdge, 0.02);
        var final = allowed.Words.Single(w => w.Text == "I");
        final.Y.Should().BeApproximately(end.Y, 0.001);
        final.X.Should().BeGreaterOrEqualTo(leftEdge + 40 - 0.02);
        Render(0.5, split: split, finalAcrossIntervals: true, missingSpaceFraction: 0.75).Words.Should()
            .Equal(Render(null, split: split, finalAcrossIntervals: true, missingSpaceFraction: 0.75).Words);
        Render(0.5, split: split, finalAcrossIntervals: true, missingSpaceFraction: -0.25).Words.Should()
            .Equal(Render(null, split: split, finalAcrossIntervals: true, missingSpaceFraction: -0.25).Words,
                "an earlier final-line interval that fits naturally must not be expanded");
    }

    static double Width(string text, double size = 12)
    {
        using var gfx = XGraphics.CreateMeasureContext(new XSize(500, 500), XGraphicsUnit.Point,
            XPageDirection.Downwards);
        return gfx.MeasureString(text, new XFont("Arial", size)).Width;
    }

    [Theory]
    [InlineData(false, false, 0, false)]
    [InlineData(false, false, 1, false)]
    [InlineData(false, false, 0, true)]
    [InlineData(false, true, 0, false)]
    [InlineData(false, true, 1, false)]
    [InlineData(false, true, 0, true)]
    [InlineData(true, false, 0, false)]
    [InlineData(true, false, 1, false)]
    [InlineData(true, false, 0, true)]
    [InlineData(true, true, 0, false)]
    [InlineData(true, true, 1, false)]
    [InlineData(true, true, 0, true)]
    public void WordLimitCapsContractionWithoutEnablingIt(bool final, bool split, int tabs, bool wrap)
    {
        var normal = Render(null, split: split, tail: !final, tabs: tabs, wrap: wrap);
        var allowed = Render(final ? null : 0.5, split: split, tail: !final, tabs: tabs, wrap: wrap,
            finalFactor: final ? 0.5 : null);
        var capped = Render(final ? null : 0.5, split: split, tail: !final, tabs: tabs, wrap: wrap,
            finalFactor: final ? 0.5 : null,
            finalLimit: final ? (0, 0) : null, nonfinalLimit: final ? null : (0, 0));
        capped.Words.Should().Equal(normal.Words);
        var last = split ? "vo" : "Bravo";
        allowed.Words.Single(w => w.Text == last).Y.Should()
            .BeApproximately(allowed.Words.Single(w => w.Text == "Alpha").Y, 0.001);
        var disabled = Render(null, split: split, tail: !final, tabs: tabs, wrap: wrap,
            finalLimit: (1, 1), nonfinalLimit: (1, 1));
        disabled.Words.Should().Equal(normal.Words);
        var insufficient = Render(final ? null : 0.9, split: split, tail: !final, tabs: tabs, wrap: wrap,
            finalFactor: final ? 0.9 : null, finalLimit: (1, 1), nonfinalLimit: (1, 1));
        insufficient.Words.Should().Equal(normal.Words, "a word cap cannot increase the space budget");
        // A cap for the other policy cannot affect this word.
        var other = Render(final ? null : 0.5, split: split, tail: !final, tabs: tabs, wrap: wrap,
            finalFactor: final ? 0.5 : null,
            finalLimit: final ? null : (0, 0), nonfinalLimit: final ? (0, 0) : null);
        other.Words.Should().Equal(allowed.Words);
    }

    [Theory]
    [InlineData(0.01, 0, false)]
    [InlineData(0.1, 0, true)]
    [InlineData(0.04, 0, true)]
    [InlineData(0, 0.1, false)]
    [InlineData(0, 0.5, true)]
    [InlineData(-1, 0, true)]
    [InlineData(0, -1, true)]
    [InlineData(double.NaN, 0, true)]
    [InlineData(0, double.PositiveInfinity, true)]
    public void WordLimitUsesWholeWordOrSpaceFloor(double fraction, double floor, bool fits)
    {
        foreach (var final in new[] { false, true })
        foreach (var split in new[] { false, true })
        {
            var result = Render(final ? null : 0.5, split: split, tail: !final,
                finalFactor: final ? 0.5 : null,
                finalLimit: final ? (fraction, floor) : null,
                nonfinalLimit: final ? null : (fraction, floor));
            var alpha = result.Words.Single(w => w.Text == "Alpha");
            var end = result.Words.Single(w => w.Text == (split ? "vo" : "Bravo"));
            if (fits)
            {
                end.Y.Should().BeApproximately(alpha.Y, 0.001);
                (end.X + Width(split ? "vo" : "Bravo")).Should()
                    .BeApproximately(20 + result.BodyWidth, 0.02);
            }
            else
            {
                end.Y.Should().NotBe(alpha.Y);
                if (split)
                    result.Words.Single(w => w.Text == "Bra").Y.Should().BeApproximately(end.Y, 0.001);
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(10)]
    public void TerminalWordCapIgnoresNonpaintingSuffix(int suffix)
    {
        foreach (var split in new[] { false, true })
        {
            var normal = Render(null, split: split, tail: false, suffix: suffix);
            var capped = Render(null, split: split, tail: false, suffix: suffix,
                finalFactor: 0.5, finalLimit: (0, 0));
            capped.Words.Should().Equal(normal.Words);
            var allowed = Render(null, split: split, tail: false, suffix: suffix,
                finalFactor: 0.5, nonfinalLimit: (0, 0));
            allowed.Words.Single(w => w.Text == (split ? "vo" : "Bravo")).Y.Should()
                .BeApproximately(allowed.Words.Single(w => w.Text == "Alpha").Y, 0.001);
        }
    }

    static ((string Text, double X, double Y)[] Words, double BodyWidth) Render(double? factor,
        bool split = false, double? finalFactor = null, bool tail = true,
        ParagraphAlignment alignment = ParagraphAlignment.Justify, int suffix = -1,
        int tabs = 0, double missingSpaceFraction = 0.25, double wordSize = 12, bool wrap = false, bool finalAcrossIntervals = false,
        (double WordWidthFraction, double SpaceWidthFloor)? finalLimit = null,
        (double WordWidthFraction, double SpaceWidthFloor)? nonfinalLimit = null)
    {
        var leftWidth = finalAcrossIntervals ? Width("Alpha Bravo") - Width(" ") * missingSpaceFraction :
            wrap ? Width("One Two Three") + Width(" ") * 0.25 : 0;
        var segmentOffset = tabs * 120 + (wrap ? leftWidth + 40 : 0);
        var bodyWidth = finalAcrossIntervals ? leftWidth + 40 + Width("I") + 1 :
            segmentOffset + Width("Alpha ") + Width("Bravo", wordSize) -
            Width(" ") * missingSpaceFraction;
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        section.PageSetup.PageWidth = Unit.FromPoint(bodyWidth + 40);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(20);
        if (wrap || finalAcrossIntervals)
        {
            section.PageSetup.TopMargin = Unit.FromPoint(20);
            var image = section.AddImage(
                "base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==");
            image.RelativeHorizontal = RelativeHorizontal.Margin;
            image.RelativeVertical = RelativeVertical.Margin;
            image.Left = Unit.FromPoint(leftWidth);
            image.Top = Unit.FromPoint(0);
            image.Width = Unit.FromPoint(40);
            image.Height = Unit.FromPoint(90);
            image.WrapFormat.Style = WrapStyle.BothSides;
        }
        var paragraph = section.AddParagraph();
        if (wrap)
            paragraph.AddText("One Two Three ");
        for (var tab = 1; tab <= tabs; tab++)
        {
            paragraph.Format.AddTabStop(Unit.FromPoint(tab * 120), TabAlignment.Left);
            paragraph.AddText("One Two Three ");
            paragraph.AddTab();
        }
        paragraph.AddText("Alpha ");
        if (split)
        {
            paragraph.AddFormattedText("Bra").Font.Size = Unit.FromPoint(wordSize);
            paragraph.AddFormattedText("vo").Font.Size = Unit.FromPoint(wordSize);
        }
        else
            paragraph.AddFormattedText("Bravo").Font.Size = Unit.FromPoint(wordSize);
        switch (suffix)
        {
            case 0:
                paragraph.AddBookmark("End", false);
                break;
            case 1:
                paragraph.Elements.Add(new Text(""));
                break;
            case 2:
                paragraph.AddInfoField(InfoFieldType.Title);
                break;
            case 3:
                paragraph.AddText("\u00AD");
                break;
            case 4:
                paragraph.AddBookmark("End1", false);
                paragraph.AddText(" ");
                paragraph.AddBookmark("End2", false);
                paragraph.AddText("");
                break;
            case 5:
                paragraph.AddLineBreak();
                break;
            case 6:
                paragraph.AddLineBreak();
                paragraph.AddLineBreak();
                break;
            case 7:
                paragraph.AddLineBreak();
                paragraph.AddBookmark("AfterBreak", false);
                break;
            case 10:
                paragraph.AddTab();
                break;
            case 11:
                paragraph.AddBookmark("BeforeTab", false);
                paragraph.AddTab();
                break;
            case 12:
                paragraph.AddTab();
                paragraph.AddBookmark("AfterTab", false);
                break;
            case 13:
                paragraph.AddTab();
                paragraph.AddTab();
                break;
            case 8:
                paragraph.AddBookmark("BeforeBreak", false);
                paragraph.AddLineBreak();
                break;
        }
        if (tail)
            paragraph.AddText(finalAcrossIntervals ? " I" : " Delta");
        paragraph.Format.Alignment = alignment;
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.JustifiedNonterminalSpaceShrinkFactorForParagraph = _ => factor;
        renderer.DocumentRenderer.JustifiedSpaceShrinkFactorForParagraph = _ => finalFactor;
        renderer.DocumentRenderer.JustifiedTerminalWordOverrunLimitForParagraph = _ => finalLimit;
        renderer.DocumentRenderer.JustifiedNonterminalWordOverrunLimitForParagraph = _ => nonfinalLimit;
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var enumerator = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0);
        var words = new List<(string Text, double X, double Y)>();
        while (enumerator.Text.MoveAndGetNext(true, out var text))
            words.Add((text!.Text, text.X, text.Y));
        return (words.ToArray(), bodyWidth);
    }
}
