// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class DeferredFramePaintTests : IDisposable
{
    public DeferredFramePaintTests()
    {
        PdfSharpCore.ResetAll();
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DefersBodyFrameInEveryLayer(int layer)
    {
        var (renderer, frame) = CreateRenderer(0);
        if (layer >= 0) frame.Layer = (TextFrameLayer)layer;
        var calls = 0;
        renderer.DeferTextFramePaint = candidate =>
        {
            candidate.Should().BeSameAs(frame);
            renderer.CurrentRenderPage.Should().Be(1);
            calls++;
            return true;
        };
        renderer.PrepareDocument();
        calls.Should().Be(0);
        var before = renderer.GetRenderInfoFromPage(1)!;
        using var pdf = Render(renderer);
        Texts(pdf).Should().Contain("Body").And.NotContain("Frame");
        calls.Should().Be(1);
        renderer.GetRenderInfoFromPage(1).Should().Equal(before);
        renderer.FormattedDocument!.PageCount.Should().Be(1);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void NullAndFalseSelectorsPreserveNormalPainting(int location, bool selector)
    {
        var (renderer, _) = CreateRenderer(location);
        if (selector) renderer.DeferTextFramePaint = _ => false;
        renderer.PrepareDocument();
        using var pdf = Render(renderer);
        Texts(pdf).Should().Contain("Body").And.Contain("Frame");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void DefersHeaderFooterWithoutMovingOtherFooterContent(int location)
    {
        var (renderer, frame) = CreateRenderer(location);
        renderer.PrepareDocument();
        using var baseline = Render(renderer);
        var baselineY = FooterY(baseline);
        renderer.DeferTextFramePaint = candidate => ReferenceEquals(candidate, frame);
        using var deferred = Render(renderer);
        Texts(deferred).Should().NotContain("Frame").And.Contain("Footer");
        FooterY(deferred).Should().Be(baselineY);
        renderer.CurrentRenderPage.Should().Be(0);
    }

    [Fact]
    public void ExplicitPreparedPaintingBypassesDeferral()
    {
        var (renderer, frame) = CreateRenderer(0);
        renderer.DeferTextFramePaint = candidate => ReferenceEquals(candidate, frame);
        renderer.PrepareDocument();
        var fragment = renderer.GetRenderInfoFromPage(1)!.Single(info => ReferenceEquals(info.DocumentObject, frame));
        renderer.PaintBehindContent = (gfx, page) => renderer.RenderPreparedObject(gfx, fragment, page, 0);
        using var pdf = Render(renderer);
        Texts(pdf).Count(text => text == "Frame").Should().Be(1);
        Texts(pdf).Should().Equal("Footer", "Frame", "Body");
    }

    [Fact]
    public void SelectionFailureRestoresPageContext()
    {
        var (renderer, _) = CreateRenderer(0); renderer.PrepareDocument();
        var expected = new InvalidOperationException("Selection failure");
        renderer.DeferTextFramePaint = _ => throw expected;
        using var pdf = new PdfDocument(); using var gfx = XGraphics.FromPdfPage(pdf.AddPage());
        Action paint = () => renderer.RenderPage(gfx, 1);
        paint.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(expected);
        renderer.CurrentRenderPage.Should().Be(0);
    }

    static (DocumentRenderer Renderer, TextFrame Frame) CreateRenderer(int location)
    {
        var document = new Document(); document.Styles.Normal.Font.Name = "Arial";
        var section = document.AddSection(); section.AddParagraph("Body");
        var frame = location == 0 ? section.AddTextFrame() :
            location == 1 ? section.Headers.Primary.AddTextFrame() : section.Footers.Primary.AddTextFrame();
        frame.RelativeVertical = RelativeVertical.Page; frame.Top = Unit.FromPoint(100);
        frame.Width = Unit.FromPoint(100); frame.Height = Unit.FromPoint(40);
        frame.AddParagraph("Frame");
        section.Footers.Primary.AddParagraph("Footer");
        return (new DocumentRenderer(document), frame);
    }
    static PdfDocument Render(DocumentRenderer renderer)
    {
        var pdf = new PdfDocument();
        using var gfx = XGraphics.FromPdfPage(pdf.AddPage()); renderer.RenderPage(gfx, 1);
        return pdf;
    }
    static string[] Texts(PdfDocument pdf)
    {
        var stream = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0); var texts = new List<string>();
        while (stream.Text.MoveAndGetNext(true, out var item))
            if (!string.IsNullOrWhiteSpace(item!.Text)) texts.Add(item.Text.Trim());
        return texts.ToArray();
    }
    static double FooterY(PdfDocument pdf)
    {
        var stream = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0);
        stream.Text.MoveAndGetNext(info => info.Text == "Footer", true, out var text).Should().BeTrue();
        return text!.Y;
    }
}
