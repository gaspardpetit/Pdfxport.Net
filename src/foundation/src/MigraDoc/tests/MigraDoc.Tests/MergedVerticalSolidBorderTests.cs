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
public class MergedVerticalSolidBorderTests : IDisposable
{
    public MergedVerticalSolidBorderTests()
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
    public void MergedRightOwnsMatchingShorterLefts(double width, bool transparent)
    {
        var (renderer, table, document) = Create(width, transparent);
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var lines = Lines(pdf.Pages[0]);
        lines.Should().ContainSingle("the merged right side already paints both shorter left borders");
        Math.Abs(lines[0].Y2 - lines[0].Y1).Should().BeApproximately(50, .01);
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
        var left = table[1, 1].Borders.Left;
        // A stronger adjacent edge wins conflict resolution instead of inheriting
        // the merged cell's border; color and dash differences then reach paint.
        left.Width = Unit.FromPoint(6);
        if (difference == "color") left.Color = Colors.Blue;
        if (difference == "alpha") left.Color = Color.FromArgb(128, 255, 0, 0);
        if (difference == "width") left.Width = Unit.FromPoint(6);
        if (difference == "dash") left.Style = BorderStyle.DashLargeGap;
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
                "move" => new XRect(rect.X + 10, rect.Y, rect.Width, rect.Height),
                "extend" => new XRect(rect.X, rect.Y, rect.Width, rect.Height + 10),
                _ => new XRect(rect.X, rect.Y + 5, rect.Width, rect.Height - 10)
            };
            return new TableCellPlacement(PaintRect: rect, ContentRect: geometry.GridRect);
        };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        Lines(pdf.Pages[0]).Should().HaveCount(change == "shrink" ? 1 : 2);
    }

    [Fact]
    public void PreviousFragmentCannotOwnTheNextPagesEdges()
    {
        var (renderer, table, document) = Create(3, false);
        table.Rows[0].Height = table.Rows[1].Height = Unit.FromPoint(60);
        for (int row = 2; row < 4; row++)
        {
            table.AddRow().Height = Unit.FromPoint(60);
            foreach (var border in new[] { table[row, 0].Borders.Right, table[row, 1].Borders.Left })
            {
                border.Visible = true;
                border.Style = BorderStyle.Single;
                border.Width = Unit.FromPoint(3);
                border.Color = Colors.Red;
            }
        }
        var setup = document.LastSection.PageSetup;
        setup.PageHeight = Unit.FromPoint(180);
        setup.TopMargin = setup.BottomMargin = Unit.FromPoint(20);
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        pdf.PageCount.Should().Be(2);
        Lines(pdf.Pages[0]).Should().ContainSingle();
        Lines(pdf.Pages[1]).Should().HaveCount(2,
            "the first page's long stroke cannot suppress matching shorter edges on another page");
    }

    [Fact]
    public void ClippedRightDoesNotClaimCompleteCoverage()
    {
        var (reference, _, _) = Create(3, false);
        reference.RenderDocument();
        using var referencePdf = reference.PdfDocument;
        var sharedX = Lines(referencePdf.Pages[0]).Single().X1;
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
        Lines(pdf.Pages[0]).Where(line => Math.Abs(line.X1 - line.X2) < .001 &&
            Math.Abs(line.X1 - sharedX) < .001).Should().HaveCount(3,
            "an exclusion-clipped stroke cannot guarantee complete coverage");
    }

    [Theory]
    [InlineData(.75, false)]
    [InlineData(.75, true)]
    [InlineData(3, false)]
    [InlineData(3, true)]
    public void MergedLeftOwnsMatchingShorterRights(double width, bool transparent)
    {
        var (renderer, _, _) = Create(width, transparent, mergeOnRight: true);
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var line = Lines(pdf.Pages[0]).Should().ContainSingle().Subject;
        Math.Abs(line.Y2 - line.Y1).Should().BeApproximately(50, .01);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void UpcomingLeftKeepsIndependentOrCustomPlacementEdges(bool spaced, bool callback)
    {
        var (renderer, table, _) = Create(3, false, mergeOnRight: true);
        if (spaced) table.CellSpacing = Unit.FromPoint(4);
        if (callback) renderer.DocumentRenderer.TableCellPlacement = (_, _) => null;
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        Lines(pdf.Pages[0]).Should().HaveCount(3);
    }

    [Fact]
    public void HiddenUpcomingLeftDoesNotOwnVisibleRights()
    {
        var (renderer, table, _) = Create(3, false, mergeOnRight: true);
        table[0, 1].Borders.Left.Visible = false;
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        Lines(pdf.Pages[0]).Should().HaveCount(2);
    }

    [Fact]
    public void UpcomingLeftDoesNotSuppressAMismatchedStroke()
    {
        var (renderer, table, _) = Create(3, false, mergeOnRight: true);
        table[0, 0].Borders.Right.Width = Unit.FromPoint(6);
        table[0, 0].Borders.Right.Color = Colors.Blue;
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        Lines(pdf.Pages[0]).Should().HaveCount(2);
    }

    [Fact]
    public void ExclusionClipsDisableUpcomingLeftOwnership()
    {
        var (reference, _, _) = Create(3, false, mergeOnRight: true);
        reference.RenderDocument();
        using var referencePdf = reference.PdfDocument;
        var sharedX = Lines(referencePdf.Pages[0]).First().X1;
        var (renderer, table, _) = Create(3, false, true, mergeOnRight: true);
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
        Lines(pdf.Pages[0]).Where(line => Math.Abs(line.X1 - line.X2) < .001 &&
            Math.Abs(line.X1 - sharedX) < .001).Should().HaveCount(3);
    }

    [Fact]
    public void UpcomingLeftOwnershipIsLimitedToTheCurrentFragment()
    {
        var (renderer, table, document) = Create(3, false, mergeOnRight: true);
        table.Rows[0].Height = table.Rows[1].Height = Unit.FromPoint(60);
        for (int row = 2; row < 4; row++)
        {
            table.AddRow().Height = Unit.FromPoint(60);
            foreach (var border in new[] { table[row, 0].Borders.Right, table[row, 1].Borders.Left })
            {
                border.Visible = true;
                border.Style = BorderStyle.Single;
                border.Width = Unit.FromPoint(3);
                border.Color = Colors.Red;
            }
        }
        document.LastSection.PageSetup.PageHeight = Unit.FromPoint(180);
        document.LastSection.PageSetup.TopMargin = document.LastSection.PageSetup.BottomMargin = Unit.FromPoint(20);
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        pdf.PageCount.Should().Be(2);
        Lines(pdf.Pages[0]).Should().ContainSingle();
        Lines(pdf.Pages[1]).Should().HaveCount(2);
    }

    [Fact]
    public void EqualRowSpansWithDifferentBottomInsetsKeepTheirExistingStrokes()
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        var table = document.AddSection().AddTable();
        table.AddColumn(Unit.FromPoint(80));
        table.AddColumn(Unit.FromPoint(80));
        table.AddRow().Height = Unit.FromPoint(50);
        table.Borders.Visible = false;
        foreach (var border in new[] { table[0, 0].Borders.Right, table[0, 1].Borders.Left })
        {
            border.Visible = true;
            border.Width = Unit.FromPoint(3);
            border.Color = Colors.Red;
        }
        for (int column = 0; column < 2; column++)
        {
            table[0, column].Borders.Bottom.Visible = true;
            table[0, column].Borders.Bottom.Color = Colors.White;
            table[0, column].Borders.Bottom.Width = Unit.FromPoint(column == 0 ? 4 : 4.00001);
        }
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        Lines(pdf.Pages[0]).Count(line => Math.Abs(line.X2 - line.X1) < .001).Should().Be(2);
    }

    [Fact]
    public void SeparateHeaderPassKeepsExistingVerticalOwnership()
    {
        var (renderer, table, document) = Create(3, false, mergeOnRight: true);
        table.Rows[0].HeadingFormat = table.Rows[1].HeadingFormat = true;
        table.AddRow().Height = Unit.FromPoint(60);
        table.AddRow().Height = Unit.FromPoint(60);
        document.LastSection.PageSetup.PageHeight = Unit.FromPoint(180);
        document.LastSection.PageSetup.TopMargin = document.LastSection.PageSetup.BottomMargin = Unit.FromPoint(20);
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        pdf.PageCount.Should().Be(2);
        Lines(pdf.Pages[0]).Should().HaveCount(3);
        Lines(pdf.Pages[1]).Should().HaveCount(3);
    }

    static (PdfDocumentRenderer Renderer, Table Table, Document Document) Create(double width, bool transparent, bool extraColumn = false, bool mergeOnRight = false)
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
        table[0, mergeOnRight ? 1 : 0].MergeDown = 1;
        var shared = mergeOnRight
            ? new[] { table[0, 0].Borders.Right, table[1, 0].Borders.Right, table[0, 1].Borders.Left }
            : new[] { table[0, 0].Borders.Right, table[0, 1].Borders.Left, table[1, 1].Borders.Left };
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
