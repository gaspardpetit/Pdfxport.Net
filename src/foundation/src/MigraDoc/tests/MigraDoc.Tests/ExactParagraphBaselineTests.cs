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
    public class ExactParagraphBaselineTests : IDisposable
    {
        public ExactParagraphBaselineTests()
        {
            PdfSharpCore.ResetAll();
#if CORE
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
        }

        public void Dispose() => PdfSharpCore.ResetAll();

        [Fact]
        public void ExactParagraphBaselineOffsetMovesEveryLineWithoutChangingLayout()
        {
            (int Pages, int Calls, (string Text, double X, double Y)[] Words) Render(
                double? offset, LineSpacingRule spacingRule = LineSpacingRule.Exactly)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                document.Styles.Normal.Font.Size = 12;
                var section = document.AddSection();
                section.PageSetup.PageWidth = Unit.FromPoint(180);
                section.PageSetup.LeftMargin = Unit.FromPoint(20);
                section.PageSetup.RightMargin = Unit.FromPoint(20);
                var paragraph = section.AddParagraph("Alpha Bravo Charlie Delta Epsilon Zeta Eta Theta");
                paragraph.Format.LineSpacingRule = spacingRule;
                paragraph.Format.LineSpacing = Unit.FromPoint(20);
                var renderer = new PdfDocumentRenderer { Document = document };
                var calls = 0;
                if (offset != null)
                    renderer.DocumentRenderer.ExactParagraphBaselineOffset = (_, fonts) =>
                    {
                        calls++;
                        fonts.Should().NotBeEmpty();
                        return offset;
                    };
                renderer.RenderDocument();
                var enumerator = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0);
                var words = new List<(string Text, double X, double Y)>();
                while (enumerator.Text.MoveAndGetNext(true, out var text))
                    words.Add((text!.Text, text.X, text.Y));
                return (renderer.PdfDocument.PageCount, calls, words.ToArray());
            }

            var normal = Render(null);
            var adjusted = Render(-3);
            normal.Words.Select(word => word.Y).Distinct().Count().Should().BeGreaterThan(1);
            adjusted.Pages.Should().Be(normal.Pages);
            adjusted.Calls.Should().Be(1);
            adjusted.Words.Select(word => (word.Text, word.X)).Should()
                .Equal(normal.Words.Select(word => (word.Text, word.X)));
            adjusted.Words.Zip(normal.Words, (after, before) => after.Y - before.Y)
                .Should().OnlyContain(difference => Math.Abs(difference - 3) < 0.01);
            Render(-3, LineSpacingRule.Single).Calls.Should().Be(0);
            Render(double.NaN).Words.Should().Equal(normal.Words);
        }
    }
}
