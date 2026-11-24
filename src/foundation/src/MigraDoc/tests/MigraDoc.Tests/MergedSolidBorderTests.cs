// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using System.Globalization;
using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using MigraDoc.DocumentObjectModel.Tables;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Quality;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class MergedSolidBorderTests : IDisposable
{
    public MergedSolidBorderTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }

    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(.75, false)]
    [InlineData(3, false)]
    [InlineData(.75, true)]
    [InlineData(3, true)]
    public void MergedBottomOwnsMatchingShorterTops(double width, bool transparent)
    {
        var (renderer, table, document) = Create(width, transparent);
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var lines = Lines(pdf.Pages[0]);
        lines.Should().ContainSingle("the merged bottom already paints both shorter top borders");
        Math.Abs(lines[0].X2 - lines[0].X1).Should().BeApproximately(160, .01);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpacedCellsRetainIndependentEdges(bool transparent)
    {
        var (renderer, table, document) = Create(3, transparent);
        table.CellSpacing = Unit.FromPoint(4);
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        Lines(pdf.Pages[0]).Should().HaveCount(3);
    }

    [Theory]
    [InlineData("color")]
    [InlineData("alpha")]
    [InlineData("width")]
    [InlineData("dash")]
    public void DifferentStrokeRemainsVisible(string difference)
    {
        var (renderer, table, document) = Create(3, false);
        var top = table[1, 1].Borders.Top;
        // A stronger lower edge wins conflict resolution instead of inheriting
        // the merged cell's border; color and dash differences then reach paint.
        top.Width = Unit.FromPoint(6);
        if (difference == "color") top.Color = Colors.Blue;
        if (difference == "alpha") top.Color = Color.FromArgb(128, 255, 0, 0);
        if (difference == "width") top.Width = Unit.FromPoint(6);
        if (difference == "dash") top.Style = BorderStyle.DashLargeGap;
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        Lines(pdf.Pages[0]).Should().HaveCount(difference == "dash" ? 4 : 2,
            "the differing shorter stroke must still paint (large dashes use separate strokes)");
    }

    [Theory]
    [InlineData("move")]
    [InlineData("extend")]
    [InlineData("shrink")]
    public void OwnershipUsesActualPaintGeometry(string change)
    {
        var (renderer, table, document) = Create(3, false);
        renderer.DocumentRenderer.TableCellPlacement = (cell, geometry) =>
        {
            if (cell.Row!.Index != 1 || cell.Column!.Index != 1) return null;
            var rect = geometry.DefaultPaintRect;
            rect = change switch
            {
                "move" => new XRect(rect.X, rect.Y + 10, rect.Width, rect.Height),
                "extend" => new XRect(rect.X, rect.Y, rect.Width + 10, rect.Height),
                _ => new XRect(rect.X + 5, rect.Y, rect.Width - 10, rect.Height)
            };
            return new TableCellPlacement(PaintRect: rect, ContentRect: geometry.GridRect);
        };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        Lines(pdf.Pages[0]).Should().HaveCount(change == "shrink" ? 1 : 2);
    }

    [Fact]
    public void PageBoundaryKeepsFollowingBodyEdges()
    {
        var (renderer, table, document) = Create(3, false);
        table.Rows[0].Height = Unit.FromPoint(100);
        table.Rows[1].Height = Unit.FromPoint(100);
        var setup = document.LastSection.PageSetup;
        setup.PageHeight = Unit.FromPoint(180);
        setup.TopMargin = setup.BottomMargin = Unit.FromPoint(20);
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        pdf.PageCount.Should().Be(2);
        Lines(pdf.Pages[0]).Should().ContainSingle();
        Lines(pdf.Pages[1]).Should().HaveCount(2, "a previous page cannot own the body edges");
    }

    [Fact]
    public void RepeatedHeaderCanOwnFollowingBodyEdges()
    {
        var (renderer, table, document) = Create(3, false);
        table.Rows[0].HeadingFormat = true;
        table.Rows[0].Height = Unit.FromPoint(40);
        table.Rows[1].Height = Unit.FromPoint(60);
        table.AddRow().Height = Unit.FromPoint(60);
        for (int column = 0; column < 2; column++)
        {
            var border = table[2, column].Borders.Top;
            border.Visible = true;
            border.Color = Colors.Red;
            border.Style = BorderStyle.Single;
            border.Width = Unit.FromPoint(3);
        }
        var setup = document.LastSection.PageSetup;
        setup.PageHeight = Unit.FromPoint(180);
        setup.TopMargin = setup.BottomMargin = Unit.FromPoint(20);
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        pdf.PageCount.Should().Be(2);
        Lines(pdf.Pages[1]).Should().ContainSingle("the repeated merged header owns the shared border");
    }

    [Fact]
    public void ClippedBottomDoesNotClaimCompleteCoverage()
    {
        var (renderer, table, _) = Create(3, false, true);
        for (int row = 0; row < 2; row++)
        {
            var borders = table[row, 2].Borders;
            borders.Visible = true;
            borders.Style = BorderStyle.Double;
            borders.Width = Unit.FromPoint(3);
            borders.Color = Colors.Blue;
        }
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        Lines(pdf.Pages[0]).Where(line => Math.Abs(line.X2 - line.X1) > 70).Should().HaveCount(3,
            "borders painted under exclusion clips cannot guarantee complete coverage");
    }

    [Theory]
    [InlineData(.75, false)]
    [InlineData(.75, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public void MergedTopOwnsMatchingShorterBottoms(double width, bool transparent)
    {
        var (renderer, _, _) = Create(width, transparent, mergeBelow: true);
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var line = Lines(pdf.Pages[0]).Should().ContainSingle().Subject;
        Math.Abs(line.X2 - line.X1).Should().BeApproximately(160, .01);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void UpcomingTopKeepsIndependentOrCustomPlacementEdges(bool spaced, bool callback)
    {
        var (renderer, table, _) = Create(3, false, mergeBelow: true);
        if (spaced) table.CellSpacing = Unit.FromPoint(4);
        if (callback) renderer.DocumentRenderer.TableCellPlacement = (_, _) => null;
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        Lines(pdf.Pages[0]).Should().HaveCount(3);
    }

    [Fact]
    public void HiddenUpcomingTopDoesNotOwnVisibleBottoms()
    {
        var (renderer, table, _) = Create(3, false, mergeBelow: true);
        table[1, 0].Borders.Top.Visible = false;
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        Lines(pdf.Pages[0]).Should().HaveCount(2);
    }

    [Fact]
    public void UpcomingTopDoesNotSuppressAMismatchedStroke()
    {
        var (renderer, table, _) = Create(3, false, mergeBelow: true);
        table[0, 0].Borders.Bottom.Width = Unit.FromPoint(6);
        table[0, 0].Borders.Bottom.Color = Colors.Blue;
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        Lines(pdf.Pages[0]).Should().HaveCount(2);
    }

    [Fact]
    public void TopOnAnotherPageCannotOwnPrecedingBottoms()
    {
        var (renderer, table, document) = Create(3, false, mergeBelow: true);
        table.Rows[0].Height = table.Rows[1].Height = Unit.FromPoint(100);
        document.LastSection.PageSetup.PageHeight = Unit.FromPoint(180);
        document.LastSection.PageSetup.TopMargin = document.LastSection.PageSetup.BottomMargin = Unit.FromPoint(20);
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        pdf.PageCount.Should().Be(2);
        Lines(pdf.Pages[0]).Should().HaveCount(2);
        Lines(pdf.Pages[1]).Should().ContainSingle();
    }

    [Fact]
    public void SeparateHeaderPassKeepsItsExistingOwnership()
    {
        var (renderer, table, document) = Create(3, false, mergeBelow: true);
        table.Rows[0].HeadingFormat = true;
        table.Rows[0].Height = Unit.FromPoint(40);
        table.Rows[1].Height = Unit.FromPoint(60);
        table.AddRow().Height = Unit.FromPoint(60);
        table[2, 0].MergeRight = 1;
        var top = table[2, 0].Borders.Top;
        top.Visible = true;
        top.Style = BorderStyle.Single;
        top.Width = Unit.FromPoint(3);
        top.Color = Colors.Red;
        document.LastSection.PageSetup.PageHeight = Unit.FromPoint(180);
        document.LastSection.PageSetup.TopMargin = document.LastSection.PageSetup.BottomMargin = Unit.FromPoint(20);
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        pdf.PageCount.Should().Be(2);
        Lines(pdf.Pages[0]).Should().HaveCount(4);
        Lines(pdf.Pages[1]).Should().HaveCount(3);
    }

    [Fact]
    public void ExclusionClipsDisableUpcomingTopOwnership()
    {
        var (renderer, table, _) = Create(3, false, true, mergeBelow: true);
        for (int row = 0; row < 2; row++)
        {
            var borders = table[row, 2].Borders;
            borders.Visible = true;
            borders.Style = BorderStyle.Double;
            borders.Width = Unit.FromPoint(3);
            borders.Color = Colors.Blue;
        }
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        Lines(pdf.Pages[0]).Where(line => Math.Abs(line.X2 - line.X1) > 70).Should().HaveCount(3);
    }

    static (PdfDocumentRenderer Renderer, Table Table, Document Document) Create(double width, bool transparent, bool extraColumn = false, bool mergeBelow = false)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        var table = document.AddSection().AddTable();
        table.AddColumn(Unit.FromPoint(80));
        table.AddColumn(Unit.FromPoint(80));
        if (extraColumn) table.AddColumn(Unit.FromPoint(20));
        table.AddRow();
        table.AddRow();
        table.Rows.Height = Unit.FromPoint(25);
        table.Borders.Visible = false;
        table[mergeBelow ? 1 : 0, 0].MergeRight = 1;
        var shared = mergeBelow
            ? new[] { table[0, 0].Borders.Bottom, table[0, 1].Borders.Bottom, table[1, 0].Borders.Top }
            : new[] { table[0, 0].Borders.Bottom, table[1, 0].Borders.Top, table[1, 1].Borders.Top };
        foreach (var border in shared)
        {
            border.Visible = true;
            border.Style = BorderStyle.Single;
            border.Width = Unit.FromPoint(width);
            border.Color = transparent ? Color.FromArgb(128, 255, 0, 0) : Colors.Red;
        }
        return (new PdfDocumentRenderer { Document = document }, table, document);
    }

    static List<(double X1, double Y1, double X2, double Y2)> Lines(PdfSharp.Pdf.PdfPage page)
    {
        var lines = new List<(double, double, double, double)>();
        double x = 0, y = 0, endX = 0, endY = 0;
        bool hasLine = false;
        foreach (var op in ContentReader.ReadContent(page).OfType<COperator>())
        {
            if (op.OpCode.Name == "m") { x = N(op.Operands[0]); y = N(op.Operands[1]); hasLine = false; }
            if (op.OpCode.Name == "l") { endX = N(op.Operands[0]); endY = N(op.Operands[1]); hasLine = true; }
            if (op.OpCode.Name == "S" && hasLine) { lines.Add((x, y, endX, endY)); hasLine = false; }
        }
        return lines;
    }

    static double N(CObject value) => double.Parse(value.ToString()!, CultureInfo.InvariantCulture);
}
