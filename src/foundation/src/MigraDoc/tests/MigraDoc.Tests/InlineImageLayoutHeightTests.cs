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
public class InlineImageLayoutHeightTests : IDisposable
{
    const string Pixel = "base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==";
    public InlineImageLayoutHeightTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(0, 30)]
    [InlineData(1, 30)]
    [InlineData(2, 30)]
    [InlineData(0, 70)]
    [InlineData(1, 70)]
    [InlineData(2, 70)]
    [InlineData(0, 50)]
    [InlineData(0, 0)]
    [InlineData(0, -1)]
    [InlineData(0, double.NaN)]
    [InlineData(0, double.PositiveInfinity)]
    public void EffectiveHeightChangesLineAdvanceButKeepsFullPicture(int location, double height)
    {
        var baseline = Render(location, null);
        var result = Render(location, height);
        var effective = height > 0 && !double.IsInfinity(height) && !double.IsNaN(height) ? height : 50;
        result.Frame.Width.Should().BeApproximately(60, 0.001);
        result.Frame.Height.Should().BeApproximately(50, 0.001);
        result.Frame.X.Should().BeApproximately(baseline.Frame.X, 0.001);
        result.Frame.Y.Should().BeApproximately(baseline.Frame.Y, 0.001);
        result.FirstHeight.Should().BeApproximately(baseline.FirstHeight + effective - 50, 0.001);
        result.FollowingY.Should().BeApproximately(baseline.FollowingY - (effective - 50), 0.001);
        result.Text.Should().Equal(baseline.Text);
    }

    [Theory]
    [InlineData(30)]
    [InlineData(70)]
    public void FailurePlaceholderUsesSameEffectiveSlot(double height)
    {
        var baseline = Render(0, null, true);
        var result = Render(0, height, true);
        result.Frame.Should().Be(baseline.Frame);
        result.FirstHeight.Should().BeApproximately(baseline.FirstHeight + height - 50, 0.001);
        result.FollowingY.Should().BeApproximately(baseline.FollowingY - (height - 50), 0.001);
    }

    [Theory]
    [InlineData(LineSpacingRule.Exactly, 30)]
    [InlineData(LineSpacingRule.Exactly, 70)]
    [InlineData(LineSpacingRule.AtLeast, 30)]
    [InlineData(LineSpacingRule.AtLeast, 70)]
    public void ParagraphSpacingRulesStillControlLineAdvance(LineSpacingRule rule, double height)
    {
        var baseline = Render(0, null, rule: rule);
        var result = Render(0, height, rule: rule);
        result.FirstHeight.Should().BeApproximately(baseline.FirstHeight, 0.001);
        result.FollowingY.Should().BeApproximately(baseline.FollowingY, 0.001);
        // With a fixed baseline the effective slot, not the full picture's
        // physical height, determines the picture's top edge.
        result.Frame.Y.Should().BeApproximately(baseline.Frame.Y + 50 - height, 0.001);
        result.Frame.Height.Should().BeApproximately(50, 0.001);
    }

    [Theory]
    [InlineData(LineSpacingRule.Double, 0, 30)]
    [InlineData(LineSpacingRule.Double, 0, 70)]
    [InlineData(LineSpacingRule.Double, 1, 30)]
    [InlineData(LineSpacingRule.Double, 1, 70)]
    [InlineData(LineSpacingRule.Double, 2, 30)]
    [InlineData(LineSpacingRule.Double, 2, 70)]
    [InlineData(LineSpacingRule.OnePtFive, 0, 30)]
    [InlineData(LineSpacingRule.OnePtFive, 0, 70)]
    [InlineData(LineSpacingRule.OnePtFive, 1, 30)]
    [InlineData(LineSpacingRule.OnePtFive, 1, 70)]
    [InlineData(LineSpacingRule.OnePtFive, 2, 30)]
    [InlineData(LineSpacingRule.OnePtFive, 2, 70)]
    [InlineData(LineSpacingRule.Multiple, 0, 30)]
    [InlineData(LineSpacingRule.Multiple, 0, 70)]
    [InlineData(LineSpacingRule.Multiple, 1, 30)]
    [InlineData(LineSpacingRule.Multiple, 1, 70)]
    [InlineData(LineSpacingRule.Multiple, 2, 30)]
    [InlineData(LineSpacingRule.Multiple, 2, 70)]
    public void MultipliedSpacingUsesEffectiveHeight(LineSpacingRule rule, int location, double height)
    {
        var baseline = Render(location, null, rule: rule, physicalHeight: height);
        var result = Render(location, height, rule: rule);
        result.FirstHeight.Should().BeApproximately(baseline.FirstHeight, 0.001);
        result.FollowingY.Should().BeApproximately(baseline.FollowingY, 0.001);
        result.Text.Should().Equal(baseline.Text);
        result.Frame.Height.Should().BeApproximately(50, 0.001);
    }

    static Result Render(int location, double? height, bool missing = false, LineSpacingRule rule = LineSpacingRule.Single, double physicalHeight = 50)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        Paragraph paragraph;
        if (location == 2)
        {
            var table = section.AddTable();
            table.AddColumn(Unit.FromPoint(150));
            paragraph = table.AddRow().Cells[0].AddParagraph();
        }
        else
            paragraph = section.AddParagraph();
        paragraph.Format.LineSpacingRule = rule;
        if (rule != LineSpacingRule.Single)
            paragraph.Format.LineSpacing = Unit.FromPoint(rule == LineSpacingRule.Multiple ? 1.75 : 100);
        if (location == 1) paragraph.AddText("Before ");
        var image = paragraph.AddImage(missing ? "missing-layout-height-test.png" : Pixel);
        image.Width = Unit.FromPoint(60);
        image.Height = Unit.FromPoint(physicalHeight);
        if (location == 1) paragraph.AddText(" After");
        section.AddParagraph("Following");
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.InlineImageLayoutHeight = actual =>
        {
            actual.Should().BeSameAs(image);
            return height;
        };
        XRect frame = default;
        renderer.DocumentRenderer.ImagePaintRectangle = (_, rect) => { frame = rect; return rect; };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var stream = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0);
        var words = new List<string>();
        double followingY = 0;
        while (stream.Text.MoveAndGetNext(true, out var text))
        {
            words.Add(text!.Text);
            if (text.Text == "Following") followingY = text.Y;
        }
        followingY.Should().BeGreaterThan(0);
        return new Result(frame, renderer.DocumentRenderer.GetRenderInfoFromPage(1)![0].LayoutInfo.ContentArea.Height.Point,
            followingY, words.ToArray());
    }

    record Result(XRect Frame, double FirstHeight, double FollowingY, string[] Text);
}
