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
    public class ParagraphRenderLineCountTests : IDisposable
    {
        public ParagraphRenderLineCountTests()
        {
            PdfSharpCore.ResetAll();
#if CORE
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
        }

        public void Dispose() => PdfSharpCore.ResetAll();

        [Fact]
        public void LineCountReportsFormattedLinesInParagraphFragment()
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            document.Styles.Normal.Font.Size = 12;
            var section = document.AddSection();
            section.PageSetup.PageWidth = Unit.FromPoint(135);
            section.PageSetup.LeftMargin = Unit.FromPoint(20);
            section.PageSetup.RightMargin = Unit.FromPoint(20);
            var paragraph = section.AddParagraph("Alpha Bravo Charlie Delta Epsilon Zeta Eta Theta");
            var renderer = new PdfDocumentRenderer { Document = document };

            renderer.RenderDocument();

            var info = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!
                .OfType<ParagraphRenderInfo>()
                .Single(candidate => ReferenceEquals(candidate.DocumentObject, paragraph));
            info.LineCount.Should().BeGreaterThan(1);
            var enumerator = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0);
            var baselines = new HashSet<double>();
            while (enumerator.Text.MoveAndGetNext(true, out var text))
                baselines.Add(Math.Round(text!.Y, 2));
            info.LineCount.Should().Be(baselines.Count);
        }
    }
}
