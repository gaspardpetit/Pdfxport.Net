using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Visitors;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests
{
    [Collection("PDFsharp")]
    public class TableLayoutInspectionTests : IDisposable
    {
        public TableLayoutInspectionTests()
        {
            PdfSharpCore.ResetAll();
#if CORE
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
        }

        public void Dispose() => PdfSharpCore.ResetAll();

        [Fact]
        public void SpacedCellsRetainTheirOwnFacingBorders()
        {
            var table = new Document().AddSection().AddTable();
            table.AddColumn(Unit.FromPoint(80));
            table.AddColumn(Unit.FromPoint(80));
            var row = table.AddRow();
            row.Cells[0].Borders.Right.Width = Unit.FromPoint(1);
            row.Cells[1].Borders.Left.Width = Unit.FromPoint(3);

            var mergedCells = new MergedCellList(table);
            mergedCells.GetEffectiveBordersPdf(row.Cells[0], -1).Right.Width.Point
                .Should().Be(3);

            table.CellSpacing = Unit.FromPoint(5);
            mergedCells = new MergedCellList(table);
            mergedCells.GetEffectiveBordersPdf(row.Cells[0], -1).Right.Width.Point
                .Should().Be(1);
            mergedCells.GetEffectiveBordersPdf(row.Cells[1], -1).Left.Width.Point
                .Should().Be(3);
        }

        [Fact]
        public void TableFragmentExposesRepeatedHeaderBoundariesAndCellContent()
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var section = document.AddSection();
            var table = section.AddTable();
            table.AddColumn(Unit.FromPoint(100));
            var header = table.AddRow();
            header.HeadingFormat = true;
            var headerParagraph = header.Cells[0].AddParagraph("Header");
            var body = table.AddRow();
            var bodyParagraph = body.Cells[0].AddParagraph("Body");
            var renderer = new PdfDocumentRenderer { Document = document };

            renderer.RenderDocument();

            var tableInfo = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!
                .Select(info => info.FormatInfo).OfType<TableFormatInfo>().Single();
            tableInfo.LastHeaderRowIndex.Should().Be(0);
            tableInfo.StartRow.Should().Be(1);
            tableInfo.RowBoundaries.Should().NotBeNull();
            tableInfo.RowBoundaries!.Keys.Should().Contain([0, 1, 2]);
            tableInfo.RowBoundaries[0].Point.Should().BeLessThan(tableInfo.RowBoundaries[1].Point);
            tableInfo.RowBoundaries[1].Point.Should().BeLessThan(tableInfo.RowBoundaries[2].Point);
            tableInfo.FormattedCells.Should().NotBeNull();
            var headerCell = tableInfo.FormattedCells![header.Cells[0]];
            var bodyCell = tableInfo.FormattedCells[body.Cells[0]];
            headerCell.ContentHeight.Point.Should().BeGreaterThan(0);
            bodyCell.ContentHeight.Point.Should().BeGreaterThan(0);
            headerCell.GetRenderInfos()!.Select(info => info.DocumentObject)
                .Should().Contain(headerParagraph);
            bodyCell.GetRenderInfos()!.Select(info => info.DocumentObject)
                .Should().Contain(bodyParagraph);
        }
    }
}
