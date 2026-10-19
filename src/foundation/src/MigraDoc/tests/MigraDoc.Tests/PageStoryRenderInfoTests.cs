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
    public class PageStoryRenderInfoTests : IDisposable
    {
        public PageStoryRenderInfoTests()
        {
            PdfSharpCore.ResetAll();
#if CORE
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
        }

        public void Dispose() => PdfSharpCore.ResetAll();

        [Fact]
        public void PageStoryAccessorsReturnTheSelectedHeaderAndFooter()
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var section = document.AddSection();
            var header = section.Headers.Primary.AddParagraph("Header marker");
            var footer = section.Footers.Primary.AddParagraph("Footer marker");
            var body = section.AddParagraph("Body marker");
            var renderer = new PdfDocumentRenderer { Document = document };

            renderer.RenderDocument();

            renderer.DocumentRenderer.GetHeaderRenderInfos(1)
                .Select(info => info.DocumentObject).Should().ContainSingle()
                .Which.Should().BeSameAs(header);
            renderer.DocumentRenderer.GetFooterRenderInfos(1)
                .Select(info => info.DocumentObject).Should().ContainSingle()
                .Which.Should().BeSameAs(footer);
            renderer.DocumentRenderer.GetRenderInfoFromPage(1)!
                .Select(info => info.DocumentObject).Should().Contain(body);
        }

        [Fact]
        public void PageStoryAccessorsReturnEmptyArraysForAbsentStories()
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            document.AddSection().AddParagraph("Body");
            var renderer = new PdfDocumentRenderer { Document = document };

            renderer.RenderDocument();

            renderer.DocumentRenderer.GetHeaderRenderInfos(1).Should().BeEmpty();
            renderer.DocumentRenderer.GetFooterRenderInfos(1).Should().BeEmpty();
        }
    }
}
