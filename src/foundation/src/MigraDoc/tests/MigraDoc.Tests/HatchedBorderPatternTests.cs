// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class HatchedBorderPatternTests : IDisposable
{
    public HatchedBorderPatternTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(false, .1)]
    [InlineData(true, .1)]
    [InlineData(false, 1.5)]
    [InlineData(true, 1.5)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public void HatchRepeatsFiveStrokesThenAnIsolatedStroke(bool vertical, double width)
    {
        var shorter = Origins(37);
        var longer = Origins(37.4);
        shorter.Length.Should().BeGreaterThan(12);
        var closeGap = Math.Abs(shorter[1] - shorter[0]);
        var gaps = new[] { 1, 1, 1, 1, 2, 2 };
        for (int i = 1; i < shorter.Length; i++)
            Math.Abs(shorter[i] - shorter[i - 1]).Should().BeApproximately(
                closeGap * gaps[(i - 1) % gaps.Length], .002);
        for (int i = 0; i < Math.Min(shorter.Length, longer.Length); i++)
            shorter[i].Should().BeApproximately(longer[i], .002,
                "truncating a border must preserve the phase of its hatch groups");

        double[] Origins(double length)
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var table = document.AddSection().AddTable();
            table.AddColumn(Unit.FromPoint(vertical ? 37 : length));
            var row = table.AddRow();
            row.HeightRule = RowHeightRule.Exactly;
            row.Height = Unit.FromPoint(vertical ? length : 37);
            var cell = row[0];
            cell.Borders.Top.Visible = cell.Borders.Bottom.Visible =
                cell.Borders.Left.Visible = cell.Borders.Right.Visible = false;
            var border = vertical ? cell.Borders.Left : cell.Borders.Bottom;
            border.Visible = true;
            border.Style = BorderStyle.DashDotStroked;
            border.Width = Unit.FromPoint(width);
            cell.AddParagraph("Cell");
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            using var pdf = renderer.PdfDocument;
            var content = PdfFileHelper.GetPageContentStream(pdf, 0);
            return Regex.Matches(content,
                    @"(?m)(?<x1>-?[\d.]+) (?<y1>-?[\d.]+) m\s+(?<x2>-?[\d.]+) (?<y2>-?[\d.]+) l\s+S")
                .Cast<Match>().Select(m => double.Parse(m.Groups[vertical ? "y2" : "x1"].Value,
                    CultureInfo.InvariantCulture)).ToArray();
        }
    }
}
