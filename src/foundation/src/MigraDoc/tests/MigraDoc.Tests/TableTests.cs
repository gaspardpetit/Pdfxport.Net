// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using System.Text;
using System.Globalization;
using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Drawing;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.Snippets.Font;
using PdfSharp.TestHelper;
using System.Text.RegularExpressions;
#if CORE
#endif
using Xunit;

namespace MigraDoc.Tests
{
    [Collection("PDFsharp")]
    public class TableTests : IDisposable
    {
        public TableTests()
        {
            PdfSharpCore.ResetAll();
#if CORE
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
        }

        public void Dispose()
        {
            PdfSharpCore.ResetAll();
        }

        [Theory]
        [InlineData(false, 141.73)]
        [InlineData(true, 141.73)]
        [InlineData(false, 143.12345649)]
        [InlineData(true, 143.12345651)]
        public void SharedBorderIsPaintedOnceAfterFollowingCellShading(bool vertical, double width)
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var table = document.AddSection().AddTable();
            table.AddColumn(Unit.FromPoint(width));
            if (vertical)
                table.AddColumn(Unit.FromPoint(width));
            for (var rowIndex = 0; rowIndex < (vertical ? 1 : 2); rowIndex++)
            {
                var row = table.AddRow();
                for (var columnIndex = 0; columnIndex < (vertical ? 2 : 1); columnIndex++)
                {
                    var cell = row[columnIndex];
                    cell.AddParagraph("Cell");
                    cell.Shading.Color = Colors.White;
                    cell.Borders.Color = Colors.Red;
                    cell.Borders.Width = 1;
                }
            }
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var content = PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0);
            var strokes = BorderStrokes(content)
                .Where(match => vertical
                    ? match.Groups["x1"].Value == match.Groups["x2"].Value
                    : match.Groups["y1"].Value == match.Groups["y2"].Value).ToArray();
            strokes.Should().HaveCount(3);
            strokes[1].Index.Should().BeGreaterThan(content.LastIndexOf("1 1 1 rg", StringComparison.Ordinal));
        }

        [Fact]
        public void SharedRowBorderWithDifferentEndpointsKeepsBothStrokes()
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var table = document.AddSection().AddTable();
            table.AddColumn("5cm");
            var first = table.AddRow()[0];
            var second = table.AddRow()[0];
            first.AddParagraph("First");
            second.AddParagraph("Second");
            first.Borders.Bottom.Color = Colors.Red;
            first.Borders.Bottom.Width = 1;
            second.Borders.Top.Color = Colors.Red;
            second.Borders.Top.Width = 1;
            first.Borders.Left.Color = Colors.White;
            first.Borders.Left.Width = 4;
            second.Borders.Left.Color = Colors.White;
            second.Borders.Left.Width = 16;
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var content = PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0);
            var strokes = BorderStrokes(content)
                .Where(match => match.Groups["y1"].Value == match.Groups["y2"].Value).ToArray();
            strokes.Should().HaveCount(2);
            strokes.Select(match => match.Groups["x2"].Value).Should().OnlyHaveUniqueItems();
        }

        [Fact]
        public void SharedRowBorderWithSubPrecisionEndpointDifferenceKeepsBothStrokes()
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var table = document.AddSection().AddTable();
            table.AddColumn("5cm");
            var first = table.AddRow()[0];
            var second = table.AddRow()[0];
            first.AddParagraph("First");
            second.AddParagraph("Second");
            first.Borders.Bottom.Color = Colors.Red;
            first.Borders.Bottom.Width = 1;
            second.Borders.Top.Color = Colors.Red;
            second.Borders.Top.Width = 1;
            first.Borders.Left.Color = Colors.White;
            first.Borders.Left.Width = Unit.FromPoint(4);
            second.Borders.Left.Color = Colors.White;
            second.Borders.Left.Width = Unit.FromPoint(4.00001);

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var strokes = BorderStrokes(PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0))
                .Where(match => match.Groups["y1"].Value == match.Groups["y2"].Value).ToArray();
            strokes.Should().HaveCount(2);
            strokes[0].Groups["x2"].Value.Should().Be(strokes[1].Groups["x2"].Value);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SharedMergedCellBordersArePaintedOnce(bool verticalMerge)
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var table = document.AddSection().AddTable();
            for (var columnIndex = 0; columnIndex < (verticalMerge ? 2 : 3); columnIndex++)
                table.AddColumn("5cm");
            if (verticalMerge)
            {
                var firstRow = table.AddRow();
                var secondRow = table.AddRow();
                firstRow[0].MergeDown = 1;
                firstRow[0].AddParagraph("Merged");
                firstRow[1].AddParagraph("Upper");
                secondRow[1].AddParagraph("Lower");
                foreach (var cell in new[] { firstRow[0], firstRow[1], secondRow[1] })
                {
                    cell.Borders.Color = Colors.Red;
                    cell.Borders.Width = 1;
                }
            }
            else
            {
                for (var rowIndex = 0; rowIndex < 2; rowIndex++)
                {
                    var row = table.AddRow();
                    row[0].MergeRight = 1;
                    row[0].AddParagraph("Merged");
                    row[2].AddParagraph("Other");
                    foreach (var cell in new[] { row[0], row[2] })
                    {
                        cell.Borders.Color = Colors.Red;
                        cell.Borders.Width = 1;
                    }
                }
            }
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var strokes = BorderStrokes(PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0))
                .Select(match => match.Value).ToArray();
            strokes.Should().OnlyHaveUniqueItems();
            strokes.Should().HaveCount(verticalMerge ? 11 : 12);
        }

        static MatchCollection BorderStrokes(string content) => Regex.Matches(content,
            @"(?m)(?<x1>-?[\d.]+) (?<y1>-?[\d.]+) m\s+(?<x2>-?[\d.]+) (?<y2>-?[\d.]+) l\s+S");

        [Theory]
        [InlineData(false, BorderStyle.Dot, 3)]
        [InlineData(true, BorderStyle.Dot, 3)]
        [InlineData(false, BorderStyle.DashSmallGap, 3)]
        [InlineData(true, BorderStyle.DashSmallGap, 3)]
        [InlineData(false, BorderStyle.DashDot, 3)]
        [InlineData(true, BorderStyle.DashDot, 3)]
        [InlineData(false, BorderStyle.DashDotDot, 3)]
        [InlineData(true, BorderStyle.DashDotDot, 3)]
        [InlineData(false, BorderStyle.Single, 1)]
        [InlineData(true, BorderStyle.Single, 1)]
        public void DottedAndDashedTableEdgesHaveVisibleEndpointStrokes(bool vertical, BorderStyle style, int expectedStrokes)
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var table = document.AddSection().AddTable();
            table.AddColumn(Unit.FromPoint(83));
            var cell = table.AddRow()[0];
            cell.AddParagraph("Cell");
            var border = vertical ? cell.Borders.Left : cell.Borders.Top;
            border.Color = Colors.Red;
            border.Width = 1;
            border.Style = style;

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var content = PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0);
            if (style == BorderStyle.DashSmallGap)
                content.Should().MatchRegex(@"\[3(?:\.0)? 1(?:\.0)?\]0 d");
            if (style == BorderStyle.DashDot)
                content.Should().MatchRegex(@"\[3(?:\.0)? 1(?:\.0)? 1(?:\.0)? 1(?:\.0)?\]0 d");
            if (style == BorderStyle.DashDotDot)
                content.Should().MatchRegex(@"\[3(?:\.0)? 1(?:\.0)? 1(?:\.0)? 1(?:\.0)? 1(?:\.0)? 1(?:\.0)?\]0 d");
            var strokes = BorderStrokes(content)
                .Cast<Match>().ToArray();
            strokes.Should().HaveCount(expectedStrokes);
            if (style is BorderStyle.Dot or BorderStyle.DashSmallGap or BorderStyle.DashDot or BorderStyle.DashDotDot)
            {
                var positions = strokes.Select(stroke => (
                    Start: double.Parse(stroke.Groups[vertical ? "y1" : "x1"].Value,
                        System.Globalization.CultureInfo.InvariantCulture),
                    End: double.Parse(stroke.Groups[vertical ? "y2" : "x2"].Value,
                        System.Globalization.CultureInfo.InvariantCulture))).ToArray();
                var minimum = positions.SelectMany(pair => new[] { pair.Start, pair.End }).Min();
                var maximum = positions.SelectMany(pair => new[] { pair.Start, pair.End }).Max();
                positions.Count(pair => Math.Abs(pair.Start - minimum) < .01 ||
                    Math.Abs(pair.End - minimum) < .01).Should().BeGreaterThan(1);
                positions.Count(pair => Math.Abs(pair.Start - maximum) < .01 ||
                    Math.Abs(pair.End - maximum) < .01).Should().BeGreaterThan(1);
            }
        }

        [Theory]
        [InlineData(BorderStyle.Dot, false)]
        [InlineData(BorderStyle.DashSmallGap, false)]
        [InlineData(BorderStyle.Dot, true)]
        [InlineData(BorderStyle.DashSmallGap, true)]
        [InlineData(BorderStyle.DashDot, false)]
        [InlineData(BorderStyle.DashDot, true)]
        [InlineData(BorderStyle.DashDotDot, false)]
        [InlineData(BorderStyle.DashDotDot, true)]
        public void MergedCellSharedPatternedEdgeIsPaintedOnce(BorderStyle style, bool upperMerged)
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var table = document.AddSection().AddTable();
            for (var column = 0; column < 3; column++)
                table.AddColumn(Unit.FromPoint(60));
            var upper = table.AddRow();
            var lower = table.AddRow();
            if (upperMerged)
                upper[0].MergeRight = 1;
            else
                lower[0].MergeRight = 1;
            var cells = upperMerged
                ? new[] { upper[0], upper[2], lower[0], lower[1], lower[2] }
                : new[] { upper[0], upper[1], upper[2], lower[0], lower[2] };
            foreach (var cell in cells)
            {
                cell.AddParagraph("Cell");
                cell.Borders.Color = Colors.Black;
                cell.Borders.Width = 0.5;
                cell.Borders.Style = style;
            }

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var horizontalRows = BorderStrokes(PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0))
                .Cast<Match>()
                .Where(stroke => stroke.Groups["y1"].Value == stroke.Groups["y2"].Value &&
                    Math.Abs(double.Parse(stroke.Groups["x2"].Value,
                        System.Globalization.CultureInfo.InvariantCulture) -
                        double.Parse(stroke.Groups["x1"].Value,
                            System.Globalization.CultureInfo.InvariantCulture)) > 10)
                .GroupBy(stroke => stroke.Groups["y1"].Value)
                .OrderBy(row => double.Parse(row.Key, System.Globalization.CultureInfo.InvariantCulture))
                .Select(row => row.Count()).ToArray();
            horizontalRows.OrderBy(count => count).Should().Equal(2, 3, 3);
        }

        [Fact]
        public void DashedBordersWithDifferentEndpointsOnSharedEdgeRemainDistinct()
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var table = document.AddSection().AddTable();
            table.AddColumn(Unit.FromPoint(83));
            var upper = table.AddRow()[0];
            var lower = table.AddRow()[0];
            upper.AddParagraph("Upper");
            lower.AddParagraph("Lower");
            upper.Borders.Bottom.Color = Colors.Red;
            upper.Borders.Bottom.Width = 0.5;
            upper.Borders.Bottom.Style = BorderStyle.DashSmallGap;
            lower.Borders.Top.Color = Colors.Red;
            lower.Borders.Top.Width = 0.5;
            lower.Borders.Top.Style = BorderStyle.DashSmallGap;
            upper.Borders.Left.Color = Colors.White;
            upper.Borders.Left.Width = 4;
            lower.Borders.Left.Color = Colors.White;
            lower.Borders.Left.Width = 16;

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var longHorizontalStrokes = BorderStrokes(PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0))
                .Cast<Match>().Where(stroke => stroke.Groups["y1"].Value == stroke.Groups["y2"].Value &&
                    Math.Abs(double.Parse(stroke.Groups["x2"].Value,
                        System.Globalization.CultureInfo.InvariantCulture) -
                        double.Parse(stroke.Groups["x1"].Value,
                            System.Globalization.CultureInfo.InvariantCulture)) > 10);
            longHorizontalStrokes.Should().HaveCount(2);
        }

        [Fact]
        public void Double_cell_borders_render_two_strokes_per_edge()
        {
            var document = new Document();
            var table = document.AddSection().AddTable();
            table.AddColumn("5cm");
            var cell = table.AddRow()[0];
            cell.AddParagraph("Cell");
            cell.Shading.Color = Colors.LightBlue;
            cell.Borders.Top.Style = BorderStyle.Double;
            cell.Borders.Top.Width = 0.5;
            cell.Borders.Left.Style = BorderStyle.Double;
            cell.Borders.Left.Width = 0.5;

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var content = string.Concat(renderer.PdfDocument.Pages[0].Contents.Elements
                .OfType<PdfSharp.Pdf.Advanced.PdfReference>()
                .Select(reference => reference.Value)
                .OfType<PdfSharp.Pdf.Advanced.PdfContent>()
                .Where(stream => stream.Stream != null)
                .Select(stream => Encoding.GetEncoding("ISO-8859-1").GetString(stream.Stream!.UnfilteredValue)));
            var strokes = Regex.Matches(content,
                @"(?m)(?<x1>[\d.]+) (?<y1>[\d.]+) m\s+(?<x2>[\d.]+) (?<y2>[\d.]+) l\s+S")
                .Cast<Match>().ToArray();

            strokes.Length.Should().Be(4);
            strokes.Count(stroke => stroke.Groups["x1"].Value == stroke.Groups["x2"].Value)
                .Should().Be(2);
            strokes.Count(stroke => stroke.Groups["y1"].Value == stroke.Groups["y2"].Value)
                .Should().Be(2);
            var vertical = strokes.Where(stroke => stroke.Groups["x1"].Value ==
                stroke.Groups["x2"].Value).Select(stroke =>
                double.Parse(stroke.Groups["x1"].Value, CultureInfo.InvariantCulture))
                .OrderBy(value => value).ToArray();
            var horizontal = strokes.Where(stroke => stroke.Groups["y1"].Value ==
                stroke.Groups["y2"].Value).Select(stroke =>
                double.Parse(stroke.Groups["y1"].Value, CultureInfo.InvariantCulture))
                .OrderBy(value => value).ToArray();
            (vertical[1] - vertical[0]).Should().BeApproximately(1, 0.01);
            (horizontal[1] - horizontal[0]).Should().BeApproximately(1, 0.01);
            var shading = Regex.Match(content,
                @"(?m)^(?<left>[\d.]+) (?<bottom>[\d.]+) [\d.]+ (?<height>[\d.]+) re\s+f$");
            shading.Success.Should().BeTrue();
            var shadingLeft = double.Parse(shading.Groups["left"].Value, CultureInfo.InvariantCulture);
            var shadingTop = double.Parse(shading.Groups["bottom"].Value, CultureInfo.InvariantCulture) +
                double.Parse(shading.Groups["height"].Value, CultureInfo.InvariantCulture);
            (shadingLeft - vertical[1]).Should().BeApproximately(0.25, 0.01);
            (horizontal[0] - shadingTop).Should().BeApproximately(0.25, 0.01);
            var verticalTops = strokes.Where(stroke => stroke.Groups["x1"].Value ==
                    stroke.Groups["x2"].Value)
                .Select(stroke => (
                    X: double.Parse(stroke.Groups["x1"].Value, CultureInfo.InvariantCulture),
                    Top: double.Parse(stroke.Groups["y2"].Value, CultureInfo.InvariantCulture)))
                .OrderBy(stroke => stroke.X).ToArray();
            verticalTops[0].Top.Should().BeApproximately(horizontal[1], 0.01);
            verticalTops[1].Top.Should().BeApproximately(shadingTop, 0.01);
        }

        [Fact]
        public void Double_right_outer_stroke_meets_outer_top_border()
        {
            var document = new Document();
            var table = document.AddSection().AddTable();
            table.AddColumn("5cm");
            var cell = table.AddRow()[0];
            cell.AddParagraph("Cell");
            cell.Borders.Top.Style = BorderStyle.Double;
            cell.Borders.Top.Width = 0.5;
            cell.Borders.Right.Style = BorderStyle.Double;
            cell.Borders.Right.Width = 0.5;

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var strokes = BorderStrokes(PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0))
                .Cast<Match>().ToArray();
            var outerHorizontalTop = strokes.Where(stroke => stroke.Groups["y1"].Value ==
                    stroke.Groups["y2"].Value)
                .Max(stroke => double.Parse(stroke.Groups["y1"].Value, CultureInfo.InvariantCulture));
            var verticalTops = strokes.Where(stroke => stroke.Groups["x1"].Value ==
                    stroke.Groups["x2"].Value)
                .Select(stroke => (
                    X: double.Parse(stroke.Groups["x1"].Value, CultureInfo.InvariantCulture),
                    Top: double.Parse(stroke.Groups["y2"].Value, CultureInfo.InvariantCulture)))
                .OrderBy(stroke => stroke.X).ToArray();
            verticalTops.Should().HaveCount(2);
            verticalTops[1].Top.Should().BeApproximately(outerHorizontalTop, 0.01);
            (verticalTops[1].Top - verticalTops[0].Top).Should().BeApproximately(1.25, 0.01);
        }

        [Fact]
        public void Double_table_perimeter_has_separate_inner_and_outer_corners()
        {
            var document = new Document();
            var table = document.AddSection().AddTable();
            table.AddColumn("5cm");
            table.Borders.Style = BorderStyle.Double;
            table.Borders.Width = 0.5;
            table.AddRow()[0].AddParagraph("Cell");

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var boxes = DoubleBorderBoxes(PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0));
            boxes.Should().HaveCount(2);
            AssertEncloses(boxes.OrderByDescending(box => box.Width * box.Height).First(),
                boxes.OrderBy(box => box.Width * box.Height).First());
        }

        static (double X, double Y, double Width, double Height)[] DoubleBorderBoxes(string content) =>
            Regex.Matches(content, @"(?m)(?<x>-?[\d.]+) (?<y>-?[\d.]+) (?<width>[\d.]+) (?<height>[\d.]+) re\s+S$")
                .Cast<Match>()
                .Select(match => (
                    double.Parse(match.Groups["x"].Value, CultureInfo.InvariantCulture),
                    double.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture),
                    double.Parse(match.Groups["width"].Value, CultureInfo.InvariantCulture),
                    double.Parse(match.Groups["height"].Value, CultureInfo.InvariantCulture)))
                .ToArray();

        static void AssertEncloses(
            (double X, double Y, double Width, double Height) outer,
            (double X, double Y, double Width, double Height) inner)
        {
            outer.X.Should().BeLessThan(inner.X);
            outer.Y.Should().BeLessThan(inner.Y);
            (outer.X + outer.Width).Should().BeGreaterThan(inner.X + inner.Width);
            (outer.Y + outer.Height).Should().BeGreaterThan(inner.Y + inner.Height);
        }

        static void AssertDoubleRailGap(double firstRail, double secondRail, double strokeWidth) =>
            (secondRail - firstRail).Should().BeApproximately(2 * strokeWidth, 0.01);

        [Fact]
        public void Mixed_compound_cells_assign_one_style_to_the_shared_vertical_edge()
        {
            var document = new Document();
            var table = document.AddSection().AddTable();
            table.AddColumn("3cm");
            table.AddColumn("3cm");
            var row = table.AddRow();
            row.Height = "2cm";
            row[0].Borders.Style = BorderStyle.ThinThickSmallGap;
            row[0].Borders.Width = 2;
            row[1].Borders.Style = BorderStyle.ThickThinSmallGap;
            row[1].Borders.Width = 2;
            row[0].AddParagraph("Left");
            row[1].AddParagraph("Right");

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var content = PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0);
            var vertical = Regex.Matches(content,
                    @"(?m)(?<x1>-?[\d.]+) (?<y1>-?[\d.]+) m\s+(?<x2>-?[\d.]+) (?<y2>-?[\d.]+) l\s+S$")
                .Cast<Match>()
                .Select(match => (
                    X1: double.Parse(match.Groups["x1"].Value, CultureInfo.InvariantCulture),
                    Y1: double.Parse(match.Groups["y1"].Value, CultureInfo.InvariantCulture),
                    X2: double.Parse(match.Groups["x2"].Value, CultureInfo.InvariantCulture),
                    Y2: double.Parse(match.Groups["y2"].Value, CultureInfo.InvariantCulture)))
                .Where(line => Math.Abs(line.X1 - line.X2) < 0.01 &&
                    Math.Abs(line.Y1 - line.Y2) > 20)
                .OrderBy(line => line.X1).ToArray();
            // Both cells contribute a complete pair of rails.  The later
            // cell clears the shared band and paints its own pair there.
            vertical.Should().HaveCount(8);
            (vertical[1].X1 - vertical[0].X1).Should().BeApproximately(2.125, .02);
            (vertical[3].X1 - vertical[2].X1).Should().BeApproximately(.625, .02);
            (vertical[5].X1 - vertical[4].X1).Should().BeApproximately(.625, .02);
            (vertical[7].X1 - vertical[6].X1).Should().BeApproximately(2.125, .02);

            var horizontal = Regex.Matches(content,
                    @"(?m)(?<x1>-?[\d.]+) (?<y1>-?[\d.]+) m\s+(?<x2>-?[\d.]+) (?<y2>-?[\d.]+) l\s+S$")
                .Cast<Match>()
                .Select(match => (
                    X1: double.Parse(match.Groups["x1"].Value, CultureInfo.InvariantCulture),
                    Y1: double.Parse(match.Groups["y1"].Value, CultureInfo.InvariantCulture),
                    X2: double.Parse(match.Groups["x2"].Value, CultureInfo.InvariantCulture),
                    Y2: double.Parse(match.Groups["y2"].Value, CultureInfo.InvariantCulture)))
                .Where(line => Math.Abs(line.Y1 - line.Y2) < .01 &&
                    Math.Abs(line.X1 - line.X2) > 20).ToArray();
            horizontal.Should().HaveCount(8);
            foreach (var rail in vertical)
            {
                foreach (var endpoint in new[] { rail.Y1, rail.Y2 })
                    horizontal.Should().Contain(line => Math.Abs(line.Y1 - endpoint) <= 1.01 &&
                        Math.Min(line.X1, line.X2) <= rail.X1 + .01 &&
                        Math.Max(line.X1, line.X2) >= rail.X1 - .01);
            }
        }

        [Fact]
        public void Mixed_two_and_three_rail_compound_cells_join_every_rail_at_corners()
        {
            var document = new Document();
            var table = document.AddSection().AddTable();
            table.AddColumn("3cm");
            table.AddColumn("3cm");
            foreach (var styles in new[]
                     {
                         new[] { BorderStyle.ThinThickSmallGap, BorderStyle.ThinThickThinSmallGap },
                         new[] { BorderStyle.ThinThickThinLargeGap, BorderStyle.ThickThinMediumGap }
                     })
            {
                var row = table.AddRow();
                row.Height = "2cm";
                for (var column = 0; column < 2; column++)
                {
                    row[column].Borders.Style = styles[column];
                    row[column].Borders.Width = 2;
                    row[column].AddParagraph("Cell");
                }
            }

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var content = PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0);
            var segments = Regex.Matches(content,
                    @"(?m)(?<x1>-?[\d.]+) (?<y1>-?[\d.]+) m\s+(?<x2>-?[\d.]+) (?<y2>-?[\d.]+) l\s+S$")
                .Cast<Match>()
                .Select(match => (
                    X1: double.Parse(match.Groups["x1"].Value, CultureInfo.InvariantCulture),
                    Y1: double.Parse(match.Groups["y1"].Value, CultureInfo.InvariantCulture),
                    X2: double.Parse(match.Groups["x2"].Value, CultureInfo.InvariantCulture),
                    Y2: double.Parse(match.Groups["y2"].Value, CultureInfo.InvariantCulture)))
                .ToArray();
            var vertical = segments.Where(line => Math.Abs(line.X1 - line.X2) < .01 &&
                Math.Abs(line.Y1 - line.Y2) > 20).ToArray();
            var horizontal = segments.Where(line => Math.Abs(line.Y1 - line.Y2) < .01 &&
                Math.Abs(line.X1 - line.X2) > 20).ToArray();
            vertical.Should().HaveCount(20);
            horizontal.Should().HaveCount(20);
            foreach (var rail in vertical)
            {
                foreach (var endpoint in new[] { rail.Y1, rail.Y2 })
                    horizontal.Should().Contain(line => Math.Abs(line.Y1 - endpoint) <= 1.01 &&
                        Math.Min(line.X1, line.X2) <= rail.X1 + .01 &&
                        Math.Max(line.X1, line.X2) >= rail.X1 - .01);
            }
        }

        [Fact]
        public void Four_neighbor_triple_junction_continues_middle_rails_and_turns_outer_rails()
        {
            var document = new Document();
            var table = document.AddSection().AddTable();
            table.AddColumn("3cm");
            table.AddColumn("3cm");
            for (var rowIndex = 0; rowIndex < 2; rowIndex++)
            {
                var row = table.AddRow();
                row.Height = "2cm";
                for (var column = 0; column < 2; column++)
                {
                    var triple = rowIndex != column;
                    row[column].Borders.Style = triple ? BorderStyle.Triple : BorderStyle.Single;
                    row[column].Borders.Width = triple ? 3 : 1;
                    row[column].AddParagraph("Cell");
                }
            }

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var content = PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0);
            content.Should().Contain("W* n");
            var lines = Regex.Matches(content,
                    @"(?m)(?<x1>-?[\d.]+) (?<y1>-?[\d.]+) m\s+(?<x2>-?[\d.]+) (?<y2>-?[\d.]+) l\s+S$")
                .Cast<Match>()
                .Select(match => (
                    X1: double.Parse(match.Groups["x1"].Value, CultureInfo.InvariantCulture),
                    Y1: double.Parse(match.Groups["y1"].Value, CultureInfo.InvariantCulture),
                    X2: double.Parse(match.Groups["x2"].Value, CultureInfo.InvariantCulture),
                    Y2: double.Parse(match.Groups["y2"].Value, CultureInfo.InvariantCulture)))
                .TakeLast(10).ToArray();
            var vertical = lines.Where(line => Math.Abs(line.X1 - line.X2) < .01).ToArray();
            var horizontal = lines.Where(line => Math.Abs(line.Y1 - line.Y2) < .01).ToArray();
            vertical.Should().HaveCount(5);
            horizontal.Should().HaveCount(5);
            var centerX = vertical.Select(line => line.X1).Distinct().OrderBy(x => x).ElementAt(1);
            var centerY = horizontal.Select(line => line.Y1).Distinct().OrderBy(y => y).ElementAt(1);
            var middleVertical = vertical.Single(line => Math.Abs(line.X1 - centerX) < .01);
            var middleHorizontal = horizontal.Single(line => Math.Abs(line.Y1 - centerY) < .01);
            Math.Abs(middleVertical.Y2 - middleVertical.Y1).Should().BeGreaterThan(
                vertical.Where(line => line != middleVertical).Max(line => Math.Abs(line.Y2 - line.Y1)));
            Math.Abs(middleHorizontal.X2 - middleHorizontal.X1).Should().BeGreaterThan(
                horizontal.Where(line => line != middleHorizontal).Max(line => Math.Abs(line.X2 - line.X1)));
            Math.Min(middleHorizontal.X1, middleHorizontal.X2).Should().BeLessThan(centerX);
            Math.Max(middleHorizontal.X1, middleHorizontal.X2).Should().BeGreaterThan(centerX);
            Math.Min(middleVertical.Y1, middleVertical.Y2).Should().BeLessThan(centerY);
            Math.Max(middleVertical.Y1, middleVertical.Y2).Should().BeGreaterThan(centerY);
        }

        [Fact]
        public void Four_neighbor_junction_respects_a_wider_single_border_on_one_arm()
        {
            var document = new Document();
            var table = document.AddSection().AddTable();
            table.AddColumn("3cm");
            table.AddColumn("3cm");
            for (var rowIndex = 0; rowIndex < 2; rowIndex++)
            {
                var row = table.AddRow();
                row.Height = "2cm";
                for (var column = 0; column < 2; column++)
                {
                    var triple = rowIndex != column;
                    row[column].Borders.Style = triple ? BorderStyle.Triple : BorderStyle.Single;
                    row[column].Borders.Width = triple ? 3 : rowIndex == 0 ? 20 : 1;
                    row[column].AddParagraph("Cell");
                }
            }

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var content = PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0);
            // The triple rails are clipped without painting an opaque band over
            // the wider single border or the page background.
            content.Should().Contain("W* n");
            Regex.Matches(content, @"(?m)-?[\d.]+ -?[\d.]+ [\d.]+ [\d.]+ re\s+f$")
                .Should().BeEmpty();
        }

        [Fact]
        public void Four_neighbor_double_junction_turns_both_rails_around_cell_corners()
        {
            var document = new Document();
            var table = document.AddSection().AddTable();
            table.AddColumn("3cm");
            table.AddColumn("3cm");
            for (var rowIndex = 0; rowIndex < 2; rowIndex++)
            {
                var row = table.AddRow();
                row.Height = "2cm";
                for (var column = 0; column < 2; column++)
                {
                    var isDouble = rowIndex != column;
                    row[column].Borders.Style = isDouble ? BorderStyle.Double : BorderStyle.Single;
                    row[column].Borders.Width = isDouble ? 2 : 1;
                    row[column].AddParagraph("Cell");
                }
            }

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var content = PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0);
            content.Should().Contain("W* n");
            var lines = Regex.Matches(content,
                    @"(?m)(?<x1>-?[\d.]+) (?<y1>-?[\d.]+) m\s+(?<x2>-?[\d.]+) (?<y2>-?[\d.]+) l\s+S$")
                .Cast<Match>()
                .Select(match => (
                    X1: double.Parse(match.Groups["x1"].Value, CultureInfo.InvariantCulture),
                    Y1: double.Parse(match.Groups["y1"].Value, CultureInfo.InvariantCulture),
                    X2: double.Parse(match.Groups["x2"].Value, CultureInfo.InvariantCulture),
                    Y2: double.Parse(match.Groups["y2"].Value, CultureInfo.InvariantCulture)))
                .TakeLast(8).ToArray();
            lines.Count(line => Math.Abs(line.X1 - line.X2) < .01).Should().Be(4);
            lines.Count(line => Math.Abs(line.Y1 - line.Y2) < .01).Should().Be(4);
        }

        [Fact]
        public void Mixed_single_double_triple_cells_keep_individual_closed_rail_boxes()
        {
            var document = new Document();
            var table = document.AddSection().AddTable();
            for (var column = 0; column < 3; column++) table.AddColumn("4cm");
            var row = table.AddRow();
            row.Height = "2cm";
            static void SetCellBorders(MigraDoc.DocumentObjectModel.Tables.Cell cell, BorderStyle style, int width)
            {
                foreach (var border in new[] { cell.Borders.Top, cell.Borders.Right,
                             cell.Borders.Bottom, cell.Borders.Left })
                {
                    border.Style = style;
                    border.Width = width;
                }
            }
            SetCellBorders(row[0], BorderStyle.Single, 1);
            SetCellBorders(row[1], BorderStyle.Double, 2);
            SetCellBorders(row[2], BorderStyle.Triple, 3);
            for (var column = 0; column < 3; column++) row[column].AddParagraph($"{column}");

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var boxes = DoubleBorderBoxes(PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0));
            boxes.Should().HaveCount(5);
            var groups = boxes.GroupBy(box => Math.Round(box.X + box.Width / 2, 2))
                .OrderBy(group => group.Key).ToArray();
            groups.Should().HaveCount(2);
            groups[0].Should().HaveCount(2);
            groups[1].Should().HaveCount(3);
            var doubleRails = groups[0].OrderBy(box => box.Width).ToArray();
            AssertEncloses(doubleRails[1], doubleRails[0]);
            (doubleRails[0].X - doubleRails[1].X).Should().BeApproximately(4, .02);
            var tripleRails = groups[1].OrderBy(box => box.Width).ToArray();
            AssertEncloses(tripleRails[2], tripleRails[1]);
            AssertEncloses(tripleRails[1], tripleRails[0]);
            (tripleRails[0].X - tripleRails[1].X).Should().BeApproximately(6, .02);
            (tripleRails[1].X - tripleRails[2].X).Should().BeApproximately(6, .02);

            var content = PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0);
            content.Should().Contain("W* n");
            Regex.Matches(content, @"(?m)-?[\d.]+ -?[\d.]+ [\d.]+ [\d.]+ re\s+f$")
                .Should().BeEmpty();
        }

        [Fact]
        public void Mixed_decorative_junction_stops_vertical_rails_at_horizontal_band()
        {
            var document = new Document();
            var table = document.AddSection().AddTable();
            table.AddColumn("4cm");
            var cell = table.AddRow()[0];
            cell.AddParagraph("Junction");
            cell.Borders.Top.Style = BorderStyle.Triple;
            cell.Borders.Top.Width = 2;
            cell.Borders.Bottom.Style = BorderStyle.DoubleWave;
            cell.Borders.Bottom.Width = 2;
            cell.Borders.Left.Style = BorderStyle.Dot;
            cell.Borders.Left.Width = 2;
            cell.Borders.Right.Style = BorderStyle.ThinThickMediumGap;
            cell.Borders.Right.Width = 2;

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var content = PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0);
            var segments = Regex.Matches(content,
                    @"(?m)(?<x1>-?[\d.]+) (?<y1>-?[\d.]+) m\s+(?<x2>-?[\d.]+) (?<y2>-?[\d.]+) l\s+S$")
                .Cast<Match>()
                .Select(match => (
                    X1: double.Parse(match.Groups["x1"].Value, CultureInfo.InvariantCulture),
                    Y1: double.Parse(match.Groups["y1"].Value, CultureInfo.InvariantCulture),
                    X2: double.Parse(match.Groups["x2"].Value, CultureInfo.InvariantCulture),
                    Y2: double.Parse(match.Groups["y2"].Value, CultureInfo.InvariantCulture)))
                .ToArray();
            var vertical = segments.Where(line => Math.Abs(line.X1 - line.X2) < .01 &&
                Math.Abs(line.Y1 - line.Y2) > 5).ToArray();
            var wave = segments.Where(line => Math.Abs(line.X1 - line.X2) > .05 &&
                Math.Abs(line.Y1 - line.Y2) > .05 && Math.Abs(line.Y1 - line.Y2) < 5).ToArray();
            vertical.Should().NotBeEmpty();
            wave.Should().NotBeEmpty();
            var waveMin = wave.Min(line => Math.Min(line.Y1, line.Y2));
            var waveMax = wave.Max(line => Math.Max(line.Y1, line.Y2));
            foreach (var line in vertical)
            {
                var overlap = Math.Min(Math.Max(line.Y1, line.Y2), waveMax) -
                    Math.Max(Math.Min(line.Y1, line.Y2), waveMin);
                overlap.Should().BeLessThan(.05);
            }
        }

        [Theory]
        [InlineData(BorderStyle.Double, 2)]
        [InlineData(BorderStyle.Triple, 3)]
        public void Mixed_multi_rail_junction_stops_vertical_rails_before_horizontal_rails(
            BorderStyle horizontalStyle, int expectedHorizontalRails)
        {
            var document = new Document();
            var table = document.AddSection().AddTable();
            table.AddColumn("4cm");
            var cell = table.AddRow()[0];
            cell.AddParagraph("Junction");
            cell.Borders.Top.Style = BorderStyle.Single;
            cell.Borders.Top.Width = 1;
            cell.Borders.Bottom.Style = horizontalStyle;
            cell.Borders.Bottom.Width = 2;
            cell.Borders.Left.Style = BorderStyle.ThinThickMediumGap;
            cell.Borders.Left.Width = 2;
            cell.Borders.Right.Style = BorderStyle.ThinThickMediumGap;
            cell.Borders.Right.Width = 2;

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var content = PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0);
            var segments = Regex.Matches(content,
                    @"(?m)(?<x1>-?[\d.]+) (?<y1>-?[\d.]+) m\s+(?<x2>-?[\d.]+) (?<y2>-?[\d.]+) l\s+S$")
                .Cast<Match>()
                .Select(match => (
                    X1: double.Parse(match.Groups["x1"].Value, CultureInfo.InvariantCulture),
                    Y1: double.Parse(match.Groups["y1"].Value, CultureInfo.InvariantCulture),
                    X2: double.Parse(match.Groups["x2"].Value, CultureInfo.InvariantCulture),
                    Y2: double.Parse(match.Groups["y2"].Value, CultureInfo.InvariantCulture)))
                .ToArray();
            var vertical = segments.Where(line => Math.Abs(line.X1 - line.X2) < .01 &&
                Math.Abs(line.Y1 - line.Y2) > 5).ToArray();
            var horizontal = segments.Where(line => Math.Abs(line.Y1 - line.Y2) < .01 &&
                Math.Abs(line.X1 - line.X2) > 20).ToArray();
            vertical.Should().HaveCount(4);
            horizontal.Should().HaveCount(expectedHorizontalRails + 1);
            var bottomRails = horizontal.OrderBy(line => line.Y1).Take(expectedHorizontalRails).ToArray();
            var bottomMin = bottomRails.Min(line => line.Y1);
            var bottomMax = bottomRails.Max(line => line.Y1);
            foreach (var line in vertical)
            {
                var overlap = Math.Min(Math.Max(line.Y1, line.Y2), bottomMax) -
                    Math.Max(Math.Min(line.Y1, line.Y2), bottomMin);
                overlap.Should().BeLessThan(.05);
            }
        }

        [Fact]
        public void Uniform_three_by_three_double_grid_has_closed_cell_and_perimeter_paths()
        {
            var document = new Document();
            var table = document.AddSection().AddTable();
            for (var column = 0; column < 3; column++)
                table.AddColumn("3.5cm");
            table.Borders.Style = BorderStyle.Double;
            table.Borders.Width = 1;
            for (var row = 0; row < 3; row++)
            {
                var rowCells = table.AddRow();
                rowCells.Height = "2cm";
                for (var column = 0; column < 3; column++)
                    rowCells[column].AddParagraph($"{row + 1},{column + 1}");
            }

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var path = Environment.GetEnvironmentVariable("PDFXPORT_GRID_OUTPUT");
            if (!string.IsNullOrEmpty(path))
                renderer.PdfDocument.Save(path);
            var content = PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0);
            var boxes = DoubleBorderBoxes(content);
            boxes.Should().HaveCount(10);
            var outer = boxes.OrderByDescending(box => box.Width * box.Height).First();
            var cells = boxes.Where(box => box != outer).ToArray();
            cells.Should().HaveCount(9);
            foreach (var inner in cells)
                AssertEncloses(outer, inner);
            cells.Select(box => Math.Round(box.X, 2)).Distinct().Should().HaveCount(3);
            cells.Select(box => Math.Round(box.Y, 2)).Distinct().Should().HaveCount(3);
            var grid = cells.OrderBy(box => box.Y).ThenBy(box => box.X).ToArray();
            var left = grid.Min(box => box.X);
            var top = grid.Min(box => box.Y);
            var right = grid.Max(box => box.X + box.Width);
            var bottom = grid.Max(box => box.Y + box.Height);
            AssertDoubleRailGap(outer.X, left, 1);
            AssertDoubleRailGap(outer.Y, top, 1);
            AssertDoubleRailGap(right, outer.X + outer.Width, 1);
            AssertDoubleRailGap(bottom, outer.Y + outer.Height, 1);
            for (var row = 0; row < 3; row++)
            {
                var rowCells = grid.Skip(row * 3).Take(3).ToArray();
                rowCells.Select(box => Math.Round(box.Y, 2)).Distinct().Should().HaveCount(1);
                rowCells.Select(box => Math.Round(box.Y + box.Height, 2)).Distinct().Should().HaveCount(1);
                for (var column = 0; column < 2; column++)
                    AssertDoubleRailGap(rowCells[column].X + rowCells[column].Width,
                        rowCells[column + 1].X, 1);
            }
            for (var row = 0; row < 2; row++)
                AssertDoubleRailGap(grid[row * 3].Y + grid[row * 3].Height, grid[(row + 1) * 3].Y, 1);
        }

        [Fact]
        public void Double_internal_row_border_stays_inside_outer_side_strokes()
        {
            var document = new Document();
            var table = document.AddSection().AddTable();
            table.AddColumn("5cm");
            table.Borders.Style = BorderStyle.Double;
            table.Borders.Width = 0.5;
            table.AddRow()[0].AddParagraph("First");
            table.AddRow()[0].AddParagraph("Second");

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var boxes = DoubleBorderBoxes(PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0));
            boxes.Should().HaveCount(3);
            var outer = boxes.OrderByDescending(box => box.Width * box.Height).First();
            foreach (var inner in boxes.Where(box => box != outer))
                AssertEncloses(outer, inner);
            var innerBoxes = boxes.Where(box => box != outer).OrderBy(box => box.Y).ToArray();
            innerBoxes[0].Y.Should().BeLessThan(innerBoxes[1].Y);
            (innerBoxes[0].Y + innerBoxes[0].Height).Should().BeLessThan(innerBoxes[1].Y);
            innerBoxes[0].X.Should().BeApproximately(innerBoxes[1].X, 0.01);
            (innerBoxes[0].X + innerBoxes[0].Width).Should().BeApproximately(
                innerBoxes[1].X + innerBoxes[1].Width, 0.01);
            AssertDoubleRailGap(innerBoxes[0].Y + innerBoxes[0].Height, innerBoxes[1].Y, 0.5);
        }

        [Fact]
        public void Double_horizontal_strokes_stop_at_internal_column_sides()
        {
            var document = new Document();
            var table = document.AddSection().AddTable();
            table.AddColumn("5cm");
            table.AddColumn("5cm");
            table.Borders.Style = BorderStyle.Double;
            table.Borders.Width = 0.5;
            var row = table.AddRow();
            row[0].AddParagraph("Left");
            row[1].AddParagraph("Right");

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var boxes = DoubleBorderBoxes(PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0));
            boxes.Should().HaveCount(3);
            var outer = boxes.OrderByDescending(box => box.Width * box.Height).First();
            var innerBoxes = boxes.Where(box => box != outer).OrderBy(box => box.X).ToArray();
            foreach (var inner in innerBoxes)
                AssertEncloses(outer, inner);
            (innerBoxes[0].X + innerBoxes[0].Width).Should().BeLessThan(innerBoxes[1].X);
            innerBoxes[0].Y.Should().BeApproximately(innerBoxes[1].Y, 0.01);
            (innerBoxes[0].Y + innerBoxes[0].Height).Should().BeApproximately(
                innerBoxes[1].Y + innerBoxes[1].Height, 0.01);
            AssertDoubleRailGap(innerBoxes[0].X + innerBoxes[0].Width, innerBoxes[1].X, 0.5);
        }

        [Fact]
        public void Partial_double_table_borders_preserve_single_side_strokes()
        {
            var document = new Document();
            var table = document.AddSection().AddTable();
            table.AddColumn("5cm");
            table.Borders.Top.Style = BorderStyle.Double;
            table.Borders.Top.Width = 1;
            table.Borders.Bottom.Style = BorderStyle.Double;
            table.Borders.Bottom.Width = 1;
            table.Borders.Left.Style = BorderStyle.Single;
            table.Borders.Left.Width = 0.5;
            table.Borders.Right.Style = BorderStyle.Single;
            table.Borders.Right.Width = 0.5;
            table.AddRow()[0].AddParagraph("Cell");

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var content = string.Concat(renderer.PdfDocument.Pages[0].Contents.Elements
                .OfType<PdfSharp.Pdf.Advanced.PdfReference>()
                .Select(reference => reference.Value)
                .OfType<PdfSharp.Pdf.Advanced.PdfContent>()
                .Where(stream => stream.Stream != null)
                .Select(stream => Encoding.GetEncoding("ISO-8859-1").GetString(stream.Stream!.UnfilteredValue)));
            var strokes = Regex.Matches(content,
                @"(?m)(?<x1>[\d.]+) (?<y1>[\d.]+) m\s+(?<x2>[\d.]+) (?<y2>[\d.]+) l\s+S")
                .Cast<Match>().ToArray();

            strokes.Count(stroke => stroke.Groups["x1"].Value == stroke.Groups["x2"].Value)
                .Should().Be(2);
            var horizontal = strokes.Where(stroke => stroke.Groups["y1"].Value ==
                    stroke.Groups["y2"].Value)
                .Select(stroke => double.Parse(stroke.Groups["y1"].Value, CultureInfo.InvariantCulture))
                .OrderBy(value => value).ToArray();
            horizontal.Length.Should().Be(4);
            (horizontal[1] - horizontal[0]).Should().BeApproximately(2, 0.01);
            (horizontal[3] - horizontal[2]).Should().BeApproximately(2, 0.01);
        }

        [Fact]
        public void Double_borders_do_not_cross_a_horizontally_merged_cell()
        {
            var document = new Document();
            var table = document.AddSection().AddTable();
            table.AddColumn("3cm");
            table.AddColumn("3cm");
            table.Borders.Style = BorderStyle.Double;
            table.Borders.Width = 0.5;
            var firstRow = table.AddRow();
            firstRow[0].MergeRight = 1;
            firstRow[0].AddParagraph("Merged");
            var secondRow = table.AddRow();
            secondRow[0].AddParagraph("Left");
            secondRow[1].AddParagraph("Right");

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var boxes = DoubleBorderBoxes(PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0));
            boxes.Should().HaveCount(4);
            var outer = boxes.OrderByDescending(box => box.Width * box.Height).First();
            foreach (var inner in boxes.Where(box => box != outer))
                AssertEncloses(outer, inner);
            var cellBoxes = boxes.Where(box => box != outer).OrderByDescending(box => box.Width).ToArray();
            cellBoxes[0].Width.Should().BeGreaterThan(cellBoxes[1].Width);
            cellBoxes[0].Width.Should().BeGreaterThan(cellBoxes[2].Width);
        }

        [Fact]
        public void Double_diagonal_cell_borders_render_two_strokes_each()
        {
            var document = new Document();
            var table = document.AddSection().AddTable();
            table.AddColumn("5cm");
            var cell = table.AddRow()[0];
            cell.AddParagraph("Diagonals");
            cell.Borders.DiagonalDown.Style = BorderStyle.Double;
            cell.Borders.DiagonalDown.Width = 0.5;
            cell.Borders.DiagonalUp.Style = BorderStyle.Double;
            cell.Borders.DiagonalUp.Width = 0.5;

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var content = string.Concat(renderer.PdfDocument.Pages[0].Contents.Elements
                .OfType<PdfSharp.Pdf.Advanced.PdfReference>()
                .Select(reference => reference.Value)
                .OfType<PdfSharp.Pdf.Advanced.PdfContent>()
                .Where(stream => stream.Stream != null)
                .Select(stream => Encoding.GetEncoding("ISO-8859-1").GetString(stream.Stream!.UnfilteredValue)));
            var diagonals = Regex.Matches(content,
                    @"(?m)(?<x1>[\d.]+) (?<y1>[\d.]+) m\s+(?<x2>[\d.]+) (?<y2>[\d.]+) l\s+S")
                .Cast<Match>()
                .Where(stroke => stroke.Groups["x1"].Value != stroke.Groups["x2"].Value &&
                    stroke.Groups["y1"].Value != stroke.Groups["y2"].Value)
                .ToArray();

            diagonals.Length.Should().Be(4);
            diagonals.Count(stroke =>
                (double.Parse(stroke.Groups["x2"].Value, CultureInfo.InvariantCulture) -
                    double.Parse(stroke.Groups["x1"].Value, CultureInfo.InvariantCulture)) *
                (double.Parse(stroke.Groups["y2"].Value, CultureInfo.InvariantCulture) -
                    double.Parse(stroke.Groups["y1"].Value, CultureInfo.InvariantCulture)) > 0)
                .Should().Be(2);
        }

        [Fact]
        public void Double_rounded_corner_renders_two_arcs()
        {
            var document = new Document();
            var table = document.AddSection().AddTable();
            table.AddColumn("5cm");
            var cell = table.AddRow()[0];
            cell.AddParagraph("Rounded");
            cell.RoundedCorner = MigraDoc.DocumentObjectModel.Tables.RoundedCorner.TopLeft;
            cell.Borders.Top.Style = BorderStyle.Double;
            cell.Borders.Top.Width = 0.5;
            cell.Borders.Left.Style = BorderStyle.Double;
            cell.Borders.Left.Width = 0.5;

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var content = string.Concat(renderer.PdfDocument.Pages[0].Contents.Elements
                .OfType<PdfSharp.Pdf.Advanced.PdfReference>()
                .Select(reference => reference.Value)
                .OfType<PdfSharp.Pdf.Advanced.PdfContent>()
                .Where(stream => stream.Stream != null)
                .Select(stream => Encoding.GetEncoding("ISO-8859-1").GetString(stream.Stream!.UnfilteredValue)));

            Regex.Matches(content, @"(?m)\bc$").Count.Should().Be(2);
        }

        [Fact]
        public void Create_Table_Cell_with_Top_Border()
        {
            // Create a MigraDoc document.
            var document = new Document();

            // Add a section to the document.
            var section = document.AddSection();

            // Add a paragraph to the section.
            var paragraph = section.AddParagraph("Dummy");

            var table = section.AddTable();
            table.AddColumn("5cm");
            var row = table.AddRow();
            var cell = row[0];
            cell.AddParagraph("Cell");
            cell.Borders.Top.Color = Colors.Blue;
            //cell.Borders.Top.Width = 5;

            // Create a renderer for the MigraDoc document.
            var pdfRenderer = new PdfDocumentRenderer()
            {
                // Associate the MigraDoc document with a renderer.
                Document = document
            };

            // Layout and render document to PDF.
            pdfRenderer.RenderDocument();

            // Save the document…
            var filename = PdfFileUtility.GetTempPdfFullFileName("unittests/migradoc/tables/TableTopBorder");
            pdfRenderer.PdfDocument.Save(filename);
            // … and start a viewer.
            PdfFileUtility.ShowDocumentIfDebugging(filename);

#if DEBUG___
            MigraDoc.DocumentObjectModel.IO.DdlWriter dw = new MigraDoc.DocumentObjectModel.IO.DdlWriter(filename + "_2.mdddl");
            dw.WriteDocument(document);
            dw.Close();
#endif
        }

        [Theory]
        [InlineData(0.5, false, false)]
        [InlineData(3, false, false)]
        [InlineData(3, true, false)]
        [InlineData(3, false, true)]
        public void Fragment_top_border_width_reserves_continuation_space_only(
            double reservation, bool repeatedHeader, bool nearPageLimit)
        {
            static PdfDocumentRenderer Render(double? topBorderWidth, bool repeatedHeader,
                bool nearPageLimit)
            {
                var document = new Document();
                var section = document.AddSection();
                section.PageSetup.PageHeight = Unit.FromPoint(792);
                section.PageSetup.TopMargin = Unit.FromPoint(72);
                section.PageSetup.BottomMargin = Unit.FromPoint(72);
                var table = section.AddTable();
                table.AddColumn(Unit.FromPoint(432));
                for (var index = 0; index < (nearPageLimit ? 50 : 27); index++)
                {
                    var row = table.AddRow();
                    if (index == 0)
                        row.HeadingFormat = repeatedHeader;
                    row.Height = Unit.FromPoint(nearPageLimit ? 25.9 : 25);
                    row.HeightRule = MigraDoc.DocumentObjectModel.Tables.RowHeightRule.Exactly;
                    row[0].AddParagraph($"Row {index + 1}");
                }

                var renderer = new PdfDocumentRenderer { Document = document };
                if (topBorderWidth != null)
                    renderer.DocumentRenderer.TableFragmentTopBorderReservation =
                        (_, _) => topBorderWidth;
                renderer.RenderDocument();
                return renderer;
            }

            var original = Render(null, repeatedHeader, nearPageLimit);
            var reserved = Render(reservation, repeatedHeader, nearPageLimit);
            var invalid = Render(double.NaN, repeatedHeader, nearPageLimit);
            original.PdfDocument.Pages.Count.Should().Be(2);
            reserved.PdfDocument.Pages.Count.Should().Be(nearPageLimit ? 3 : 2);
            invalid.PdfDocument.Pages.Count.Should().Be(2);
            var firstOriginal = original.DocumentRenderer.GetRenderInfoFromPage(1)!
                .Single(info => info.FormatInfo is TableFormatInfo);
            var firstReserved = reserved.DocumentRenderer.GetRenderInfoFromPage(1)!
                .Single(info => info.FormatInfo is TableFormatInfo);
            var secondOriginal = original.DocumentRenderer.GetRenderInfoFromPage(2)!
                .Single(info => info.FormatInfo is TableFormatInfo);
            var secondReserved = reserved.DocumentRenderer.GetRenderInfoFromPage(2)!
                .Single(info => info.FormatInfo is TableFormatInfo);
            var secondInvalid = invalid.DocumentRenderer.GetRenderInfoFromPage(2)!
                .Single(info => info.FormatInfo is TableFormatInfo);
            ((TableFormatInfo)firstOriginal.FormatInfo).StartRow.Should().Be(
                ((TableFormatInfo)firstReserved.FormatInfo).StartRow);
            ((TableFormatInfo)secondOriginal.FormatInfo).StartRow.Should().Be(
                ((TableFormatInfo)secondReserved.FormatInfo).StartRow);
            firstReserved.LayoutInfo.ContentArea.Height.Point.Should().BeApproximately(
                firstOriginal.LayoutInfo.ContentArea.Height.Point, 0.001);
            secondInvalid.LayoutInfo.ContentArea.Height.Point.Should().BeApproximately(
                secondOriginal.LayoutInfo.ContentArea.Height.Point, 0.001);
            if (nearPageLimit)
            {
                var secondEnd = ((TableFormatInfo)secondReserved.FormatInfo).EndRow;
                secondEnd.Should().Be(((TableFormatInfo)secondOriginal.FormatInfo).EndRow - 1);
                var thirdReserved = reserved.DocumentRenderer.GetRenderInfoFromPage(3)!
                    .Single(info => info.FormatInfo is TableFormatInfo);
                ((TableFormatInfo)thirdReserved.FormatInfo).StartRow.Should().Be(secondEnd + 1);
                ((TableFormatInfo)thirdReserved.FormatInfo).EndRow.Should().Be(49);
            }
            else
                (secondReserved.LayoutInfo.ContentArea.Height.Point -
                    secondOriginal.LayoutInfo.ContentArea.Height.Point).Should().BeApproximately(
                        repeatedHeader ? 0 : reservation, 0.001);
        }

        [Fact]
        public void Create_a_cloned_table()
        {
            // Create a MigraDoc document.
            var document = new Document();

            // Add a section to the document.
            var section = document.AddSection();

            // Add a paragraph to the section.
            var paragraph = section.AddParagraph("Dummy");

            Style style = document.Styles[StyleNames.Normal]!;
            style.Font.Name = "Arial";

            var sec = document.LastSection;
            var p = sec.AddParagraph("Creating a table");
            var table = sec.AddTable();
            table.Tag = "Original";
            table.AddColumn("2cm");
            table.AddColumn("2cm");
            var row = table.AddRow();
            row[0].AddParagraph("H1");
            row[1].AddParagraph("H2");
            row.HeadingFormat = true;
            row = table.AddRow();
            row[0].AddParagraph("C1");
            row[1].AddParagraph("C2");
            sec.AddParagraph("Cloned table");
            var clone = table.Clone();
            clone.Tag = "Clone";
            sec.Add(clone);
            sec.AddParagraph("End of table test");

            // Create a renderer for the MigraDoc document.
            var pdfRenderer = new PdfDocumentRenderer()
            {
                // Associate the MigraDoc document with a renderer.
                Document = document
            };

            // Layout and render document to PDF.
            pdfRenderer.RenderDocument();

            // Save the document…
            var filename = PdfFileUtility.GetTempPdfFullFileName("unittests/migradoc/tables/ClonedTable");
            pdfRenderer.PdfDocument.Save(filename);
            // … and start a viewer.
            PdfFileUtility.ShowDocumentIfDebugging(filename);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Render_Table_With_Vertically_Merged_Cell(bool addUnmergedColumn)
        {
            var document = new Document();
            var table = document.AddSection().AddTable();
            table.AddColumn("5cm");
            if (addUnmergedColumn)
                table.AddColumn("5cm");
            var firstRow = table.AddRow();
            firstRow[0].MergeDown = 1;
            firstRow[0].AddParagraph("Merged cell");
            var secondRow = table.AddRow();
            if (addUnmergedColumn)
            {
                firstRow[1].AddParagraph("First row");
                secondRow[1].AddParagraph("Second row");
            }

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();

            if (renderer.PdfDocument.Pages.Count != 1)
                throw new InvalidOperationException("The merged table must fit on one page.");
        }

        [Fact]
        public void Render_Table_With_Different_Vertical_Merge_Lengths()
        {
            var document = new Document();
            var table = document.AddSection().AddTable();
            table.AddColumn("5cm");
            table.AddColumn("5cm");
            var firstRow = table.AddRow();
            firstRow[0].MergeDown = 2;
            firstRow[0].AddParagraph("Long merge");
            firstRow[1].MergeDown = 1;
            firstRow[1].AddParagraph("Short merge");
            table.AddRow();
            table.AddRow()[1].AddParagraph("Third row");

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();

            if (renderer.PdfDocument.Pages.Count != 1)
                throw new InvalidOperationException("The mixed-merge table must fit on one page.");
        }

        [Fact]
        public void Create_a_table_with_cloned_rows()
        {
            // Create a MigraDoc document.
            var document = new Document();

            // Add a section to the document.
            var section = document.AddSection();

            // Add a paragraph to the section.
            var paragraph = section.AddParagraph("Dummy");

            Style style = document.Styles[StyleNames.Normal]!;
            style.Font.Name = "Arial";

            var sec = document.LastSection;
            var p = sec.AddParagraph("Creating a table");
            var table = sec.AddTable();
            table.Tag = "Original";
            table.AddColumn("2cm");
            table.AddColumn("2cm");
            var row = table.AddRow();
            row[0].AddParagraph("H1");
            row[1].AddParagraph("H2");
            row.HeadingFormat = true;
            row = table.AddRow();
            row[0].AddParagraph("C1");
            row[1].AddParagraph("C2");
            row = table.Rows[1].Clone();
            table.Rows.Add(row);
            row = table.Rows[2].Clone();
            table.Rows.Add(row);
            row = table.Rows[1].Clone();
            table.Rows.Add(row);
            sec.AddParagraph("End of table test");

            // Create a renderer for the MigraDoc document.
            var pdfRenderer = new PdfDocumentRenderer()
            {
                // Associate the MigraDoc document with a renderer.
                Document = document
            };

            // Layout and render document to PDF.
            pdfRenderer.RenderDocument();

            // Save the document…
            var filename = PdfFileUtility.GetTempPdfFullFileName("unittests/migradoc/tables/ClonedTable");
            pdfRenderer.PdfDocument.Save(filename);
            // … and start a viewer.
            PdfFileUtility.ShowDocumentIfDebugging(filename);
        }

        [Theory]
        [InlineData(BorderStyle.Emboss3D)]
        [InlineData(BorderStyle.Engrave3D)]
        public void Black_three_dimensional_borders_include_visible_highlights(BorderStyle style)
        {
            var document = new Document();
            var table = document.AddSection().AddTable();
            table.AddColumn("3cm");
            var cell = table.AddRow()[0];
            cell.Borders.Style = style;
            cell.Borders.Width = 3;
            cell.AddParagraph("Cell");

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var content = PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0);
            // Both grey rails and the original black rail must be present.
            content.Should().Contain(".4 .4 .4 RG");
            content.Should().Contain(".149 .149 .149 RG");
            content.Should().Contain("0 0 0 RG");
        }
    }
}
