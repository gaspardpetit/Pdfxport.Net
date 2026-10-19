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
    public class TopBorderDistanceTests : IDisposable
    {
        public TopBorderDistanceTests()
        {
            PdfSharpCore.ResetAll();
#if CORE
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
        }

        public void Dispose() => PdfSharpCore.ResetAll();

        [Fact]
        public void ReplacementMovesFirstLineWithoutChangingBorderStroke()
        {
            (double Y, int Calls, string Content) Render(double? replacement)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                var paragraph = document.AddSection().AddParagraph("Bordered text");
                paragraph.Format.Borders.Top.Width = 0.5;
                paragraph.Format.Borders.DistanceFromTop = 1;
                var calls = 0;
                var renderer = new PdfDocumentRenderer { Document = document };
                renderer.DocumentRenderer.TopBorderDistanceAtAreaStart = (candidate, page, _) =>
                {
                    candidate.Should().BeSameAs(paragraph);
                    page.Should().Be(1);
                    calls++;
                    return replacement;
                };
                renderer.RenderDocument();
                var text = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0).Text;
                text.MoveAndGetNext(true, out var info).Should().BeTrue();
                return (info!.Y, calls, PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0));
            }

            var original = Render(null);
            var replaced = Render(0);
            var invalid = Render(double.NaN);
            var negative = Render(-1);
            original.Calls.Should().BeGreaterThan(0);
            replaced.Calls.Should().BeGreaterThan(0);
            replaced.Y.Should().BeApproximately(original.Y + 1, 0.01);
            invalid.Y.Should().BeApproximately(original.Y, 0.01);
            negative.Y.Should().BeApproximately(original.Y, 0.01);
            original.Content.Should().Contain(".5 w");
            replaced.Content.Should().Contain(".5 w");
        }

        [Fact]
        public void CallbackReceivesPhysicalPageAfterExplicitBreak()
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var section = document.AddSection();
            var first = section.AddParagraph("First page");
            first.Format.Borders.Top.Width = 0.5;
            section.AddPageBreak();
            var second = section.AddParagraph("Second page");
            second.Format.Borders.Top.Width = 0.5;
            var pages = new Dictionary<Paragraph, int>();
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.DocumentRenderer.TopBorderDistanceAtAreaStart = (paragraph, page, _) =>
            {
                pages[paragraph] = page;
                return null;
            };
            renderer.RenderDocument();
            renderer.PdfDocument.PageCount.Should().Be(2);
            pages[first].Should().Be(1);
            pages[second].Should().Be(2);
        }
    }
}
