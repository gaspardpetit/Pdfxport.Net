// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class ImagePaintRectangleTests : IDisposable
{
    const string Pixel = "base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==";
    public ImagePaintRectangleTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    [InlineData(false, 5)]
    [InlineData(true, 5)]
    [InlineData(false, 6)]
    [InlineData(true, 6)]
    public void PaintRectangleChangesImageAndLinkWithoutMovingText(bool inline, int mode)
    {
        var baseline = Render(inline, 0);
        var result = Render(inline, mode);
        result.Text.Should().Equal(baseline.Text);
        result.Layout.Should().Equal(baseline.Layout);
        if (mode != 1)
        {
            result.Paint.Should().Be(baseline.Paint);
            result.Link.Should().Be(baseline.Link);
            result.Matrix.Should().Equal(baseline.Matrix);
            return;
        }
        result.Paint.Should().Be(new XRect(baseline.Paint.X + 7, baseline.Paint.Y + 11, 90, 20));
        result.Matrix[0].Should().BeApproximately(baseline.Matrix[0] * 1.5, 0.001);
        result.Matrix[3].Should().BeApproximately(baseline.Matrix[3] * 0.5, 0.001);
        result.Matrix[4].Should().BeApproximately(baseline.Matrix[4] + 7, 0.001);
        // Raster image matrices place the unit square at the bottom of its frame.
        Math.Abs(result.Matrix[5] - baseline.Matrix[5]).Should().BeApproximately(9, 0.001);
        result.Link.X1.Should().BeApproximately(baseline.Link.X1 + 7, 0.001);
        result.Link.Y2.Should().BeApproximately(baseline.Link.Y2 - 11, 0.001);
        result.Link.Width.Should().BeApproximately(90, 0.001);
        result.Link.Height.Should().BeApproximately(20, 0.001);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CanChangePaintBoundsAfterPreparationIncludingFailurePlaceholder(bool missing)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        var image = document.AddSection().AddImage(missing ? "missing-image-paint-test.png" : Pixel);
        image.Width = Unit.FromPoint(60);
        image.Height = Unit.FromPoint(40);
        var renderer = new DocumentRenderer(document);
        renderer.ImageWebLink = (_, _) => "https://example.com";
        renderer.PrepareDocument();
        var fragment = renderer.GetRenderInfoFromPage(1)!.Single();
        var originalY = fragment.LayoutInfo.ContentArea.Y;
        using var pdf = new PdfDocument();
        var baseline = pdf.AddPage();
        using (var gfx = XGraphics.FromPdfPage(baseline))
            renderer.RenderPreparedObject(gfx, fragment, 1, 0);
        renderer.ImagePaintRectangle = (_, frame) => new XRect(frame.X + 7, frame.Y + 11, 90, 20);
        var moved = pdf.AddPage();
        using (var gfx = XGraphics.FromPdfPage(moved))
            renderer.RenderPreparedObject(gfx, fragment, 1, 0);
        fragment.LayoutInfo.ContentArea.Y.Should().Be(originalY);
        var first = baseline.Annotations[0].Rectangle;
        var second = moved.Annotations[0].Rectangle;
        second.X1.Should().BeApproximately(first.X1 + 7, 0.001);
        second.Y2.Should().BeApproximately(first.Y2 - 11, 0.001);
        second.Width.Should().BeApproximately(90, 0.001);
        second.Height.Should().BeApproximately(20, 0.001);
    }

    static Result Render(bool inline, int mode)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        var section = document.AddSection();
        var paragraph = section.AddParagraph("Before ");
        var image = inline ? paragraph.AddImage(Pixel) : section.AddImage(Pixel);
        image.Width = Unit.FromPoint(60);
        image.Height = Unit.FromPoint(40);
        if (inline) paragraph.AddText(" After");
        section.AddParagraph("Following");
        var renderer = new PdfDocumentRenderer { Document = document };
        if (mode != 0)
            renderer.DocumentRenderer.ImagePaintRectangle = (actual, frame) =>
            {
                actual.Should().BeSameAs(image);
                return mode switch
                {
                    1 => new XRect(frame.X + 7, frame.Y + 11, 90, 20),
                    2 => frame,
                    3 => XRect.Empty,
                    4 => new XRect(double.NaN, frame.Y, frame.Width, frame.Height),
                    5 => new XRect(frame.X, frame.Y, 0, frame.Height),
                    _ => new XRect(frame.X, double.PositiveInfinity, frame.Width, frame.Height)
                };
            };
        XRect paint = default;
        renderer.DocumentRenderer.ImageWebLink = (_, frame) => { paint = frame; return "https://example.com"; };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var page = pdf.Pages[0];
        var operators = ContentReader.ReadContent(page).OfType<COperator>().ToArray();
        var drawIndex = Array.FindIndex(operators, op => op.OpCode.Name == "Do");
        drawIndex.Should().BeGreaterThan(0);
        var matrix = operators.Take(drawIndex).Last(op => op.OpCode.Name == "cm")
            .Operands.Select(value => value switch
            {
                CReal real => real.Value,
                CInteger integer => (double)integer.Value,
                _ => throw new InvalidOperationException()
            }).ToArray();
        var stream = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0);
        var text = new List<(string, double, double)>();
        while (stream.Text.MoveAndGetNext(true, out var item)) text.Add((item!.Text, item.X, item.Y));
        var layout = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!
            .Select(info => (info.LayoutInfo.ContentArea.X.Point, info.LayoutInfo.ContentArea.Y.Point,
                info.LayoutInfo.ContentArea.Width.Point, info.LayoutInfo.ContentArea.Height.Point)).ToArray();
        return new Result(paint, page.Annotations[0].Rectangle, matrix, text.ToArray(), layout);
    }

    record Result(XRect Paint, PdfRectangle Link, double[] Matrix,
        (string, double, double)[] Text, (double, double, double, double)[] Layout);
}
