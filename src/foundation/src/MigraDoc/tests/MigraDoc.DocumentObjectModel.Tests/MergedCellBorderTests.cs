// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.DocumentObjectModel.Visitors;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.DocumentObjectModel.Tests;

[Collection("PDFsharp")]
public class MergedCellBorderTests : IDisposable
{
    public MergedCellBorderTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }

    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void EmptyTerminalSlotRetainsOwnerBorder(int span, bool rtf)
    {
        var cell = CreateCell(span);
        var border = Effective(cell, rtf).Right;
        border.Width.Point.Should().Be(2.25);
        border.Color.Should().Be(Colors.Magenta);
        cell.Borders.Right.Width.Point.Should().Be(2.25);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExplicitTerminalBorderStillTakesPrecedence(bool hidden, bool rtf)
    {
        var cell = CreateCell(1);
        var terminal = cell.Table![0, 1];
        terminal.Borders.Right.Width = Unit.FromPoint(3.5);
        terminal.Borders.Right.Color = Colors.Blue;
        terminal.Borders.Right.Visible = !hidden;
        var border = Effective(cell, rtf).Right;
        border.Width.Point.Should().Be(3.5);
        border.Color.Should().Be(Colors.Blue);
        border.Visible.Should().Be(!hidden);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitZeroWidthTerminalBorderStillSuppressesOwner(bool rtf)
    {
        var cell = CreateCell(1);
        cell.Table![0, 1].Borders.Right.Width = Unit.FromPoint(0);
        Effective(cell, rtf).Right.Width.Point.Should().Be(0);
    }

    [Theory]
    [InlineData(false, 4, true)]
    [InlineData(true, 4, true)]
    [InlineData(false, 2.25, false)]
    [InlineData(true, 2.25, false)]
    public void NeighborBorderConflictRulesRemainUnchanged(bool rtf, double width, bool neighborWins)
    {
        var cell = CreateCell(1);
        var neighbor = cell.Table![0, 2];
        neighbor.Borders.Left.Width = Unit.FromPoint(width);
        neighbor.Borders.Left.Color = Colors.Blue;
        var border = Effective(cell, rtf).Right;
        border.Width.Point.Should().Be(neighborWins ? width : 2.25);
        border.Color.Should().Be(neighborWins ? Colors.Blue : Colors.Magenta);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnmergedOwnerBorderIsUnchanged(bool rtf)
    {
        Effective(CreateCell(0), rtf).Right.Width.Point.Should().Be(2.25);
    }

    [Fact]
    public void MergedOuterBorderPaintMatchesUnmergedOuterEdge()
    {
        (double X1, double Y1, double X2, double Y2) Render(bool merge)
        {
            var cell = CreateCell(merge ? 1 : 0, outerEdge: true);
            cell.Document!.Styles.Normal.Font.Name = "Arial";
            cell.Row!.Height = Unit.FromPoint(40);
            cell.Row!.HeightRule = RowHeightRule.Exactly;
            cell.AddParagraph("Cell");
            var renderer = new PdfDocumentRenderer { Document = cell.Document };
            renderer.RenderDocument();
            renderer.PageCount.Should().Be(1);
            var stream = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0);
            stream.Line.MoveAndGetNext(true, out var line).Should().BeTrue();
            var result = (line!.X1, line.Y1, line.X2, line.Y2);
            line.X1.Should().BeApproximately(line.X2, .001);
            stream.Line.MoveAndGetNext(true, out _).Should().BeFalse();
            return result;
        }

        Render(true).Should().Be(Render(false));
    }

    static Borders Effective(Cell cell, bool rtf)
    {
        var cells = new MergedCellList(cell.Table!);
        return rtf ? cells.GetEffectiveBordersRtf(cell) : cells.GetEffectiveBordersPdf(cell, -1);
    }

    static Cell CreateCell(int mergeRight, bool outerEdge = false)
    {
        var table = new Document().AddSection().AddTable();
        for (var index = 0; index < 4; index++)
            table.AddColumn(Unit.FromPoint(72));
        var row = table.AddRow();
        var cell = row[outerEdge ? 3 - mergeRight : 0];
        cell.MergeRight = mergeRight;
        cell.Borders.Right.Style = BorderStyle.Single;
        cell.Borders.Right.Width = Unit.FromPoint(2.25);
        cell.Borders.Right.Color = Colors.Magenta;
        return cell;
    }
}
