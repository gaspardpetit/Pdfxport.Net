// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class InlineImageOverlayTests : IDisposable
{
    const string Pixel = "base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==";
    public InlineImageOverlayTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(0, false, true)]
    [InlineData(0, true, false)]
    [InlineData(0, true, true)]
    [InlineData(1, false, false)]
    [InlineData(1, false, true)]
    [InlineData(2, false, false)]
    [InlineData(2, false, true)]
    public void OverlayFollowsInlineImageAndCannotMoveFollowingText(int location, bool missing, bool transform)
    {
        var baseline = Render(location, missing, false, transform);
        var painted = Render(location, missing, true, transform);
        painted.Layout.Should().Equal(baseline.Layout);
        painted.FinalTextDepth.Should().Be(baseline.FinalTextDepth);
        var body = painted.Text.Where(t => t.Text != "Overlay").ToArray();
        body.Select(t => t.Text).Should().Equal(baseline.Text.Select(t => t.Text));
        for (var i = 0; i < body.Length; i++)
        {
            body[i].X.Should().BeApproximately(baseline.Text[i].X, 0.001);
            body[i].Y.Should().BeApproximately(baseline.Text[i].Y, 0.001);
        }
        if (location == 2)
        {
            painted.Calls.Should().Be(0);
            painted.Text.Should().NotContain(t => t.Text == "Overlay");
            return;
        }
        painted.Calls.Should().Be(1);
        painted.Frame.Should().Be(painted.LayoutFrame);
        painted.Frame.Width.Should().BeApproximately(60, 0.001);
        painted.Frame.Height.Should().BeApproximately(40, 0.001);
        var names = painted.Text.Select(t => t.Text).ToList();
        names.IndexOf("Overlay").Should().BeGreaterThan(names.IndexOf("Before"));
        names.IndexOf("After").Should().BeGreaterThan(names.IndexOf("Overlay"));
        names.IndexOf("Following").Should().BeGreaterThan(names.IndexOf("Overlay"));
        if (!missing)
            painted.ImageBeforeOverlay.Should().BeTrue();
    }

    static Result Render(int location, bool missing, bool overlay, bool transform)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        var section = document.AddSection();
        var paragraph = section.AddParagraph("Before ");
        var path = missing ? "missing-overlay-test.png" : Pixel;
        var image = location switch
        {
            1 => paragraph.AddFormattedText().AddImage(path),
            2 => section.AddImage(path),
            _ => paragraph.AddImage(path)
        };
        image.Width = Unit.FromPoint(60);
        image.Height = Unit.FromPoint(40);
        paragraph.AddText(" After");
        section.AddParagraph("Following");
        var renderer = new PdfDocumentRenderer { Document = document };
        XRect layoutFrame = default;
        renderer.DocumentRenderer.ImagePaintRectangle = (_, rect) =>
        {
            layoutFrame = rect;
            return new XRect(rect.X + 7, rect.Y + 11, 30, 20);
        };
        renderer.DocumentRenderer.ImagePaintClipRectangle = (_, rect) =>
            new XRect(rect.X, rect.Y, 10, 10);
        var calls = 0;
        XRect frame = default;
        if (overlay)
            renderer.DocumentRenderer.InlineImageOverlay = (actual, gfx, x, y, width, height) =>
            {
                actual.Should().BeSameAs(image);
                calls++;
                frame = new XRect(x, y, width, height);
                gfx.DrawString("Overlay", new XFont("Arial", 9), XBrushes.Red, x, y + 9);
                if (transform)
                {
                    // Deliberately leave changes for the renderer to restore.
                    gfx.TranslateTransform(70, 80);
                    gfx.ScaleTransform(2);
                    gfx.IntersectClip(new XRect(0, 0, 1, 1));
                }
            };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var stream = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0);
        var text = new List<(string Text, double X, double Y)>();
        while (stream.Text.MoveAndGetNext(true, out var item)) text.Add((item!.Text, item.X, item.Y));
        var layout = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!
            .Select(info => (info.LayoutInfo.ContentArea.X.Point, info.LayoutInfo.ContentArea.Y.Point,
                info.LayoutInfo.ContentArea.Width.Point, info.LayoutInfo.ContentArea.Height.Point)).ToArray();
        var ops = ContentReader.ReadContent(pdf.Pages[0]).OfType<COperator>().ToArray();
        ops.Count(op => op.OpCode.Name == "q").Should().Be(ops.Count(op => op.OpCode.Name == "Q"));
        // The first text draw after the image is the overlay, followed by body text.
        var imageIndex = Array.FindIndex(ops, op => op.OpCode.Name == "Do");
        var imageBeforeOverlay = imageIndex >= 0 && ops.Skip(imageIndex + 1)
            .Count(op => op.OpCode.Name is "Tj" or "TJ") >= 3;
        var depth = 0;
        var finalTextDepth = 0;
        foreach (var op in ops)
        {
            if (op.OpCode.Name == "q") depth++;
            if (op.OpCode.Name == "Q") depth--;
            if (op.OpCode.Name is "Tj" or "TJ") finalTextDepth = depth;
        }
        return new Result(text.ToArray(), layout, calls, frame, imageBeforeOverlay, layoutFrame, finalTextDepth);
    }

    record Result((string Text, double X, double Y)[] Text,
        (double, double, double, double)[] Layout, int Calls, XRect Frame, bool ImageBeforeOverlay, XRect LayoutFrame, int FinalTextDepth);
}
