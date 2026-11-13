using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.IO;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class FlowAnchorPaginationTests : IDisposable
{
    public FlowAnchorPaginationTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(RelativeVertical.Page)]
    [InlineData(RelativeVertical.Margin)]
    [InlineData(RelativeVertical.Paragraph)]
    public void FirstBreakUsesRemainingFlowIncludingPreviousAfterSpace(RelativeVertical reference)
    {
        var result = Render(true, reference: reference);
        result.PageLines.Should().Equal(Enumerable.Repeat(1, 14).Concat(Enumerable.Repeat(2, 16)));
        result.FirstFrameY.Should().BeApproximately(reference == RelativeVertical.Page ? 80 : reference == RelativeVertical.Margin ? 100 : 140, 0.001);
        result.ContinuationFrameY.Should().BeApproximately(reference == RelativeVertical.Page ? 0 : 20, 0.001);
        result.Pages.Should().Be(2);
    }

    [Theory]
    [InlineData(RelativeVertical.Page, false)]
    [InlineData(RelativeVertical.Margin, false)]
    [InlineData(RelativeVertical.Paragraph, false)]
    [InlineData(RelativeVertical.Paragraph, true)]
    public void VisualTopDoesNotChangeOptedInBreaks(RelativeVertical reference, bool previousTop)
    {
        var low = Render(true, top: 80, reference: reference, previousTop: previousTop);
        var high = Render(true, top: 20, reference: reference, previousTop: previousTop);
        low.PageLines.Should().Equal(high.PageLines);
        (low.FirstFrameY - high.FirstFrameY).Should().BeApproximately(60, 0.001);
        low.ContinuationFrameY.Should().Be(high.ContinuationFrameY);
    }

    [Fact]
    public void MorePrecedingFlowLeavesLessSpaceOnTheFirstPage()
    {
        Render(true, lead: 70).PageLines.Count(page => page == 1).Should().Be(11);
        Render(true, after: 0).PageLines.Count(page => page == 1).Should().Be(16);
    }

    [Fact]
    public void ContinuationsUseFullBodyHeight()
    {
        var result = Render(true, lines: 55);
        result.PageLines.Should().Equal(Enumerable.Repeat(1, 14)
            .Concat(Enumerable.Repeat(2, 20)).Concat(Enumerable.Repeat(3, 20)).Concat(new[] { 4 }));
        result.Pages.Should().Be(4);
    }

    [Fact]
    public void DisabledSettingKeepsVisualOffsetBreaks()
    {
        var result = Render(false);
        result.PageLines.Count(page => page == 1).Should().Be(12);
        result.FirstFrameY.Should().BeApproximately(100, 0.001);
    }

    [Fact]
    public void NonpaginatedFramesIgnoreTheOption()
        => Render(true, paginate: false).Should().BeEquivalentTo(Render(false, paginate: false));

    [Fact]
    public void PreviousParagraphTopPlacementDoesNotReplaceTheFlowCapacity()
    {
        var result = Render(true, reference: RelativeVertical.Paragraph, previousTop: true);
        result.PageLines.Count(page => page == 1).Should().Be(14);
        result.FirstFrameY.Should().BeApproximately(100, 0.001);
    }

    [Theory]
    [InlineData(RelativeVertical.Page, false)]
    [InlineData(RelativeVertical.Margin, false)]
    [InlineData(RelativeVertical.Paragraph, false)]
    [InlineData(RelativeVertical.Paragraph, true)]
    public void KeepProbePreservesTheFlowCapacityAndPlacement(RelativeVertical reference, bool previousTop)
    {
        var ordinary = Render(true, reference: reference, previousTop: previousTop);
        var kept = Render(true, reference: reference, previousTop: previousTop, keep: true);
        kept.Should().BeEquivalentTo(ordinary);
        kept.PageLines.Count(page => page == 1).Should().Be(14);
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
        if (enabled.HasValue) frame.PaginateFromFlowAnchor = enabled.Value;
        frame.PaginateFromFlowAnchor.Should().Be(enabled ?? false);
        frame.Clone().Values.PaginateFromFlowAnchor.Should().Be(enabled);
        var ddl = DdlWriter.WriteToString(document);
        ddl.Contains("PaginateFromFlowAnchor").Should().Be(enabled.HasValue);
        var restored = (TextFrame)DdlReader.DocumentFromString(ddl)!.Sections[0].Elements[0];
        restored.Values.PaginateFromFlowAnchor.Should().Be(enabled);
    }

    record Result(List<int> PageLines, double FirstFrameY, double? ContinuationFrameY, int Pages);
    static Result Render(bool enabled, RelativeVertical reference = RelativeVertical.Margin,
        double top = 80, double lead = 40, double after = 20, int lines = 30, bool paginate = true, bool previousTop = false, bool keep = false)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 8;
        document.Styles.Normal.ParagraphFormat.LineSpacingRule = LineSpacingRule.Exactly;
        document.Styles.Normal.ParagraphFormat.LineSpacing = Unit.FromPoint(10);
        var section = document.AddSection();
        section.PageSetup.PageWidth = Unit.FromPoint(240);
        section.PageSetup.PageHeight = Unit.FromPoint(240);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromPoint(20);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(20);
        var previous = section.AddParagraph("Before");
        previous.Format.LineSpacing = Unit.FromPoint(lead);
        previous.Format.SpaceAfter = Unit.FromPoint(after);
        previous.Format.KeepWithNext = keep;
        var frame = section.AddTextFrame();
        frame.Width = Unit.FromPoint(180);
        frame.RelativeVertical = reference;
        frame.Top = Unit.FromPoint(top);
        frame.WrapFormat.Style = WrapStyle.TopBottom;
        frame.WrapFormat.PositionFromPreviousParagraphTop = previousTop;
        frame.PaginateContent = paginate;
        frame.PaginateFromFlowAnchor = enabled;
        frame.MarginTop = frame.MarginBottom = Unit.FromPoint(0);
        for (var index = 0; index < lines; index++) frame.AddParagraph($"Line{index:D2}");
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var pageLines = new List<int>();
        for (var page = 0; page < pdf.PageCount; page++)
        {
            var text = PdfFileHelper.GetPageContentStreamEnumerator(pdf, page).Text;
            while (text.MoveAndGetNext(true, out var item))
                if (item!.Text.StartsWith("Line", StringComparison.Ordinal)) pageLines.Add(page + 1);
        }
        pageLines.Count.Should().Be(lines);
        var first = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!
            .Single(info => info.DocumentObject == frame);
        var continuation = pdf.PageCount > 1 ? renderer.DocumentRenderer.GetRenderInfoFromPage(2)!
            .SingleOrDefault(info => info.DocumentObject == frame) : null;
        return new Result(pageLines, first.LayoutInfo.ContentArea.Y.Point,
            continuation?.LayoutInfo.ContentArea.Y.Point, pdf.PageCount);
    }
}
