using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
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
public class CellRenderedOriginTests : IDisposable
{
    public CellRenderedOriginTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(VerticalAlignment.Top, 0)]
    [InlineData(VerticalAlignment.Top, 2)]
    [InlineData(VerticalAlignment.Center, 0)]
    [InlineData(VerticalAlignment.Center, 2)]
    [InlineData(VerticalAlignment.Bottom, 0)]
    [InlineData(VerticalAlignment.Bottom, 2)]
    public void OriginMapsNestedCoordinatesToPaintedText(VerticalAlignment alignment, double border)
    {
        var (renderer, table) = Prepare(alignment, border);
        var cell = Cells(renderer, 1)[table[0, 0]];
        cell.GetRenderedContentOrigin(1).Should().BeNull();
        cell.GetRenderedContentOrigin(0).Should().BeNull();
        cell.GetRenderedContentOrigin(-1).Should().BeNull();
        using var pdf = Paint(renderer);
        var origin = cell.GetRenderedContentOrigin(1)!.Value;
        var nested = cell.GetRenderInfos()!.Single().LayoutInfo.ContentArea;
        var control = renderer.GetRenderInfoFromPage(1)!.First().LayoutInfo.ContentArea;
        var text = Text(pdf, 0);
        var baseline = text["Control"].Y - control.Y.Point;
        (origin.X + nested.X.Point).Should().BeApproximately(text["Header"].X, 0.001);
        (origin.Y + nested.Y.Point + baseline).Should().BeApproximately(text["Header"].Y, 0.001);
        cell.GetRenderedContentOrigin(2).Should().BeNull();
        // Painting does not leave nested render infos shifted to page coordinates.
        cell.GetRenderInfos()!.Single().LayoutInfo.ContentArea.X.Should().Be(nested.X);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RepeatedHeaderRetainsIndependentOriginsWhenPagesPaintOutOfOrder(bool reverse)
    {
        var (renderer, table) = Prepare(VerticalAlignment.Top, 1, rows: 12);
        renderer.FormattedDocument.PageCount.Should().BeGreaterThan(1);
        var header = Cells(renderer, 1)[table[0, 0]];
        using var pdf = Paint(renderer, reverse);
        var nested = header.GetRenderInfos()!.Single().LayoutInfo.ContentArea;
        var controlArea = renderer.GetRenderInfoFromPage(1)!.First().LayoutInfo.ContentArea;
        var baseline = Text(pdf, 0)["Control"].Y - controlArea.Y.Point;
        for (var page = 1; page <= pdf.PageCount; page++)
        {
            var origin = header.GetRenderedContentOrigin(page)!.Value;
            var text = Text(pdf, page - 1)["Header"];
            (origin.X + nested.X.Point).Should().BeApproximately(text.X, 0.001);
            (origin.Y + nested.Y.Point + baseline).Should().BeApproximately(text.Y, 0.001);
        }
        header.GetRenderedContentOrigin(1)!.Value.Y.Should().BeGreaterThan(
            header.GetRenderedContentOrigin(2)!.Value.Y);
        var firstBody = Cells(renderer, 1)[table[1, 0]];
        firstBody.GetRenderedContentOrigin(pdf.PageCount).Should().BeNull();
    }

    [Fact]
    public void RepreparingCreatesFreshUnpaintedCellState()
    {
        var (renderer, table) = Prepare(VerticalAlignment.Top, 0);
        var original = Cells(renderer, 1)[table[0, 0]];
        using var pdf = Paint(renderer);
        original.GetRenderedContentOrigin(1).Should().NotBeNull();
        renderer.PrepareDocument();
        var fresh = Cells(renderer, 1)[table[0, 0]];
        fresh.Should().NotBeSameAs(original);
        fresh.GetRenderedContentOrigin(1).Should().BeNull();
    }

    static Dictionary<Cell, FormattedCell> Cells(DocumentRenderer renderer, int page) =>
        ((TableFormatInfo)renderer.GetRenderInfoFromPage(page)!.OfType<TableRenderInfo>().Single().FormatInfo).FormattedCells!;

    static (DocumentRenderer Renderer, Table Table) Prepare(VerticalAlignment alignment, double border, int rows = 2)
    {
        var doc = new Document();
        doc.Styles.Normal.Font.Name = "Arial";
        doc.Styles.Normal.Font.Size = 8;
        var section = doc.AddSection();
        section.PageSetup.PageWidth = Unit.FromPoint(240);
        section.PageSetup.PageHeight = Unit.FromPoint(240);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(20);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromPoint(20);
        section.AddParagraph("Control");
        var table = section.AddTable();
        table.AddColumn(Unit.FromPoint(160));
        table.Columns[0].LeftPadding = Unit.FromPoint(7);
        table.Rows.LeftIndent = Unit.FromPoint(11);
        table.TopPadding = Unit.FromPoint(5);
        table.BottomPadding = Unit.FromPoint(3);
        table.Borders.Width = Unit.FromPoint(border);
        for (var i = 0; i < rows; i++)
        {
            var row = table.AddRow();
            row.Height = Unit.FromPoint(40);
            row.HeightRule = RowHeightRule.Exactly;
            row.VerticalAlignment = alignment;
            row[0].AddParagraph(i == 0 ? "Header" : $"Body{i}");
        }
        table.Rows[0].HeadingFormat = true;
        var renderer = new DocumentRenderer(doc);
        renderer.PrepareDocument();
        return (renderer, table);
    }

    static PdfDocument Paint(DocumentRenderer renderer, bool reverse = false)
    {
        var pdf = new PdfDocument();
        for (var i = 0; i < renderer.FormattedDocument.PageCount; i++)
        {
            var page = pdf.AddPage();
            page.Width = page.Height = XUnit.FromPoint(240);
        }
        var pages = Enumerable.Range(1, pdf.PageCount);
        if (reverse) pages = pages.Reverse();
        foreach (var page in pages)
        {
            using var gfx = XGraphics.FromPdfPage(pdf.Pages[page - 1]);
            renderer.RenderPage(gfx, page);
        }
        return pdf;
    }

    static Dictionary<string, (double X, double Y)> Text(PdfDocument pdf, int page)
    {
        var result = new Dictionary<string, (double, double)>();
        var reader = PdfFileHelper.GetPageContentStreamEnumerator(pdf, page).Text;
        while (reader.MoveAndGetNext(true, out var item))
            result[item!.Text] = (item.X, pdf.Pages[page].Height.Point - item.Y);
        return result;
    }
}
