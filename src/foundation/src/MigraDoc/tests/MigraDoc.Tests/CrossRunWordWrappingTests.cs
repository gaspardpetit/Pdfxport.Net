// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
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
        [InlineData("\u200B")]
        [InlineData("\u200C")]
        public void MarkersAreExcludedFromOutlineAndVisibleFontCallbacks(string marker)
        {
            var document = CreateDocument(280);
            var paragraph = document.LastSection.AddParagraph("Lead");
            paragraph.Format.OutlineLevel = OutlineLevel.Level1;
            paragraph.AddFormattedText(marker).Size = 72;
            paragraph.AddText(" text");
            paragraph.AddText(marker);
            var renderer = new PdfDocumentRenderer { Document = document };
            var called = false;
            renderer.DocumentRenderer.ParagraphPaintBaselineOffset = (_, fonts) =>
            {
                called = true;
                fonts.Should().OnlyContain(font => font.Size == 12);
                return null;
            };
            renderer.DocumentRenderer.HangingTerminalLineSpaceThreshold = (_, word, _) =>
            {
                word.Should().Be("text");
                return null;
            };
            renderer.RenderDocument();
            called.Should().BeTrue();
            renderer.PdfDocument.Outlines[0].Title.Should().Be("Lead text");
        }

        [Theory]
        [InlineData("\u200B", 0)]
        [InlineData("\u200C", 0)]
        [InlineData("\u200B", 1)]
        [InlineData("\u200C", 1)]
        [InlineData("\u200B", 2)]
        [InlineData("\u200C", 2)]
        public void MarkerFontDoesNotAffectLineMetrics(string marker, int position)
        {
            var document = CreateDocument(280);
            var paragraph = document.LastSection.AddParagraph();
            if (position == 0)
                paragraph.AddFormattedText(marker).Size = 72;
            paragraph.AddText("Lead");
            if (position == 1)
                paragraph.AddFormattedText(marker).Size = 72;
            paragraph.AddText(" text");
            if (position == 2)
                paragraph.AddFormattedText(marker).Size = 72;
            var control = document.LastSection.AddParagraph("Lead text");
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var paragraphs = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!
                .OfType<ParagraphRenderInfo>().ToArray();
            paragraphs.Single(info => ReferenceEquals(info.DocumentObject, paragraph)).LayoutInfo.ContentArea.Height.Point
                .Should().BeApproximately(paragraphs.Single(info => ReferenceEquals(info.DocumentObject, control))
                    .LayoutInfo.ContentArea.Height.Point, 0.01);
        }

        [Theory]
        [InlineData("\u200B")]
        [InlineData("\u200C")]
        public void OptionalSpaceLooksPastBreakMarker(string marker)
        {
            (double X, double Y) Render(bool extraSpace)
            {
                using var measure = XGraphics.CreateMeasureContext(new XSize(200, 200),
                    XGraphicsUnit.Point, XPageDirection.Downwards);
                var font = new XFont("Arial", 12);
                var document = CreateDocument(measure.MeasureString("LeadDelta", font).Width +
                    measure.MeasureString(" ", font).Width / 2);
                var paragraph = document.LastSection.AddParagraph("Lead");
                var extra = extraSpace ? paragraph.AddSpace(1) : null;
                paragraph.AddText(marker + "Delta");
                var renderer = new PdfDocumentRenderer { Document = document };
                if (extra != null)
                    renderer.DocumentRenderer.CollapsibleSpaceBeforeWrap = (_, space) => ReferenceEquals(space, extra);
                renderer.RenderDocument();
                var stream = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0);
                stream.Text.MoveAndGetNext(info => info.Text == "Delta", true, out var word).Should().BeTrue();
                return (word!.X, word.Y);
            }
            var control = Render(false);
            var actual = Render(true);
            actual.X.Should().BeApproximately(control.X, 0.01);
            actual.Y.Should().BeApproximately(control.Y, 0.01);
        }

        [Theory]
        [InlineData("")]
        [InlineData("\u200B")]
        [InlineData("\u200C")]
        [InlineData("\u200B\u200C")]
        public void DecimalTabSkipsLeadingBreakMarkers(string markers)
        {
            var document = CreateDocument(280);
            document.Culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
            var paragraph = document.LastSection.AddParagraph();
            paragraph.Format.TabStops.AddTabStop(Unit.FromPoint(150), TabAlignment.Decimal);
            paragraph.AddTab();
            paragraph.AddText(markers + "12.34");
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            using var measure = XGraphics.CreateMeasureContext(new XSize(200, 200),
                XGraphicsUnit.Point, XPageDirection.Downwards);
            var integerWidth = measure.MeasureString("12", new XFont("Arial", 12)).Width;
            var stream = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0);
            stream.Text.MoveAndGetNext(info => info.Text == "12.34", true, out var number).Should().BeTrue();
            number!.X.Should().BeApproximately(20 + 150 - integerWidth, 0.01);
            renderer.DocumentRenderer.GetRenderInfoFromPage(1)!
                .OfType<ParagraphRenderInfo>().Single().LineCount.Should().Be(1);
        }

        [Fact]
        public void DecimalTabFollowedOnlyByBreakMarkersDoesNotThrow()
        {
            var document = CreateDocument(280);
            var paragraph = document.LastSection.AddParagraph("Lead");
            paragraph.Format.TabStops.AddTabStop(Unit.FromPoint(150), TabAlignment.Decimal);
            paragraph.AddTab();
            paragraph.AddText("\u200B\u200C");
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            renderer.DocumentRenderer.GetRenderInfoFromPage(1)!
                .OfType<ParagraphRenderInfo>().Single().LineCount.Should().Be(1);
        }

        [Theory]
        [InlineData(12, 24, 1)]
        [InlineData(24, 12, 2)]
        public void JoinedTerminalContractionUsesFinalFragmentFont(int firstSize, int finalSize, int expectedLines)
        {
            using var measure = XGraphics.CreateMeasureContext(new XSize(200, 200),
                XGraphicsUnit.Point, XPageDirection.Downwards);
            var firstFont = new XFont("Arial", firstSize);
            var finalFont = new XFont("Arial", finalSize);
            var firstAllowance = 0.5 * measure.MeasureString(" ", firstFont).Width;
            var finalAllowance = 0.5 * measure.MeasureString(" ", finalFont).Width;
            var bodyWidth = measure.MeasureString("Lead ", new XFont("Arial", 12)).Width +
                measure.MeasureString("Alpha", firstFont).Width + measure.MeasureString("bet", finalFont).Width -
                (firstAllowance + finalAllowance) / 2;
            var document = CreateDocument(bodyWidth);
            var paragraph = document.LastSection.AddParagraph("Lead ");
            paragraph.Format.Alignment = ParagraphAlignment.Justify;
            paragraph.AddFormattedText("Alpha").Size = firstSize;
            paragraph.AddFormattedText("bet").Size = finalSize;
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.DocumentRenderer.JustifiedSpaceShrinkFactorForParagraph = _ => 0.5;
            renderer.RenderDocument();
            renderer.DocumentRenderer.GetRenderInfoFromPage(1)!
                .OfType<ParagraphRenderInfo>().Single().LineCount.Should().Be(expectedLines);
        }

        [Theory]
        [InlineData(false, "\u200B", 1)]
        [InlineData(true, "\u200B", 1)]
        [InlineData(false, "\u200C", 1)]
        [InlineData(true, "\u200C", 1)]
        [InlineData(false, " \u200B ", 1)]
        [InlineData(true, " \u200B ", 1)]
        [InlineData(false, " \u200C ", 1)]
        [InlineData(true, " \u200C ", 1)]
        [InlineData(false, "\u200B tail", 2)]
        [InlineData(true, "\u200B tail", 2)]
        [InlineData(false, "\u200C tail", 2)]
        [InlineData(true, "\u200C tail", 2)]
        public void BreakMarkersPreserveTerminalContractionPolicy(bool joined, string suffix, int expectedLines)
        {
            using var measure = XGraphics.CreateMeasureContext(new XSize(200, 200),
                XGraphicsUnit.Point, XPageDirection.Downwards);
            var document = CreateDocument(measure.MeasureString("Lead Alphabet", new XFont("Arial", 12)).Width - 1);
            var paragraph = document.LastSection.AddParagraph("Lead ");
            paragraph.Format.Alignment = ParagraphAlignment.Justify;
            if (joined)
            {
                paragraph.AddFormattedText("Alpha").Color = Colors.Blue;
                paragraph.AddFormattedText("bet").Color = Colors.Red;
            }
            else
                paragraph.AddText("Alphabet");
            paragraph.AddText(suffix);
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.DocumentRenderer.JustifiedSpaceShrinkFactorForParagraph = _ => 0.1;
            renderer.RenderDocument();
            renderer.DocumentRenderer.GetRenderInfoFromPage(1)!
                .OfType<ParagraphRenderInfo>().Single().LineCount.Should().Be(expectedLines);
        }

        [Theory]
        [InlineData("\u200B")]
        [InlineData("\u200C")]
        public void ExplicitZeroWidthBreakIsPreserved(string separator)
        {
            var document = CreateDocument(65);
            document.LastSection.AddParagraph("Lead Alpha" + separator + "bet");
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            AssertPositions(renderer, firstOnLeadLine: true, lastOnLeadLine: false);
        }

        [Theory]
        [InlineData(false, false, "")]
        [InlineData(true, false, "")]
        [InlineData(false, true, "")]
        [InlineData(true, true, "")]
        [InlineData(true, true, " \u200B ")]
        [InlineData(true, true, " \u200C ")]
        public void JoinedWordHonorsFinalFragmentOverhang(bool additive, bool contraction, string suffix)
        {
            using var measure = XGraphics.CreateMeasureContext(new XSize(200, 200),
                XGraphicsUnit.Point, XPageDirection.Downwards);
            var bodyWidth = measure.MeasureString("Lead Alphabet.", new XFont("Arial", 12)).Width -
                (contraction ? 4 : 1);
            var document = CreateDocument(bodyWidth);
            var paragraph = document.LastSection.AddParagraph("Lead ");
            if (contraction)
                paragraph.Format.Alignment = ParagraphAlignment.Justify;
            paragraph.AddFormattedText("Alpha").Color = Colors.Blue;
            paragraph.AddFormattedText("bet.").Color = Colors.Red;
            paragraph.AddText(suffix);
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.DocumentRenderer.LineEndOverhang = (_, word, _) => word == "bet." ? (contraction ? 3 : 2) : null;
            renderer.DocumentRenderer.AdditiveTerminalLineEndOverhang = additive;
            if (contraction)
                renderer.DocumentRenderer.JustifiedSpaceShrinkFactorForParagraph = _ => 0.4;
            renderer.RenderDocument();
            AssertPositions(renderer, additive || !contraction, additive || !contraction, "bet.");
        }

        [Fact]
        public void JoinedWordMovesToOtherSideOfImageOnSameLine()
        {
            var document = CreateDocument(280);
            var section = document.LastSection;
            section.PageSetup.TopMargin = Unit.FromPoint(20);
            var image = section.AddImage(
                "base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==");
            image.RelativeHorizontal = RelativeHorizontal.Margin;
            image.RelativeVertical = RelativeVertical.Margin;
            image.Left = Unit.FromPoint(65);
            image.Top = Unit.FromPoint(0);
            image.Width = Unit.FromPoint(60);
            image.Height = Unit.FromPoint(90);
            image.WrapFormat.Style = WrapStyle.BothSides;
            var paragraph = section.AddParagraph("Lead ");
            paragraph.AddFormattedText("Alpha").Color = Colors.Blue;
            paragraph.AddFormattedText("bet").Color = Colors.Red;
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            AssertPositions(renderer, true, true, minimumFirstX: 145);
        }

        static Document CreateDocument(double bodyWidth)
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            document.Styles.Normal.Font.Size = 12;
            var section = document.AddSection();
            section.PageSetup.PageWidth = Unit.FromPoint(bodyWidth + 40);
            section.PageSetup.LeftMargin = Unit.FromPoint(20);
            section.PageSetup.RightMargin = Unit.FromPoint(20);
            return document;
        }

        static void AssertPositions(PdfDocumentRenderer renderer, bool firstOnLeadLine,
            bool lastOnLeadLine, string lastText = "bet", double minimumFirstX = 20)
        {
            var stream = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0);
            stream.Text.MoveAndGetNext(info => info.Text == "Lead", true, out var lead).Should().BeTrue();
            stream.Text.MoveAndGetNext(info => info.Text == "Alpha", true, out var first).Should().BeTrue();
            stream.Text.MoveAndGetNext(info => info.Text == lastText, true, out var last).Should().BeTrue();
            if (firstOnLeadLine)
                first!.Y.Should().BeApproximately(lead!.Y, 0.01);
            else
                first!.Y.Should().BeLessThan(lead!.Y);
            if (lastOnLeadLine)
                last!.Y.Should().BeApproximately(lead!.Y, 0.01);
            else
                last!.Y.Should().BeLessThan(lead!.Y);
            first.X.Should().BeGreaterOrEqualTo(minimumFirstX - 0.01);
        }

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
