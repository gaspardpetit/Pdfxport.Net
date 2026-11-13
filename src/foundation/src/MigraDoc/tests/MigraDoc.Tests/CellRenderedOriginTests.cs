using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
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
        fresh.GetRenderedContentTransform(1).Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    [InlineData(-12)]
    public void PreparedObjectOffsetsAreIncludedAndReplacedOnRepaint(double offset)
    {
        var (renderer, table) = Prepare(VerticalAlignment.Top, 1);
        var cell = Cells(renderer, 1)[table[0, 0]];
        using var original = Paint(renderer);
        var first = cell.GetRenderedContentOrigin(1)!.Value;
        using var pdf = new PdfDocument();
        var page = pdf.AddPage();
        page.Width = page.Height = XUnit.FromPoint(240);
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            renderer.RenderPreparedObject(gfx, renderer.GetRenderInfoFromPage(1)!.OfType<TableRenderInfo>().Single(),
                1, XUnitPt.FromPoint(offset));
            gfx.Transform.IsIdentity.Should().BeTrue();
        }
        renderer.CurrentRenderPage.Should().Be(0);
        var moved = cell.GetRenderedContentOrigin(1)!.Value;
        moved.X.Should().BeApproximately(first.X, 0.001);
        moved.Y.Should().BeApproximately(first.Y + offset, 0.001);
        AssertPoint(cell.GetRenderedContentTransform(1)!.Value.Transform(new XPoint(13, 19)),
            new XPoint(first.X + 13, first.Y + offset + 19));
    }

    [Theory]
    [InlineData(0, 1, 1)]
    [InlineData(30, 1, 1)]
    [InlineData(0, 2, 0.5)]
    public void GraphicsTransformsMapEveryNestedPoint(double angle, double scaleX, double scaleY)
    {
        var (renderer, table) = Prepare(VerticalAlignment.Center, 1);
        var cell = Cells(renderer, 1)[table[0, 0]];
        using var original = Paint(renderer);
        var origin = cell.GetRenderedContentOrigin(1)!.Value;
        using var pdf = new PdfDocument();
        var page = pdf.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            gfx.TranslateTransform(17, 29);
            gfx.RotateTransform(angle);
            gfx.ScaleTransform(scaleX, scaleY);
            var graphicsTransform = gfx.Transform;
            renderer.RenderPage(gfx, 1);
            var contentTransform = cell.GetRenderedContentTransform(1)!.Value;
            foreach (var point in new[] { new XPoint(0, 0), new XPoint(13, 19) })
                AssertPoint(contentTransform.Transform(point),
                    graphicsTransform.Transform(new XPoint(origin.X + point.X, origin.Y + point.Y)));
        }
        if (angle == 0 && scaleX == 1 && scaleY == 1)
            AssertPoint(cell.GetRenderedContentOrigin(1)!.Value, new XPoint(origin.X + 17, origin.Y + 29));
        else
            cell.GetRenderedContentOrigin(1).Should().BeNull();
        cell.GetRenderedContentTransform(0).Should().BeNull();
        cell.GetRenderedContentTransform(2).Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(37)]
    public void FrameTranslationsAndRotationsAreIncluded(double rotation)
    {
        var doc = new Document();
        doc.Styles.Normal.Font.Name = "Arial";
        doc.Styles.Normal.Font.Size = 8;
        var section = doc.AddSection();
        section.PageSetup.PageWidth = section.PageSetup.PageHeight = Unit.FromPoint(240);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromPoint(20);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(20);
        var frame = section.AddTextFrame();
        frame.RelativeVertical = RelativeVertical.Margin;
        frame.Left = Unit.FromPoint(35);
        frame.Width = Unit.FromPoint(160);
        frame.Height = Unit.FromPoint(200);
        frame.Rotation = rotation;
        frame.PaginateContent = true;
        frame.MarginLeft = frame.MarginRight = frame.MarginTop = frame.MarginBottom = Unit.Zero;
        var filler = frame.AddParagraph("Filler");
        filler.Format.LineSpacingRule = LineSpacingRule.Exactly;
        filler.Format.LineSpacing = Unit.FromPoint(180);
        var table = frame.Elements.AddTable();
        table.AddColumn(Unit.FromPoint(140));
        table.Columns[0].LeftPadding = Unit.FromPoint(7);
        table.Rows.LeftIndent = Unit.FromPoint(0);
        table.TopPadding = Unit.FromPoint(5);
        var row = table.AddRow();
        row.Height = Unit.FromPoint(40);
        row.HeightRule = RowHeightRule.Exactly;
        row[0].AddParagraph("Nested");
        var renderer = new DocumentRenderer(doc);
        renderer.PrepareDocument();
        var frameInfo = renderer.GetRenderInfoFromPage(2)!.Single(info => info.DocumentObject == frame);
        var nestedTable = renderer.GetTextFrameContinuationRenderInfos(frameInfo)!.OfType<TableRenderInfo>().Single();
        var cell = ((TableFormatInfo)nestedTable.FormatInfo).FormattedCells![row[0]];
        var area = frameInfo.LayoutInfo.ContentArea;
        var tableArea = nestedTable.LayoutInfo.ContentArea;
        var localOrigin = new XPoint(tableArea.X.Point + 7, tableArea.Y.Point + 5);
        using var pdf = Paint(renderer);
        var transform = cell.GetRenderedContentTransform(2)!.Value;
        // Independent geometry: local table translation, then frame position,
        // then rotation about the frame's center.
        foreach (var point in new[] { new XPoint(0, 0), new XPoint(11, 23) })
        {
            var x = area.X.Point + localOrigin.X + point.X;
            var y = area.Y.Point + localOrigin.Y + point.Y;
            var cx = area.X.Point + area.Width.Point / 2;
            var cy = area.Y.Point + area.Height.Point / 2;
            var radians = rotation * Math.PI / 180;
            AssertPoint(transform.Transform(point), new XPoint(
                cx + (x - cx) * Math.Cos(radians) - (y - cy) * Math.Sin(radians),
                cy + (x - cx) * Math.Sin(radians) + (y - cy) * Math.Cos(radians)));
        }
        if (rotation == 0)
            AssertPoint(cell.GetRenderedContentOrigin(2)!.Value,
                new XPoint(area.X.Point + localOrigin.X, area.Y.Point + localOrigin.Y));
        else
            cell.GetRenderedContentOrigin(2).Should().BeNull();
    }

    static void AssertPoint(XPoint actual, XPoint expected)
    {
        actual.X.Should().BeApproximately(expected.X, 0.001);
        actual.Y.Should().BeApproximately(expected.Y, 0.001);
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
