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
public class ImagePaintClipTests : IDisposable
{
    const string Pixel = "base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==";
    public ImagePaintClipTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 1)]
    [InlineData(false, false, 2)]
    [InlineData(true, false, 2)]
    [InlineData(false, false, 3)]
    [InlineData(true, false, 3)]
    [InlineData(false, false, 4)]
    [InlineData(true, false, 4)]
    [InlineData(false, false, 5)]
    [InlineData(true, false, 5)]
    [InlineData(false, false, 6)]
    [InlineData(true, false, 6)]
    [InlineData(false, true, 1)]
    [InlineData(true, true, 1)]
    [InlineData(false, true, 2)]
    [InlineData(true, true, 2)]
    public void ClipPreservesLayoutAndLimitsLinkToVisibleImage(bool inline, bool missing, int mode)
    {
        var baseline = Render(inline, missing, 0);
        var result = Render(inline, missing, mode);
        result.Text.Select(t => t.Item1).Should().Equal(baseline.Text.Select(t => t.Item1));
        for (var i = 0; i < result.Text.Length; i++)
        {
            result.Text[i].Item2.Should().BeApproximately(baseline.Text[i].Item2, 0.001);
            result.Text[i].Item3.Should().BeApproximately(baseline.Text[i].Item3, 0.001);
        }
        result.Layout.Should().Equal(baseline.Layout);
        result.FinalTextDepth.Should().Be(baseline.FinalTextDepth);
        result.Paint.Should().Be(baseline.Paint);
        result.Clips.Should().Be(baseline.Clips + (mode is 1 or 2 ? 1 : 0));
        if (mode == 2)
        {
            result.Links.Should().BeEmpty();
            result.LinkCalls.Should().Be(0);
        }
        else
        {
            result.Links.Should().HaveCount(1);
            result.LinkCalls.Should().Be(1);
            if (mode == 1)
            {
                var original = baseline.Links[0];
                var clipped = result.Links[0];
                clipped.X1.Should().BeApproximately(original.X1 + 10, 0.001);
                clipped.Y2.Should().BeApproximately(original.Y2 - 5, 0.001);
                clipped.Width.Should().BeApproximately(20, 0.001);
                clipped.Height.Should().BeApproximately(15, 0.001);
            }
            else
                result.Links.Should().Equal(baseline.Links);
        }
    }

    static Result Render(bool inline, bool missing, int mode)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        var section = document.AddSection();
        var paragraph = section.AddParagraph("Before ");
        var path = missing ? "missing-image-clip-test.png" : Pixel;
        var image = inline ? paragraph.AddImage(path) : section.AddImage(path);
        image.Width = Unit.FromPoint(60);
        image.Height = Unit.FromPoint(40);
        if (inline) paragraph.AddText(" After");
        section.AddParagraph("Following");
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.ImagePaintRectangle = (_, frame) => new XRect(frame.X + 7, frame.Y + 11, 60, 40);
        XRect paint = default;
        if (mode != 0)
            renderer.DocumentRenderer.ImagePaintClipRectangle = (actual, frame) =>
            {
                actual.Should().BeSameAs(image);
                paint = frame;
                return mode switch
                {
                    1 => new XRect(frame.X + 10, frame.Y + 5, 20, 15),
                    2 => new XRect(frame.Right + 1, frame.Y, 20, 15),
                    3 => XRect.Empty,
                    4 => new XRect(double.NaN, frame.Y, 20, 15),
                    5 => new XRect(frame.X, frame.Y, 0, 15),
                    _ => null
                };
            };
        var linkCalls = 0;
        renderer.DocumentRenderer.ImageWebLink = (_, frame) =>
        {
            if (mode != 0) frame.Should().Be(paint);
            paint = frame;
            linkCalls++;
            return "https://example.com";
        };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var page = pdf.Pages[0];
        var ops = ContentReader.ReadContent(page).OfType<COperator>().ToArray();
        ops.Count(op => op.OpCode.Name == "q").Should().Be(ops.Count(op => op.OpCode.Name == "Q"));
        // The extra clipping scope must close before the following text is drawn.
        var clipIndex = Array.FindIndex(ops, op => op.OpCode.Name is "W" or "W*");
        if (mode is 1 or 2)
        {
            clipIndex.Should().BeGreaterThan(0);
            var restoreIndex = Array.FindIndex(ops, clipIndex, op => op.OpCode.Name == "Q");
            restoreIndex.Should().BeGreaterThan(clipIndex);
            ops.Skip(restoreIndex + 1).Should().Contain(op => op.OpCode.Name == "Tj" || op.OpCode.Name == "TJ");
        }
        var depth = 0;
        var finalTextDepth = 0;
        foreach (var op in ops)
        {
            if (op.OpCode.Name == "q") depth++;
            if (op.OpCode.Name == "Q") depth--;
            if (op.OpCode.Name is "Tj" or "TJ") finalTextDepth = depth;
        }
        var text = new List<(string, double, double)>();
        var stream = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0);
        while (stream.Text.MoveAndGetNext(true, out var item)) text.Add((item!.Text, item.X, item.Y));
        var layout = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!
            .Select(info => (info.LayoutInfo.ContentArea.X.Point, info.LayoutInfo.ContentArea.Y.Point,
                info.LayoutInfo.ContentArea.Width.Point, info.LayoutInfo.ContentArea.Height.Point)).ToArray();
        return new Result(paint, Enumerable.Range(0, page.Annotations.Count).Select(i => page.Annotations[i].Rectangle).ToArray(), linkCalls,
            ops.Count(op => op.OpCode.Name is "W" or "W*"), text.ToArray(), layout, finalTextDepth);
    }

    record Result(XRect Paint, PdfRectangle[] Links, int LinkCalls, int Clips,
        (string, double, double)[] Text, (double, double, double, double)[] Layout, int FinalTextDepth);
}
