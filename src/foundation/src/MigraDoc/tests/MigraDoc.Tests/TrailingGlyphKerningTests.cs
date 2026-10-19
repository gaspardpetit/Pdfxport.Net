// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using MigraDoc.DocumentObjectModel;
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
    public class TrailingGlyphKerningTests : IDisposable
    {
        public TrailingGlyphKerningTests()
        {
            PdfSharpCore.ResetAll();
#if CORE
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
        }

        public void Dispose() => PdfSharpCore.ResetAll();

        [Fact]
        public void TrailingGlyphKerningPositionsTheGlyphAndChangesLineFit()
        {
            using var measure = XGraphics.CreateMeasureContext(new XSize(200, 200),
                XGraphicsUnit.Point, XPageDirection.Downwards);
            var font = new XFont("Arial", 12);
            const string closingWord = "authors.\u2019";
            var contentWidth = measure.MeasureString("of", font).Width +
                measure.MeasureString(" ", font).Width +
                measure.MeasureString(closingWord, font).Width - 0.7;

            (double PrefixY, double WordY, double PrefixX, double MarkX) Render(bool useKerning)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                document.Styles.Normal.Font.Size = 12;
                var section = document.AddSection();
                section.PageSetup.PageWidth = Unit.FromPoint(contentWidth + 40);
                section.PageSetup.LeftMargin = Unit.FromPoint(20);
                section.PageSetup.RightMargin = Unit.FromPoint(20);
                section.AddParagraph("of " + closingWord);
                var renderer = new PdfDocumentRenderer { Document = document };
                if (useKerning)
                    renderer.DocumentRenderer.TrailingGlyphKerning = (_, word, _) =>
                        word == closingWord ? -1.4 : null;
                renderer.RenderDocument();
                var enumerator = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0);
                var words = new List<(string Text, double X, double Y)>();
                while (enumerator.Text.MoveAndGetNext(true, out var text))
                    words.Add((text!.Text, text.X, text.Y));
                var first = words.Single(word => word.Text == "of");
                var prefix = words.Single(word => word.Text.StartsWith("authors.", StringComparison.Ordinal));
                var mark = useKerning
                    ? words.Last(word => word.Y == prefix.Y && word.X > prefix.X)
                    : default;
                return (first.Y, prefix.Y, prefix.X, mark.X);
            }

            var normal = Render(false);
            var positioned = Render(true);
            normal.WordY.Should().NotBe(normal.PrefixY);
            positioned.WordY.Should().Be(positioned.PrefixY);
            positioned.MarkX.Should().BeApproximately(positioned.PrefixX +
                measure.MeasureString("authors.", font).Width - 1.4, 0.1);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void NonFiniteTrailingGlyphKerningPreservesDefaultContent(double adjustment)
        {
            string Render(double? value)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                document.AddSection().AddParagraph("of authors.\u2019 next");
                var renderer = new PdfDocumentRenderer { Document = document };
                if (value != null)
                    renderer.DocumentRenderer.TrailingGlyphKerning = (_, _, _) => value;
                renderer.RenderDocument();
                return PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0);
            }

            Render(adjustment).Should().Be(Render(null));
        }

        [Fact]
        public void RenderOnlyTrailingGlyphKerningMovesTheGlyphWithoutChangingLineFit()
        {
            using var measure = XGraphics.CreateMeasureContext(new XSize(200, 200),
                XGraphicsUnit.Point, XPageDirection.Downwards);
            var font = new XFont("Arial", 12);
            const string closingWord = "authors.\u2019";
            var contentWidth = measure.MeasureString("of " + closingWord, font).Width - 0.7;

            (double FirstY, double WordY, double MarkX, double PrefixX) Render(bool adjusted)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                document.Styles.Normal.Font.Size = 12;
                var section = document.AddSection();
                section.PageSetup.PageWidth = Unit.FromPoint(contentWidth + 40);
                section.PageSetup.LeftMargin = Unit.FromPoint(20);
                section.PageSetup.RightMargin = Unit.FromPoint(20);
                section.AddParagraph("of " + closingWord);
                var renderer = new PdfDocumentRenderer { Document = document };
                if (adjusted)
                    renderer.DocumentRenderer.RenderOnlyTrailingGlyphKerning = (_, word, _) =>
                        word == closingWord ? -1.4 : null;
                renderer.RenderDocument();
                var enumerator = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0);
                var words = new List<(string Text, double X, double Y)>();
                while (enumerator.Text.MoveAndGetNext(true, out var text))
                    words.Add((text!.Text, text.X, text.Y));
                var first = words.Single(word => word.Text == "of");
                var prefix = words.Single(word => word.Text.StartsWith("authors.", StringComparison.Ordinal));
                var mark = adjusted ? words.Last(word => word.Y == prefix.Y && word.X > prefix.X) : default;
                return (first.Y, prefix.Y, mark.X, prefix.X);
            }

            var normal = Render(false);
            var positioned = Render(true);
            normal.WordY.Should().NotBe(normal.FirstY);
            positioned.WordY.Should().Be(normal.WordY);
            positioned.MarkX.Should().BeApproximately(positioned.PrefixX +
                measure.MeasureString("authors.", font).Width - 1.4, 0.1);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void NonFiniteRenderOnlyTrailingGlyphKerningPreservesDefaultContent(double adjustment)
        {
            string Render(double? value)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                document.AddSection().AddParagraph("of authors.\u2019 next");
                var renderer = new PdfDocumentRenderer { Document = document };
                if (value != null)
                    renderer.DocumentRenderer.RenderOnlyTrailingGlyphKerning = (_, _, _) => value;
                renderer.RenderDocument();
                return PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0);
            }

            Render(adjustment).Should().Be(Render(null));
        }
    }
}
