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
    public class InlineImageParagraphHeightTests : IDisposable
    {
        public InlineImageParagraphHeightTests()
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

        [Theory]
        [InlineData(0.8)]
        [InlineData(1)]
        [InlineData(1.158)]
        [InlineData(1.5)]
        public void MultipleLineSpacingDoesNotScaleInlineImageHeight(double factor)
        {
            double Height(double imageSize, double? secondImageSize = null)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                document.Styles.Normal.Font.Size = Unit.FromPoint(12);
                document.Styles.Normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(8);
                document.Styles.Normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Multiple;
                document.Styles.Normal.ParagraphFormat.LineSpacing = Unit.FromPoint(factor);
                var section = document.AddSection();
                section.AddParagraph("Line 1");
                section.AddParagraph("Line 2");
                section.AddParagraph("Line 3");
                var imageParagraph = section.AddParagraph("Line 4");
                const string imageData = "base64:iVBORw0KGgoAAAANSUhEUgAAACAAAAAgCAYAAABzenr0AAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAAzSURBVFhH7c4hAQAwCABBqhGH2EsyPAHAnHjz6iLr/ctiju0AAAAAAAAAAAAAAAAAAAAa+E8ol4K1jaQAAAAASUVORK5CYII=";
                var image = imageParagraph.AddImage(imageData);
                image.Width = Unit.FromPoint(imageSize);
                image.Height = Unit.FromPoint(imageSize);
                if (secondImageSize is double secondSize)
                {
                    var secondImage = imageParagraph.AddImage(imageData);
                    secondImage.Width = Unit.FromPoint(secondSize);
                    secondImage.Height = Unit.FromPoint(secondSize);
                }
                section.AddParagraph("Line 5");

                var renderer = new PdfDocumentRenderer { Document = document };
                renderer.RenderDocument();
                return renderer.DocumentRenderer.FormattedDocument!.GetRenderInfos(1)!
                    .OfType<ParagraphRenderInfo>()
                    .Single(info => ReferenceEquals(info.DocumentObject, imageParagraph))
                    .LayoutInfo.ContentArea.Height.Point;
            }

            (Height(48) - Height(24)).Should().BeApproximately(24, 0.05);
            (Height(48) - Height(16)).Should().BeApproximately(32, 0.05);
            (Height(48) - Height(12)).Should().BeApproximately(36, 0.05);
            (Height(8) - Height(4)).Should().BeApproximately(0, 0.05);
            Height(48, 16).Should().BeApproximately(Height(48), 0.05);
            Height(16, 48).Should().BeApproximately(Height(48), 0.05);
        }
    }
}
