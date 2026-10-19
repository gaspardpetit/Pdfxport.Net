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
    public class AtLeastBaselineOffsetTests : IDisposable
    {
        public AtLeastBaselineOffsetTests()
        {
            PdfSharpCore.ResetAll();
#if CORE
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
        }

        public void Dispose() => PdfSharpCore.ResetAll();

        [Fact]
        public void AtLeastBaselineOffsetSurvivesCloneAndDdl()
        {
            var document = new Document();
            var paragraph = document.AddSection().AddParagraph("Ruby");
            paragraph.Format.LineSpacingRule = LineSpacingRule.AtLeast;
            paragraph.Format.AtLeastBaselineOffset = Unit.FromPoint(-2.4);

            paragraph.Clone().Format.AtLeastBaselineOffset.Point.Should().BeApproximately(-2.4, 0.001);
            var ddl = MigraDoc.DocumentObjectModel.IO.DdlWriter.WriteToString(document);
            var restored = MigraDoc.DocumentObjectModel.IO.DdlReader.DocumentFromString(ddl);
            restored.Sections[0].Elements.OfType<Paragraph>().Single().Format
                .AtLeastBaselineOffset.Point.Should().BeApproximately(-2.4, 0.001);
        }

        [Fact]
        public void AtLeastBaselineOffsetMovesPaintOnlyInSelectedParagraph()
        {
            var original = Render(null);
            var selected = Render(2.4);

            selected.Pages.Should().Be(original.Pages);
            selected.Words.Select(word => (word.Text, word.X)).Should()
                .Equal(original.Words.Select(word => (word.Text, word.X)));
            selected.Words.Select(word => word.Text).Should()
                .Equal(original.Words.Select(word => word.Text));
            selected.Words.Where(word => word.Text == "Before" || word.Text == "After")
                .Select(word => word.Y).Should()
                .Equal(original.Words.Where(word => word.Text == "Before" || word.Text == "After")
                    .Select(word => word.Y));
            var shifted = selected.Words.Zip(original.Words, (after, before) =>
                (after.Text, Difference: after.Y - before.Y))
                .Where(item => item.Text != "Before" && item.Text != "After")
                .ToArray();
            shifted.Should().NotBeEmpty();
            shifted.Should().OnlyContain(item => Math.Abs(item.Difference + 2.4) < 0.01);
        }

        [Fact]
        public void OtherLineSpacingRulesIgnoreAtLeastBaselineOffset()
        {
            Render(2.4, LineSpacingRule.Single).Words.Should()
                .Equal(Render(null, LineSpacingRule.Single).Words);
            Render(2.4, LineSpacingRule.Exactly).Words.Should()
                .Equal(Render(null, LineSpacingRule.Exactly).Words);
        }

        private static (int Pages, (string Text, double X, double Y)[] Words) Render(
            double? offset, LineSpacingRule selectedRule = LineSpacingRule.AtLeast)
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            document.Styles.Normal.Font.Size = 12;
            var section = document.AddSection();
            section.PageSetup.PageWidth = Unit.FromPoint(180);
            section.PageSetup.LeftMargin = Unit.FromPoint(20);
            section.PageSetup.RightMargin = Unit.FromPoint(20);
            section.AddParagraph("Before");
            var selected = section.AddParagraph("Alpha Bravo Charlie Delta Epsilon Zeta");
            selected.Format.LineSpacingRule = selectedRule;
            selected.Format.LineSpacing = Unit.FromPoint(20);
            section.AddParagraph("After");
            if (offset is { } value)
                selected.Format.AtLeastBaselineOffset = Unit.FromPoint(value);

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var enumerator = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0);
            var words = new List<(string Text, double X, double Y)>();
            while (enumerator.Text.MoveAndGetNext(true, out var text))
                words.Add((text!.Text, text.X, text.Y));
            return (renderer.PdfDocument.PageCount, words.ToArray());
        }
    }
}
