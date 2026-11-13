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
public class ParagraphTopAnchorTests : IDisposable
{
    public ParagraphTopAnchorTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(false, 0, 20, 5)]
    [InlineData(true, 0, 20, 5)]
    [InlineData(false, 30, 20, 5)]
    [InlineData(true, 30, 20, 5)]
    [InlineData(false, 10, 20, -5)]
    [InlineData(true, 10, 20, -5)]
    public void UsesParagraphOriginBeforeUncollapsedBeforeSpace(bool frame, double leadAfter, double before, double top)
    {
        var result = Prepare(true, frame, leadAfter: leadAfter, before: before, top: top);
        result.Shape.Y.Should().BeApproximately(30 + leadAfter + top, 0.001);
        result.Following.Y.Should().BeApproximately(result.Anchor.Y + 20 + 7, 0.001);
        result.Following.Should().Be(Prepare(false, frame, leadAfter: leadAfter, before: before, top: top).Following);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConsecutiveNonflowingShapesShareOneParagraphWithoutRewindingBodyFlow(bool frame)
    {
        var result = Prepare(true, frame, top: 5, second: true);
        result.Shape.Y.Should().BeApproximately(35, 0.001);
        result.Second!.Y.Should().BeApproximately(55, 0.001);
        result.Following.Y.Should().BeApproximately(57, 0.001);
    }

    [Theory]
    [InlineData(false, RelativeVertical.Page)]
    [InlineData(true, RelativeVertical.Page)]
    [InlineData(false, RelativeVertical.Margin)]
    [InlineData(true, RelativeVertical.Margin)]
    [InlineData(false, RelativeVertical.Line)]
    [InlineData(true, RelativeVertical.Line)]
    public void OtherVerticalReferencesKeepTheirPlacement(bool frame, RelativeVertical reference)
        => Prepare(true, frame, reference: reference).Should().Be(Prepare(false, frame, reference: reference));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoPrecedingParagraphUsesOrdinaryPlacement(bool frame)
        => Prepare(true, frame, noAnchor: true).Should().Be(Prepare(false, frame, noAnchor: true));

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void KeepProbeUsesParagraphOriginForFlowingShapes(bool frame, bool chain)
    {
        // 50 points remain after the ten-point anchor paragraph. A 25-point
        // offset plus a 30-point shape fits from its top, but not from its end.
        var result = Prepare(true, frame, leadHeight: chain ? 30 : 40, anchorHeight: 10, after: 0,
            top: 25, keep: true, flowing: true, pageHeight: 140, chain: chain);
        result.Anchor.Page.Should().Be(1);
        result.Shape.Page.Should().Be(1);
        result.Shape.Y.Should().BeApproximately(85, 0.001);
        result.Following.Page.Should().Be(2);
        result.Following.Y.Should().BeApproximately(20, 0.001);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UsesActualCollapsedMarginAcrossAnInterveningPositionedShape(bool frame)
    {
        var result = Prepare(true, frame, leadAfter: 30, before: 20, top: 5, intervening: true);
        result.Anchor.Y.Should().BeApproximately(60, 0.001);
        result.Shape.Y.Should().BeApproximately(65, 0.001);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DoesNotUseAnAnchorFromThePreviousPage(bool frame)
        => Prepare(true, frame, areaBreak: true).Should().Be(Prepare(false, frame, areaBreak: true));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeepChainRetainsTheProbedParagraphsUncollapsedBeforeMargin(bool frame)
    {
        var result = Prepare(true, frame, leadHeight: 20, anchorHeight: 10, before: 20,
            after: 0, top: 25, keep: true, flowing: true, pageHeight: 140, chain: true);
        result.Anchor.Page.Should().Be(1);
        result.Anchor.Y.Should().BeApproximately(70, 0.001);
        result.Shape.Y.Should().BeApproximately(75, 0.001);
        result.Following.Should().Be(new Position(1, 105));
    }

    [Theory]
    [InlineData(false, WrapStyle.None, 90)]
    [InlineData(true, WrapStyle.None, 90)]
    [InlineData(false, WrapStyle.BothSides, 90)]
    [InlineData(true, WrapStyle.BothSides, 90)]
    [InlineData(false, WrapStyle.None, 110)]
    [InlineData(true, WrapStyle.None, 110)]
    [InlineData(false, WrapStyle.BothSides, 110)]
    [InlineData(true, WrapStyle.BothSides, 110)]
    public void NonflowingShapesAtOrBelowBodyBottomDoNotMoveTheirKeepParagraph(
        bool frame, WrapStyle style, double top)
    {
        var ordinary = Prepare(true, frame, top: top, pageHeight: 140, style: style);
        var kept = Prepare(true, frame, top: top, pageHeight: 140, style: style, keep: true);
        kept.Should().Be(ordinary);
        kept.Anchor.Page.Should().Be(1);
        kept.Shape.Should().Be(new Position(1, 30 + top));
        kept.Following.Should().Be(new Position(1, 57));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FlowingShapeBelowBodyBottomStillRequiresAnotherPage(bool frame)
    {
        var result = Prepare(true, frame, leadHeight: 60, top: 40, pageHeight: 140, flowing: true, keep: true);
        result.Anchor.Page.Should().Be(2);
        result.Shape.Should().Be(new Position(2, 60));
        result.Following.Should().Be(new Position(2, 90));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void NullableValueSurvivesCloneAndDdl(bool? enabled)
    {
        var document = new Document();
        var frame = document.AddSection().AddTextFrame();
        frame.AddParagraph("Content");
        if (enabled.HasValue) frame.WrapFormat.PositionFromPreviousParagraphTop = enabled.Value;
        frame.WrapFormat.PositionFromPreviousParagraphTop.Should().Be(enabled ?? false);
        frame.Clone().WrapFormat.Values.PositionFromPreviousParagraphTop.Should().Be(enabled);
        var ddl = DdlWriter.WriteToString(document);
        ddl.Contains("PositionFromPreviousParagraphTop").Should().Be(enabled.HasValue);
        var restored = (TextFrame)DdlReader.DocumentFromString(ddl)!.Sections[0].Elements[0];
        restored.WrapFormat.Values.PositionFromPreviousParagraphTop.Should().Be(enabled);
    }

    record Position(int Page, double Y);
    record Geometry(Position Anchor, Position Shape, Position? Second, Position Following);
    static Geometry Prepare(bool enabled, bool frame, double leadAfter = 0, double before = 0,
        double top = 0, bool second = false, RelativeVertical reference = RelativeVertical.Paragraph,
        bool noAnchor = false, double leadHeight = 10, double anchorHeight = 20, double after = 7,
        bool keep = false, bool flowing = false, double pageHeight = 300,
        bool intervening = false, bool areaBreak = false, bool chain = false, WrapStyle style = WrapStyle.None)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 8;
        document.Styles.Normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Exactly;
        document.Styles.Normal.ParagraphFormat.LineSpacing = Unit.FromPoint(10);
        var section = document.AddSection();
        section.PageSetup.PageHeight = Unit.FromPoint(pageHeight);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromPoint(20);
        Paragraph? anchor = null;
        if (!noAnchor)
        {
            var lead = section.AddParagraph("Lead");
            lead.Format.LineSpacing = Unit.FromPoint(leadHeight);
            lead.Format.SpaceAfter = Unit.FromPoint(leadAfter);
            if (intervening)
            {
                var positioned = section.AddTextFrame();
                positioned.Width = positioned.Height = Unit.FromPoint(10);
                positioned.RelativeVertical = RelativeVertical.Margin;
                positioned.WrapFormat.Style = WrapStyle.None;
                positioned.WrapFormat.DistanceBottom = Unit.FromPoint(12);
            }
            if (chain)
                section.AddParagraph("Bridge").Format.KeepWithNext = true;
            anchor = section.AddParagraph("Anchor");
            anchor.Format.LineSpacing = Unit.FromPoint(anchorHeight);
            anchor.Format.SpaceBefore = Unit.FromPoint(before);
            anchor.Format.SpaceAfter = Unit.FromPoint(after);
            anchor.Format.KeepWithNext = keep;
        }
        if (areaBreak) section.AddPageBreak();
        Shape AddShape(double offset)
        {
            Shape shape = frame ? section.AddTextFrame() : section.AddImage(
                "base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==");
            shape.Width = shape.Height = Unit.FromPoint(30);
            shape.Top = Unit.FromPoint(offset);
            shape.RelativeVertical = reference;
            shape.WrapFormat.Style = flowing ? WrapStyle.TopBottom : style;
            shape.WrapFormat.PositionFromPreviousParagraphTop = enabled;
            return shape;
        }
        var shape = AddShape(top);
        var another = second ? AddShape(top + 20) : null;
        var following = section.AddParagraph("After");
        var renderer = new DocumentRenderer(document);
        renderer.PrepareDocument();
        var pages = Enumerable.Range(1, renderer.FormattedDocument.PageCount)
            .SelectMany(page => renderer.GetRenderInfoFromPage(page)!.Select(info => (Page: page, Info: info))).ToList();
        Position Locate(DocumentObject obj)
        {
            var item = pages.Single(item => item.Info.DocumentObject == obj);
            return new Position(item.Page, item.Info.LayoutInfo.ContentArea.Y.Point);
        }
        return new Geometry(anchor == null ? new Position(0, 0) : Locate(anchor),
            Locate(shape), another == null ? null : Locate(another), Locate(following));
    }
}
