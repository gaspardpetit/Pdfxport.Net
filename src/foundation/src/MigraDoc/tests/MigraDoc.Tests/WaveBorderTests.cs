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
public class WaveBorderTests : IDisposable
{
    public WaveBorderTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(BorderStyle.Wave, false, .1)]
    [InlineData(BorderStyle.Wave, true, .1)]
    [InlineData(BorderStyle.Wave, false, 3)]
    [InlineData(BorderStyle.Wave, true, 3)]
    [InlineData(BorderStyle.DoubleWave, false, .1)]
    [InlineData(BorderStyle.DoubleWave, true, .1)]
    [InlineData(BorderStyle.DoubleWave, false, 3)]
    [InlineData(BorderStyle.DoubleWave, true, 3)]
    public void WaveRailsAreContinuousAndKeepTheirPitchAtTheClippedEnd(BorderStyle style, bool vertical, double width)
    {
        var shorter = Render(37);
        var longer = Render(37.4);
        int rails = style == BorderStyle.DoubleWave ? 2 : 1;
        (shorter.Length % rails).Should().Be(0);
        (longer.Length % rails).Should().Be(0);
        int shortCount = shorter.Length / rails, longCount = longer.Length / rails;
        shortCount.Should().BeGreaterThan(4);
        for (int rail = 0; rail < rails; rail++)
        {
            var shortRail = shorter.Skip(rail * shortCount).Take(shortCount).ToArray();
            var longRail = longer.Skip(rail * longCount).Take(longCount).ToArray();
            var step = AlongDistance(shortRail[0]);
            for (int i = 0; i < shortRail.Length; i++)
            {
                // Length changes only the clip, never the final wave's phase.
                AlongDistance(shortRail[i]).Should().BeApproximately(step, .002);
                if (i > 0)
                {
                    shortRail[i].X1.Should().BeApproximately(shortRail[i - 1].X2, .002);
                    shortRail[i].Y1.Should().BeApproximately(shortRail[i - 1].Y2, .002);
                }
            }
            for (int i = 0; i < Math.Min(shortCount, longCount); i++)
            {
                shortRail[i].X1.Should().BeApproximately(longRail[i].X1, .002);
                shortRail[i].Y1.Should().BeApproximately(longRail[i].Y1, .002);
                shortRail[i].X2.Should().BeApproximately(longRail[i].X2, .002);
                shortRail[i].Y2.Should().BeApproximately(longRail[i].Y2, .002);
            }
        }

        double AlongDistance(Segment line) => Math.Abs(vertical ? line.Y2 - line.Y1 : line.X2 - line.X1);
        Segment[] Render(double length)
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
            border.Style = style;
            border.Width = Unit.FromPoint(width);
            cell.AddParagraph("Cell");
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            using var pdf = renderer.PdfDocument;
            var content = PdfFileHelper.GetPageContentStream(pdf, 0);
            return Regex.Matches(content,
                    @"(?m)(?<x1>-?[\d.]+) (?<y1>-?[\d.]+) m\s+(?<x2>-?[\d.]+) (?<y2>-?[\d.]+) l\s+S")
                .Cast<Match>().Select(m => new Segment(N(m, "x1"), N(m, "y1"), N(m, "x2"), N(m, "y2"))).ToArray();
        }
    }

    static double N(Match match, string group) => double.Parse(match.Groups[group].Value, CultureInfo.InvariantCulture);
    record Segment(double X1, double Y1, double X2, double Y2);
}
