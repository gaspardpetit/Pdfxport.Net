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
public class InlineImageEffectExtentTests : IDisposable
{
    const string Pixel = "base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==";
    public InlineImageEffectExtentTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(0, LineSpacingRule.Single, false)]
    [InlineData(1, LineSpacingRule.Single, false)]
    [InlineData(2, LineSpacingRule.Single, false)]
    [InlineData(0, LineSpacingRule.Double, false)]
    [InlineData(1, LineSpacingRule.OnePtFive, false)]
    [InlineData(2, LineSpacingRule.Multiple, false)]
    [InlineData(0, LineSpacingRule.Single, true)]
    public void PaddingReservesSpaceAndKeepsPictureSize(int location, LineSpacingRule rule, bool missing)
    {
        var baseline = Render(location, null, rule, missing);
        var padded = Render(location, (7, 11, 13, 17), rule, missing);
        var slotSized = Render(location, null, rule, missing, physicalHeight: 78);
        padded.Height.Should().BeApproximately(slotSized.Height, 0.001);
        padded.FollowingY.Should().BeApproximately(slotSized.FollowingY, 0.001);
        padded.Frame.Width.Should().BeApproximately(60, 0.001);
        padded.Frame.Height.Should().BeApproximately(50, 0.001);
        padded.Frame.X.Should().BeApproximately(baseline.Frame.X + 7, 0.001);
        if (rule == LineSpacingRule.Single)
            padded.Frame.Y.Should().BeApproximately(baseline.Frame.Y + 11, 0.001);
        if (location == 1)
            padded.AfterX.Should().BeApproximately(baseline.AfterX + 20, 0.001);
        padded.Overlay.Should().Be(padded.Frame);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SmallImagePaddingUsesTheSameSlotMetrics(int location)
    {
        var padded = Render(location, (0, 2, 0, 2), physicalHeight: 10);
        var slotSized = Render(location, null, physicalHeight: 14);
        padded.Height.Should().BeApproximately(slotSized.Height, 0.001);
        padded.FollowingY.Should().BeApproximately(slotSized.FollowingY, 0.001);
        padded.Frame.Y.Should().BeApproximately(slotSized.Frame.Y + 2, 0.001);
    }

    [Theory]
    [InlineData(LineSpacingRule.Exactly)]
    [InlineData(LineSpacingRule.AtLeast)]
    public void FixedSpacingStillControlsLineAdvance(LineSpacingRule rule)
    {
        var baseline = Render(0, null, rule);
        var padded = Render(0, (7, 11, 13, 17), rule);
        padded.Height.Should().BeApproximately(baseline.Height, 0.001);
        padded.FollowingY.Should().BeApproximately(baseline.FollowingY, 0.001);
        padded.Frame.Y.Should().BeApproximately(baseline.Frame.Y - 17, 0.001);
    }

    [Fact]
    public void PaddingCombinesWithEffectiveLayoutHeight()
    {
        var padded = Render(0, (0, 11, 0, 17), layoutHeight: 30);
        var slotSized = Render(0, null, physicalHeight: 58);
        padded.Height.Should().BeApproximately(slotSized.Height, 0.001);
        padded.FollowingY.Should().BeApproximately(slotSized.FollowingY, 0.001);
        padded.Frame.Height.Should().BeApproximately(50, 0.001);
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]
    [InlineData(0, -1, 0, 0)]
    [InlineData(0, 0, -1, 0)]
    [InlineData(0, 0, 0, -1)]
    [InlineData(double.NaN, 0, 0, 0)]
    [InlineData(0, double.PositiveInfinity, 0, 0)]
    [InlineData(0, 0, double.NegativeInfinity, 0)]
    [InlineData(double.MaxValue, 0, double.MaxValue, 0)]
    [InlineData(0, 0, 0, 0)]
    public void InvalidOrZeroExtentsPreserveOriginalLayout(double left, double top, double right, double bottom)
    {
        Render(1, (left, top, right, bottom)).Should().BeEquivalentTo(Render(1, null));
    }

    [Fact]
    public void HorizontalPaddingParticipatesInWrapping()
    {
        var padded = Render(1, (10, 0, 30, 0), narrow: true);
        var wider = Render(1, null, physicalWidth: 100, narrow: true);
        padded.Height.Should().BeGreaterThan(Render(1, null, narrow: true).Height);
        padded.Height.Should().BeApproximately(wider.Height, 0.001);
        padded.FollowingY.Should().BeApproximately(wider.FollowingY, 0.001);
        padded.AfterX.Should().BeApproximately(wider.AfterX, 0.001);
    }

    static Result Render(int location, (double Left, double Top, double Right, double Bottom)? extent,
        LineSpacingRule rule = LineSpacingRule.Single, bool missing = false,
        double physicalHeight = 50, double? layoutHeight = null, double physicalWidth = 60, bool narrow = false)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        if (narrow)
        {
            section.PageSetup.PageWidth = Unit.FromPoint(230);
            section.PageSetup.LeftMargin = Unit.FromPoint(50);
            section.PageSetup.RightMargin = Unit.FromPoint(50);
        }
        Paragraph paragraph;
        if (location == 2)
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
        var image = paragraph.AddImage(missing ? "missing-effect-extent-test.png" : Pixel);
        image.Width = Unit.FromPoint(physicalWidth);
        image.Height = Unit.FromPoint(physicalHeight);
        if (location == 1) paragraph.AddText(" After");
        section.AddParagraph("Following");
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.InlineImageEffectExtent = actual =>
        {
            actual.Should().BeSameAs(image);
            return extent;
        };
        renderer.DocumentRenderer.InlineImageLayoutHeight = _ => layoutHeight;
        XRect frame = default, overlay = default;
        renderer.DocumentRenderer.ImagePaintRectangle = (_, rect) => { frame = rect; return rect; };
        renderer.DocumentRenderer.InlineImageOverlay = (_, _, x, y, w, h) => overlay = new XRect(x, y, w, h);
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var stream = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0);
        double followingY = 0, afterX = 0;
        while (stream.Text.MoveAndGetNext(true, out var text))
        {
            if (text!.Text == "Following") followingY = text.Y;
            if (text.Text == "After") afterX = text.X;
        }
        followingY.Should().BeGreaterThan(0);
        return new Result(frame, overlay, renderer.DocumentRenderer.GetRenderInfoFromPage(1)![0].LayoutInfo.ContentArea.Height.Point,
            followingY, afterX);
    }

    record Result(XRect Frame, XRect Overlay, double Height, double FollowingY, double AfterX);
}
