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
public class FitBoundaryKerningTests : IDisposable
{
    public FitBoundaryKerningTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Fact]
    public void NegativeBoundaryAdjustmentFitsWholeJoinedWord()
    {
        Render(null, .7).SameLine.Should().BeFalse();
        var selected = Render(-1.4, .7);
        selected.SameLine.Should().BeTrue();
        selected.Content.Should().Be(Render(null).Content);
        selected.Calls.Should().BeGreaterThan(0);
    }

    [Fact]
    public void PositiveBoundaryAdjustmentMovesWholeJoinedWord()
    {
        Render(null, -1).SameLine.Should().BeTrue();
        var selected = Render(2, -1);
        selected.SameLine.Should().BeFalse();
        selected.FirstWordX.Should().BeApproximately(20, .002);
    }

    [Theory]
    [InlineData(-1000)]
    [InlineData(-2)]
    [InlineData(2)]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void FittingSelectorNeverDirectlyMovesPaint(double adjustment)
        => Render(adjustment).Content.Should().Be(Render(null).Content);

    [Theory]
    [InlineData("font")]
    [InlineData("size")]
    [InlineData("style")]
    [InlineData("script")]
    [InlineData("surrogate")]
    [InlineData("tag")]
    [InlineData("tab")]
    [InlineData("break")]
    public void UnsupportedBoundariesDoNotInvokeSelector(string exclusion)
    {
        var selected = Render(-2, exclusion: exclusion);
        selected.Calls.Should().Be(0);
        selected.Content.Should().Be(Render(null, exclusion: exclusion).Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryBlankPreservesPreviousTag(bool characterBlank)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        var paragraph = document.AddSection().AddParagraph();
        paragraph.AddText("A");
        if (characterBlank)
            paragraph.AddCharacter(SymbolName.Blank).Tag = "blank";
        else
            paragraph.AddText(" ").Tag = "blank";
        paragraph.AddText("B").Tag = "B";
        var renderer = new PdfDocumentRenderer { Document = document };
        var calls = 0;
        renderer.DocumentRenderer.FitOnlyTextBoundaryKerning = (p, previous, current, left, right, font) =>
        {
            p.Should().BeSameAs(paragraph);
            previous.Should().Be("blank");
            current.Should().Be("B");
            left.Should().Be(' ');
            right.Should().Be('B');
            calls++;
            return -.5;
        };
        renderer.RenderDocument();
        renderer.PdfDocument.Dispose();
        calls.Should().BeGreaterThan(0);
    }

    [Fact]
    public void FirstLeafDoesNotInvokeSelector()
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.AddSection().AddParagraph().AddText("Bravo").Tag = "target";
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.FitOnlyTextBoundaryKerning = (_, _, _, _, _, _) => throw new InvalidOperationException();
        renderer.RenderDocument();
        renderer.PdfDocument.Dispose();
    }

    [Theory]
    [InlineData(ParagraphAlignment.Left, false)]
    [InlineData(ParagraphAlignment.Center, false)]
    [InlineData(ParagraphAlignment.Right, false)]
    [InlineData(ParagraphAlignment.Justify, false)]
    [InlineData(ParagraphAlignment.Left, true)]
    [InlineData(ParagraphAlignment.Center, true)]
    [InlineData(ParagraphAlignment.Right, true)]
    [InlineData(ParagraphAlignment.Justify, true)]
    public void RenderingAndFieldRemeasurementNeverInvokeFittingSelector(ParagraphAlignment alignment, bool tab)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        var paragraph = document.AddSection().AddParagraph();
        paragraph.Format.Alignment = alignment;
        paragraph.AddText("A");
        paragraph.AddText("B").Tag = "B";
        paragraph.AddLineBreak();
        if (tab)
        {
            paragraph.Format.TabStops.AddTabStop(Unit.FromPoint(100), TabAlignment.Right);
            paragraph.AddTab();
        }
        paragraph.AddPageField();
        paragraph.AddText("C").Tag = "C";
        var renderer = new PdfDocumentRenderer { Document = document };
        var calls = 0;
        renderer.DocumentRenderer.FitOnlyTextBoundaryKerning = (_, _, _, _, _, _) => { calls++; return -.5; };
        renderer.PrepareRenderPages();
        calls.Should().BeGreaterThan(0);
        renderer.DocumentRenderer.FitOnlyTextBoundaryKerning = (_, _, _, _, _, _) => throw new InvalidOperationException("Fitting callback during paint");
        renderer.RenderPages(1, renderer.PageCount);
        renderer.PdfDocument.Dispose();
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonfiniteResultsDoNotChangeWrapping(double adjustment)
        => Render(adjustment, .7).Content.Should().Be(Render(null, .7).Content);

    static Result Render(double? adjustment, double? overrun = null, string? exclusion = null)
    {
        using var measure = XGraphics.CreateMeasureContext(new XSize(300, 300), XGraphicsUnit.Point, XPageDirection.Downwards);
        var font = new XFont("Arial", 12);
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(20);
        section.PageSetup.PageWidth = Unit.FromPoint(overrun is double value
            ? 40 + measure.MeasureString("Alpha Bravo", font).Width - value : 400);
        var paragraph = section.AddParagraph();
        paragraph.AddText("Alpha ");
        var first = paragraph.AddFormattedText();
        first.AddText("Bra").Tag = "previous";
        if (exclusion == "tab") paragraph.AddTab();
        if (exclusion == "break") paragraph.AddLineBreak();
        if (exclusion == "surrogate") first.AddText("\uD83D");
        var second = paragraph.AddFormattedText();
        second.AddText("vo").Tag = exclusion == "tag" ? null : "target";
        if (exclusion == "font") second.Font.Name = "Times New Roman";
        if (exclusion == "size") second.Font.Size = 13;
        if (exclusion == "style") second.Font.Bold = true;
        if (exclusion == "script") second.Font.Superscript = true;
        var renderer = new PdfDocumentRenderer { Document = document };
        var calls = 0;
        if (adjustment.HasValue)
            renderer.DocumentRenderer.FitOnlyTextBoundaryKerning = (p, previous, current, left, right, actualFont) =>
            {
                if (!Equals(current, "target")) return null;
                p.Should().BeSameAs(paragraph);
                previous.Should().Be("previous");
                left.Should().Be('a');
                right.Should().Be('v');
                actualFont.Size.Should().Be(12);
                calls++;
                return adjustment;
            };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var reader = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        var text = new List<(string Text, double X, double Y)>();
        while (reader.MoveAndGetNext(true, out var item)) text.Add((item!.Text, item.X, item.Y));
        var start = text.First(item => item.Text == "Bra");
        return new Result(start.Y == text[0].Y, start.X, calls, PdfFileHelper.GetPageContentStream(pdf, 0));
    }
    record Result(bool SameLine, double FirstWordX, int Calls, string Content);
}
