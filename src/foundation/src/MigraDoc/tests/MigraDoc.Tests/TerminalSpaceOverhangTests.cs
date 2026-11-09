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
public class TerminalSpaceOverhangTests : IDisposable
{
    public TerminalSpaceOverhangTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }

    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData("Alpha Bravo.", 1)]
    [InlineData("Alpha beta Bravo.", 2)]
    [InlineData("Alpha Bravo.   ", 1)]
    public void TerminalAllowanceReceivesLineSpaceCount(string text, int spaces)
    {
        var baseline = Render(text);
        var adjusted = Render(text, count => count == spaces ? 5 : null);
        baseline.SameLine.Should().BeFalse();
        adjusted.SameLine.Should().BeTrue();
        adjusted.Counts.Should().NotBeEmpty().And.OnlyContain(count => count == spaces);
        adjusted.BravoX.Should().BeApproximately(baseline.BravoX +
            Measure(text.TrimEnd()[..^"Bravo.".Length]), 0.01);
    }

    [Theory]
    [InlineData(" ")]
    [InlineData("   ")]
    public void TrailingBlanksDoNotChangeWrappingWithCountOnlySelector(string blanks)
    {
        var baseline = Render("Alpha Bravo.", count => count == 1 ? 5 : null, anyWord: true);
        var result = Render("Alpha Bravo." + blanks, count => count == 1 ? 5 : null, anyWord: true);
        baseline.SameLine.Should().BeTrue();
        result.SameLine.Should().BeTrue();
        result.BravoX.Should().BeApproximately(baseline.BravoX, 0.001);
        result.Words.Should().NotBeEmpty().And.OnlyContain(word => word == "Bravo.");
    }

    [Theory]
    [InlineData("", "", false)]
    [InlineData("  ", "", false)]
    [InlineData("", "  ", false)]
    [InlineData("", "", true)]
    [InlineData("  ", "", true)]
    [InlineData("", "  ", true)]
    public void TrailingBookmarkDoesNotChangeTerminalAllowance(string before, string after, bool additive)
    {
        double? Allowance(int count) => count == 1 ? (additive ? 1.2 : 5) : null;
        var baseline = Render("Alpha Bravo.", Allowance, anyWord: true, additive: additive ? true : null);
        var result = Render("Alpha Bravo." + before, Allowance, anyWord: true,
            additive: additive ? true : null, bookmarkSuffix: after);
        baseline.SameLine.Should().BeTrue();
        result.SameLine.Should().BeTrue();
        result.BravoX.Should().BeApproximately(baseline.BravoX, 0.001);
        result.Words.Should().NotBeEmpty().And.OnlyContain(word => word == "Bravo.");
        result.Counts.Should().OnlyContain(count => count == 1);
    }

    [Fact]
    public void BookmarkBeforeMoreTextDoesNotMakeWordTerminal()
    {
        var result = Render("Alpha Bravo.", _ => 100, anyWord: true, bookmarkSuffix: " tail");
        result.SameLine.Should().BeFalse();
        result.Words.Should().NotContain("Bravo.");
    }

    [Theory]
    [InlineData("\u00AD", false)]
    [InlineData("\u00AD  ", false)]
    [InlineData("\u00AD", true)]
    [InlineData("\u00AD  ", true)]
    public void TrailingSoftHyphenDoesNotChangeWrapping(string suffix, bool additive)
    {
        Measure("\u00AD").Should().BeGreaterThan(3);
        double? Allowance(int count) => count == 1 ? (additive ? 1.2 : 5) : null;
        var baseline = Render("Alpha Bravo.", Allowance, anyWord: true, additive: additive ? true : null);
        var result = Render("Alpha Bravo.", Allowance, anyWord: true,
            additive: additive ? true : null, suffix: suffix);
        baseline.SameLine.Should().BeTrue();
        result.SameLine.Should().BeTrue();
        result.BravoX.Should().BeApproximately(baseline.BravoX, 0.001);
        result.Words.Should().NotBeEmpty().And.OnlyContain(word => word == "Bravo.");
    }

    [Fact]
    public void NonterminalWordsDoNotUseTerminalPolicy()
    {
        var baseline = Render("Alpha Bravo. tail");
        var adjusted = Render("Alpha Bravo. tail", _ => 100);
        adjusted.SameLine.Should().BeFalse();
        adjusted.Content.Should().Be(baseline.Content);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidAllowancesPreserveExistingPolicy(double allowance)
    {
        Render("Alpha Bravo.", _ => allowance, existing: 5).Content.Should()
            .Be(Render("Alpha Bravo.", existing: 5).Content);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidExistingAllowanceDoesNotSuppressValidSelection(double existing)
    {
        Render("Alpha Bravo.", _ => 5, existing).Content.Should()
            .Be(Render("Alpha Bravo.", existing: 5).Content);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(5, 1)]
    public void LargerAllowanceWins(double existing, double selected)
    {
        Render("Alpha Bravo.", _ => selected, existing).Content.Should()
            .Be(Render("Alpha Bravo.", existing: 5).Content);
    }

    [Fact]
    public void TwoInsufficientAllowancesAreNotAdded()
    {
        Render("Alpha Bravo.", _ => 1.2, existing: 1.2).SameLine.Should().BeFalse();
    }

    [Fact]
    public void CountResetsAfterAnExplicitLineBreak()
    {
        var result = Render("Alpha Bravo.", count => count == 1 ? 5 : null, hardBreak: true);
        result.SameLine.Should().BeTrue();
        result.Counts.Should().NotBeEmpty().And.OnlyContain(count => count == 1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingAdditiveModeAlsoAppliesToSelectedAllowance(bool additive)
    {
        var result = Render("Alpha Bravo.", _ => 1.2, additive: additive);
        result.SameLine.Should().Be(additive);
    }

    static double Measure(string text)
    {
        using var graphics = XGraphics.CreateMeasureContext(new XSize(300, 300),
            XGraphicsUnit.Point, XPageDirection.Downwards);
        return graphics.MeasureString(text, new XFont("Arial", 12)).Width;
    }

    static Result Render(string text, Func<int, double?>? policy = null, double? existing = null,
        bool hardBreak = false, bool? additive = null, bool anyWord = false, string? bookmarkSuffix = null, string? suffix = null)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        var fitText = text.Split(" tail")[0].TrimEnd();
        section.PageSetup.PageWidth = Unit.FromPoint(Measure(fitText) - 2 + 40);
        section.PageSetup.LeftMargin = Unit.FromPoint(20);
        section.PageSetup.RightMargin = Unit.FromPoint(20);
        var paragraph = section.AddParagraph();
        if (hardBreak)
        {
            paragraph.AddText("Earlier words");
            paragraph.AddLineBreak();
        }
        paragraph.AddText(text);
        if (suffix != null)
            paragraph.AddText(suffix);
        if (bookmarkSuffix != null)
        {
            paragraph.AddBookmark("End", false);
            if (bookmarkSuffix.Length > 0)
                paragraph.AddText(bookmarkSuffix);
        }
        var renderer = new PdfDocumentRenderer { Document = document };
        if (additive is bool enabled)
        {
            paragraph.Format.Alignment = ParagraphAlignment.Justify;
            renderer.DocumentRenderer.JustifiedSpaceShrinkFactorForParagraph = _ => 0.75;
            renderer.DocumentRenderer.AdditiveTerminalLineEndOverhang = enabled;
        }
        var counts = new List<int>();
        var words = new List<string>();
        if (policy != null)
            renderer.DocumentRenderer.TerminalLineEndOverhangWithSpaceCount = (actual, word, _, count) =>
            {
                actual.Should().BeSameAs(paragraph);
                words.Add(word);
                if (!anyWord && word != "Bravo.") return null;
                counts.Add(count);
                return policy(count);
            };
        if (existing != null)
            renderer.DocumentRenderer.LineEndOverhang = (_, word, _) => word == "Bravo." ? existing : null;
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        pdf.PageCount.Should().Be(1);
        var reader = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        var positions = new List<(string Text, double X, double Y)>();
        while (reader.MoveAndGetNext(true, out var item))
            positions.Add((item!.Text, item.X, item.Y));
        var first = positions.First(item => item.Text == "Alpha");
        var bravo = positions.First(item => item.Text == "Bravo.");
        return new Result(first.Y == bravo.Y, bravo.X, counts, words,
            PdfFileHelper.GetPageContentStream(pdf, 0));
    }

    record Result(bool SameLine, double BravoX, List<int> Counts, List<string> Words, string Content);
}
