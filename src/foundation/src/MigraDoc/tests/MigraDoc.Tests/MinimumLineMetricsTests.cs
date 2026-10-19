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
    public class MinimumLineMetricsTests : IDisposable
    {
        public MinimumLineMetricsTests()
        {
            PdfSharpCore.ResetAll();
#if CORE
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
        }

        public void Dispose() => PdfSharpCore.ResetAll();

        [Fact]
        public void SelectedFontChangesItsLineAndFollowingContent()
        {
            (double Before, double Mixed, double After) Render(bool useMinimum)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                document.Styles.Normal.Font.Size = 12;
                var section = document.AddSection();
                section.AddParagraph("Before");
                var mixed = section.AddParagraph();
                mixed.AddText("Mixed ");
                mixed.AddFormattedText("mark", new Font { Name = "Times New Roman", Size = 12 });
                section.AddParagraph("After");
                var renderer = new PdfDocumentRenderer { Document = document };
                if (useMinimum)
                    renderer.DocumentRenderer.MinimumLineMetricsForFont = (paragraph, font) =>
                        ReferenceEquals(paragraph, mixed) && font.FontFamily.Name == "Times New Roman"
                            ? (30, 25) : null;
                renderer.RenderDocument();
                var positions = new Dictionary<string, double>();
                var stream = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0);
                while (stream.Text.MoveAndGetNext(true, out var text))
                    if (text!.Text is "Before" or "Mixed" or "After")
                        positions[text.Text] = text.Y;
                return (positions["Before"], positions["Mixed"], positions["After"]);
            }

            var baseline = Render(false);
            var adjusted = Render(true);
            adjusted.Before.Should().Be(baseline.Before);
            adjusted.Mixed.Should().BeLessThan(baseline.Mixed - 1);
            adjusted.After.Should().BeLessThan(baseline.After - 1);
        }

        [Fact]
        public void NullSelectorPreservesDefaultContent()
        {
            string Render(bool useSelector)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                var paragraph = document.AddSection().AddParagraph("Unchanged text");
                var renderer = new PdfDocumentRenderer { Document = document };
                if (useSelector)
                    renderer.DocumentRenderer.MinimumLineMetricsForFont = (candidate, _) =>
                        ReferenceEquals(candidate, paragraph) ? null : (30, 25);
                renderer.RenderDocument();
                return PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0);
            }

            Render(true).Should().Be(Render(false));
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void InvalidMetricsPreserveDefaultContent(double invalidHeight)
        {
            string Render(bool useSelector)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                document.AddSection().AddParagraph("Unchanged text");
                var renderer = new PdfDocumentRenderer { Document = document };
                if (useSelector)
                    renderer.DocumentRenderer.MinimumLineMetricsForFont = (_, _) => (invalidHeight, 25);
                renderer.RenderDocument();
                return PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0);
            }

            Render(true).Should().Be(Render(false));
        }
    }
}
