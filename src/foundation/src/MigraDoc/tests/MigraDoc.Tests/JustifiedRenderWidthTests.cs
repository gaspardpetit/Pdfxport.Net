// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;
using FluentAssertions;

namespace MigraDoc.Tests
{
    [Collection("PDFsharp")]
    public class JustifiedRenderWidthTests : IDisposable
    {
        public JustifiedRenderWidthTests()
        {
            PdfSharpCore.ResetAll();
#if CORE
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
        }

        public void Dispose() => PdfSharpCore.ResetAll();

        [Fact]
        public void JustifiedRenderWordWidthAdjustmentPreservesLineBreaks()
        {
            (int Pages, (string Text, double X, double Y)[] Words) Render(double? adjustment,
                bool justify = true)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                document.Styles.Normal.Font.Size = 12;
                var section = document.AddSection();
                section.PageSetup.PageWidth = Unit.FromPoint(180);
                section.PageSetup.LeftMargin = Unit.FromPoint(20);
                section.PageSetup.RightMargin = Unit.FromPoint(20);
                var paragraph = section.AddParagraph("Alpha Bravo Charlie Delta Epsilon Zeta Eta Theta");
                paragraph.Format.Alignment = justify ? ParagraphAlignment.Justify : ParagraphAlignment.Left;
                var renderer = new PdfDocumentRenderer { Document = document };
                if (adjustment != null)
                    renderer.DocumentRenderer.JustifiedRenderWordWidthAdjustment = (_, word, _) =>
                        word == "Alpha" ? adjustment : null;
                renderer.RenderDocument();
                var enumerator = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0);
                var words = new List<(string Text, double X, double Y)>();
                while (enumerator.Text.MoveAndGetNext(true, out var text))
                    words.Add((text!.Text, text.X, text.Y));
                return (renderer.PdfDocument.PageCount, words.ToArray());
            }

            var normal = Render(null);
            var adjusted = Render(-4);
            adjusted.Pages.Should().Be(normal.Pages);
            adjusted.Words.Select(word => (word.Text, word.Y)).Should()
                .Equal(normal.Words.Select(word => (word.Text, word.Y)));
            adjusted.Words.First(word => word.Text == "Bravo").X.Should()
                .BeGreaterThan(normal.Words.First(word => word.Text == "Bravo").X);
            Render(-4, false).Words.Should().Equal(Render(null, false).Words);
            Render(double.NaN).Words.Should().Equal(normal.Words);
            Render(double.PositiveInfinity).Words.Should().Equal(normal.Words);
        }
    }
}
