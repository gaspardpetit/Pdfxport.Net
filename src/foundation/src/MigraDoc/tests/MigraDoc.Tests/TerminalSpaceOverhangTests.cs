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
        bool hardBreak = false, bool? additive = null)
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
        var renderer = new PdfDocumentRenderer { Document = document };
        if (additive is bool enabled)
        {
            paragraph.Format.Alignment = ParagraphAlignment.Justify;
            renderer.DocumentRenderer.JustifiedSpaceShrinkFactorForParagraph = _ => 0.75;
            renderer.DocumentRenderer.AdditiveTerminalLineEndOverhang = enabled;
        }
        var counts = new List<int>();
        if (policy != null)
            renderer.DocumentRenderer.TerminalLineEndOverhangWithSpaceCount = (actual, word, _, count) =>
            {
                actual.Should().BeSameAs(paragraph);
                if (word != "Bravo.") return null;
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
        return new Result(first.Y == bravo.Y, bravo.X, counts,
            PdfFileHelper.GetPageContentStream(pdf, 0));
    }

    record Result(bool SameLine, double BravoX, List<int> Counts, string Content);
}
