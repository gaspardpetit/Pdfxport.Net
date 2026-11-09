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
public class ImageOnlyTableWrapperTests : IDisposable
{
    const string Pixel = "base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==";
    public ImageOnlyTableWrapperTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(1, LineSpacingRule.Single)]
    [InlineData(1, LineSpacingRule.Double)]
    [InlineData(1, LineSpacingRule.OnePtFive)]
    [InlineData(1, LineSpacingRule.Multiple)]
    [InlineData(1, LineSpacingRule.Exactly)]
    [InlineData(1, LineSpacingRule.AtLeast)]
    [InlineData(2, LineSpacingRule.Single)]
    [InlineData(2, LineSpacingRule.Double)]
    [InlineData(2, LineSpacingRule.OnePtFive)]
    [InlineData(2, LineSpacingRule.Multiple)]
    [InlineData(2, LineSpacingRule.Exactly)]
    [InlineData(2, LineSpacingRule.AtLeast)]
    [InlineData(3, LineSpacingRule.Single)]
    [InlineData(3, LineSpacingRule.Double)]
    [InlineData(3, LineSpacingRule.OnePtFive)]
    [InlineData(3, LineSpacingRule.Multiple)]
    [InlineData(3, LineSpacingRule.Exactly)]
    [InlineData(3, LineSpacingRule.AtLeast)]
    [InlineData(4, LineSpacingRule.Single)]
    [InlineData(4, LineSpacingRule.Double)]
    [InlineData(4, LineSpacingRule.OnePtFive)]
    [InlineData(4, LineSpacingRule.Multiple)]
    [InlineData(4, LineSpacingRule.Exactly)]
    [InlineData(4, LineSpacingRule.AtLeast)]
    [InlineData(5, LineSpacingRule.Single)]
    [InlineData(5, LineSpacingRule.Double)]
    [InlineData(5, LineSpacingRule.OnePtFive)]
    [InlineData(5, LineSpacingRule.Multiple)]
    [InlineData(5, LineSpacingRule.Exactly)]
    [InlineData(5, LineSpacingRule.AtLeast)]
    public void ImageOnlyCellMatchesExplicitDescentPolicy(int shape, LineSpacingRule rule)
    {
        var expected = Render(shape, rule, true);
        var result = Render(shape, rule, false);
        result.Should().BeEquivalentTo(expected);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    [InlineData(14)]
    [InlineData(50)]
    public void FormattingWrappersPreserveDirectImageCellGeometry(double height)
    {
        Render(2, imageHeight: height).Should().BeEquivalentTo(Render(0, imageHeight: height));
    }

    [Theory]
    [InlineData(SymbolName.Blank)]
    [InlineData(SymbolName.En)]
    [InlineData(SymbolName.Em)]
    [InlineData(SymbolName.EmQuarter)]
    [InlineData(SymbolName.NonBreakableBlank)]
    [InlineData(SymbolName.Tab)]
    public void ExplicitWhitespaceCharactersKeepImageOnlyCellBehavior(SymbolName space)
    {
        Render(0, space: space).Should().BeEquivalentTo(Render(0, selectDescent: true, space: space));
    }

    [Fact]
    public void VisibleTextStillReservesItsFontSpace()
    {
        var result = Render(6);
        result.Height.Should().BeGreaterThan(Render(6, selectDescent: true).Height);
    }

    static Result Render(int shape, LineSpacingRule rule = LineSpacingRule.Single,
        bool selectDescent = false, double imageHeight = 50, SymbolName? space = null)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        var table = section.AddTable();
        table.AddColumn(Unit.FromPoint(300));
        var paragraph = table.AddRow().Cells[0].AddParagraph();
        paragraph.Format.LineSpacingRule = rule;
        if (rule != LineSpacingRule.Single)
            paragraph.Format.LineSpacing = Unit.FromPoint(rule == LineSpacingRule.Multiple ? 1.75 : 100);
        if (space.HasValue) paragraph.AddCharacter(space.Value);
        if (shape == 3) paragraph.AddText(" ");
        if (shape == 6) paragraph.AddText("Visible ");
        var run = shape is 1 or 2 or 5 ? paragraph.AddFormattedText("") : null;
        if (shape == 2) run = run!.AddFormattedText("");
        var image = run != null ? run.AddImage(Pixel) : paragraph.AddImage(Pixel);
        image.Width = Unit.FromPoint(30);
        image.Height = Unit.FromPoint(imageHeight);
        if (shape is 4 or 5)
        {
            paragraph.AddTab();
            var second = paragraph.AddImage(Pixel);
            second.Width = Unit.FromPoint(30);
            second.Height = Unit.FromPoint(imageHeight);
        }
        if (shape == 3) paragraph.AddText(" ");
        section.AddParagraph("Following");
        var renderer = new PdfDocumentRenderer { Document = document };
        if (selectDescent) renderer.DocumentRenderer.InlineImageOccupiesDescent = _ => true;
        var frames = new List<XRect>();
        renderer.DocumentRenderer.ImagePaintRectangle = (_, rect) => { frames.Add(rect); return rect; };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var stream = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0);
        double followingY = 0;
        while (stream.Text.MoveAndGetNext(true, out var text))
            if (text!.Text == "Following") followingY = text.Y;
        followingY.Should().BeGreaterThan(0);
        frames.Should().HaveCount(shape is 4 or 5 ? 2 : 1);
        return new Result(frames.ToArray(), renderer.DocumentRenderer.GetRenderInfoFromPage(1)![0].LayoutInfo.ContentArea.Height.Point,
            followingY);
    }

    record Result(XRect[] Frames, double Height, double FollowingY);
}
