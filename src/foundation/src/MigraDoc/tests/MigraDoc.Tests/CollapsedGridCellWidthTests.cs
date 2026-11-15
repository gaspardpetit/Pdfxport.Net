using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.IO;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class CollapsedGridCellWidthTests : IDisposable
{
    public CollapsedGridCellWidthTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(false, 0, 0)]
    [InlineData(false, 4, 7)]
    [InlineData(true, 0, 0)]
    [InlineData(true, 4, 7)]
    public void GridWidthRetainsPaddingAndAllowsTextToFit(bool merged, double left, double right)
    {
        var normal = Render(false, merged, left, right);
        var grid = Render(true, merged, left, right);
        normal.ContentHeight.Should().BeApproximately(20, 0.001);
        grid.ContentHeight.Should().BeApproximately(10, 0.001);
        grid.Origin.Should().Be(normal.Origin);
        grid.Borders.Should().NotBeEmpty();
        grid.Borders.Should().Equal(normal.Borders);
        grid.TextLines.Should().Be(1);
        normal.TextLines.Should().Be(2);
    }

    [Theory]
    [InlineData(BorderStyle.Single)]
    [InlineData(BorderStyle.Double)]
    public void UsesTheEffectiveSharedBorderWithoutMovingNeighborCells(BorderStyle style)
    {
        var normal = Render(false, false, 4, 7, neighbor: true, style: style);
        var grid = Render(true, false, 4, 7, neighbor: true, style: style);
        normal.ContentHeight.Should().BeApproximately(20, 0.001);
        grid.ContentHeight.Should().BeApproximately(10, 0.001);
        grid.NeighborOrigin.Should().Be(normal.NeighborOrigin);
        grid.Borders.Should().Equal(normal.Borders);
    }

    [Fact]
    public void UnsetAndExplicitFalseHaveIdenticalOutput() =>
        Render(null, false, 4, 7).Should().BeEquivalentTo(Render(false, false, 4, 7));

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void NullableSettingSurvivesCloneAndDdl(bool? enabled)
    {
        var document = new Document();
        var table = document.AddSection().AddTable();
        table.AddColumn(Unit.FromPoint(100));
        table.AddRow()[0].AddParagraph("Text");
        if (enabled.HasValue) table.CellMarginsMeasuredFromCollapsedGridEdge = enabled.Value;
        table.CellMarginsMeasuredFromCollapsedGridEdge.Should().Be(enabled ?? false);
        table.Clone().Values.CellMarginsMeasuredFromCollapsedGridEdge.Should().Be(enabled);
        var ddl = DdlWriter.WriteToString(document);
        ddl.Contains("CellMarginsMeasuredFromCollapsedGridEdge").Should().Be(enabled.HasValue);
        var restored = (Table)DdlReader.DocumentFromString(ddl)!.Sections[0].Elements[0];
        restored.Values.CellMarginsMeasuredFromCollapsedGridEdge.Should().Be(enabled);
    }

    [Fact]
    public void NoVisibleRightBorderMakesThePolicyANoOp()
    {
        Render(true, false, 4, 7, border: 0).Should().BeEquivalentTo(
            Render(false, false, 4, 7, border: 0));
    }

    [Fact]
    public void HiddenRightBorderMakesThePolicyANoOp()
    {
        Render(true, false, 4, 7, visible: false).Should().BeEquivalentTo(
            Render(false, false, 4, 7, visible: false));
    }

    record Result(double ContentHeight, XPoint Origin, List<(double, double, double, double)> Borders, int TextLines, XPoint? NeighborOrigin);
    static Result Render(bool? enabled, bool merged, double left, double right, double border = 10, bool visible = true, bool neighbor = false, BorderStyle style = BorderStyle.Single)
    {
        using var measure = XGraphics.CreateMeasureContext(new XSize(300, 300),
            XGraphicsUnit.Point, XPageDirection.Downwards);
        var textWidth = measure.MeasureString("Alpha Beta", new XFont("Arial", 10)).Width;
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 10;
        var section = document.AddSection();
        var table = section.AddTable();
        if (enabled.HasValue) table.CellMarginsMeasuredFromCollapsedGridEdge = enabled.Value;
        var totalWidth = textWidth + left + right + 0.5;
        table.AddColumn(Unit.FromPoint(merged ? totalWidth / 2 : totalWidth));
        if (merged) table.AddColumn(Unit.FromPoint(totalWidth / 2));
        if (neighbor) table.AddColumn(Unit.FromPoint(80));
        table.LeftPadding = Unit.FromPoint(left);
        table.RightPadding = Unit.FromPoint(right);
        table.Borders.Width = Unit.FromPoint(border);
        table.Borders.Visible = visible;
        table.Borders.Style = style;
        var row = table.AddRow();
        row.Height = Unit.FromPoint(40);
        row.HeightRule = RowHeightRule.Exactly;
        row[0].MergeRight = merged ? 1 : 0;
        if (neighbor)
        {
            row[0].Borders.Right.Width = Unit.FromPoint(2);
            row[1].Borders.Left.Width = Unit.FromPoint(10);
            row[1].AddParagraph("Neighbor");
        }
        var paragraph = row[0].AddParagraph("Alpha Beta");
        paragraph.Format.LineSpacingRule = LineSpacingRule.Exactly;
        paragraph.Format.LineSpacing = Unit.FromPoint(10);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var format = (TableFormatInfo)renderer.DocumentRenderer.GetRenderInfoFromPage(1)!.Single().FormatInfo;
        var cell = format.FormattedCells![row[0]];
        var borders = new List<(double, double, double, double)>();
        var lines = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Line;
        while (lines.MoveAndGetNext(true, out var line))
            borders.Add((line!.X1, line.Y1, line.X2, line.Y2));
        var ys = new HashSet<double>();
        var texts = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        while (texts.MoveAndGetNext(true, out var text)) ys.Add(text!.Y);
        return new Result(cell.ContentHeight.Point, cell.GetRenderedContentOrigin(1)!.Value, borders, ys.Count,
            neighbor ? format.FormattedCells[row[1]].GetRenderedContentOrigin(1) : null);
    }
}
