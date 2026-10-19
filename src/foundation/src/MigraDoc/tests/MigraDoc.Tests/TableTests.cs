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
            var strokes = BorderStrokes(PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0))
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
            var content = string.Concat(renderer.PdfDocument.Pages[0].Contents.Elements
                .OfType<PdfSharp.Pdf.Advanced.PdfReference>()
                .Select(reference => reference.Value)
                .OfType<PdfSharp.Pdf.Advanced.PdfContent>()
                .Where(stream => stream.Stream != null)
                .Select(stream => Encoding.GetEncoding("ISO-8859-1").GetString(stream.Stream!.UnfilteredValue)));
            var vertical = Regex.Matches(content,
                    @"(?m)(?<x1>[\d.]+) (?<y1>[\d.]+) m\s+(?<x2>[\d.]+) (?<y2>[\d.]+) l\s+S")
                .Cast<Match>()
                .Where(stroke => stroke.Groups["x1"].Value == stroke.Groups["x2"].Value)
                .Select(stroke => (
                    X: double.Parse(stroke.Groups["x1"].Value, CultureInfo.InvariantCulture),
                    Top: Math.Min(double.Parse(stroke.Groups["y1"].Value, CultureInfo.InvariantCulture),
                        double.Parse(stroke.Groups["y2"].Value, CultureInfo.InvariantCulture)),
                    Bottom: Math.Max(double.Parse(stroke.Groups["y1"].Value, CultureInfo.InvariantCulture),
                        double.Parse(stroke.Groups["y2"].Value, CultureInfo.InvariantCulture))))
                .ToArray();
            var middle = (vertical.Min(stroke => stroke.X) + vertical.Max(stroke => stroke.X)) / 2;
            var internalEdges = vertical.Where(stroke => Math.Abs(stroke.X - middle) < 2).ToArray();
            internalEdges.Should().NotBeEmpty();
            internalEdges.Should().OnlyContain(stroke => stroke.Bottom <
                vertical.Max(edge => edge.Bottom) - 1);
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
    }
}
