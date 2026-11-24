// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using System.Globalization;
using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Quality;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class HiddenMergedBorderTests : IDisposable
{
    public HiddenMergedBorderTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }

    public void Dispose() => PdfSharpCore.ResetAll();

    public static IEnumerable<object[]> HiddenCases()
    {
        foreach (bool vertical in new[] { false, true })
        foreach (bool rounded in new[] { false, true })
        foreach (var style in new[] { BorderStyle.Single, BorderStyle.Double })
        foreach (double width in new[] { .75, 3.0 })
            yield return new object[] { vertical, rounded, style, width };
    }

    [Theory]
    [MemberData(nameof(HiddenCases))]
    public void HiddenMergedSideDoesNotExpandTheNeighborsVisibleEdge(
        bool vertical, bool rounded, BorderStyle style, double width)
    {
        var (document, table) = Create(vertical, style, width);
        if (vertical) table[0, 0].Borders.Right.Visible = false;
        else table[0, 0].Borders.Bottom.Visible = false;
        if (rounded) table[0, 0].RoundedCorner = RoundedCorner.TopLeft;
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var lines = Lines(pdf.Pages[0]);
        lines.Should().HaveCount(style == BorderStyle.Double ? 2 : 1);
        foreach (var line in lines)
            Math.Abs(vertical ? line.Y2 - line.Y1 : line.X2 - line.X1)
                .Should().BeApproximately(vertical ? 25 : 80, .01,
                    "only the neighboring cell requested a visible border");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void VisibleOrUnspecifiedMergedSideKeepsItsFullLength(bool vertical, bool explicitVisible)
    {
        var (document, table) = Create(vertical, BorderStyle.Single, 3);
        if (explicitVisible)
        {
            var border = vertical ? table[0, 0].Borders.Right : table[0, 0].Borders.Bottom;
            border.Visible = true;
            border.Style = BorderStyle.Single;
            border.Width = Unit.FromPoint(3);
            border.Color = Colors.Red;
        }
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var line = Lines(pdf.Pages[0]).Should().ContainSingle().Subject;
        Math.Abs(vertical ? line.Y2 - line.Y1 : line.X2 - line.X1)
            .Should().BeApproximately(vertical ? 50 : 160, .01);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void TerminalCoveredCellControlsItsOuterSide(bool vertical, bool rounded, bool terminalVisible)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        var table = document.AddSection().AddTable();
        for (int i = 0; i < 3; i++) table.AddColumn(Unit.FromPoint(80));
        for (int i = 0; i < 3; i++) table.AddRow();
        table.Rows.Height = Unit.FromPoint(25);
        table.Borders.Visible = false;
        table[0, 0].MergeDown = table[0, 0].MergeRight = 1;
        if (rounded) table[0, 0].RoundedCorner = RoundedCorner.TopLeft;
        var leading = vertical ? table[0, 0].Borders.Right : table[0, 0].Borders.Bottom;
        var terminal = vertical ? table[0, 1].Borders.Right : table[1, 0].Borders.Bottom;
        var neighbor = vertical ? table[0, 2].Borders.Left : table[2, 0].Borders.Top;
        foreach (var border in new[] { leading, terminal, neighbor })
        {
            border.Visible = true;
            border.Style = BorderStyle.Single;
            border.Width = Unit.FromPoint(3);
            border.Color = Colors.Red;
        }
        leading.Visible = !terminalVisible;
        terminal.Visible = terminalVisible;
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var lengths = Lines(pdf.Pages[0]).Select(line => Math.Abs(
            vertical ? line.Y2 - line.Y1 : line.X2 - line.X1)).ToArray();
        lengths.Should().NotBeEmpty();
        lengths.Max().Should().BeApproximately(
            vertical ? (terminalVisible ? 50 : 25) : (terminalVisible ? 160 : 80), .01,
            "the terminal covered cell replaces the leading cell's outer-side definition");
    }

    static (Document Document, Table Table) Create(bool vertical, BorderStyle style, double width)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        var table = document.AddSection().AddTable();
        table.AddColumn(Unit.FromPoint(80));
        table.AddColumn(Unit.FromPoint(80));
        table.AddRow();
        table.AddRow();
        table.Rows.Height = Unit.FromPoint(25);
        table.Borders.Visible = false;
        if (vertical) table[0, 0].MergeDown = 1;
        else table[0, 0].MergeRight = 1;
        var visible = vertical ? table[0, 1].Borders.Left : table[1, 0].Borders.Top;
        visible.Visible = true;
        visible.Style = style;
        visible.Width = Unit.FromPoint(width);
        visible.Color = Colors.Red;
        return (document, table);
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
