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
public class MergedRowHeightTests : IDisposable
{
    public MergedRowHeightTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    public static IEnumerable<object[]> ExplicitHeightCases()
    {
        foreach (bool coveredOnly in new[] { false, true })
        foreach (var first in new[] { RowHeightRule.AtLeast, RowHeightRule.Exactly })
        foreach (var second in new[] { RowHeightRule.AtLeast, RowHeightRule.Exactly })
            yield return new object[] { coveredOnly, first, second };
    }

    [Theory]
    [MemberData(nameof(ExplicitHeightCases))]
    public void EverySpannedRowsExplicitHeightIsRespected(bool coveredOnly, RowHeightRule first, RowHeightRule second)
    {
        var (document, table) = Create(2);
        table.Rows[0].Height = Unit.FromPoint(40);
        table.Rows[1].Height = Unit.FromPoint(60);
        table.Rows[0].HeightRule = first;
        table.Rows[1].HeightRule = second;
        table[0, 0].MergeDown = 1;
        if (coveredOnly) table[0, 1].MergeDown = 1;
        Measure(document, table).Height.Should().BeApproximately(100, .01);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThreeRowsKeepTheirMinimaWithUniformOrStaggeredMerges(bool staggered)
    {
        var (document, table) = Create(3);
        for (int row = 0; row < 3; row++) table.Rows[row].Height = Unit.FromPoint(20 + 10 * row);
        table[0, 0].MergeDown = 2;
        table[0, 1].MergeDown = staggered ? 1 : 2;
        Measure(document, table).Height.Should().BeApproximately(90, .01);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AutoRowsIgnoreTheirNominalHeight(bool firstAuto, bool secondAuto)
    {
        var (document, table) = Create(2);
        table.Rows[0].Height = Unit.FromPoint(40);
        table.Rows[1].Height = Unit.FromPoint(60);
        table.Rows[0].HeightRule = firstAuto ? RowHeightRule.Auto : RowHeightRule.AtLeast;
        table.Rows[1].HeightRule = secondAuto ? RowHeightRule.Auto : RowHeightRule.AtLeast;
        table[0, 0].MergeDown = table[0, 1].MergeDown = 1;
        var (referenceDocument, referenceTable) = Create(2);
        referenceTable.Rows.HeightRule = RowHeightRule.Auto;
        referenceTable[0, 0].MergeDown = referenceTable[0, 1].MergeDown = 1;
        var contentHeight = Measure(referenceDocument, referenceTable).Height;
        var minimum = (firstAuto ? 0 : 40) + (secondAuto ? 0 : 60);
        Measure(document, table).Height.Should().BeApproximately(Math.Max(contentHeight, minimum), .01);
    }

    [Fact]
    public void AtLeastRowsStillGrowForMergedContent()
    {
        var (document, table) = Create(2);
        table.Rows[0].Height = Unit.FromPoint(20);
        table.Rows[1].Height = Unit.FromPoint(30);
        table[0, 0].MergeDown = table[0, 1].MergeDown = 1;
        var paragraph = table[0, 0].AddParagraph("Line");
        paragraph.Format.LineSpacingRule = LineSpacingRule.Exactly;
        paragraph.Format.LineSpacing = Unit.FromPoint(10);
        for (int line = 1; line < 8; line++) { paragraph.AddLineBreak(); paragraph.AddText("Line"); }
        Measure(document, table).Height.Should().BeApproximately(80, .01);
    }

    [Fact]
    public void PaginationUsesTheFullMergedRowMinimum()
    {
        var (document, table) = Create(2, precedingParagraph: true);
        table.Rows[0].Height = Unit.FromPoint(40);
        table.Rows[1].Height = Unit.FromPoint(60);
        table[0, 0].MergeDown = table[0, 1].MergeDown = 1;
        document.LastSection.PageSetup.PageHeight = Unit.FromPoint(160);
        document.LastSection.PageSetup.TopMargin = document.LastSection.PageSetup.BottomMargin = Unit.FromPoint(20);
        var result = Measure(document, table);
        result.Height.Should().BeApproximately(100, .01);
        result.Pages.Should().Be(2);
    }

    static (Document Document, Table Table) Create(int rows, bool precedingParagraph = false)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 8;
        var section = document.AddSection();
        if (precedingParagraph)
        {
            var paragraph = section.AddParagraph("Before");
            paragraph.Format.LineSpacingRule = LineSpacingRule.Exactly;
            paragraph.Format.LineSpacing = Unit.FromPoint(40);
        }
        var table = section.AddTable();
        table.AddColumn(Unit.FromPoint(80));
        table.AddColumn(Unit.FromPoint(80));
        for (int row = 0; row < rows; row++) table.AddRow();
        table.Rows.HeightRule = RowHeightRule.AtLeast;
        table.TopPadding = table.BottomPadding = Unit.Zero;
        table.Borders.Visible = false;
        return (document, table);
    }

    static (double Height, int Pages) Measure(Document document, Table table)
    {
        double? height = null;
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.TableCellPlacement = (cell, geometry) =>
        {
            if (ReferenceEquals(cell, table[0, 0])) height = geometry.DefaultPaintRect.Height;
            return null;
        };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        height.Should().NotBeNull();
        return (height!.Value, pdf.PageCount);
    }
}
