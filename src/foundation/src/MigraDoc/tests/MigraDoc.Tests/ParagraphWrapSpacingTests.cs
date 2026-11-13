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
public class ParagraphWrapSpacingTests : IDisposable
{
    public ParagraphWrapSpacingTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(false, 20, 8)]
    [InlineData(true, 20, 8)]
    [InlineData(false, 8, 20)]
    [InlineData(true, 8, 20)]
    [InlineData(false, 0, 8)]
    [InlineData(true, 20, 0)]
    public void AddsSpacingInsteadOfCollapsingIt(bool frame, double after, double distance)
    {
        var baseline = Prepare(false, frame, after, distance);
        var additive = Prepare(true, frame, after, distance);
        baseline.ShapeY.Should().BeApproximately(baseline.PreviousBottom + Math.Max(after, distance), 0.001);
        additive.ShapeY.Should().BeApproximately(additive.PreviousBottom + after + distance, 0.001);
        (additive.FollowingY - baseline.FollowingY).Should().BeApproximately(Math.Min(after, distance), 0.001);
    }

    [Theory]
    [InlineData(RelativeVertical.Page, WrapStyle.TopBottom)]
    [InlineData(RelativeVertical.Margin, WrapStyle.TopBottom)]
    [InlineData(RelativeVertical.Line, WrapStyle.TopBottom)]
    [InlineData(RelativeVertical.Paragraph, WrapStyle.None)]
    [InlineData(RelativeVertical.Paragraph, WrapStyle.BothSides)]
    public void DoesNotChangeOtherAnchorsOrNonflowingShapes(RelativeVertical relative, WrapStyle style)
    {
        Prepare(true, true, 20, 8, relative, style).Should().Be(
            Prepare(false, true, 20, 8, relative, style));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void PreservesNullableValueInCloneAndDdl(bool? enabled)
    {
        var document = new Document();
        var frame = document.AddSection().AddTextFrame();
        frame.AddParagraph("Content");
        if (enabled.HasValue) frame.WrapFormat.PositionAfterPreviousParagraphSpacing = enabled.Value;
        frame.WrapFormat.PositionAfterPreviousParagraphSpacing.Should().Be(enabled ?? false);
        frame.Clone().WrapFormat.Values.PositionAfterPreviousParagraphSpacing.Should().Be(enabled);
        var ddl = DdlWriter.WriteToString(document);
        ddl.Contains("PositionAfterPreviousParagraphSpacing").Should().Be(enabled.HasValue);
        var restored = (TextFrame)DdlReader.DocumentFromString(ddl)!.Sections[0].Elements[0];
        restored.WrapFormat.Values.PositionAfterPreviousParagraphSpacing.Should().Be(enabled);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MovingShapeToNextPageDoesNotRepeatPrecedingSpacing(bool frame)
    {
        var baseline = Prepare(false, frame, 35, 30, pageHeight: 120);
        var additive = Prepare(true, frame, 35, 30, pageHeight: 120);
        baseline.ShapePage.Should().Be(1);
        additive.ShapePage.Should().Be(2);
        additive.ShapeY.Should().BeApproximately(50, 0.001);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void KeepWithNextUsesTheSameShapeSpacingAsActualPlacement(bool frame, bool enabled, bool keep)
    {
        // 60 points remain after Before. Collapsed spacing + shape requires 50;
        // additive spacing + shape requires 70. The pair fits on a fresh page.
        var result = Prepare(enabled, frame, 20, 20, pageHeight: 160, filler: true, keep: keep);
        result.ShapePage.Should().Be(enabled ? 2 : 1);
        result.PreviousPage.Should().Be(enabled && keep ? 2 : 1);
        if (enabled && keep)
            result.ShapeY.Should().BeApproximately(result.PreviousBottom + 40, 0.001);
    }

    record Geometry(double PreviousBottom, double ShapeY, double FollowingY, int ShapePage, int PreviousPage);
    static Geometry Prepare(bool enabled, bool frame, double after, double distance,
        RelativeVertical relative = RelativeVertical.Paragraph, WrapStyle style = WrapStyle.TopBottom, double pageHeight = 300, bool filler = false, bool keep = false)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 8;
        var section = document.AddSection();
        section.PageSetup.PageHeight = Unit.FromPoint(pageHeight);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromPoint(20);
        if (filler)
        {
            var lead = section.AddParagraph("Filler");
            lead.Format.LineSpacingRule = LineSpacingRule.Exactly;
            lead.Format.LineSpacing = Unit.FromPoint(50);
        }
        var previous = section.AddParagraph("Before");
        previous.Format.KeepWithNext = keep;
        if (filler)
        {
            previous.Format.LineSpacingRule = LineSpacingRule.Exactly;
            previous.Format.LineSpacing = Unit.FromPoint(10);
        }
        previous.Format.SpaceAfter = Unit.FromPoint(after);
        Shape shape = frame ? section.AddTextFrame() : section.AddImage(
            "base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==");
        shape.Width = shape.Height = Unit.FromPoint(30);
        shape.RelativeVertical = relative;
        shape.WrapFormat.Style = style;
        shape.WrapFormat.DistanceTop = Unit.FromPoint(distance);
        shape.WrapFormat.PositionAfterPreviousParagraphSpacing = enabled;
        var following = section.AddParagraph("After");
        var renderer = new DocumentRenderer(document);
        renderer.PrepareDocument();
        var pages = Enumerable.Range(1, renderer.FormattedDocument.PageCount)
            .SelectMany(page => renderer.GetRenderInfoFromPage(page)!.Select(info => (Page: page, Info: info))).ToList();
        var infos = pages.Select(item => item.Info).ToList();
        var beforeArea = infos.Single(info => info.DocumentObject == previous).LayoutInfo.ContentArea;
        return new Geometry(beforeArea.Y.Point + beforeArea.Height.Point,
            infos.Single(info => info.DocumentObject == shape).LayoutInfo.ContentArea.Y.Point,
            infos.Single(info => info.DocumentObject == following).LayoutInfo.ContentArea.Y.Point,
            pages.Single(item => item.Info.DocumentObject == shape).Page,
            pages.Single(item => item.Info.DocumentObject == previous).Page);
    }
}
