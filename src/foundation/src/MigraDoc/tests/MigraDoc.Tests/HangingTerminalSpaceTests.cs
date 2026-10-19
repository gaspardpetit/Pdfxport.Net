using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests
{
    [Collection("PDFsharp")]
    public class HangingTerminalSpaceTests : IDisposable
    {
        public HangingTerminalSpaceTests()
        {
            PdfSharpCore.ResetAll();
#if CORE
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
        }

        public void Dispose() => PdfSharpCore.ResetAll();

        [Fact]
        public void TerminalHangingSpaceDoesNotMoveTextWithoutBlank()
        {
            string Render(bool adjusted)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                document.AddSection().AddParagraph("AlphaBravo.").Format.Alignment = ParagraphAlignment.Left;
                var renderer = new PdfDocumentRenderer { Document = document };
                if (adjusted)
                    renderer.DocumentRenderer.HangingTerminalLineSpaceThreshold = (_, _, _) => 0.25;
                renderer.RenderDocument();
                return PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0);
            }

            Render(true).Should().Be(Render(false));
        }

        [Fact]
        public void TerminalHangingSpaceExpandsFinalBlankWithoutChangingLineFit()
        {
            using var measure = XGraphics.CreateMeasureContext(new XSize(200, 200),
                XGraphicsUnit.Point, XPageDirection.Downwards);
            var font = new XFont("Arial", 12);
            const string closingWord = "Bravo.";
            var contentWidth = measure.MeasureString("Alpha " + closingWord, font).Width - 4;

            (double FirstY, double SecondY, double SecondX) Render(bool adjusted)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                document.Styles.Normal.Font.Size = 12;
                var section = document.AddSection();
                section.PageSetup.PageWidth = Unit.FromPoint(contentWidth + 40);
                section.PageSetup.LeftMargin = Unit.FromPoint(20);
                section.PageSetup.RightMargin = Unit.FromPoint(20);
                var paragraph = section.AddParagraph();
                paragraph.Format.Alignment = ParagraphAlignment.Left;
                paragraph.AddText("Alpha ");
                paragraph.AddFormattedText(closingWord);
                var renderer = new PdfDocumentRenderer { Document = document };
                renderer.DocumentRenderer.LineEndOverhang = (_, word, _) =>
                    word == closingWord ? 8 : null;
                renderer.DocumentRenderer.AdditiveTerminalLineEndOverhang = true;
                if (adjusted)
                {
                    renderer.DocumentRenderer.RenderOnlyTrailingGlyphKerning = (_, word, _) =>
                        word == closingWord ? -1 : null;
                    renderer.DocumentRenderer.HangingTerminalLineSpaceThreshold = (_, word, _) =>
                        word == closingWord ? 0.25 : null;
                }
                renderer.RenderDocument();
                var positions = new List<(double X, double Y)>();
                var stream = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0);
                while (stream.Text.MoveAndGetNext(true, out var text))
                    positions.Add((text!.X, text.Y));
                return (positions[0].Y, positions[1].Y, positions[1].X);
            }

            var baseline = Render(false);
            var adjusted = Render(true);
            baseline.FirstY.Should().Be(baseline.SecondY);
            adjusted.FirstY.Should().Be(adjusted.SecondY);
            adjusted.SecondX.Should().BeGreaterThan(baseline.SecondX);
        }

        [Fact]
        public void NearEdgeToleranceDoesNotExpandUnrelatedClosingMark()
        {
            using var measure = XGraphics.CreateMeasureContext(new XSize(200, 200),
                XGraphicsUnit.Point, XPageDirection.Downwards);
            var font = new XFont("Arial", 18);
            var contentWidth = measure.MeasureString("of authors.\u201d", font).Width - 0.05;

            double[] Render(bool nearEdge)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                document.Styles.Normal.Font.Size = 18;
                var section = document.AddSection();
                section.PageSetup.PageWidth = Unit.FromPoint(contentWidth + 40);
                section.PageSetup.LeftMargin = Unit.FromPoint(20);
                section.PageSetup.RightMargin = Unit.FromPoint(20);
                foreach (var mark in new[] { "\u201d", "\u2019" })
                {
                    var paragraph = section.AddParagraph();
                    paragraph.Format.Alignment = ParagraphAlignment.Left;
                    paragraph.AddText("of ");
                    paragraph.AddFormattedText("authors." + mark);
                }
                var renderer = new PdfDocumentRenderer { Document = document };
                renderer.DocumentRenderer.LineEndOverhang = (_, word, _) =>
                    word.EndsWith("\u201d", StringComparison.Ordinal) ? 6 : null;
                renderer.DocumentRenderer.HangingTerminalLineSpaceThreshold = (_, word, _) =>
                    word.EndsWith("\u201d", StringComparison.Ordinal) ||
                    word.EndsWith("\u2019", StringComparison.Ordinal) ? 0.25 : null;
                if (nearEdge)
                    renderer.DocumentRenderer.HangingTerminalLineNearEdgeThreshold = (_, _, _) => 0.25;
                renderer.RenderDocument();
                var stream = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0);
                var positions = new List<double>();
                while (stream.Text.MoveAndGetNext(true, out var text))
                    if (text!.Text.StartsWith("authors.", StringComparison.Ordinal))
                        positions.Add(text.X);
                return positions.ToArray();
            }

            var baseline = Render(false);
            var adjusted = Render(true);
            baseline.Should().HaveCount(2);
            adjusted.Should().HaveCount(2);
            adjusted[0].Should().BeGreaterThan(baseline[0] + 4);
            adjusted[1].Should().Be(baseline[1]);
        }

        [Fact]
        public void NonterminalHangingSpaceUsesWidthClippedDuringLineFitting()
        {
            using var measure = XGraphics.CreateMeasureContext(new XSize(300, 300),
                XGraphicsUnit.Point, XPageDirection.Downwards);
            var font = new XFont("Arial", 12);
            var contentWidth = measure.MeasureString("A short sequence xxxxxxxxx authors.\"", font).Width - 2;

            (double FirstY, double SecondY, double ShortX) Render(bool expand)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                document.Styles.Normal.Font.Size = 12;
                var section = document.AddSection();
                section.PageSetup.PageWidth = Unit.FromPoint(contentWidth + 40);
                section.PageSetup.LeftMargin = Unit.FromPoint(20);
                section.PageSetup.RightMargin = Unit.FromPoint(20);
                section.AddParagraph("A short sequence xxxxxxxxx authors.\" next words here.")
                    .Format.Alignment = ParagraphAlignment.Left;
                var renderer = new PdfDocumentRenderer { Document = document };
                renderer.DocumentRenderer.LineEndOverhang = (_, word, _) =>
                    word.EndsWith("\"", StringComparison.Ordinal) ? 5 : null;
                if (expand)
                    renderer.DocumentRenderer.HangingNonterminalLineSpaceLimit = (_, word, _) =>
                        word.EndsWith("\"", StringComparison.Ordinal) ? 1 : null;
                renderer.RenderDocument();
                var stream = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0);
                var words = new List<(string Text, double X, double Y)>();
                while (stream.Text.MoveAndGetNext(true, out var text))
                    words.Add((text!.Text, text.X, text.Y));
                var first = words.Single(word => word.Text == "A");
                var shortWord = words.Single(word => word.Text == "short");
                var next = words.Single(word => word.Text == "next");
                return (first.Y, next.Y, shortWord.X);
            }

            var baseline = Render(false);
            var adjusted = Render(true);
            baseline.FirstY.Should().NotBe(baseline.SecondY);
            adjusted.FirstY.Should().Be(baseline.FirstY);
            adjusted.SecondY.Should().Be(baseline.SecondY);
            adjusted.ShortX.Should().BeGreaterThan(baseline.ShortX);
        }
    }
}
