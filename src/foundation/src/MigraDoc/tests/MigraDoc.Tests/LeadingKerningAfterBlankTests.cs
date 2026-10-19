// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Fields;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;
using FluentAssertions;

namespace MigraDoc.Tests
{
    [Collection("PDFsharp")]
    public class LeadingKerningAfterBlankTests : IDisposable
    {
        public LeadingKerningAfterBlankTests()
        {
            PdfSharpCore.ResetAll();
#if CORE
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
        }

        public void Dispose() => PdfSharpCore.ResetAll();

        [Fact]
        public void LeadingKerningAfterBlankPositionsNextWord()
        {
            (double firstX, double secondX, int callbackCount) Render(double? adjustment)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                var paragraph = document.AddSection().AddParagraph();
                paragraph.AddText("Alpha ");
                paragraph.AddText("“beta");

                var callbackCount = 0;
                var renderer = new PdfDocumentRenderer { Document = document };
                renderer.DocumentRenderer = new DocumentRenderer(document)
                {
                    LeadingKerningAfterBlank = (candidate, word, _) =>
                    {
                        candidate.Should().BeSameAs(paragraph);
                        word.Should().Be("“beta");
                        callbackCount++;
                        return adjustment;
                    }
                };
                renderer.RenderDocument();
                var text = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0).Text;
                text.MoveAndGetNext(true, out var first).Should().BeTrue();
                text.MoveAndGetNext(true, out var second).Should().BeTrue();
                return (first!.X, second!.X, callbackCount);
            }

            var original = Render(null);
            var kerned = Render(-1);
            var invalid = Render(double.NaN);

            original.callbackCount.Should().BeGreaterThan(0);
            kerned.firstX.Should().BeApproximately(original.firstX, 0.01);
            kerned.secondX.Should().BeApproximately(original.secondX - 1, 0.01);
            invalid.secondX.Should().BeApproximately(original.secondX, 0.01);
        }

        [Fact]
        public void LeadingKerningAfterBlankIsNeutralWithoutFiniteAdjustment()
        {
            string Render(Func<Paragraph, string, XFont, double?>? adjustment)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                var paragraph = document.AddSection().AddParagraph();
                paragraph.AddText("Alpha ");
                paragraph.AddText("“beta");

                var renderer = new PdfDocumentRenderer { Document = document };
                if (adjustment != null)
                {
                    renderer.DocumentRenderer = new DocumentRenderer(document)
                    {
                        LeadingKerningAfterBlank = adjustment
                    };
                }
                renderer.RenderDocument();
                return string.Concat(renderer.PdfDocument.Pages[0].Contents.Elements
                    .OfType<PdfSharp.Pdf.Advanced.PdfReference>()
                    .Select(reference => reference.Value)
                    .OfType<PdfSharp.Pdf.Advanced.PdfContent>()
                    .Where(stream => stream.Stream != null)
                    .Select(stream => Encoding.GetEncoding(28591).GetString(stream.Stream!.UnfilteredValue)));
            }

            var original = Render(null);
            Render((_, _, _) => null).Should().Be(original);
            Render((_, _, _) => double.NaN).Should().Be(original);
            Render((_, _, _) => double.PositiveInfinity).Should().Be(original);
        }

        [Fact]
        public void LeadingKerningAfterBlankDoesNotMoveWordWrappedToNextLine()
        {
            (double x, double y) Render(double? adjustment)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                var section = document.AddSection();
                section.PageSetup.PageWidth = Unit.FromPoint(80);
                section.PageSetup.LeftMargin = Unit.FromPoint(20);
                section.PageSetup.RightMargin = Unit.FromPoint(20);
                var paragraph = section.AddParagraph();
                paragraph.AddText("Alpha ");
                paragraph.AddText("“beta");

                var renderer = new PdfDocumentRenderer { Document = document };
                renderer.DocumentRenderer = new DocumentRenderer(document)
                {
                    LeadingKerningAfterBlank = (_, _, _) => adjustment
                };
                renderer.RenderDocument();
                var text = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0).Text;
                text.MoveAndGetNext(true, out var first).Should().BeTrue();
                text.MoveAndGetNext(true, out var second).Should().BeTrue();
                second!.Y.Should().NotBe(first!.Y);
                return (second.X, second.Y);
            }

            var original = Render(null);
            var kerned = Render(-1);
            kerned.x.Should().BeApproximately(original.x, 0.01);
            kerned.y.Should().BeApproximately(original.y, 0.01);
        }

        [Fact]
        public void LeadingKerningAfterBlankCanFitWordOnCurrentLine()
        {
            (double firstY, double secondY) Render(double? adjustment)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                var section = document.AddSection();
                section.PageSetup.PageWidth = Unit.FromPoint(85);
                section.PageSetup.LeftMargin = Unit.FromPoint(20);
                section.PageSetup.RightMargin = Unit.FromPoint(20);
                var paragraph = section.AddParagraph();
                paragraph.AddText("Alpha ");
                paragraph.AddText("“beta");

                var renderer = new PdfDocumentRenderer { Document = document };
                renderer.DocumentRenderer = new DocumentRenderer(document)
                {
                    LeadingKerningAfterBlank = (_, _, _) => adjustment
                };
                renderer.RenderDocument();
                var text = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0).Text;
                text.MoveAndGetNext(true, out var first).Should().BeTrue();
                text.MoveAndGetNext(true, out var second).Should().BeTrue();
                return (first!.Y, second!.Y);
            }

            var original = Render(null);
            var kerned = Render(-10);
            original.secondY.Should().NotBe(original.firstY);
            kerned.secondY.Should().BeApproximately(kerned.firstY, 0.01);
        }

        [Fact]
        public void LeadingKerningAfterBlankKeepsUnderlineContinuous()
        {
            (double start, double end) Render(double? adjustment)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                var paragraph = document.AddSection().AddParagraph();
                paragraph.Format.Font.Underline = Underline.Single;
                paragraph.AddText("Alpha ");
                paragraph.AddText("“beta");

                var renderer = new PdfDocumentRenderer { Document = document };
                renderer.DocumentRenderer = new DocumentRenderer(document)
                {
                    LeadingKerningAfterBlank = (_, _, _) => adjustment
                };
                renderer.RenderDocument();
                var content = string.Concat(renderer.PdfDocument.Pages[0].Contents.Elements
                    .OfType<PdfSharp.Pdf.Advanced.PdfReference>()
                    .Select(reference => reference.Value)
                    .OfType<PdfSharp.Pdf.Advanced.PdfContent>()
                    .Where(stream => stream.Stream != null)
                    .Select(stream => Encoding.GetEncoding(28591).GetString(stream.Stream!.UnfilteredValue)));
                var underlines = Regex.Matches(content,
                    @"(?m)(?<x1>[\d.]+) (?<y1>[\d.]+) m\s+(?<x2>[\d.]+) (?<y2>[\d.]+) l\s+S")
                    .Cast<Match>()
                    .Where(match => match.Groups["y1"].Value == match.Groups["y2"].Value &&
                        match.Groups["x1"].Value != match.Groups["x2"].Value).ToArray();
                underlines.Should().ContainSingle();
                return (double.Parse(underlines[0].Groups["x1"].Value, CultureInfo.InvariantCulture),
                    double.Parse(underlines[0].Groups["x2"].Value, CultureInfo.InvariantCulture));
            }

            var original = Render(null);
            var kerned = Render(-1);
            kerned.start.Should().BeApproximately(original.start, 0.01);
            kerned.end.Should().BeApproximately(original.end - 1, 0.01);
        }

        [Fact]
        public void LeadingKerningAfterBlankMovesHyperlinkRectangleWithWord()
        {
            (double left, double right) Render(double? adjustment)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                var paragraph = document.AddSection().AddParagraph();
                paragraph.AddText("Alpha ");
                paragraph.AddHyperlink("https://example.org", HyperlinkType.Url).AddText("“beta");

                var renderer = new PdfDocumentRenderer { Document = document };
                renderer.DocumentRenderer = new DocumentRenderer(document)
                {
                    LeadingKerningAfterBlank = (_, _, _) => adjustment
                };
                renderer.RenderDocument();
                renderer.PdfDocument.Pages[0].Annotations.Count.Should().Be(1);
                var rectangle = renderer.PdfDocument.Pages[0].Annotations[0].Rectangle;
                return (rectangle.X1, rectangle.X2);
            }

            var original = Render(null);
            var kerned = Render(-1);
            kerned.left.Should().BeApproximately(original.left - 1, 0.01);
            kerned.right.Should().BeApproximately(original.right - 1, 0.01);
        }

    }
}
