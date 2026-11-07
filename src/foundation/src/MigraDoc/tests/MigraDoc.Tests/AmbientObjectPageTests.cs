// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel;
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
public class AmbientObjectPageTests : IDisposable
{
    public AmbientObjectPageTests()
    {
        PdfSharpCore.ResetAll();
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(1, "9", "1", "2")]
    [InlineData(2, "10", "1", "2")]
    [InlineData(3, "3", "2", "1")]
    public void ImplicitObjectFieldsUseActivePhysicalPage(int page, string number, string section, string sectionPages)
    {
        var (renderer, fields) = CreateRenderer();
        renderer.PaintBehindContent = (gfx, _) => renderer.RenderObject(gfx, 20, 30, 400, fields);
        using var pdf = Render(renderer, page);
        Texts(pdf).Take(12).Should().Equal("Page", number, "Total", "3", "Section", section,
            "SectionPages", sectionPages, "Reference", "9", "Date", "2024-05-06");
        renderer.CurrentRenderPage.Should().Be(0);
    }

    [Fact]
    public void NestedPaintingUsesAndRestoresEnclosingFieldContext()
    {
        var (renderer, fields) = CreateRenderer();
        using var inner = new PdfDocument();
        renderer.PaintBehindContent = (gfx, page) =>
        {
            if (page == 1)
            {
                using var innerGfx = XGraphics.FromPdfPage(inner.AddPage());
                renderer.RenderPage(innerGfx, 2);
            }
            renderer.RenderObject(gfx, 20, 30, 400, fields);
        };
        using var outer = Render(renderer, 1);
        Texts(inner).Take(2).Should().Equal("Page", "10");
        Texts(outer).Take(2).Should().Equal("Page", "9");
        renderer.CurrentRenderPage.Should().Be(0);
    }

    [Fact]
    public void PreparedFrameCallbackUsesPreparedPageContext()
    {
        var (renderer, fields) = CreateRenderer(false);
        var frame = fields.Document!.Sections[0].AddTextFrame();
        frame.Width = Unit.FromPoint(100); frame.Height = Unit.FromPoint(20); frame.AddParagraph("Frame");
        renderer.PrepareDocument();
        var fragment = renderer.GetRenderInfoFromPage(2)!.Single(info => ReferenceEquals(info.DocumentObject, frame));
        using var pdf = new PdfDocument();
        using (var gfx = XGraphics.FromPdfPage(pdf.AddPage()))
        {
            renderer.ClipTextFrameContent = _ =>
            {
                renderer.RenderObject(gfx, 20, 30, 400, fields);
                return false;
            };
            renderer.RenderPreparedObject(gfx, fragment, 2, 0);
        }
        Texts(pdf).Take(2).Should().Equal("Page", "10");
        renderer.CurrentRenderPage.Should().Be(0);
    }

    [Fact]
    public void AmbientFormattingKeepsPreparedBookmarkLocations()
    {
        var (renderer, fields) = CreateRenderer();
        var bookmarkOwner = renderer.GetDocumentObjectsFromPage(1)[0];
        renderer.PaintBehindContent = (gfx, _) => renderer.RenderObject(gfx, 20, 30, 400, bookmarkOwner);
        using var moved = Render(renderer, 2);
        renderer.PaintBehindContent = null;
        using var reference = new PdfDocument();
        using (var gfx = XGraphics.FromPdfPage(reference.AddPage()))
            renderer.RenderObject(gfx, 20, 30, 400, fields, 3);
        Texts(reference).Should().ContainInOrder("Reference", "9");
    }

    static (DocumentRenderer Renderer, Paragraph Fields) CreateRenderer(bool prepare = true)
    {
        var document = new Document(); document.Styles.Normal.Font.Name = "Arial";
        var section = document.AddSection(); section.PageSetup.StartingNumber = 9;
        section.AddParagraph("First").AddBookmark("Target");
        section.AddPageBreak(); section.AddParagraph("Second");
        section = document.AddSection(); section.PageSetup.StartingNumber = 3;
        var fields = section.AddParagraph("Page "); fields.AddPageField();
        fields.AddText(" Total "); fields.AddNumPagesField();
        fields.AddText(" Section "); fields.AddSectionField();
        fields.AddText(" SectionPages "); fields.AddSectionPagesField();
        fields.AddText(" Reference "); fields.AddPageRefField("Target");
        fields.AddText(" Date "); fields.AddDateField("yyyy-MM-dd");
        var renderer = new DocumentRenderer(document) { PrintDate = new DateTimeOffset(2024, 5, 6, 0, 0, 0, TimeSpan.Zero) };
        if (prepare) renderer.PrepareDocument();
        return (renderer, fields);
    }
    static PdfDocument Render(DocumentRenderer renderer, int page)
    {
        var pdf = new PdfDocument(); using var gfx = XGraphics.FromPdfPage(pdf.AddPage());
        renderer.RenderPage(gfx, page); return pdf;
    }
    static string[] Texts(PdfDocument pdf)
    {
        var stream = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0); var texts = new List<string>();
        while (stream.Text.MoveAndGetNext(true, out var item))
            if (!string.IsNullOrWhiteSpace(item!.Text)) texts.Add(item.Text.Trim());
        return texts.ToArray();
    }
}
