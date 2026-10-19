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
    public class AtLeastBottomLineSpaceTests : IDisposable
    {
        public AtLeastBottomLineSpaceTests()
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
        public void AtLeastBottomLineSpaceSurvivesCloneAndDdl()
        {
            var document = new Document();
            var paragraph = document.AddSection().AddParagraph("Radical");
            paragraph.Format.LineSpacingRule = LineSpacingRule.AtLeast;
            paragraph.Format.AtLeastBottomLineSpace = Unit.FromPoint(3.11);

            paragraph.Clone().Format.AtLeastBottomLineSpace.Point.Should().BeApproximately(3.11, 0.001);
            var ddl = MigraDoc.DocumentObjectModel.IO.DdlWriter.WriteToString(document);
            var restored = MigraDoc.DocumentObjectModel.IO.DdlReader.DocumentFromString(ddl);
            restored.Sections[0].Elements.OfType<Paragraph>().Single().Format
                .AtLeastBottomLineSpace.Point.Should().BeApproximately(3.11, 0.001);
        }

        [Fact]
        public void SelectedAtLeastParagraphAddsSpaceBelowItsBaseline()
        {
            var original = Render(null);
            var selected = Render(3.11);

            selected["Before"].Should().BeApproximately(original["Before"], 0.001);
            selected["Radical"].Should().BeApproximately(original["Radical"], 0.001);
            (selected["After"] - original["After"]).Should().BeApproximately(-3.11, 0.001);
        }

        [Fact]
        public void TallInlineObjectKeepsItsBaselineWhenBottomSpaceIsAdded()
        {
            var original = Render(null, tallInlineObject: true);
            var selected = Render(3.11, tallInlineObject: true);

            selected["Before"].Should().BeApproximately(original["Before"], 0.001);
            selected["Radical"].Should().BeApproximately(original["Radical"], 0.001);
            (selected["After"] - original["After"]).Should().BeApproximately(-3.11, 0.001);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-2)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void InvalidOrDisabledBottomSpacePreservesDefaultBaselines(double bottomSpace)
        {
            Render(bottomSpace).Should().Equal(Render(null));
        }

        [Fact]
        public void ExactlyParagraphDoesNotUseAtLeastBottomSpace()
        {
            Render(3.11, LineSpacingRule.Exactly).Should()
                .Equal(Render(null, LineSpacingRule.Exactly));
        }

        [Fact]
        public void SelectedBottomSpaceParticipatesInPageFitting()
        {
            var fittingThresholdFound = false;
            for (var pageHeight = 70; pageHeight <= 130; pageHeight++)
            {
                if (RenderPageCount(pageHeight, null) != 1 ||
                    RenderPageCount(pageHeight, 3.11) != 2)
                    continue;

                fittingThresholdFound = true;
                break;
            }

            fittingThresholdFound.Should().BeTrue();
        }

        private static int RenderPageCount(double pageHeight, double? bottomSpace)
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            document.Styles.Normal.Font.Size = 12;
            var section = document.AddSection();
            section.PageSetup.PageWidth = Unit.FromPoint(200);
            section.PageSetup.PageHeight = Unit.FromPoint(pageHeight);
            section.PageSetup.TopMargin = Unit.FromPoint(15);
            section.PageSetup.BottomMargin = Unit.FromPoint(15);
            section.AddParagraph("Before");
            var selectedParagraph = section.AddParagraph("Radical");
            selectedParagraph.Format.LineSpacingRule = LineSpacingRule.AtLeast;
            selectedParagraph.Format.LineSpacing = Unit.FromPoint(20);
            section.AddParagraph("After");

            if (bottomSpace is { } value)
                selectedParagraph.Format.AtLeastBottomLineSpace = Unit.FromPoint(value);
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            return renderer.PdfDocument.PageCount;
        }

        private static Dictionary<string, double> Render(double? bottomSpace,
            LineSpacingRule lineSpacingRule = LineSpacingRule.AtLeast,
            bool tallInlineObject = false)
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            document.Styles.Normal.Font.Size = 12;
            var section = document.AddSection();
            section.AddParagraph("Before");
            var selectedParagraph = section.AddParagraph("Radical");
            selectedParagraph.Format.LineSpacingRule = lineSpacingRule;
            selectedParagraph.Format.LineSpacing = Unit.FromPoint(20);
            if (tallInlineObject)
            {
                const string imageData = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR4nGP4z8AAAAMBAQDJ/pLvAAAAAElFTkSuQmCC";
                var image = selectedParagraph.AddImage("base64:" + imageData);
                image.Width = Unit.FromPoint(12);
                image.Height = Unit.FromPoint(30);
            }
            section.AddParagraph("After");

            if (bottomSpace is { } value)
                selectedParagraph.Format.AtLeastBottomLineSpace = Unit.FromPoint(value);
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();

            var stream = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0);
            var baselines = new Dictionary<string, double>();
            while (stream.Text.MoveAndGetNext(true, out var text))
                baselines.Add(text!.Text, text.Y);
            baselines.Should().HaveCount(3);
            return baselines;
        }
    }
}
