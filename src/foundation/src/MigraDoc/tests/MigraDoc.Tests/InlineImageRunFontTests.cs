// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class InlineImageRunFontTests : IDisposable
{
    const string Pixel = "base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==";
    public InlineImageRunFontTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(8, 10, false)]
    [InlineData(24, 10, false)]
    [InlineData(8, 50, false)]
    [InlineData(24, 50, false)]
    [InlineData(8, 10, true)]
    [InlineData(24, 10, true)]
    [InlineData(8, 50, true)]
    [InlineData(24, 50, true)]
    public void SelectedFontMatchesExplicitImageRun(double size, double imageHeight, bool missing)
    {
        var expected = Render(size, imageHeight, missing, false);
        var selected = Render(size, imageHeight, missing, true);
        selected.Height.Should().BeApproximately(expected.Height, 0.001);
        selected.Frame.Should().Be(expected.Frame);
        selected.Text.Should().Equal(expected.Text);
        selected.Positions.Should().Equal(expected.Positions);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(24)]
    public void NullSelectionPreservesContainingRun(double size)
    {
        var expected = Render(size, 10, false, false);
        var selected = Render(size, 10, false, false, nullPolicy: true);
        selected.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void LargerSelectedFontChangesLineMetricsWithoutResizingPicture()
    {
        var baseline = Render(12, 10, false, false);
        var selected = Render(24, 10, false, true);
        selected.Height.Should().BeGreaterThan(baseline.Height);
        selected.Frame.Width.Should().BeApproximately(60, 0.001);
        selected.Frame.Height.Should().BeApproximately(10, 0.001);
        selected.Frame.X.Should().BeApproximately(baseline.Frame.X, 0.001);
        selected.Positions[^1].Y.Should().BeLessThan(baseline.Positions[^1].Y);
    }

    static Result Render(double size, double imageHeight, bool missing, bool select, bool nullPolicy = false)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        var paragraph = section.AddParagraph();
        paragraph.AddText("Before ");
        var run = paragraph.AddFormattedText("");
        run.Font.Name = "Arial";
        run.Font.Size = select ? 12 : size;
        var image = run.AddImage(missing ? "missing-run-font-test.png" : Pixel);
        image.Width = Unit.FromPoint(60);
        image.Height = Unit.FromPoint(imageHeight);
        paragraph.AddText(" After");
        section.AddParagraph("Following");
        var selectedFont = new Font("Arial", Unit.FromPoint(size));
        var renderer = new PdfDocumentRenderer { Document = document };
        int calls = 0;
        if (select || nullPolicy)
            renderer.DocumentRenderer.InlineImageRunFont = actual =>
            {
                actual.Should().BeSameAs(image);
                calls++;
                return select ? selectedFont : null;
            };
        XRect frame = default;
        renderer.DocumentRenderer.ImagePaintRectangle = (_, rect) => { frame = rect; return rect; };
        renderer.RenderDocument();
        if (select || nullPolicy) calls.Should().BeGreaterThan(0);
        using var pdf = renderer.PdfDocument;
        var stream = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0);
        var words = new List<string>();
        var positions = new List<(double X, double Y)>();
        while (stream.Text.MoveAndGetNext(true, out var text))
        {
            words.Add(text!.Text);
            positions.Add((text.X, text.Y));
        }
        words.Should().Contain("Before").And.Contain("After").And.Contain("Following");
        return new Result(frame, renderer.DocumentRenderer.GetRenderInfoFromPage(1)![0].LayoutInfo.ContentArea.Height.Point,
            words.ToArray(), positions.ToArray());
    }

    record Result(XRect Frame, double Height, string[] Text, (double X, double Y)[] Positions);
}
