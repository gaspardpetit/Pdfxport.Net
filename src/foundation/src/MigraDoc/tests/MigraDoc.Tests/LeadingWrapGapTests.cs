using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class LeadingWrapGapTests : IDisposable
{
    public LeadingWrapGapTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(WrapStyle.BothSides, 10)]
    [InlineData(WrapStyle.BothSides, 30)]
    [InlineData(WrapStyle.TopBottom, 10)]
    [InlineData(WrapStyle.TopBottom, 30)]
    public void LeadingGapIsPaintedAndConsumedExactlyOnce(WrapStyle style, double height)
    {
        var reference = Create(1);
        reference.LastSection.AddParagraph("Spacer").Format.LineSpacing = Unit.FromPoint(height);
        AddLines(reference.LastSection, 2, false);
        var expected = Render(reference);
        var document = Create(1);
        AddShape(document.LastSection, style, 20, height);
        AddLines(document.LastSection, 2, false);
        var renderer = Render(document);
        Words(renderer, 1).Should().Equal(Words(expected, 1));
        var paragraphs = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!
            .OfType<ParagraphRenderInfo>().ToArray();
        paragraphs[0].LayoutInfo.ContentArea.Y.Point.Should().BeApproximately(20, 0.01);
        paragraphs[0].LayoutInfo.ContentArea.Height.Point.Should().BeApproximately(height + 10, 0.01);
        paragraphs[1].LayoutInfo.ContentArea.Y.Point.Should().BeApproximately(30 + height, 0.01);
    }

    [Theory]
    [InlineData(WrapStyle.BothSides, 1, false)]
    [InlineData(WrapStyle.BothSides, 1, true)]
    [InlineData(WrapStyle.BothSides, 2, false)]
    [InlineData(WrapStyle.BothSides, 2, true)]
    [InlineData(WrapStyle.TopBottom, 1, false)]
    [InlineData(WrapStyle.TopBottom, 1, true)]
    [InlineData(WrapStyle.TopBottom, 2, false)]
    [InlineData(WrapStyle.TopBottom, 2, true)]
    public void DisjointBandsLeaveCorrectLineCapacityAndPreserveAllText(WrapStyle style, int columns, bool split)
    {
        var document = Create(columns);
        AddShape(document.LastSection, style, 20, 10);
        AddShape(document.LastSection, style, 60, 10);
        AddLines(document.LastSection, columns * 10 + 2, split);
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        var first = Words(renderer, 1);
        first.Should().HaveCount(columns * 10);
        first[1].Y.Should().BeApproximately(first[0].Y + 10, 0.01);
        first[3].Y.Should().BeApproximately(first[2].Y + 20, 0.01);
        if (columns == 2)
        {
            first[10].X.Should().BeApproximately(130, 0.01);
            first[10].Y.Should().BeApproximately(first[0].Y, 0.01);
        }
        var all = Enumerable.Range(1, 2).SelectMany(page => Words(renderer, page));
        all.Select(t => t.Text).Should().Equal(Enumerable.Range(1, columns * 10 + 2).Select(i => "Body" + i));
        var unwrapped = Create(columns);
        AddLines(unwrapped.LastSection, 1, false);
        Words(renderer, 2)[0].Y.Should().BeApproximately(Words(Render(unwrapped), 1)[0].Y, 0.01);
    }

    [Theory]
    [InlineData(WrapStyle.BothSides)]
    [InlineData(WrapStyle.TopBottom)]
    public void AGapAfterTheFirstLineKeepsItsExistingPlacement(WrapStyle style)
    {
        var document = Create(1);
        AddShape(document.LastSection, style, 40, 10);
        AddLines(document.LastSection, 4, true);
        var renderer = Render(document);
        var words = Words(renderer, 1);
        words.Should().HaveCount(4);
        words[1].Y.Should().BeApproximately(words[0].Y + 10, 0.01);
        words[2].Y.Should().BeApproximately(words[1].Y + 20, 0.01);
        words[3].Y.Should().BeApproximately(words[2].Y + 10, 0.01);
        var info = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!.OfType<ParagraphRenderInfo>().Single();
        info.LayoutInfo.ContentArea.Height.Point.Should().BeApproximately(50, 0.01);
    }

    [Theory]
    [InlineData(20, 60, 30)]
    [InlineData(50, 50, 40)]
    public void BoundarySpacingKeepsFlowBoundsAndPaintedBaselineConsistent(
        double bandTop, double expectedOrigin, double expectedBaselineDifference)
    {
        var document = Create(1);
        AddShape(document.LastSection, WrapStyle.TopBottom, bandTop, 10);
        AddLines(document.LastSection, 2, false);
        var after = document.LastSection.Elements.OfType<Paragraph>().Last();
        after.Format.SpaceBefore = Unit.FromPoint(20);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.RestartParagraphSpacingAfterTopBottomBands = true;
        renderer.RenderDocument();
        var info = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!
            .Single(i => ReferenceEquals(i.DocumentObject, after));
        info.LayoutInfo.ContentArea.Y.Point.Should().BeApproximately(expectedOrigin, 0.01);
        (info.LayoutInfo.ContentArea.Y.Point + info.LayoutInfo.ContentArea.Height.Point)
            .Should().BeApproximately(70, 0.01);
        var words = Words(renderer, 1);
        words[1].Y.Should().BeApproximately(words[0].Y + expectedBaselineDifference, 0.01);
    }

    static void AddShape(Section section, WrapStyle style, double top, double height)
    {
        var shape = section.AddTextFrame();
        shape.RelativeHorizontal = RelativeHorizontal.Page;
        shape.RelativeVertical = RelativeVertical.Page;
        shape.Left = Unit.Zero;
        shape.Top = Unit.FromPoint(top);
        shape.Width = Unit.FromPoint(240);
        shape.Height = Unit.FromPoint(height);
        shape.WrapFormat.Style = style;
        shape.WrapFormat.ExcludeFollowingText = true;
    }

    static void AddLines(Section section, int count, bool split)
    {
        var paragraph = split ? section.AddParagraph() : null;
        if (paragraph != null) paragraph.Format.WidowControl = false;
        for (var i = 1; i <= count; i++)
        {
            if (paragraph == null) section.AddParagraph("Body" + i);
            else
            {
                if (i > 1) paragraph.AddLineBreak();
                paragraph.AddText("Body" + i);
            }
        }
    }

    static (string Text, double X, double Y)[] Words(PdfDocumentRenderer renderer, int page)
    {
        var result = new List<(string, double, double)>();
        var stream = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, page - 1);
        while (stream.Text.MoveAndGetNext(true, out var item))
            if (item!.Text.StartsWith("Body", StringComparison.Ordinal))
                result.Add((item.Text, item.X, renderer.PdfDocument.Pages[page - 1].Height.Point - item.Y));
        return result.ToArray();
    }

    static PdfDocumentRenderer Render(Document document)
    {
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        return renderer;
    }

    static Document Create(int columns)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 8;
        var format = document.Styles.Normal.ParagraphFormat;
        format.LineSpacingRule = LineSpacingRule.Exactly;
        format.LineSpacing = Unit.FromPoint(10);
        format.SpaceBefore = format.SpaceAfter = Unit.Zero;
        var section = document.AddSection();
        var setup = section.PageSetup;
        setup.PageHeight = Unit.FromPoint(160);
        setup.PageWidth = Unit.FromPoint(240);
        setup.TopMargin = setup.BottomMargin = Unit.FromPoint(20);
        setup.LeftMargin = setup.RightMargin = Unit.FromPoint(20);
        setup.HeaderDistance = setup.FooterDistance = Unit.FromPoint(10);
        setup.ColumnCount = columns;
        setup.ColumnSpacing = Unit.FromPoint(20);
        return document;
    }
}
