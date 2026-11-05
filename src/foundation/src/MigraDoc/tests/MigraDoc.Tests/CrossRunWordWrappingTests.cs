// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

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
    public class CrossRunWordWrappingTests : IDisposable
    {
        public CrossRunWordWrappingTests()
        {
            PdfSharpCore.ResetAll();
#if CORE
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
        }

        public void Dispose() => PdfSharpCore.ResetAll();

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void JoinedTerminalWordRetainsConfiguredSpaceContraction(bool trailingSpace)
        {
            using var measure = XGraphics.CreateMeasureContext(new XSize(200, 200),
                XGraphicsUnit.Point, XPageDirection.Downwards);
            var font = new XFont("Arial", 12);
            var bodyWidth = measure.MeasureString("Lead Alphabet", font).Width - 1;
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            document.Styles.Normal.Font.Size = 12;
            var section = document.AddSection();
            section.PageSetup.PageWidth = Unit.FromPoint(bodyWidth + 40);
            section.PageSetup.LeftMargin = Unit.FromPoint(20);
            section.PageSetup.RightMargin = Unit.FromPoint(20);
            var paragraph = section.AddParagraph("Lead ");
            paragraph.Format.Alignment = ParagraphAlignment.Justify;
            paragraph.AddFormattedText("Alpha").Color = Colors.Blue;
            paragraph.AddFormattedText("bet").Color = Colors.Red;
            if (trailingSpace)
                paragraph.AddText(" ");
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.DocumentRenderer.JustifiedSpaceShrinkFactorForParagraph = _ => 0.1;

            renderer.RenderDocument();

            renderer.DocumentRenderer.GetRenderInfoFromPage(1)!
                .OfType<ParagraphRenderInfo>().Single().LineCount.Should().Be(1);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public void ExistingBreakOpportunitiesAndOversizedWordsArePreserved(bool spaceBetweenParts,
            bool oversizedWord)
        {
            var firstPart = oversizedWord ? "i" : "Alpha";
            var lastPart = oversizedWord ? "MMMMMMMM" : "bet";
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            document.Styles.Normal.Font.Size = 12;
            var section = document.AddSection();
            var bodyWidth = oversizedWord ? 60 : spaceBetweenParts ? 65 : 200;
            section.PageSetup.PageWidth = Unit.FromPoint(bodyWidth + 40);
            section.PageSetup.LeftMargin = Unit.FromPoint(20);
            section.PageSetup.RightMargin = Unit.FromPoint(20);
            var paragraph = section.AddParagraph("Lead ");
            paragraph.AddFormattedText(firstPart).Color = Colors.Blue;
            if (spaceBetweenParts)
                paragraph.AddSpace(1);
            paragraph.AddFormattedText(lastPart).Color = Colors.Red;
            var renderer = new PdfDocumentRenderer { Document = document };

            renderer.RenderDocument();

            renderer.PageCount.Should().Be(1);
            var stream = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0);
            stream.Text.MoveAndGetNext(info => info.Text == "Lead", true, out var lead).Should().BeTrue();
            stream.Text.MoveAndGetNext(info => info.Text == firstPart, true, out var first).Should().BeTrue();
            first!.Y.Should().BeApproximately(lead!.Y, 0.01);
            var lines = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!
                .OfType<ParagraphRenderInfo>().Single().LineCount;
            if (spaceBetweenParts || oversizedWord)
                lines.Should().BeGreaterThan(1);
            else
                lines.Should().Be(1);
        }

        [Theory]
        [InlineData(ParagraphAlignment.Left, false)]
        [InlineData(ParagraphAlignment.Justify, false)]
        [InlineData(ParagraphAlignment.Left, true)]
        [InlineData(ParagraphAlignment.Justify, true)]
        public void FormattingBoundaryDoesNotSplitAWordThatFitsOnTheNextLine(
            ParagraphAlignment alignment, bool followingWord)
        {
            using var measure = XGraphics.CreateMeasureContext(new XSize(200, 200),
                XGraphicsUnit.Point, XPageDirection.Downwards);
            var font = new XFont("Arial", 12);
            var wordWidth = measure.MeasureString("Alphabet", font).Width;
            var leadWidth = measure.MeasureString("Lead ", font).Width;
            var bodyWidth = wordWidth + leadWidth / 2;
            (leadWidth + measure.MeasureString("Alpha", font).Width).Should().BeLessThan(bodyWidth);

            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            document.Styles.Normal.Font.Size = 12;
            var section = document.AddSection();
            section.PageSetup.PageWidth = Unit.FromPoint(bodyWidth + 40);
            section.PageSetup.LeftMargin = Unit.FromPoint(20);
            section.PageSetup.RightMargin = Unit.FromPoint(20);
            var paragraph = section.AddParagraph("Lead ");
            paragraph.Format.Alignment = alignment;
            paragraph.AddFormattedText("Alpha").Color = Colors.Blue;
            paragraph.AddFormattedText("bet").Color = Colors.Red;
            if (followingWord)
                paragraph.AddText(" tail");
            var renderer = new PdfDocumentRenderer { Document = document };

            renderer.RenderDocument();

            renderer.PageCount.Should().Be(1);
            var stream = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0);
            stream.Text.MoveAndGetNext(info => info.Text == "Lead", true, out var lead).Should().BeTrue();
            stream.Text.MoveAndGetNext(info => info.Text == "Alpha", true, out var first).Should().BeTrue();
            stream.Text.MoveAndGetNext(info => info.Text == "bet", true, out var last).Should().BeTrue();
            first!.Y.Should().BeLessThan(lead!.Y);
            last!.Y.Should().BeApproximately(first.Y, 0.01);
            first.X.Should().BeApproximately(20, 0.01);
            last.X.Should().BeGreaterThan(first.X);
        }
    }
}
