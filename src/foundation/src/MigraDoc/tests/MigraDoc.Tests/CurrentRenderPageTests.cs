// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Quality;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class CurrentRenderPageTests : IDisposable
{
    public CurrentRenderPageTests()
    {
        PdfSharpCore.ResetAll();
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void ReportsPhysicalPageAndResetsAfterRendering(int page)
    {
        var renderer = CreateRenderer();
        renderer.CurrentRenderPage.Should().Be(0);
        var seen = new List<int>();
        renderer.PaintBehindContent = (_, physicalPage) =>
        {
            physicalPage.Should().Be(page);
            seen.Add(renderer.CurrentRenderPage);
        };
        renderer.PrepareDocument();
        renderer.CurrentRenderPage.Should().Be(0);
        using var pdf = new PdfDocument();
        using (var gfx = XGraphics.FromPdfPage(pdf.AddPage())) renderer.RenderPage(gfx, page);
        seen.Should().Equal(page);
        renderer.CurrentRenderPage.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedRenderingRestoresEnclosingPageEvenOnException(bool throwInside)
    {
        var renderer = CreateRenderer();
        renderer.PrepareDocument();
        var seen = new List<int>();
        var failure = new InvalidOperationException("Nested failure");
        renderer.PaintBehindContent = (_, page) =>
        {
            seen.Add(renderer.CurrentRenderPage);
            if (page == 1)
            {
                using var nestedPdf = new PdfDocument();
                using var nestedGfx = XGraphics.FromPdfPage(nestedPdf.AddPage());
                Action nested = () => renderer.RenderPage(nestedGfx, 2);
                if (throwInside) nested.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);
                else nested.Should().NotThrow();
                seen.Add(renderer.CurrentRenderPage);
            }
            else if (throwInside) throw failure;
        };
        using var pdf = new PdfDocument();
        using (var gfx = XGraphics.FromPdfPage(pdf.AddPage())) renderer.RenderPage(gfx, 1);
        seen.Should().Equal(1, 2, 1);
        renderer.CurrentRenderPage.Should().Be(0);
    }

    [Fact]
    public void TopLevelExceptionResetsContext()
    {
        var renderer = CreateRenderer();
        renderer.PrepareDocument();
        renderer.PaintBehindContent = (_, _) => throw new InvalidOperationException("Failure");
        using var pdf = new PdfDocument();
        using var gfx = XGraphics.FromPdfPage(pdf.AddPage());
        Action render = () => renderer.RenderPage(gfx, 1);
        render.Should().Throw<InvalidOperationException>();
        renderer.CurrentRenderPage.Should().Be(0);
    }

    [Fact]
    public void BlankPageDoesNotReplaceEnclosingContext()
    {
        var renderer = CreateRenderer(true);
        renderer.PrepareDocument();
        renderer.PaintBehindContent = (_, _) =>
        {
            using var blankPdf = new PdfDocument();
            using var blankGfx = XGraphics.FromPdfPage(blankPdf.AddPage());
            renderer.RenderPage(blankGfx, 2);
            renderer.CurrentRenderPage.Should().Be(1);
        };
        using var pdf = new PdfDocument();
        using (var gfx = XGraphics.FromPdfPage(pdf.AddPage())) renderer.RenderPage(gfx, 1);
        renderer.CurrentRenderPage.Should().Be(0);
    }

    [Fact]
    public void ObjectRenderingDoesNotEstablishPageRenderingContext()
    {
        var renderer = CreateRenderer();
        renderer.PrepareDocument();
        using var pdf = new PdfDocument();
        using (var gfx = XGraphics.FromPdfPage(pdf.AddPage()))
            renderer.RenderObject(gfx, 20, 30, 400, renderer.GetDocumentObjectsFromPage(1)[0], 2);
        renderer.CurrentRenderPage.Should().Be(0);
    }

    static DocumentRenderer CreateRenderer(bool blankPage = false)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        var section = document.AddSection();
        section.PageSetup.StartingNumber = 9;
        section.AddParagraph("First");
        if (blankPage)
        {
            section = document.AddSection();
            section.PageSetup.SectionStart = BreakType.BreakOddPage;
        }
        else section.AddPageBreak();
        section.AddParagraph("Second");
        return new DocumentRenderer(document);
    }
}
