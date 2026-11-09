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
public class LineBoxBottomTests : IDisposable
{
    const string Pixel = "base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==";
    public LineBoxBottomTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 0)]
    [InlineData(true, false, 7)]
    [InlineData(false, true, 0)]
    [InlineData(true, true, 0)]
    [InlineData(true, true, 7)]
    public void PreparedBottomsMatchPaintedBaselines(bool border, bool split, double distance)
    {
        var renderer = Create(border, split, distance, false);
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var font = new XFont("Arial", 12);
        var descent = font.Metrics.Descent * 20.0 / font.FontFamily.GetEmHeight(font.Style);
        int lines = 0;
        for (int page = 0; page < pdf.PageCount; page++)
        {
            var paragraph = renderer.DocumentRenderer.GetRenderInfoFromPage(page + 1)!.OfType<ParagraphRenderInfo>().Single();
            var text = PdfFileHelper.GetPageContentStreamEnumerator(pdf, page).Text;
            for (int line = 0; line < paragraph.LineCount; line++)
            {
                text.MoveAndGetNext(true, out var painted).Should().BeTrue();
                paragraph.GetLineBoxBottom(line).Should().BeApproximately(pdf.Pages[page].Height.Point - painted!.Y + descent, 0.001);
                lines++;
            }
        }
        lines.Should().Be(split ? 12 : 3);
        if (split) pdf.PageCount.Should().BeGreaterThan(1);
    }

    [Fact]
    public void GapBelowWrappingImageIsIncluded()
    {
        var renderer = Create(false, false, 0, true);
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var paragraph = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!.OfType<ParagraphRenderInfo>().Single();
        paragraph.LineCount.Should().Be(3);
        (paragraph.GetLineBoxBottom(2) - paragraph.GetLineBoxBottom(1)).Should().BeGreaterThan(20);
        var text = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        var font = new XFont("Arial", 12);
        var descent = font.Metrics.Descent * 20.0 / font.FontFamily.GetEmHeight(font.Style);
        for (int line = 0; line < paragraph.LineCount; line++)
        {
            text.MoveAndGetNext(true, out var painted).Should().BeTrue();
            paragraph.GetLineBoxBottom(line).Should().BeApproximately(pdf.Pages[0].Height.Point - painted!.Y + descent, 0.001);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(int.MaxValue)]
    public void RejectsIndicesOutsideThePreparedFragment(int index)
    {
        var renderer = Create(false, false, 0, false);
        renderer.DocumentRenderer.PrepareDocument();
        var paragraph = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!.OfType<ParagraphRenderInfo>().Single();
        Action act = () => paragraph.GetLineBoxBottom(index);
        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("lineIndex");
        paragraph.GetLineBoxBottom(2).Should().BeApproximately(paragraph.LayoutInfo.ContentArea.Y.Point + 60, 0.001);
    }

    static PdfDocumentRenderer Create(bool border, bool split, double distance, bool wrap)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        section.PageSetup.PageWidth = Unit.FromPoint(280);
        section.PageSetup.PageHeight = Unit.FromPoint(split ? 160 : 400);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(40);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromPoint(40);
        if (wrap)
        {
            var image = section.AddImage(Pixel);
            image.RelativeHorizontal = MigraDoc.DocumentObjectModel.Shapes.RelativeHorizontal.Margin;
            image.RelativeVertical = MigraDoc.DocumentObjectModel.Shapes.RelativeVertical.Margin;
            image.Left = Unit.FromPoint(0);
            image.Top = Unit.FromPoint(40);
            image.Width = Unit.FromPoint(200);
            image.Height = Unit.FromPoint(40);
            image.WrapFormat.Style = MigraDoc.DocumentObjectModel.Shapes.WrapStyle.BothSides;
        }
        var paragraph = section.AddParagraph();
        paragraph.Format.KeepTogether = false;
        paragraph.Format.WidowControl = false;
        paragraph.Format.LineSpacingRule = LineSpacingRule.Exactly;
        paragraph.Format.LineSpacing = Unit.FromPoint(20);
        for (int line = 0; line < (split ? 12 : 3); line++)
        {
            if (line > 0) paragraph.AddLineBreak();
            paragraph.AddText($"Line{line:D2}");
        }
        if (border)
        {
            paragraph.Format.Borders.Top.Width = Unit.FromPoint(1);
            paragraph.Format.Borders.DistanceFromTop = Unit.FromPoint(3);
        }
        var renderer = new PdfDocumentRenderer { Document = document };
        if (border)
            renderer.DocumentRenderer.TopBorderDistanceAtAreaStart = (_, _, _) => distance;
        return renderer;
    }
}
