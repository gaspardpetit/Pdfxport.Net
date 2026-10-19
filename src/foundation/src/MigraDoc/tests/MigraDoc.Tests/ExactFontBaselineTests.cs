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
    public class ExactFontBaselineTests : IDisposable
    {
        public ExactFontBaselineTests()
        {
            PdfSharpCore.ResetAll();
#if CORE
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
        }

        public void Dispose() => PdfSharpCore.ResetAll();

        [Fact]
        public void ExactFontBaselineOffsetMovesOnlySelectedFontWithoutChangingLayout()
        {
            (int Pages, (string Text, double X, double Y)[] Words) Render(
                double? offset, LineSpacingRule spacingRule = LineSpacingRule.Exactly)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                document.Styles.Normal.Font.Size = 12;
                var paragraph = document.AddSection().AddParagraph();
                paragraph.Format.LineSpacingRule = spacingRule;
                paragraph.Format.LineSpacing = Unit.FromPoint(20);
                paragraph.AddText("Alpha ");
                paragraph.AddFormattedText("Bravo").Font.Size = 14;
                paragraph.AddText(" Charlie");
                var renderer = new PdfDocumentRenderer { Document = document };
                if (offset is { } value)
                    renderer.DocumentRenderer.ExactFontBaselineOffset = (_, font) =>
                        font.Size > 12 ? value : null;
                renderer.RenderDocument();
                var enumerator = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0);
                var words = new List<(string Text, double X, double Y)>();
                while (enumerator.Text.MoveAndGetNext(true, out var text))
                    words.Add((text!.Text, text.X, text.Y));
                return (renderer.PdfDocument.PageCount, words.ToArray());
            }

            var normal = Render(null);
            var adjusted = Render(2);
            adjusted.Pages.Should().Be(normal.Pages);
            adjusted.Words.Select(word => (word.Text, word.X)).Should()
                .Equal(normal.Words.Select(word => (word.Text, word.X)));
            adjusted.Words.Zip(normal.Words, (after, before) => (after.Text, Shift: after.Y - before.Y))
                .Should().OnlyContain(word => Math.Abs(word.Shift -
                    (word.Text == "Bravo" ? -2 : 0)) < 0.01);
            Render(double.NaN).Words.Should().Equal(normal.Words);
            Render(2, LineSpacingRule.Single).Words.Should()
                .Equal(Render(null, LineSpacingRule.Single).Words);
        }
    }
}
