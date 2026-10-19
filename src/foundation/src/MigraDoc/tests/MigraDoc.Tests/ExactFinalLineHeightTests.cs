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
    public class ExactFinalLineHeightTests : IDisposable
    {
        public ExactFinalLineHeightTests()
        {
            PdfSharpCore.ResetAll();
#if CORE
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
        }

        public void Dispose() => PdfSharpCore.ResetAll();

        [Theory]
        [InlineData(17, 3)]
        [InlineData(double.NaN, 0)]
        [InlineData(-1, 0)]
        public void ShorterFinalLineMovesFollowingParagraphWithoutChangingText(double requestedHeight,
            double expectedShift)
        {
            (double Height, double FollowingY, string[] Text) Render(double? height)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                document.Styles.Normal.Font.Size = 12;
                var section = document.AddSection();
                section.PageSetup.PageWidth = Unit.FromPoint(135);
                section.PageSetup.LeftMargin = Unit.FromPoint(20);
                section.PageSetup.RightMargin = Unit.FromPoint(20);
                var exact = section.AddParagraph("Alpha Bravo Charlie Delta Epsilon Zeta");
                exact.Format.LineSpacingRule = LineSpacingRule.Exactly;
                exact.Format.LineSpacing = Unit.FromPoint(20);
                var following = section.AddParagraph("Following");
                var renderer = new PdfDocumentRenderer { Document = document };
                if (height is { } value)
                    renderer.DocumentRenderer.ExactFinalLineHeight = (paragraph, fonts) =>
                        ReferenceEquals(paragraph, exact) && fonts.Count > 0 ? value : null;
                renderer.RenderDocument();
                var infos = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!;
                var exactInfo = infos.OfType<ParagraphRenderInfo>()
                    .Single(info => ReferenceEquals(info.DocumentObject, exact));
                var followingInfo = infos.OfType<ParagraphRenderInfo>()
                    .Single(info => ReferenceEquals(info.DocumentObject, following));
                var enumerator = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0);
                var text = new List<string>();
                while (enumerator.Text.MoveAndGetNext(true, out var item))
                    text.Add(item!.Text);
                return (exactInfo.LayoutInfo.ContentArea.Height.Point,
                    followingInfo.LayoutInfo.ContentArea.Y.Point, text.ToArray());
            }

            var baseline = Render(null);
            var adjusted = Render(requestedHeight);
            (baseline.Height - adjusted.Height).Should().BeApproximately(expectedShift, 0.01);
            (baseline.FollowingY - adjusted.FollowingY).Should().BeApproximately(expectedShift, 0.01);
            adjusted.Text.Should().Equal(baseline.Text);
        }
    }
}
