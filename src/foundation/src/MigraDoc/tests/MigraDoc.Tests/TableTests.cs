// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

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
