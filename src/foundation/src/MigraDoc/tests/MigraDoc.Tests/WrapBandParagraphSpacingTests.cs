using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class WrapBandParagraphSpacingTests : IDisposable
{
    public WrapBandParagraphSpacingTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(40, 10, 20, 70)] // Band entirely inside the spacing.
    [InlineData(30, 10, 20, 60)] // Band starts at the body cursor.
    [InlineData(20, 10, 20, 50)] // Band ends at the cursor: no overlap.
    [InlineData(50, 10, 20, 60)] // Band starts at spacing end: line collision only.
    [InlineData(40, 10, 0, 30)]  // Zero spacing leaves line wrapping unchanged.
    public void RestartsOnlyIntersectingPositiveSpacing(double top, double height, double spacing, double expected)
    {
        var result = Prepare(true, top, height, spacing);
        result.Y.Should().BeApproximately(expected, 0.001);
        result.Page.Should().Be(1);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(20, 0)]
    [InlineData(10, 20)]
    [InlineData(20, 10)]
    public void UsesCollapsedBeforeAndAfterSpacing(double before, double after)
    {
        Prepare(true, 40, 10, before, after: after).Y.Should().BeApproximately(70, 0.001);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TraversesDisjointBandsRegardlessOfOrder(bool reverse)
    {
        var bands = new[] { (20.0, 10.0), (35.0, 10.0) };
        if (reverse) Array.Reverse(bands);
        // Cursor 30; bands 40..50 and 55..65. Full 20pt spacing follows 65.
        Prepare(true, 20, 1, 20, bands: bands).Y.Should().BeApproximately(85, 0.001);
    }

    [Theory]
    [InlineData(WrapStyle.Left)]
    [InlineData(WrapStyle.Right)]
    [InlineData(WrapStyle.BothSides)]
    [InlineData(WrapStyle.None)]
    public void DoesNotChangeOtherWrapStyles(WrapStyle style)
    {
        Prepare(true, 40, 10, 20, style: style).Should().Be(
            Prepare(false, 40, 10, 20, style: style));
    }

    [Fact]
    public void DisabledByDefaultPreservesOriginalPlacement()
    {
        new DocumentRenderer(new Document()).RestartParagraphSpacingAfterTopBottomBands.Should().BeFalse();
        Prepare(false, 40, 10, 20).Y.Should().BeApproximately(50, 0.001);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeepProbeAgreesWithActualPlacement(bool keep)
    {
        var result = Prepare(true, 55, 10, 20, keep: keep, pageHeight: 110, filler: true);
        result.Page.Should().Be(2);
        result.Y.Should().BeApproximately(keep ? 50 : 20, 0.001);
        result.PreviousPage.Should().Be(keep ? 2 : 1);
    }

    record Geometry(double Y, int Page, int PreviousPage);
    static Geometry Prepare(bool enabled, double top, double height, double spacing,
        (double, double)[]? bands = null, WrapStyle style = WrapStyle.TopBottom,
        bool keep = false, double pageHeight = 300, bool filler = false, double after = 0)
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
            lead.Format.LineSpacing = Unit.FromPoint(20);
        }
        var frame = section.AddTextFrame();
        frame.Width = Unit.FromPoint(20);
        frame.Height = Unit.FromPoint(height);
        frame.Top = Unit.FromPoint(top);
        frame.RelativeVertical = RelativeVertical.Page;
        frame.WrapFormat.Style = style;
        frame.WrapFormat.ExcludeFollowingText = true;
        var previous = section.AddParagraph("Before");
        previous.Format.LineSpacingRule = LineSpacingRule.Exactly;
        previous.Format.LineSpacing = Unit.FromPoint(10);
        previous.Format.KeepWithNext = keep;
        previous.Format.SpaceAfter = Unit.FromPoint(after);
        var following = section.AddParagraph("After");
        following.Format.SpaceBefore = Unit.FromPoint(spacing);
        following.Format.LineSpacingRule = LineSpacingRule.Exactly;
        following.Format.LineSpacing = Unit.FromPoint(10);
        var renderer = new DocumentRenderer(document)
        {
            RestartParagraphSpacingAfterTopBottomBands = enabled,
            AdditionalTopBottomWrapBands = _ => bands,
        };
        renderer.PrepareDocument();
        var infos = Enumerable.Range(1, renderer.FormattedDocument.PageCount)
            .SelectMany(page => renderer.GetRenderInfoFromPage(page)!.Select(info => (Page: page, Info: info))).ToList();
        var followingInfo = infos.Single(item => item.Info.DocumentObject == following);
        return new Geometry(followingInfo.Info.LayoutInfo.ContentArea.Y.Point, followingInfo.Page,
            infos.Single(item => item.Info.DocumentObject == previous).Page);
    }
}
