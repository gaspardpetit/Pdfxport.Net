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
public class InlineImageDescentTests : IDisposable
{
    const string Pixel = "base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==";
    public InlineImageDescentTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(0, true)]
    public void SelectedTallImageIncludesDescentInItsSlot(int location, bool missing)
    {
        var baseline = Render(location, null, missing: missing);
        var selected = Render(location, true, missing: missing);
        selected.Height.Should().BeApproximately(50, 0.001);
        if (location == 1)
            selected.Height.Should().BeLessThan(baseline.Height);
        else
            selected.Height.Should().BeApproximately(baseline.Height, 0.001);
        selected.Frame.Should().Be(baseline.Frame);
        selected.FollowingY.Should().BeApproximately(baseline.FollowingY + baseline.Height - selected.Height, 0.001);
    }

    [Theory]
    [InlineData(LineSpacingRule.Double, 2)]
    [InlineData(LineSpacingRule.OnePtFive, 1.5)]
    [InlineData(LineSpacingRule.Multiple, 1.75)]
    public void MultipliedSpacingUsesTheSelectedSlot(LineSpacingRule rule, double factor)
    {
        var baseline = Render(1, null, rule);
        var selected = Render(1, true, rule);
        var singleDifference = Render(1, null).Height - Render(1, true).Height;
        selected.Height.Should().BeApproximately(baseline.Height - singleDifference * factor, 0.001);
        selected.FollowingY.Should().BeApproximately(baseline.FollowingY + singleDifference * factor, 0.001);
    }

    [Theory]
    [InlineData(LineSpacingRule.Exactly)]
    [InlineData(LineSpacingRule.AtLeast)]
    public void FixedSpacingPreservesAdvanceAndPicturePlacement(LineSpacingRule rule)
    {
        Render(0, true, rule).Should().BeEquivalentTo(Render(0, null, rule));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void FalsePolicyPreservesExistingLayout(int location)
    {
        Render(location, false).Should().BeEquivalentTo(Render(location, null));
    }

    [Theory]
    [InlineData(LineSpacingRule.Single)]
    [InlineData(LineSpacingRule.Double)]
    [InlineData(LineSpacingRule.AtLeast)]
    public void TallImageOnlyTableCellKeepsExistingDescentBehavior(LineSpacingRule rule)
    {
        Render(3, true, rule).Should().BeEquivalentTo(Render(3, null, rule));
    }

    [Fact]
    public void SmallSelectedImageRetainsFontMinimum()
    {
        var selected = Render(0, true, imageHeight: 5);
        var font = new XFont("Arial", 12);
        selected.Height.Should().BeApproximately(font.GetHeight(), 0.001);
        selected.Frame.Height.Should().BeApproximately(5, 0.001);
    }

    [Fact]
    public void EffectiveHeightAndEffectExtentsFormTheDescentSlot()
    {
        var selected = Render(0, true, layoutHeight: 30, padding: true);
        selected.Height.Should().BeApproximately(58, 0.001);
        selected.Frame.Height.Should().BeApproximately(50, 0.001);
    }

    static Result Render(int location, bool? selected, LineSpacingRule rule = LineSpacingRule.Single,
        bool missing = false, double imageHeight = 50, double? layoutHeight = null, bool padding = false)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        Paragraph paragraph;
        if (location == 3)
        {
            var table = section.AddTable();
            table.AddColumn(Unit.FromPoint(150));
            paragraph = table.AddRow().Cells[0].AddParagraph();
        }
        else paragraph = section.AddParagraph();
        paragraph.Format.LineSpacingRule = rule;
        if (rule != LineSpacingRule.Single)
            paragraph.Format.LineSpacing = Unit.FromPoint(rule == LineSpacingRule.Multiple ? 1.75 : 100);
        if (location == 1) paragraph.AddText("Before ");
        var container = location == 2 ? paragraph.AddFormattedText("") : null;
        var image = container != null
            ? container.AddImage(missing ? "missing-descent-test.png" : Pixel)
            : paragraph.AddImage(missing ? "missing-descent-test.png" : Pixel);
        image.Width = Unit.FromPoint(60);
        image.Height = Unit.FromPoint(imageHeight);
        if (location == 1) paragraph.AddText(" After");
        section.AddParagraph("Following");
        var renderer = new PdfDocumentRenderer { Document = document };
        if (selected.HasValue)
            renderer.DocumentRenderer.InlineImageOccupiesDescent = actual =>
            {
                actual.Should().BeSameAs(image);
                return selected.Value;
            };
        renderer.DocumentRenderer.InlineImageLayoutHeight = _ => layoutHeight;
        if (padding) renderer.DocumentRenderer.InlineImageEffectExtent = _ => (0, 11, 0, 17);
        XRect frame = default;
        renderer.DocumentRenderer.ImagePaintRectangle = (_, rect) => { frame = rect; return rect; };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var stream = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0);
        double followingY = 0;
        while (stream.Text.MoveAndGetNext(true, out var text))
            if (text!.Text == "Following") followingY = text.Y;
        followingY.Should().BeGreaterThan(0);
        return new Result(frame, renderer.DocumentRenderer.GetRenderInfoFromPage(1)![0].LayoutInfo.ContentArea.Height.Point,
            followingY);
    }

    record Result(XRect Frame, double Height, double FollowingY);
}
