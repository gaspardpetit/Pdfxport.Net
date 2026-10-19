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
    public class ParagraphPaintBaselineOffsetTests : IDisposable
    {
        public ParagraphPaintBaselineOffsetTests()
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

        [Fact]
        public void SelectedParagraphMovesOnlyItsPaintedBaseline()
        {
            var original = Render(null);
            var shifted = Render(0.07);

            shifted.PageCount.Should().Be(original.PageCount);
            shifted.Baselines["Before"].Should().BeApproximately(original.Baselines["Before"], 0.001);
            (shifted.Baselines["Selected"] - original.Baselines["Selected"])
                .Should().BeApproximately(-0.07, 0.001);
            shifted.Baselines["After"].Should().BeApproximately(original.Baselines["After"], 0.001);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void NonFiniteOffsetPreservesDefaultBaselines(double offset)
        {
            Render(offset).Baselines.Should().Equal(Render(null).Baselines);
        }

        [Fact]
        public void NonMatchingLineFontPreservesDefaultBaselines()
        {
            Render(0.07, "Times New Roman").Baselines.Should()
                .Equal(Render(null, "Times New Roman").Baselines);
        }

        [Fact]
        public void PaintOffsetAddsToExactParagraphOffset()
        {
            double Baseline(bool addPaintOffset)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                var paragraph = document.AddSection().AddParagraph("Exact text");
                paragraph.Format.LineSpacingRule = LineSpacingRule.Exactly;
                paragraph.Format.LineSpacing = Unit.FromPoint(20);
                var renderer = new PdfDocumentRenderer { Document = document };
                renderer.DocumentRenderer.ExactParagraphBaselineOffset = (_, _) => 1;
                if (addPaintOffset)
                    renderer.DocumentRenderer.ParagraphPaintBaselineOffset = (_, _) => 0.07;
                renderer.RenderDocument();
                var text = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0).Text;
                text.MoveAndGetNext(true, out var info).Should().BeTrue();
                return info!.Y;
            }

            (Baseline(true) - Baseline(false)).Should().BeApproximately(-0.07, 0.001);
        }

        private static (int PageCount, Dictionary<string, double> Baselines) Render(double? offset,
            string? selectedFontName = null)
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            document.Styles.Normal.Font.Size = 12;
            var section = document.AddSection();
            section.AddParagraph("Before");
            var selectedParagraph = section.AddParagraph("Selected");
            if (selectedFontName != null)
                selectedParagraph.Format.Font.Name = selectedFontName;
            section.AddParagraph("After");

            var renderer = new PdfDocumentRenderer { Document = document };
            if (offset is { } value)
                renderer.DocumentRenderer.ParagraphPaintBaselineOffset = (paragraph, fonts) =>
                    ReferenceEquals(paragraph, selectedParagraph) && fonts.Count > 0 &&
                    fonts.All(font => font.FontFamily.Name == "Arial") ? value : null;
            renderer.RenderDocument();

            var stream = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0);
            var baselines = new Dictionary<string, double>();
            while (stream.Text.MoveAndGetNext(true, out var renderedText))
                baselines.Add(renderedText!.Text, renderedText.Y);
            baselines.Should().HaveCount(3);
            return (renderer.PdfDocument.PageCount, baselines);
        }
    }
}
