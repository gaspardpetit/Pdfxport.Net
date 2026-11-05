// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests
{
    [Collection("PDFsharp")]
    public class WidowControlTests : IDisposable
    {
        public WidowControlTests()
        {
            PdfSharpCore.ResetAll();
#if CORE
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
        }

        public void Dispose() => PdfSharpCore.ResetAll();

        [Theory]
        [InlineData(45)]
        [InlineData(50)]
        [InlineData(59)]
        public void ParagraphTallerThanPageStillMakesProgress(double bodyHeight)
        {
            var document = CreateReviewDocument(bodyHeight);
            var paragraph = document.LastSection.AddParagraph("Line1\nLine2\nLine3");
            var renderer = new PdfDocumentRenderer { Document = document };
            var iterations = 0;
            renderer.DocumentRenderer.PrepareDocumentProgress += (_, _) =>
            {
                // Bound the regression: do not let a pagination loop hang the suite.
                if (++iterations > 20)
                    throw new InvalidOperationException("Pagination did not make progress.");
            };
            renderer.RenderDocument();
            renderer.PageCount.Should().Be(2);
            for (var page = 1; page <= 2; page++)
                renderer.DocumentRenderer.GetRenderInfoFromPage(page)!
                    .OfType<ParagraphRenderInfo>()
                    .Single(info => ReferenceEquals(info.DocumentObject, paragraph))
                    .LineCount.Should().Be(page);
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void ClearedOpeningFragmentHasNoHeightOrDecoration(bool shading, bool border)
        {
            PdfDocumentRenderer Render(bool decorate)
            {
                var document = CreateReviewDocument(100);
                document.LastSection.AddParagraph("Before").Format.SpaceAfter = Unit.FromPoint(30);
                var paragraph = document.LastSection.AddParagraph("Line1\nLine2\nLine3");
                if (decorate && shading)
                    paragraph.Format.Shading.Color = Colors.Yellow;
                if (decorate && border)
                    paragraph.Format.Borders.Width = Unit.FromPoint(1);
                var renderer = new PdfDocumentRenderer { Document = document };
                renderer.RenderDocument();
                return renderer;
            }

            var plain = Render(false);
            var decorated = Render(true);
            decorated.PageCount.Should().Be(2);
            var empty = decorated.DocumentRenderer.GetRenderInfoFromPage(1)!
                .OfType<ParagraphRenderInfo>().Where(info => info.LineCount == 0).ToArray();
            foreach (var info in empty)
                info.LayoutInfo.ContentArea.Height.Point.Should().Be(0);
            PdfFileHelper.GetPageContentStream(decorated.PdfDocument, 0)
                .Should().Be(PdfFileHelper.GetPageContentStream(plain.PdfDocument, 0));
            PdfFileHelper.GetPageContentStream(decorated.PdfDocument, 1)
                .Should().NotBe(PdfFileHelper.GetPageContentStream(plain.PdfDocument, 1));
        }

        static Document CreateReviewDocument(double bodyHeight)
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            document.Styles.Normal.Font.Size = 10;
            document.Styles.Normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Exactly;
            document.Styles.Normal.ParagraphFormat.LineSpacing = Unit.FromPoint(20);
            document.Styles.Normal.ParagraphFormat.WidowControl = true;
            var section = document.AddSection();
            section.PageSetup.PageWidth = Unit.FromPoint(240);
            section.PageSetup.PageHeight = Unit.FromPoint(bodyHeight + 40);
            section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromPoint(20);
            section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(20);
            return document;
        }

        [Theory]
        [InlineData(3, 2, true, false, 0, 3)]
        [InlineData(4, 3, true, false, 2, 2)]
        [InlineData(3, 2, false, false, 2, 1)]
        [InlineData(3, 3, true, true, 0, 3)]
        [InlineData(4, 4, true, true, 2, 2)]
        [InlineData(3, 3, false, true, 2, 1)]
        [InlineData(3, 3, true, false, 3, 0)]
        [InlineData(8, 2, true, false, 2, 4, 2)]
        [InlineData(3, 1, true, false, 0, 3)]
        public void MovingEndingPreservesOpeningLines(int lineCount, int availableLines,
            bool widowControl, bool keepWithNext, params int[] expectedLines)
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            document.Styles.Normal.Font.Size = 10;
            document.Styles.Normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Exactly;
            document.Styles.Normal.ParagraphFormat.LineSpacing = Unit.FromPoint(20);
            document.Styles.Normal.ParagraphFormat.SpaceBefore = 0;
            document.Styles.Normal.ParagraphFormat.SpaceAfter = 0;
            var section = document.AddSection();
            section.PageSetup.PageWidth = Unit.FromPoint(240);
            section.PageSetup.PageHeight = Unit.FromPoint(140);
            section.PageSetup.TopMargin = Unit.FromPoint(20);
            section.PageSetup.BottomMargin = Unit.FromPoint(20);
            section.PageSetup.LeftMargin = Unit.FromPoint(20);
            section.PageSetup.RightMargin = Unit.FromPoint(20);
            var before = section.AddParagraph("Before");
            // The body holds five 20-point lines. Leave exactly availableLines
            // after the preceding paragraph and its bottom spacing.
            before.Format.SpaceAfter = Unit.FromPoint(80 - availableLines * 20);
            var paragraph = section.AddParagraph();
            paragraph.Format.WidowControl = widowControl;
            paragraph.Format.KeepWithNext = keepWithNext;
            for (var line = 1; line <= lineCount; line++)
            {
                if (line > 1)
                    paragraph.AddLineBreak();
                paragraph.AddText("Line" + line);
            }
            var after = section.AddParagraph("After");
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();

            renderer.PageCount.Should().Be(expectedLines.Length);
            int LinesOnPage(int page) => renderer.DocumentRenderer.GetRenderInfoFromPage(page)!
                .OfType<ParagraphRenderInfo>()
                .Where(info => ReferenceEquals(info.DocumentObject, paragraph))
                .Sum(info => info.LineCount);
            var nextLine = 1;
            for (var page = 1; page <= renderer.PageCount; page++)
            {
                LinesOnPage(page).Should().Be(expectedLines[page - 1]);
                var stream = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, page - 1);
                var paintedLines = new List<string>();
                while (stream.Text.MoveAndGetNext(true, out var text))
                {
                    if (text!.Text.StartsWith("Line", StringComparison.Ordinal))
                        paintedLines.Add(text.Text);
                }
                paintedLines.Should().Equal(Enumerable.Range(nextLine, expectedLines[page - 1])
                    .Select(line => "Line" + line));
                nextLine += expectedLines[page - 1];
            }
            nextLine.Should().Be(lineCount + 1);
            renderer.DocumentRenderer.GetRenderInfoFromPage(renderer.PageCount)!
                .Should().Contain(info => ReferenceEquals(info.DocumentObject, after));
        }
    }
}
