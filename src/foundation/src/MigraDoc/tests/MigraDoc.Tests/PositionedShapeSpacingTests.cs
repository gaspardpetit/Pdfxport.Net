// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using MigraDoc.DocumentObjectModel.Shapes;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class PositionedShapeSpacingTests : IDisposable
{
    const string Pixel = "base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==";
    public PositionedShapeSpacingTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(false, false, 1, 30, 10)]
    [InlineData(false, false, 1, 10, 30)]
    [InlineData(false, false, 1, 0, 0)]
    [InlineData(false, false, 2, 30, 10)]
    [InlineData(false, false, 2, 10, 30)]
    [InlineData(false, false, 2, 0, 0)]
    [InlineData(false, true, 1, 30, 10)]
    [InlineData(false, true, 1, 10, 30)]
    [InlineData(false, true, 1, 0, 0)]
    [InlineData(false, true, 2, 30, 10)]
    [InlineData(false, true, 2, 10, 30)]
    [InlineData(false, true, 2, 0, 0)]
    [InlineData(true, false, 1, 30, 10)]
    [InlineData(true, false, 1, 10, 30)]
    [InlineData(true, false, 1, 0, 0)]
    [InlineData(true, false, 2, 30, 10)]
    [InlineData(true, false, 2, 10, 30)]
    [InlineData(true, false, 2, 0, 0)]
    [InlineData(true, true, 1, 30, 10)]
    [InlineData(true, true, 1, 10, 30)]
    [InlineData(true, true, 1, 0, 0)]
    [InlineData(true, true, 2, 30, 10)]
    [InlineData(true, true, 2, 10, 30)]
    [InlineData(true, true, 2, 0, 0)]
    public void PositionedShapesPreserveParagraphMarginCollapse(bool frame, bool page, int count, double after, double before)
    {
        var baseline = Render(frame, page, 0, after, before);
        var result = Render(frame, page, count, after, before);
        result.FirstY.Should().BeApproximately(baseline.FirstY, 0.001);
        result.SecondY.Should().BeApproximately(baseline.SecondY, 0.001);
        result.SecondTop.Should().BeApproximately(baseline.SecondTop, 0.001);
    }

    [Fact]
    public void ParagraphRelativeFlowingShapeStillAdvancesBody()
    {
        var baseline = Render(true, false, 0, 10, 10);
        var flowing = Render(true, false, 1, 10, 10, flowing: true);
        flowing.SecondY.Should().BeLessThan(baseline.SecondY);
    }

    static Result Render(bool frame, bool page, int count, double after, double before, bool flowing = false)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        var first = section.AddParagraph("First");
        first.Format.SpaceAfter = Unit.FromPoint(after);
        for (int i = 0; i < count; i++)
        {
            Shape shape;
            if (frame)
            {
                var textFrame = section.AddTextFrame();
                textFrame.AddParagraph("Decoration");
                shape = textFrame;
            }
            else shape = section.AddImage(Pixel);
            shape.RelativeHorizontal = RelativeHorizontal.Margin;
            shape.RelativeVertical = flowing ? RelativeVertical.Paragraph : page ? RelativeVertical.Page : RelativeVertical.Margin;
            shape.Left = Unit.FromPoint(flowing ? 0 : 300);
            shape.Top = Unit.FromPoint(flowing ? 0 : 400 + i * 20);
            shape.Width = Unit.FromPoint(50);
            shape.Height = Unit.FromPoint(20);
            shape.WrapFormat.Style = flowing ? WrapStyle.TopBottom : WrapStyle.None;
        }
        var second = section.AddParagraph("Second");
        second.Format.SpaceBefore = Unit.FromPoint(before);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        pdf.PageCount.Should().Be(1);
        var text = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        double firstY = 0, secondY = 0;
        while (text.MoveAndGetNext(true, out var item))
        {
            if (item!.Text == "First") firstY = item.Y;
            if (item.Text == "Second") secondY = item.Y;
        }
        firstY.Should().BeGreaterThan(secondY);
        secondY.Should().BeGreaterThan(0);
        var infos = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!;
        var info = infos.OfType<ParagraphRenderInfo>().Single(item => ReferenceEquals(item.DocumentObject, second));
        return new Result(firstY, secondY, info.LayoutInfo.ContentArea.Y.Point);
    }

    record Result(double FirstY, double SecondY, double SecondTop);
}
