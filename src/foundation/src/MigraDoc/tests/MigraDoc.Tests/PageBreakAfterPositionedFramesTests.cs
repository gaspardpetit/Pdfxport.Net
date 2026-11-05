// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests
{
    [Collection("PDFsharp")]
    public class PageBreakAfterPositionedFramesTests : IDisposable
    {
        public PageBreakAfterPositionedFramesTests()
        {
            PdfSharpCore.ResetAll();
#if CORE
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
        }

        public void Dispose() => PdfSharpCore.ResetAll();

        [Theory]
        [InlineData(RelativeVertical.Margin, 0, false, 1)]
        [InlineData(RelativeVertical.Margin, 1, false, 1)]
        [InlineData(RelativeVertical.Margin, 2, false, 1)]
        [InlineData(RelativeVertical.Page, 1, false, 1)]
        [InlineData(RelativeVertical.Page, 2, false, 1)]
        [InlineData(RelativeVertical.Margin, 0, true, 2)]
        [InlineData(RelativeVertical.Margin, 2, true, 2)]
        [InlineData(RelativeVertical.Page, 2, true, 2)]
        [InlineData(RelativeVertical.Paragraph, 1, false, 2)]
        public void PageBreakBeforeDependsOnPrecedingFlowContent(RelativeVertical relativeVertical,
            int frameCount, bool precedingParagraph, int expectedPages)
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var section = document.AddSection();
            var body = precedingParagraph ? section.AddParagraph("Earlier body content") : null;
            var frames = new List<TextFrame>();
            for (var index = 0; index < frameCount; index++)
            {
                var frame = section.AddTextFrame();
                frame.RelativeHorizontal = RelativeHorizontal.Margin;
                frame.RelativeVertical = relativeVertical;
                frame.Left = Unit.FromPoint(index * 36);
                frame.Top = Unit.FromPoint(0);
                frame.Width = Unit.FromPoint(24);
                frame.Height = Unit.FromPoint(18);
                frame.AddParagraph(((char)('A' + index)).ToString());
                frames.Add(frame);
            }
            var heading = section.AddParagraph("Body heading");
            heading.Format.PageBreakBefore = true;
            var renderer = new PdfDocumentRenderer { Document = document };

            renderer.RenderDocument();

            renderer.PageCount.Should().Be(expectedPages);
            var firstPage = renderer.DocumentRenderer.GetRenderInfoFromPage(1);
            foreach (var frame in frames)
                firstPage.Should().Contain(info => ReferenceEquals(info.DocumentObject, frame));
            if (body != null)
                firstPage.Should().Contain(info => ReferenceEquals(info.DocumentObject, body));
            renderer.DocumentRenderer.GetRenderInfoFromPage(expectedPages)
                .Should().Contain(info => ReferenceEquals(info.DocumentObject, heading));
        }

        [Theory]
        [InlineData(RelativeVertical.Margin)]
        [InlineData(RelativeVertical.Page)]
        public void ExplicitPageBreakStillAdvancesAfterPositionedFrame(RelativeVertical relativeVertical)
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var section = document.AddSection();
            var frame = section.AddTextFrame();
            frame.RelativeVertical = relativeVertical;
            frame.Top = Unit.FromPoint(0);
            frame.Width = Unit.FromPoint(24);
            frame.Height = Unit.FromPoint(18);
            frame.AddParagraph("A");
            section.AddPageBreak();
            var heading = section.AddParagraph("Next page heading");
            var renderer = new PdfDocumentRenderer { Document = document };

            renderer.RenderDocument();

            renderer.PageCount.Should().Be(2);
            renderer.DocumentRenderer.GetRenderInfoFromPage(1)
                .Should().Contain(info => ReferenceEquals(info.DocumentObject, frame));
            renderer.DocumentRenderer.GetRenderInfoFromPage(2)
                .Should().Contain(info => ReferenceEquals(info.DocumentObject, heading));
        }
    }
}
