using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.IO;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class ConsumeParagraphSpacingTests : IDisposable
{
    public ConsumeParagraphSpacingTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(false, RelativeVertical.Page, 24, 10)]
    [InlineData(true, RelativeVertical.Page, 24, 10)]
    [InlineData(false, RelativeVertical.Margin, 24, 10)]
    [InlineData(true, RelativeVertical.Margin, 24, 10)]
    [InlineData(false, RelativeVertical.Page, 24, 0)]
    [InlineData(true, RelativeVertical.Margin, 10, 24)]
    public void ConsumesOnlyPendingPrecedingSpacing(bool frame, RelativeVertical anchor, double after, double before)
    {
        var baseline = Prepare(false, frame, anchor, after, before);
        var consumed = Prepare(true, frame, anchor, after, before);
        baseline.FollowingY.Should().BeApproximately(30 + Math.Max(after, before), 0.001);
        consumed.FollowingY.Should().BeApproximately(30 + before, 0.001);
        consumed.ShapeY.Should().Be(baseline.ShapeY);
        consumed.ShapePage.Should().Be(baseline.ShapePage);
        consumed.PreviousY.Should().Be(baseline.PreviousY);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearingPendingSpacingAvoidsAnUnnecessaryPageBreak(bool frame)
    {
        var baseline = Prepare(false, frame, RelativeVertical.Margin, 90, 0, pageHeight: 140);
        var consumed = Prepare(true, frame, RelativeVertical.Margin, 90, 0, pageHeight: 140);
        baseline.FollowingPage.Should().Be(2);
        consumed.FollowingPage.Should().Be(1);
        consumed.FollowingY.Should().BeApproximately(30, 0.001);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FlowingShapeKeepsItsOwnTrailingSpacing(bool frame)
    {
        var baseline = Prepare(false, frame, RelativeVertical.Paragraph, 24, 5, flowing: true);
        var consumed = Prepare(true, frame, RelativeVertical.Paragraph, 24, 5, flowing: true);
        consumed.Should().Be(baseline);
        consumed.FollowingY.Should().BeApproximately(consumed.ShapeY + 30 + 12, 0.001);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LaterPositionedShapeDoesNotRestoreConsumedSpacing(bool frame)
    {
        var result = Prepare(true, frame, RelativeVertical.Margin, 24, 10, secondShape: true);
        result.FollowingY.Should().BeApproximately(40, 0.001);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void NullableValueSurvivesCloneAndDdl(bool? enabled)
    {
        var doc = new Document();
        var frame = doc.AddSection().AddTextFrame();
        frame.AddParagraph("Content");
        if (enabled.HasValue) frame.WrapFormat.ConsumePreviousParagraphSpacing = enabled.Value;
        frame.WrapFormat.ConsumePreviousParagraphSpacing.Should().Be(enabled ?? false);
        frame.Clone().WrapFormat.Values.ConsumePreviousParagraphSpacing.Should().Be(enabled);
        var ddl = DdlWriter.WriteToString(doc);
        ddl.Contains("ConsumePreviousParagraphSpacing").Should().Be(enabled.HasValue);
        var restored = (TextFrame)DdlReader.DocumentFromString(ddl)!.Sections[0].Elements[0];
        restored.WrapFormat.Values.ConsumePreviousParagraphSpacing.Should().Be(enabled);
    }

    record Geometry(double PreviousY, double ShapeY, int ShapePage, double FollowingY, int FollowingPage);
    static Geometry Prepare(bool enabled, bool frame, RelativeVertical anchor, double after, double before,
        bool flowing = false, double pageHeight = 300, bool secondShape = false)
    {
        var doc = new Document();
        doc.Styles.Normal.Font.Name = "Arial";
        doc.Styles.Normal.Font.Size = 8;
        doc.Styles.Normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Exactly;
        doc.Styles.Normal.ParagraphFormat.LineSpacing = Unit.FromPoint(10);
        var section = doc.AddSection();
        section.PageSetup.PageHeight = Unit.FromPoint(pageHeight);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromPoint(20);
        var previous = section.AddParagraph("Before");
        previous.Format.SpaceAfter = Unit.FromPoint(after);
        Shape shape = frame ? section.AddTextFrame() : section.AddImage(
            "base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==");
        shape.Width = shape.Height = Unit.FromPoint(30);
        shape.RelativeVertical = anchor;
        shape.WrapFormat.Style = flowing ? WrapStyle.TopBottom : WrapStyle.None;
        shape.WrapFormat.DistanceBottom = Unit.FromPoint(12);
        shape.WrapFormat.ConsumePreviousParagraphSpacing = enabled;
        if (secondShape)
        {
            var another = section.AddTextFrame();
            another.RelativeVertical = RelativeVertical.Margin;
            another.WrapFormat.Style = WrapStyle.None;
            another.Width = another.Height = Unit.FromPoint(10);
        }
        var following = section.AddParagraph("After");
        following.Format.SpaceBefore = Unit.FromPoint(before);
        var renderer = new DocumentRenderer(doc);
        renderer.PrepareDocument();
        var pages = Enumerable.Range(1, renderer.FormattedDocument.PageCount)
            .SelectMany(page => renderer.GetRenderInfoFromPage(page)!.Select(info => (Page: page, Info: info))).ToList();
        var previousInfo = pages.Single(item => item.Info.DocumentObject == previous);
        var shapeInfo = pages.Single(item => item.Info.DocumentObject == shape);
        var followingInfo = pages.Single(item => item.Info.DocumentObject == following);
        return new Geometry(previousInfo.Info.LayoutInfo.ContentArea.Y.Point,
            shapeInfo.Info.LayoutInfo.ContentArea.Y.Point, shapeInfo.Page,
            followingInfo.Info.LayoutInfo.ContentArea.Y.Point, followingInfo.Page);
    }
}
