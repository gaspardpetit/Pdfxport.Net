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
public class TextTrailingAdvanceTests : IDisposable
{
    public TextTrailingAdvanceTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(2)]
    [InlineData(-2)]
    public void OutgoingAdvanceMovesFollowingText(double value)
    {
        var original = Render(null);
        var selected = Render(value);
        selected.FirstX.Should().BeApproximately(original.FirstX, .001);
        selected.NextX.Should().BeApproximately(original.NextX + value, .001);
        selected.NextY.Should().BeApproximately(original.NextY, .001);
    }

    [Fact]
    public void NegativeAdvanceCanAdmitFollowingWord()
    {
        var original = Render(null, overrun: 1);
        original.NextY.Should().BeLessThan(original.FirstY);
        var selected = Render(-2, overrun: 1);
        selected.NextY.Should().BeApproximately(selected.FirstY, .001);
    }

    [Fact]
    public void PositiveAdvanceCanWrapFollowingWord()
    {
        var original = Render(null, overrun: -1);
        original.NextY.Should().BeApproximately(original.FirstY, .001);
        var selected = Render(2, overrun: -1);
        selected.NextY.Should().BeLessThan(selected.FirstY);
    }

    [Theory]
    [InlineData(ParagraphAlignment.Center, 1)]
    [InlineData(ParagraphAlignment.Right, 2)]
    public void TerminalAdvanceContributesToAlignment(ParagraphAlignment alignment, double shift)
    {
        var original = Render(null, ending: "end", alignment: alignment);
        Render(2, ending: "end", alignment: alignment).FirstX
            .Should().BeApproximately(original.FirstX - shift, .001);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0)]
    public void InvalidOrZeroAdvancePreservesOutput(double value)
        => Render(value).Content.Should().Be(Render(null).Content);

    [Fact]
    public void NegativeAdvanceIsClampedToLeafWidth()
    {
        var selected = Render(-10000);
        selected.NextX.Should().BeApproximately(20 + SpaceWidth(), .001);
    }

    [Fact]
    public void AdvanceCrossesEmptyFormattedRun()
        => Render(2, emptyRun: true).Content.Should().Be(Render(2).Content);

    [Theory]
    [InlineData("tab")]
    [InlineData("field")]
    public void AdvanceIsIgnoredBeforeNonTextElement(string ending)
        => Render(2, ending: ending).Content.Should().Be(Render(null, ending: ending).Content);

    [Fact]
    public void AdvanceBeforeManualBreakContributesToAlignment()
    {
        var original = Render(null, ending: "break", alignment: ParagraphAlignment.Right);
        Render(2, ending: "break", alignment: ParagraphAlignment.Right).FirstX
            .Should().BeApproximately(original.FirstX - 2, .001);
    }

    [Fact]
    public void AdvanceAppliesBeforeInlineImage()
    {
        var original = Render(null, ending: "image");
        Render(2, ending: "image").NextX.Should().BeApproximately(original.NextX + 2, .001);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SameFontBlanksUseTheirOwnAdvanceForAlignment(int selectedBlank)
    {
        var original = RenderBlanks(null, selectedBlank);
        var selected = RenderBlanks(2, selectedBlank);
        selected.FirstX.Should().BeApproximately(original.FirstX - 2, .001);
        selected.NextX.Should().BeApproximately(original.NextX, .001);
    }

    [Theory]
    [InlineData(0, 3, true)]
    [InlineData(1, 1, false)]
    public void SameFontBlanksUseTheirOwnAdvanceForWrapping(int selectedBlank, double spare, bool fits)
    {
        var selected = RenderBlanks(2, selectedBlank, spare);
        (Math.Abs(selected.FirstY - selected.NextY) < .001).Should().Be(fits);
    }

    [Theory]
    [InlineData("12.34")]
    [InlineData("1234")]
    public void DecimalTabDoesNotIncludeOutgoingAdvanceInAnchor(string number)
    {
        RenderDecimalTab(2, number).Should().BeApproximately(RenderDecimalTab(null, number), .001);
    }

    [Fact]
    public void TaggedMultiwordTextDoesNotLeakAdvanceIntoFollowingBlanks()
    {
        Result RenderMultiword(double? advance)
        {
            var document = NewDocument(out var section);
            var paragraph = section.AddParagraph();
            paragraph.Format.Alignment = ParagraphAlignment.Right;
            var tag = new object();
            paragraph.Elements.Add(new Text("Alpha Bravo") { Tag = tag });
            paragraph.AddText(" Charlie Delta");
            return Read(document, tag, advance, "Alpha", "Delta");
        }
        var original = RenderMultiword(null);
        var selected = RenderMultiword(2);
        // Flattening assigns the same tag to Alpha, the blank, and Bravo.
        selected.FirstX.Should().BeApproximately(original.FirstX - 6, .001);
        selected.NextX.Should().BeApproximately(original.NextX, .001);
    }

    static double RenderDecimalTab(double? advance, string number)
    {
        var document = NewDocument(out var section);
        document.Culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        var paragraph = section.AddParagraph();
        paragraph.Format.TabStops.AddTabStop(Unit.FromPoint(100), TabAlignment.Decimal);
        paragraph.AddTab();
        var tag = new object();
        paragraph.Elements.Add(new Text(number) { Tag = tag });
        var result = Read(document, tag, advance, number);
        using var gfx = XGraphics.CreateMeasureContext(new XSize(300, 300), XGraphicsUnit.Point, XPageDirection.Downwards);
        var prefix = number.Split('.')[0];
        var decimalX = result.FirstX + gfx.MeasureString(prefix, new XFont("Arial", 12)).Width;
        decimalX.Should().BeApproximately(120, .001);
        return result.FirstX;
    }

    static Result RenderBlanks(double? advance, int selectedBlank, double? spare = null)
    {
        var document = NewDocument(out var section);
        if (spare != null)
        {
            using var gfx = XGraphics.CreateMeasureContext(new XSize(300, 300), XGraphicsUnit.Point, XPageDirection.Downwards);
            section.PageSetup.PageWidth = Unit.FromPoint(40 + gfx.MeasureString("Alpha Bravo Charlie", new XFont("Arial", 12)).Width + spare.Value);
        }
        var paragraph = section.AddParagraph();
        paragraph.Format.Alignment = spare == null ? ParagraphAlignment.Right : ParagraphAlignment.Left;
        var tag = new object();
        paragraph.AddText("Alpha");
        paragraph.Elements.Add(new Text(" ") { Tag = selectedBlank == 0 ? tag : null });
        paragraph.AddText("Bravo");
        paragraph.Elements.Add(new Text(" ") { Tag = selectedBlank == 1 ? tag : null });
        paragraph.AddText("Charlie");
        return Read(document, tag, advance, "Alpha", "Charlie");
    }

    static Document NewDocument(out Section section)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        section = document.AddSection();
        section.PageSetup.LeftMargin = Unit.FromPoint(20);
        section.PageSetup.RightMargin = Unit.FromPoint(20);
        return document;
    }

    static Result Read(Document document, object tag, double? advance, string firstText, string? nextText = null)
    {
        var renderer = new PdfDocumentRenderer { Document = document };
        if (advance != null)
            renderer.DocumentRenderer.TextTrailingAdvance = actual => ReferenceEquals(actual, tag) ? advance : null;
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var reader = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        var positions = new List<(string Text, double X, double Y)>();
        while (reader.MoveAndGetNext(true, out var item)) positions.Add((item!.Text, item.X, item.Y));
        var first = positions.First(item => item.Text == firstText);
        var next = positions.FirstOrDefault(item => item.Text == nextText);
        return new Result(first.X, first.Y, next.X, next.Y, PdfFileHelper.GetPageContentStream(pdf, 0));
    }

    static double SpaceWidth()
    {
        using var gfx = XGraphics.CreateMeasureContext(new XSize(300, 300), XGraphicsUnit.Point, XPageDirection.Downwards);
        return gfx.MeasureString(" ", new XFont("Arial", 12)).Width;
    }

    static Result Render(double? advance, double? overrun = null, string ending = "text",
        ParagraphAlignment alignment = ParagraphAlignment.Left, bool emptyRun = false)
    {
        using var gfx = XGraphics.CreateMeasureContext(new XSize(300, 300), XGraphicsUnit.Point, XPageDirection.Downwards);
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        section.PageSetup.LeftMargin = Unit.FromPoint(20);
        section.PageSetup.RightMargin = Unit.FromPoint(20);
        if (overrun != null)
            section.PageSetup.PageWidth = Unit.FromPoint(40 + gfx.MeasureString("Alpha Bravo", new XFont("Arial", 12)).Width - overrun.Value);
        var paragraph = section.AddParagraph();
        paragraph.Format.Alignment = alignment;
        var tag = new object();
        paragraph.Elements.Add(new Text("Alpha") { Tag = tag });
        if (emptyRun) paragraph.AddFormattedText("");
        if (ending == "text") paragraph.AddText(" ");
        if (ending == "tab") paragraph.AddTab();
        if (ending == "field") paragraph.AddPageField();
        if (ending == "break") paragraph.AddLineBreak();
        if (ending == "image")
        {
            var image = paragraph.AddImage("base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==");
            image.Width = Unit.FromPoint(5);
            image.Height = Unit.FromPoint(5);
        }
        if (ending != "end") paragraph.AddFormattedText("Bravo");
        var renderer = new PdfDocumentRenderer { Document = document };
        if (advance != null)
            renderer.DocumentRenderer.TextTrailingAdvance = actual => ReferenceEquals(actual, tag) ? advance : null;
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        pdf.PageCount.Should().Be(1);
        var reader = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        var positions = new List<(string Text, double X, double Y)>();
        while (reader.MoveAndGetNext(true, out var item)) positions.Add((item!.Text, item.X, item.Y));
        var first = positions.First(item => item.Text == "Alpha");
        var next = positions.FirstOrDefault(item => item.Text == "Bravo");
        return new Result(first.X, first.Y, next.X, next.Y, PdfFileHelper.GetPageContentStream(pdf, 0));
    }
    record Result(double FirstX, double FirstY, double NextX, double NextY, string Content);
}
