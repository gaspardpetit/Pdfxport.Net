// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using MigraDoc.DocumentObjectModel.Shapes;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class BehindContentPaintTests : IDisposable
{
    public BehindContentPaintTests()
    {
        PdfSharpCore.ResetAll();
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PaintsAfterHeaderFooterAndBeforeBody(bool enabled)
    {
        var document = CreateDocument();
        var section = document.LastSection;
        section.Headers.Primary.AddParagraph("Header");
        section.Footers.Primary.AddParagraph("Footer");
        var frame = section.AddTextFrame();
        frame.RelativeVertical = RelativeVertical.Page;
        frame.Top = Unit.FromPoint(40);
        frame.Width = Unit.FromPoint(100);
        frame.Height = Unit.FromPoint(20);
        frame.AddParagraph("Frame");
        var renderer = new DocumentRenderer(document) { TextFrameBehindContent = _ => true };
        if (enabled)
            renderer.PaintBehindContent = (gfx, _) =>
                gfx.DrawString("Behind", new XFont("Arial", 10), XBrushes.Black, 20, 20);
        renderer.PrepareDocument();
        using var pdf = new PdfDocument();
        using (var gfx = XGraphics.FromPdfPage(pdf.AddPage())) renderer.RenderPage(gfx, 1);
        Texts(pdf).Should().Equal(enabled ? new[] { "Header", "Footer", "Behind", "Frame", "Body" } : new[] { "Header", "Footer", "Frame", "Body" });
    }

    [Theory]
    [InlineData(PageRenderOptions.None, 0)]
    [InlineData(PageRenderOptions.RenderHeader, 0)]
    [InlineData(PageRenderOptions.RenderFooter, 0)]
    [InlineData(PageRenderOptions.RenderHeader | PageRenderOptions.RenderFooter, 0)]
    [InlineData(PageRenderOptions.RenderContent, 1)]
    public void RespectsContentRenderOption(PageRenderOptions options, int expectedCalls)
    {
        var renderer = new DocumentRenderer(CreateDocument());
        var calls = 0;
        renderer.PaintBehindContent = (_, _) => calls++;
        renderer.PrepareDocument();
        using var pdf = new PdfDocument();
        using (var gfx = XGraphics.FromPdfPage(pdf.AddPage())) renderer.RenderPage(gfx, 1, options);
        calls.Should().Be(expectedCalls);
    }

    [Theory]
    [InlineData(1, "9")]
    [InlineData(2, "10")]
    public void SuppliesPhysicalPageForPageAwareObjectRendering(int page, string shownNumber)
    {
        var document = CreateDocument();
        var section = document.LastSection;
        section.PageSetup.StartingNumber = 9;
        var fields = section.Elements[0] as Paragraph;
        fields!.AddPageField();
        section.AddPageBreak();
        section.AddParagraph("Second");
        var renderer = new DocumentRenderer(document);
        var seen = new List<int>();
        renderer.PaintBehindContent = (gfx, physicalPage) =>
        {
            seen.Add(physicalPage);
            renderer.RenderObject(gfx, 20, 30, 400, fields, physicalPage);
        };
        renderer.PrepareDocument();
        using var pdf = new PdfDocument();
        using (var gfx = XGraphics.FromPdfPage(pdf.AddPage())) renderer.RenderPage(gfx, page);
        seen.Should().Equal(page);
        Texts(pdf).Take(2).Should().Equal("Body", shownNumber);
    }

    [Fact]
    public void SkipsAutomaticallyInsertedBlankPage()
    {
        var document = CreateDocument();
        var section = document.AddSection();
        section.PageSetup.SectionStart = BreakType.BreakOddPage;
        section.AddParagraph("Third");
        var renderer = new DocumentRenderer(document);
        var calls = 0;
        renderer.PaintBehindContent = (_, _) => calls++;
        renderer.PrepareDocument();
        renderer.FormattedDocument!.PageCount.Should().Be(3);
        using var pdf = new PdfDocument();
        using (var gfx = XGraphics.FromPdfPage(pdf.AddPage())) renderer.RenderPage(gfx, 2);
        calls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RestoresGraphicsStateEvenWhenCallbackThrows(bool throws)
    {
        var renderer = new DocumentRenderer(CreateDocument());
        var expected = new InvalidOperationException("Callback failure");
        renderer.PaintBehindContent = (gfx, _) =>
        {
            gfx.TranslateTransform(100, 200);
            if (throws) throw expected;
        };
        renderer.PrepareDocument();
        using var pdf = new PdfDocument();
        using var gfx = XGraphics.FromPdfPage(pdf.AddPage());
        var originalTransform = gfx.Transform;
        Action render = () => renderer.RenderPage(gfx, 1);
        if (throws) render.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(expected);
        else render.Should().NotThrow();
        gfx.Transform.Should().Be(originalTransform);
    }

    static Document CreateDocument()
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.AddSection().AddParagraph("Body");
        return document;
    }

    static string[] Texts(PdfDocument document)
    {
        var stream = PdfFileHelper.GetPageContentStreamEnumerator(document, 0);
        var texts = new List<string>();
        while (stream.Text.MoveAndGetNext(true, out var item))
            if (!string.IsNullOrWhiteSpace(item!.Text)) texts.Add(item.Text.Trim());
        return texts.ToArray();
    }
}
