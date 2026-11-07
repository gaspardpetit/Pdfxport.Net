// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class PreparedObjectPaintTests : IDisposable
{
    public PreparedObjectPaintTests()
    {
        PdfSharpCore.ResetAll();
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Fact]
    public void PaintsOnlyPreparedContinuationLines()
    {
        var document = CreateDocument();
        var paragraph = document.LastSection.AddParagraph();
        for (var i = 1; i <= 14; i++)
        {
            if (i > 1) paragraph.AddLineBreak();
            paragraph.AddText("Line" + i);
        }
        var renderer = new DocumentRenderer(document);
        renderer.PrepareDocument();
        renderer.FormattedDocument!.PageCount.Should().Be(2);
        var fragment = renderer.GetRenderInfoFromPage(2)!.Single();
        using var pdf = Paint(renderer, fragment, 2, 0);
        Texts(pdf).Should().Equal("Line13", "Line14");
        renderer.CurrentRenderPage.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void OffsetsEntireFragmentWithoutChangingStoredLayout(int kind)
    {
        var document = CreateDocument();
        var section = document.LastSection;
        Paragraph target = null!;
        if (kind == 0) target = section.AddParagraph();
        if (kind == 1)
        {
            var frame = section.AddTextFrame();
            frame.Width = Unit.FromPoint(100); frame.Height = Unit.FromPoint(20);
            target = frame.AddParagraph();
        }
        if (kind == 2)
        {
            var table = section.AddTable(); table.AddColumn(Unit.FromPoint(100));
            target = table.AddRow().Cells[0].AddParagraph();
        }
        target.AddHyperlink("https://example.com", HyperlinkType.Web).AddText("Object");
        var renderer = new DocumentRenderer(document);
        renderer.PrepareDocument();
        var fragment = renderer.GetRenderInfoFromPage(1)!.Single();
        var originalY = fragment.LayoutInfo.ContentArea.Y;
        using var baseline = Paint(renderer, fragment, 1, 0);
        using var moved = Paint(renderer, fragment, 1, 15);
        using var repeated = Paint(renderer, fragment, 1, 0);
        fragment.LayoutInfo.ContentArea.Y.Should().Be(originalY);
        Texts(moved).Should().Equal("Object");
        var baselineRect = baseline.Pages[0].Annotations[0].Rectangle;
        var movedRect = moved.Pages[0].Annotations[0].Rectangle;
        movedRect.Y1.Should().BeApproximately(baselineRect.Y1 - 15, 0.001);
        movedRect.Y2.Should().BeApproximately(baselineRect.Y2 - 15, 0.001);
        repeated.Pages[0].Annotations[0].Rectangle.Should().Be(baselineRect);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UsesHeaderAndFooterPageFields(bool footer)
    {
        var document = CreateDocument();
        var section = document.LastSection;
        section.PageSetup.StartingNumber = 9;
        section.AddParagraph("Body");
        var paragraph = (footer ? section.Footers.Primary : section.Headers.Primary).AddParagraph("Page ");
        paragraph.AddPageField(); paragraph.AddText(" Date "); paragraph.AddDateField("yyyy-MM-dd");
        var renderer = new DocumentRenderer(document) { PrintDate = new DateTimeOffset(2024, 5, 6, 0, 0, 0, TimeSpan.Zero) };
        renderer.PrepareDocument();
        var fragment = (footer ? renderer.GetFooterRenderInfos(1) : renderer.GetHeaderRenderInfos(1)).Single();
        using var pdf = Paint(renderer, fragment, 1, 0);
        Texts(pdf).Should().Equal("Page", "9", "Date", "2024-05-06");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsForeignOrStaleFragments(bool stale)
    {
        var document = CreateDocument(); document.LastSection.AddParagraph("Body");
        var renderer = new DocumentRenderer(document); renderer.PrepareDocument();
        var fragment = renderer.GetRenderInfoFromPage(1)!.Single();
        if (stale) renderer.PrepareDocument();
        else { renderer = new DocumentRenderer(document); renderer.PrepareDocument(); }
        using var pdf = new PdfDocument(); using var gfx = XGraphics.FromPdfPage(pdf.AddPage());
        Action paint = () => renderer.RenderPreparedObject(gfx, fragment, 1, 0);
        paint.Should().Throw<ArgumentException>().WithParameterName("renderInfo");
        renderer.CurrentRenderPage.Should().Be(0);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void RejectsInvalidPages(int page)
    {
        var document = CreateDocument(); document.LastSection.AddParagraph("Body");
        var renderer = new DocumentRenderer(document); renderer.PrepareDocument();
        var fragment = renderer.GetRenderInfoFromPage(1)!.Single();
        using var pdf = new PdfDocument(); using var gfx = XGraphics.FromPdfPage(pdf.AddPage());
        Action paint = () => renderer.RenderPreparedObject(gfx, fragment, page, 0);
        paint.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("page");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsNonfiniteOffsets(double offset)
    {
        var document = CreateDocument(); document.LastSection.AddParagraph("Body");
        var renderer = new DocumentRenderer(document); renderer.PrepareDocument();
        var fragment = renderer.GetRenderInfoFromPage(1)!.Single();
        using var pdf = new PdfDocument(); using var gfx = XGraphics.FromPdfPage(pdf.AddPage());
        Action paint = () => renderer.RenderPreparedObject(gfx, fragment, 1, offset);
        paint.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("verticalOffset");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoresNestedPageAndGraphicsStateOnPaintFailure(bool fail)
    {
        var document = CreateDocument(); var section = document.LastSection;
        var frame = section.AddTextFrame(); frame.Width = Unit.FromPoint(100); frame.Height = Unit.FromPoint(20);
        frame.AddParagraph("Frame"); section.AddPageBreak(); section.AddParagraph("Second");
        var renderer = new DocumentRenderer(document); renderer.PrepareDocument();
        var fragment = renderer.GetRenderInfoFromPage(1)!.Single();
        var originalY = fragment.LayoutInfo.ContentArea.Y;
        var expected = new InvalidOperationException("Paint failure");
        renderer.ClipTextFrameContent = _ =>
        {
            renderer.CurrentRenderPage.Should().Be(1);
            if (fail) throw expected;
            return false;
        };
        renderer.PaintBehindContent = (gfx, _) =>
        {
            var transform = gfx.Transform;
            Action paint = () => renderer.RenderPreparedObject(gfx, fragment, 1, 15);
            if (fail) paint.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(expected);
            else paint.Should().NotThrow();
            renderer.CurrentRenderPage.Should().Be(2);
            gfx.Transform.Should().Be(transform);
        };
        using var pdf = new PdfDocument();
        using (var gfx = XGraphics.FromPdfPage(pdf.AddPage())) renderer.RenderPage(gfx, 2);
        renderer.CurrentRenderPage.Should().Be(0);
        fragment.LayoutInfo.ContentArea.Y.Should().Be(originalY);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void RejectsMissingPreparationWrongPageAndBlankPage(int scenario)
    {
        var document = CreateDocument(); var section = document.LastSection;
        section.AddParagraph("First");
        if (scenario == 2)
        {
            section = document.AddSection();
            section.PageSetup.SectionStart = BreakType.BreakOddPage;
        }
        else section.AddPageBreak();
        section.AddParagraph("Second");
        var renderer = new DocumentRenderer(document); renderer.PrepareDocument();
        var fragment = renderer.GetRenderInfoFromPage(1)!.Single();
        if (scenario == 0) renderer = new DocumentRenderer(document);
        using var pdf = new PdfDocument(); using var gfx = XGraphics.FromPdfPage(pdf.AddPage());
        Action paint = () => renderer.RenderPreparedObject(gfx, fragment, 2, 0);
        if (scenario == 0) paint.Should().Throw<InvalidOperationException>();
        else paint.Should().Throw<ArgumentException>().WithParameterName(scenario == 1 ? "renderInfo" : "page");
        renderer.CurrentRenderPage.Should().Be(0);
    }

    static Document CreateDocument()
    {
        var document = new Document(); document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 8;
        document.Styles.Normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Exactly;
        document.Styles.Normal.ParagraphFormat.LineSpacing = Unit.FromPoint(10);
        var section = document.AddSection();
        section.PageSetup.PageHeight = Unit.FromPoint(160); section.PageSetup.PageWidth = Unit.FromPoint(240);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromPoint(20);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(20);
        return document;
    }

    static PdfDocument Paint(DocumentRenderer renderer, RenderInfo fragment, int page, double offset)
    {
        var pdf = new PdfDocument();
        using var gfx = XGraphics.FromPdfPage(pdf.AddPage());
        var transform = gfx.Transform;
        renderer.RenderPreparedObject(gfx, fragment, page, offset);
        gfx.Transform.Should().Be(transform);
        return pdf;
    }
    static string[] Texts(PdfDocument pdf)
    {
        var stream = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0); var texts = new List<string>();
        while (stream.Text.MoveAndGetNext(true, out var item))
            if (!string.IsNullOrWhiteSpace(item!.Text)) texts.Add(item.Text.Trim());
        return texts.ToArray();
    }
}
