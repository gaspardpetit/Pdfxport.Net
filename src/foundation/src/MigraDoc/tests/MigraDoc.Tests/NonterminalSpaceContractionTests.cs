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

    static double Width(string text)
    {
        using var gfx = XGraphics.CreateMeasureContext(new XSize(500, 500), XGraphicsUnit.Point,
            XPageDirection.Downwards);
        return gfx.MeasureString(text, new XFont("Arial", 12)).Width;
    }

    static ((string Text, double X, double Y)[] Words, double BodyWidth) Render(double? factor,
        bool split = false, double? finalFactor = null, bool tail = true,
        ParagraphAlignment alignment = ParagraphAlignment.Justify)
    {
        var bodyWidth = Width("Alpha Bravo") - Width(" ") * 0.25;
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        section.PageSetup.PageWidth = Unit.FromPoint(bodyWidth + 40);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(20);
        var paragraph = section.AddParagraph("Alpha ");
        if (split)
        {
            paragraph.AddFormattedText("Bra");
            paragraph.AddFormattedText("vo");
        }
        else
            paragraph.AddText("Bravo");
        if (tail)
            paragraph.AddText(" Delta");
        paragraph.Format.Alignment = alignment;
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.JustifiedNonterminalSpaceShrinkFactorForParagraph = _ => factor;
        renderer.DocumentRenderer.JustifiedSpaceShrinkFactorForParagraph = _ => finalFactor;
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var enumerator = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0);
        var words = new List<(string Text, double X, double Y)>();
        while (enumerator.Text.MoveAndGetNext(true, out var text))
            words.Add((text!.Text, text.X, text.Y));
        return (words.ToArray(), bodyWidth);
    }
}
