// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class RowBoundaryPlacementTests : IDisposable
{
    public RowBoundaryPlacementTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void MixedOuterBoundaryReportsBothBandExtremes(double spacing)
    {
        var document = Create(1, 2, spacing);
        var table = Table(document);
        Set(table[0, 0].Borders.Top, 1);
        Set(table[0, 1].Borders.Top, 2, BorderStyle.Double);
        var contexts = Observe(document, out _);
        contexts.Should().HaveCount(2);
        foreach (var sample in contexts)
        {
            sample.Context.RowTopNarrowestBand.Should().BeApproximately(1, .001);
            sample.Context.RowTopBand.Should().BeApproximately(6, .001);
        }
        contexts.Single(s => s.Column == 1).Context.TopBand.Should().BeApproximately(6, .001);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(3, .5)]
    public void InternalBoundaryUsesResolvedOrIndependentEdges(double spacing, double expectedNarrowest)
    {
        var document = Create(2, 2, spacing);
        var table = Table(document);
        Set(table[0, 0].Borders.Bottom, .5);
        Set(table[1, 0].Borders.Top, 2);
        Set(table[0, 1].Borders.Bottom, 1, BorderStyle.Double);
        Set(table[1, 1].Borders.Top, 2);
        foreach (var sample in Observe(document, out _).Where(s => s.Row == 1))
        {
            sample.Context.RowTopNarrowestBand.Should().BeApproximately(expectedNarrowest, .001);
            sample.Context.RowTopBand.Should().BeApproximately(3, .001);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HiddenOrZeroWidthBoundaryReportsZero(bool zeroWidth)
    {
        var document = Create(1, 2, 0);
        foreach (var cell in new[] { Table(document)[0, 0], Table(document)[0, 1] })
        {
            Set(cell.Borders.Top, zeroWidth ? 0 : 4);
            cell.Borders.Top.Visible = zeroWidth;
        }
        foreach (var sample in Observe(document, out _))
        {
            sample.Context.RowTopNarrowestBand.Should().Be(0);
            sample.Context.RowTopBand.Should().Be(0);
        }
    }

    [Fact]
    public void ZeroWidthEdgesDoNotReplaceAPositiveMinimum()
    {
        var document = Create(1, 2, 0);
        Set(Table(document)[0, 0].Borders.Top, 0);
        Set(Table(document)[0, 1].Borders.Top, 2);
        foreach (var sample in Observe(document, out _))
            sample.Context.RowTopNarrowestBand.Should().BeApproximately(2, .001);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void ContinuingMergedCellDoesNotContributeItsOuterEdgesToAnInteriorBoundary(double spacing)
    {
        var document = Create(2, 3, spacing);
        var table = Table(document);
        table[0, 0].MergeDown = 1;
        Set(table[0, 0].Borders.Top, 10);
        Set(table[0, 0].Borders.Bottom, 10);
        for (int col = 1; col < 3; col++)
        {
            Set(table[0, col].Borders.Bottom, col == 1 ? 1 : 3);
            Set(table[1, col].Borders.Top, col == 1 ? 1 : 3);
        }
        var samples = Observe(document, out _).Where(s => s.Row == 1).ToArray();
        samples.Should().NotBeEmpty();
        foreach (var sample in samples)
        {
            sample.Context.RowTopNarrowestBand.Should().BeApproximately(1, .001);
            sample.Context.RowTopBand.Should().BeApproximately(3, .001);
        }
    }

    [Fact]
    public void RepeatedHeaderKeepsItsLogicalBoundaryMeasurements()
    {
        var document = Create(4, 2, 0);
        var setup = document.LastSection.PageSetup;
        setup.PageWidth = Unit.FromPoint(250); setup.PageHeight = Unit.FromPoint(200);
        setup.TopMargin = setup.BottomMargin = setup.LeftMargin = setup.RightMargin = Unit.FromPoint(20);
        var table = Table(document);
        table.Rows[0].HeadingFormat = true;
        foreach (var row in table.Rows.Cast<Row>())
        {
            row.HeightRule = RowHeightRule.Exactly;
            row.Height = Unit.FromPoint(60);
        }
        Set(table[0, 0].Borders.Top, 1);
        Set(table[0, 1].Borders.Top, 1, BorderStyle.Triple);
        var samples = Observe(document, out int pages).Where(s => s.Row == 0).ToArray();
        pages.Should().BeGreaterThan(1);
        samples.Length.Should().BeGreaterThan(2);
        foreach (var sample in samples)
        {
            sample.Context.RowTopNarrowestBand.Should().BeApproximately(1, .001);
            sample.Context.RowTopBand.Should().BeApproximately(5, .001);
        }
    }

    [Fact]
    public void ExistingConstructorAndDeconstructionRemainAvailable()
    {
        var context = new TableCellPlacementContext(default, default, 1, 2, 3, 4, 5, 6, 7, 8, 9);
        var (_, _, column, pitch, top, bottom, left, right, ownTop, ownBottom, padding) = context;
        new[] { column, pitch, top, bottom, left, right, ownTop, ownBottom, padding }
            .Should().Equal(1, 2, 3, 4, 5, 6, 7, 8, 9);
        context.RowTopNarrowestBand.Should().Be(0);
        var enriched = context with { RowTopNarrowestBand = .5 };
        enriched.RowTopNarrowestBand.Should().Be(.5);
        (enriched with { RowTopNarrowestBand = 0 }).Should().Be(context);
    }

    static Document Create(int rows, int columns, double spacing)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        var table = document.AddSection().AddTable();
        table.CellSpacing = Unit.FromPoint(spacing);
        for (int col = 0; col < columns; col++) table.AddColumn("3cm");
        for (int row = 0; row < rows; row++)
        {
            var cells = table.AddRow();
            for (int col = 0; col < columns; col++)
            {
                cells[col].Borders.Top.Visible = cells[col].Borders.Bottom.Visible =
                    cells[col].Borders.Left.Visible = cells[col].Borders.Right.Visible = false;
                cells[col].AddParagraph("Cell");
            }
        }
        return document;
    }
    static Table Table(Document document) => (Table)document.LastSection.Elements[0]!;
    static void Set(Border border, double width, BorderStyle style = BorderStyle.Single)
    {
        border.Visible = true; border.Width = Unit.FromPoint(width); border.Style = style;
    }
    static List<(int Row, int Column, TableCellPlacementContext Context)> Observe(Document document, out int pages)
    {
        var result = new List<(int, int, TableCellPlacementContext)>();
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.TableCellPlacement = (cell, context) =>
        {
            result.Add((cell.Row!.Index, cell.Column!.Index, context));
            return null;
        };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        pages = pdf.PageCount;
        return result;
    }
}
