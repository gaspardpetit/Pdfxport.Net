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
public class RenderObjectPageTests : IDisposable
{
    public RenderObjectPageTests()
    {
        PdfSharpCore.ResetAll();
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(1, "9", "1", "2", 0)]
    [InlineData(2, "10", "1", "2", 0)]
    [InlineData(3, "3", "2", "1", 0)]
    [InlineData(2, "10", "1", "2", 1)]
    [InlineData(3, "3", "2", "1", 2)]
    public void UsesPreparedPageFieldsWithoutRenderingPageFirst(int page, string number,
        string sectionNumber, string sectionPages, int objectKind)
    {
        var (document, paragraph) = CreateDocument();
        DocumentObject obj = paragraph;
        if (objectKind == 1)
        {
            var textFrame = document.LastSection.AddTextFrame();
            textFrame.Width = Unit.FromPoint(400);
            textFrame.Height = Unit.FromPoint(100);
            textFrame.Elements.Add(paragraph.Clone());
            obj = textFrame;
        }
        if (objectKind == 2)
        {
            var table = document.LastSection.AddTable();
            table.AddColumn(Unit.FromPoint(400));
            table.AddRow().Cells[0].Elements.Add(paragraph.Clone());
            obj = table;
        }
        var renderer = new DocumentRenderer(document) { PrintDate = new DateTimeOffset(2024, 5, 6, 0, 0, 0, TimeSpan.Zero) };
        renderer.PrepareDocument();
        using var pdf = new PdfDocument();
        using (var gfx = XGraphics.FromPdfPage(pdf.AddPage()))
            renderer.RenderObject(gfx, 20, 30, 400, obj, page);
        Texts(pdf).Should().Equal("Page", number, "Total", "3", "Section", sectionNumber,
            "SectionPages", sectionPages, "Reference", "9", "Date", "2024-05-06");
        renderer.FormattedDocument!.PageCount.Should().Be(3);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4)]
    public void RejectsInvalidPhysicalPage(int page)
    {
        var (document, paragraph) = CreateDocument();
        var renderer = new DocumentRenderer(document);
        renderer.PrepareDocument();
        using var pdf = new PdfDocument();
        using var gfx = XGraphics.FromPdfPage(pdf.AddPage());
        Action render = () => renderer.RenderObject(gfx, 20, 30, 400, paragraph, page);
        render.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("page");
    }

    [Fact]
    public void RequiresPreparedDocument()
    {
        var (document, paragraph) = CreateDocument();
        var renderer = new DocumentRenderer(document);
        using var pdf = new PdfDocument();
        using var gfx = XGraphics.FromPdfPage(pdf.AddPage());
        Action render = () => renderer.RenderObject(gfx, 20, 30, 400, paragraph, 1);
        render.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RejectsInsertedBlankPage()
    {
        var (document, paragraph) = CreateDocument();
        document.Sections[1].PageSetup.SectionStart = BreakType.BreakEvenPage;
        var renderer = new DocumentRenderer(document);
        renderer.PrepareDocument();
        using var pdf = new PdfDocument();
        using var gfx = XGraphics.FromPdfPage(pdf.AddPage());
        Action render = () => renderer.RenderObject(gfx, 20, 30, 400, paragraph, 3);
        render.Should().Throw<ArgumentException>().WithParameterName("page");
    }

    [Fact]
    public void LegacyOverloadStillRendersWithoutPreparation()
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        var paragraph = document.AddSection().AddParagraph("Legacy");
        paragraph.Format.Font.Name = "Arial";
        paragraph.Format.Font.Size = 10;
        var renderer = new DocumentRenderer(document);
        using var pdf = new PdfDocument();
        using (var gfx = XGraphics.FromPdfPage(pdf.AddPage()))
            renderer.RenderObject(gfx, 20, 30, 400, paragraph);
        Texts(pdf).Should().Equal("Legacy");
    }

    static string[] Texts(PdfDocument document)
    {
        var stream = PdfFileHelper.GetPageContentStreamEnumerator(document, 0);
        var text = new List<string>();
        while (stream.Text.MoveAndGetNext(true, out var item))
            if (!string.IsNullOrWhiteSpace(item!.Text)) text.Add(item.Text.Trim());
        return text.ToArray();
    }

    static (Document Document, Paragraph Fields) CreateDocument()
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        var section = document.AddSection();
        section.PageSetup.StartingNumber = 9;
        section.AddParagraph("First").AddBookmark("FirstTarget");
        section.AddPageBreak();
        section.AddParagraph("Second");
        section = document.AddSection();
        section.PageSetup.StartingNumber = 3;
        var fields = section.AddParagraph();
        fields.AddText("Page "); fields.AddPageField();
        fields.AddText(" Total "); fields.AddNumPagesField();
        fields.AddText(" Section "); fields.AddSectionField();
        fields.AddText(" SectionPages "); fields.AddSectionPagesField();
        fields.AddText(" Reference "); fields.AddPageRefField("FirstTarget");
        fields.AddText(" Date "); fields.AddDateField("yyyy-MM-dd");
        return (document, fields);
    }
}
