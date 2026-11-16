using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.IO;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.DocumentObjectModel.Visitors;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class CellSpacingTests : IDisposable
{
    public CellSpacingTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(3.0)]
    public void SpacingSurvivesCloneAndDdl(double? spacing)
    {
        var document = Create();
        var table = (Table)document.Sections[0].Elements[0];
        if (spacing.HasValue) table.CellSpacing = Unit.FromPoint(spacing.Value);
        table.Clone().Values.CellSpacing.Should().Be(table.Values.CellSpacing);
        var ddl = DdlWriter.WriteToString(document);
        var restored = (Table)DdlReader.DocumentFromString(ddl)!.Sections[0].Elements[0];
        restored.Values.CellSpacing.Should().Be(table.Values.CellSpacing);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidSpacingIsRejected(double spacing)
    {
        var table = new Table();
        Action set = () => table.CellSpacing = Unit.FromPoint(spacing);
        set.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-3, false)]
    [InlineData(double.NaN, false)]
    [InlineData(double.PositiveInfinity, false)]
    [InlineData(double.NegativeInfinity, false)]
    [InlineData(-3, true)]
    [InlineData(double.NaN, true)]
    [InlineData(double.PositiveInfinity, true)]
    [InlineData(double.NegativeInfinity, true)]
    public void StoredSpacingRejectsInvalidAssignmentsWithoutChangingValue(double spacing, bool throughSetValue)
    {
        var table = new Table { CellSpacing = Unit.FromPoint(4) };
        var exception = Record.Exception(() =>
        {
            if (throughSetValue) table.SetValue("CellSpacing", Unit.FromPoint(spacing));
            else table.Values.CellSpacing = Unit.FromPoint(spacing);
        });
        exception.Should().NotBeNull();
        exception!.GetBaseException().Should().BeOfType<ArgumentOutOfRangeException>();
        table.CellSpacing.Point.Should().Be(4);
    }

    [Fact]
    public void NegativeSpacingInDdlIsRejected()
    {
        var document = Create();
        ((Table)document.Sections[0].Elements[0]).CellSpacing = Unit.FromPoint(3);
        var ddl = DdlWriter.WriteToString(document);
        ddl.Should().Contain("CellSpacing = 3");
        ddl = ddl.Replace("CellSpacing = 3", "CellSpacing = \"-3pt\"");
        var errors = new DdlReaderErrors();
        using var input = new StringReader(ddl);
        var reader = new DdlReader(input, errors);
        // The parser reports assignment failures through its error collection;
        // its later recovery may also reject the remaining table syntax.
        Record.Exception(() => reader.ReadDocument());
        reader.Close();
        errors.Cast<DdlReaderError>().Should().Contain(error =>
            error.ErrorMessage.Contains(nameof(ArgumentOutOfRangeException)) &&
            error.ErrorMessage.Contains("CellSpacing"));
    }

    [Fact]
    public void StoredSpacingCanBeClearedAndSetThroughMetadata()
    {
        var table = new Table { CellSpacing = Unit.FromPoint(4) };
        table.Values.CellSpacing = null;
        table.Values.CellSpacing.Should().BeNull();
        table.CellSpacing.Should().Be(Unit.Zero);
        table.SetValue("CellSpacing", Unit.FromPoint(2));
        table.CellSpacing.Point.Should().Be(2);
        table.SetValue("CellSpacing", null);
        table.Values.CellSpacing.Should().BeNull();
        table.CellSpacing.Should().Be(Unit.Zero);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpacedCellsKeepTheirOwnOpposingBorders(bool vertical)
    {
        var document = Create();
        var table = (Table)document.Sections[0].Elements[0];
        table.CellSpacing = Unit.FromPoint(3);
        var first = table[0, 0];
        var next = vertical ? table[1, 0] : table[0, 1];
        var near = vertical ? first.Borders.Bottom : first.Borders.Right;
        var far = vertical ? next.Borders.Top : next.Borders.Left;
        near.Width = Unit.FromPoint(1);
        far.Width = Unit.FromPoint(7);
        var cells = new MergedCellList(table);
        var effective = cells.GetEffectiveBordersPdf(first, -1);
        (vertical ? effective.Bottom : effective.Right).Width.Point.Should().Be(1);
        table.CellSpacing = Unit.Zero;
        effective = new MergedCellList(table).GetEffectiveBordersPdf(first, -1);
        (vertical ? effective.Bottom : effective.Right).Width.Point.Should().Be(7);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(8)]
    public void SpacingReservesIndependentBandsInsideFixedColumnPitch(double spacing)
    {
        var document = Create();
        var table = (Table)document.Sections[0].Elements[0];
        table.CellSpacing = Unit.FromPoint(spacing);
        var renderer = Render(document);
        using var pdf = renderer.PdfDocument;
        var info = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!.Single();
        info.LayoutInfo.ContentArea.Height.Point.Should().BeApproximately(2 * (20 + 4 + 2 * spacing), .001);
        var format = (TableFormatInfo)info.FormatInfo;
        var first = format.FormattedCells![table[0, 0]].GetRenderedContentOrigin(1)!.Value;
        var next = format.FormattedCells[table[0, 1]].GetRenderedContentOrigin(1)!.Value;
        var below = format.FormattedCells[table[1, 0]].GetRenderedContentOrigin(1)!.Value;
        (next.X - first.X).Should().BeApproximately(100, .001);
        (below.Y - first.Y).Should().BeApproximately(24 + 2 * spacing, .001);
        (first.Y - info.LayoutInfo.ContentArea.Y.Point).Should().BeApproximately(spacing + 2, .001);
    }

    [Fact]
    public void ExplicitZeroPreservesDefaultOutput()
    {
        static List<(double, double, double, double)> Lines(double? spacing)
        {
            var document = Create();
            if (spacing.HasValue) ((Table)document.Sections[0].Elements[0]).CellSpacing = Unit.FromPoint(spacing.Value);
            var renderer = Render(document);
            using var pdf = renderer.PdfDocument;
            var result = new List<(double, double, double, double)>();
            var lines = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0).Line;
            while (lines.MoveAndGetNext(true, out var line))
                result.Add((line!.X1, line.Y1, line.X2, line.Y2));
            return result;
        }
        Lines(0).Should().Equal(Lines(null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpacingReducesTextWidthEvenWithCollapsedGridOption(bool grid)
    {
        using var measure = XGraphics.CreateMeasureContext(new XSize(300,300), XGraphicsUnit.Point, XPageDirection.Downwards);
        var width = measure.MeasureString("Alpha Beta", new XFont("Arial", 10)).Width;
        var document = Create();
        var table = (Table)document.Sections[0].Elements[0];
        table.CellSpacing = Unit.FromPoint(5);
        table.CellMarginsMeasuredFromCollapsedGridEdge = grid;
        table.Columns[0].Width = Unit.FromPoint(width + 4.5);
        var renderer = Render(document);
        using var pdf = renderer.PdfDocument;
        var format = (TableFormatInfo)renderer.DocumentRenderer.GetRenderInfoFromPage(1)!.Single().FormatInfo;
        format.FormattedCells![table[0, 0]].ContentHeight.Point.Should().BeApproximately(20, .001);
    }

    [Fact]
    public void PaintedSingleBordersLeaveTheDeclaredGap()
    {
        var document = Create();
        var table = (Table)document.Sections[0].Elements[0];
        table.CellSpacing = Unit.FromPoint(5);
        var renderer = Render(document);
        using var pdf = renderer.PdfDocument;
        var vertical = new List<double>();
        var horizontal = new List<double>();
        var lines = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Line;
        while (lines.MoveAndGetNext(true, out var line))
        {
            if (Math.Abs(line!.X1 - line.X2) < .001) vertical.Add(line.X1);
            if (Math.Abs(line.Y1 - line.Y2) < .001) horizontal.Add(line.Y1);
        }
        var xs = vertical.Distinct().OrderBy(x => x).ToArray();
        var ys = horizontal.Distinct().OrderBy(y => y).ToArray();
        xs.Length.Should().Be(4);
        ys.Length.Should().Be(4);
        (xs[2] - xs[1] - 2).Should().BeApproximately(10, .001);
        (ys[2] - ys[1] - 2).Should().BeApproximately(10, .001);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MergedCellsReserveOnlyTheirOuterSpacing(bool vertical)
    {
        var document = Create();
        var table = (Table)document.Sections[0].Elements[0];
        table.CellSpacing = Unit.FromPoint(5);
        if (vertical) table[0,0].MergeDown = 1;
        else table[0,0].MergeRight = 1;
        var renderer = Render(document);
        using var pdf = renderer.PdfDocument;
        var info = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!.Single();
        var format = (TableFormatInfo)info.FormatInfo;
        format.FormattedCells!.Count.Should().Be(3);
        format.FormattedCells[table[0,0]].ContentHeight.Point.Should().BeApproximately(10, .001);
        info.LayoutInfo.ContentArea.Height.Point.Should().BeApproximately(68, .001);
    }

    [Theory]
    [InlineData(BorderStyle.Single)]
    [InlineData(BorderStyle.Double)]
    [InlineData(BorderStyle.Triple)]
    [InlineData(BorderStyle.ThinThickSmallGap)]
    public void RepeatedHeadersRetainIndependentCellOrigins(BorderStyle style)
    {
        var document = Create();
        var section = document.Sections[0];
        section.PageSetup.PageHeight = Unit.FromPoint(160);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromPoint(20);
        var table = (Table)section.Elements[0];
        table.CellSpacing = Unit.FromPoint(3);
        table.Borders.Style = style;
        table.Rows[0].HeadingFormat = true;
        for (var i=0; i<5; i++)
        {
            var row = table.AddRow();
            row.HeightRule = RowHeightRule.Exactly;
            row.Height = Unit.FromPoint(20);
            row[0].AddParagraph("Body");
            row[1].AddParagraph("Body");
        }
        var renderer = Render(document);
        using var pdf = renderer.PdfDocument;
        pdf.PageCount.Should().BeGreaterThan(1);
        var origins = new List<double>();
        for (var page=1; page<=pdf.PageCount; page++)
        {
            var info = renderer.DocumentRenderer.GetRenderInfoFromPage(page)!.Single();
            var cells = ((TableFormatInfo)info.FormatInfo).FormattedCells!;
            var header = cells[table[0,0]].GetRenderedContentOrigin(page)!.Value;
            origins.Add(header.Y);
            info.LayoutInfo.ContentArea.Height.Point.Should().BeLessThanOrEqualTo(120.001);
        }
        origins.Should().OnlyContain(y => Math.Abs(y - origins[0]) < .001);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PdfAndRtfKeepSeparateEffectiveBorderCaches(bool vertical, bool pdfFirst)
    {
        var document = Create();
        var table = (Table)document.Sections[0].Elements[0];
        table.CellSpacing = Unit.FromPoint(3);
        var first = table[0,0];
        var neighbor = vertical ? table[1,0] : table[0,1];
        (vertical ? first.Borders.Bottom : first.Borders.Right).Width = Unit.FromPoint(1);
        (vertical ? neighbor.Borders.Top : neighbor.Borders.Left).Width = Unit.FromPoint(7);
        var cells = new MergedCellList(table);
        double Width(bool pdf)
        {
            var borders = pdf ? cells.GetEffectiveBordersPdf(first, -1) : cells.GetEffectiveBordersRtf(first);
            return (vertical ? borders.Bottom : borders.Right).Width.Point;
        }
        Width(pdfFirst).Should().Be(pdfFirst ? 1 : 7);
        Width(!pdfFirst).Should().Be(pdfFirst ? 7 : 1);
        Width(pdfFirst).Should().Be(pdfFirst ? 1 : 7);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PdfSpacingDoesNotChangeRtfBorderOutput(bool vertical)
    {
        string RenderRtf(double spacing)
        {
            var document = Create();
            var table = (Table)document.Sections[0].Elements[0];
            table.CellSpacing = Unit.FromPoint(spacing);
            var neighbor = vertical ? table[1,0] : table[0,1];
            (vertical ? table[0,0].Borders.Bottom : table[0,0].Borders.Right).Width = Unit.FromPoint(1);
            (vertical ? neighbor.Borders.Top : neighbor.Borders.Left).Width = Unit.FromPoint(7);
            return new MigraDoc.RtfRendering.RtfDocumentRenderer().RenderToString(document, ".");
        }
        var shared = RenderRtf(0);
        shared.Should().Contain(@"\brdrw140");
        RenderRtf(3).Should().Be(shared);
    }

    static Document Create()
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 10;
        var table = document.AddSection().AddTable();
        table.AddColumn(Unit.FromPoint(100));
        table.AddColumn(Unit.FromPoint(100));
        table.LeftPadding = table.RightPadding = Unit.Zero;
        table.TopPadding = table.BottomPadding = Unit.Zero;
        table.Borders.Width = Unit.FromPoint(2);
        for (var i = 0; i < 2; i++)
        {
            var row = table.AddRow();
            row.Height = Unit.FromPoint(20);
            row.HeightRule = RowHeightRule.Exactly;
            for (var column = 0; column < table.Columns.Count; column++)
            {
                var cell = row[column];
                var paragraph = cell.AddParagraph("Alpha Beta");
                paragraph.Format.LineSpacingRule = LineSpacingRule.Exactly;
                paragraph.Format.LineSpacing = Unit.FromPoint(10);
            }
        }
        return document;
    }
    static PdfDocumentRenderer Render(Document document)
    {
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        return renderer;
    }
}
