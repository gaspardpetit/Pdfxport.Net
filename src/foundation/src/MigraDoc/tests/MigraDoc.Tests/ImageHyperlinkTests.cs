// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf.IO;
using PdfSharp.Quality;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class ImageHyperlinkTests : IDisposable
{
    const string Pixel = "base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==";

    public ImageHyperlinkTests()
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
    public void LinkFollowsGraphicsTranslationIncludingFailurePlaceholder(bool missingImage)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        var image = document.AddSection().AddImage(missingImage ? "missing-image-for-link-test.png" : Pixel);
        image.Width = Unit.FromPoint(60);
        image.Height = Unit.FromPoint(40);
        var renderer = new DocumentRenderer(document);
        XRect? painted = null;
        renderer.ImageWebLink = (_, rect) => { painted = rect; return "https://example.com"; };
        renderer.PrepareDocument();
        var fragment = renderer.GetRenderInfoFromPage(1)!.Single();
        using var pdf = new PdfSharp.Pdf.PdfDocument();
        var page = pdf.AddPage();
        using (var gfx = XGraphics.FromPdfPage(page))
        {
            gfx.TranslateTransform(17, 23);
            var originalTransform = gfx.Transform;
            renderer.RenderPreparedObject(gfx, fragment, 1, 0);
            gfx.Transform.Should().Be(originalTransform);
        }
        page.Annotations.Count.Should().Be(1);
        var link = page.Annotations[0];
        var bounds = painted!.Value;
        link.Rectangle.X1.Should().BeApproximately(bounds.X + 17, 0.001);
        link.Rectangle.X2.Should().BeApproximately(bounds.Right + 17, 0.001);
        link.Rectangle.Y1.Should().BeApproximately(page.Height.Point - bounds.Bottom - 23, 0.001);
        link.Rectangle.Y2.Should().BeApproximately(page.Height.Point - bounds.Y - 23, 0.001);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    public void ImageLinksUsePaintedBoundsAndSelectedTarget(bool inline, int target)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        var section = document.AddSection();
        section.PageSetup.PageWidth = Unit.FromPoint(300);
        section.PageSetup.PageHeight = Unit.FromPoint(400);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(30);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromPoint(30);
        var paragraph = section.AddParagraph("Before ");
        var image = inline ? paragraph.AddImage(Pixel) : section.AddImage(Pixel);
        image.Width = Unit.FromPoint(60);
        image.Height = Unit.FromPoint(40);
        section.AddParagraph().AddBookmark("Destination");
        var renderer = new PdfDocumentRenderer { Document = document };
        XRect? painted = null;
        var documentCalls = 0;
        if (target != 0)
        {
            renderer.DocumentRenderer.ImageWebLink = (actual, rect) =>
            {
                actual.Should().BeSameAs(image);
                painted = rect;
                return target is 1 or 3 ? "https://example.com/image" : "";
            };
            renderer.DocumentRenderer.ImageDocumentLink = (actual, rect) =>
            {
                actual.Should().BeSameAs(image);
                rect.Should().Be(painted!.Value);
                documentCalls++;
                return target is 2 or 3 ? "Destination" : null;
            };
        }
        renderer.RenderDocument();
        using var output = new MemoryStream();
        renderer.PdfDocument.Save(output, false);
        output.Position = 0;
        using var pdf = PdfReader.Open(output, PdfDocumentOpenMode.Import);
        var page = pdf.Pages[0];
        if (target is 0 or 4)
        {
            page.Annotations.Count.Should().Be(0);
            return;
        }
        page.Annotations.Count.Should().Be(1);
        var link = page.Annotations[0];
        var rect = painted!.Value;
        rect.Width.Should().BeApproximately(60, 0.001);
        rect.Height.Should().BeApproximately(40, 0.001);
        link.Rectangle.X1.Should().BeApproximately(rect.X, 0.001);
        link.Rectangle.X2.Should().BeApproximately(rect.Right, 0.001);
        link.Rectangle.Y1.Should().BeApproximately(page.Height.Point - rect.Bottom, 0.001);
        link.Rectangle.Y2.Should().BeApproximately(page.Height.Point - rect.Y, 0.001);
        var action = link.Elements.GetDictionary("/A")!;
        if (target is 1 or 3)
        {
            action.Elements.GetName("/S").Should().Be("/URI");
            action.Elements.GetString("/URI").Should().Be("https://example.com/image");
            documentCalls.Should().Be(0);
        }
        else
        {
            action.Elements.GetName("/S").Should().Be("/GoTo");
            action.Elements.GetString("/D").Should().Be("Destination");
            documentCalls.Should().Be(1);
        }
    }
}
