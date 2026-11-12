// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.
using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Pdf.IO;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;
namespace MigraDoc.Tests;
[Collection("PDFsharp")]
public class TextLinkMarginTests : IDisposable
{
    public TextLinkMarginTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(0.5)]
    [InlineData(2.25)]
    [InlineData(10.0)]
    public void HorizontalPaddingExtendsBothSidesOfEveryLinkLine(double padding)
        => AssertResult(Render(padding: padding), Render(), padding, 0, 0);

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.MaxValue)]
    public void InvalidPaddingKeepsOriginalBounds(double padding)
        => AssertResult(Render(padding: padding), Render(), 0, 0, 0);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParagraphSpacingExtendsOnlyFirstAndFinalLines(bool inherited)
        => AssertResult(Render(spacing: true, inherited: inherited), Render(inherited: inherited), 0, inherited ? 4 : 12, 8);

    [Fact]
    public void PaddingSpacingAndExplicitLeadingSpaceCompose()
        => AssertResult(Render(spacing: true, padding: 2.25, leading: 7), Render(), 2.25, 19, 8);

    [Fact]
    public void SingleLineReceivesBothParagraphMargins()
        => AssertResult(Render(spacing: true, singleLine: true), Render(singleLine: true), 0, 12, 8);

    [Fact]
    public void PageBreaksDoNotAddParagraphMarginsToContinuationEdges()
    {
        var original = Render(manyPages: true);
        var selected = Render(spacing: true, padding: 2.25, manyPages: true);
        selected.Content.Count.Should().BeGreaterThan(2);
        AssertResult(selected, original, 2.25, 12, 8);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(4.0)]
    [InlineData(20.0)]
    public void TopExtensionUsesOnlySpaceRemainingAfterMarginCollapse(double precedingSpace)
        => AssertResult(Render(spacing: true, priorAfter: precedingSpace), Render(priorAfter: precedingSpace),
            0, Math.Max(0, 12 - precedingSpace), 8);

    [Fact]
    public void SuppressedTopOfPageSpacingIsNotReintroduced()
        => AssertResult(Render(spacing: true, firstOnPage: true), Render(firstOnPage: true), 0, 0, 8);

    [Fact]
    public void DefaultOptionsRetainOriginalGeometry()
    {
        var renderer = new DocumentRenderer(new Document());
        renderer.IncludeParagraphSpacingInTextLinkBounds.Should().BeFalse();
        renderer.TextLinkHorizontalPadding.Should().Be(0);
    }

    static void AssertResult(Result selected, Result original, double padding, double top, double bottom)
    {
        selected.Content.Should().Equal(original.Content);
        selected.Rects.Count.Should().Be(original.Rects.Count).And.BeGreaterThan(0);
        for (var i = 0; i < original.Rects.Count; i++)
        {
            var a = selected.Rects[i];
            var b = original.Rects[i];
            a.Page.Should().Be(b.Page);
            a.Left.Should().BeApproximately(b.Left - padding, 0.001);
            a.Right.Should().BeApproximately(b.Right + padding, 0.001);
            var onFirstLine = Math.Abs(b.Top - original.Rects[0].Top) < 0.001 && b.Page == 0;
            var onLastLine = Math.Abs(b.Bottom - original.Rects.Last().Bottom) < 0.001 && b.Page == original.Rects.Last().Page;
            a.Top.Should().BeApproximately(b.Top + (onFirstLine ? top : 0), 0.001);
            a.Bottom.Should().BeApproximately(b.Bottom - (onLastLine ? bottom : 0), 0.001);
        }
    }
    record Rect(int Page, double Left, double Bottom, double Right, double Top);
    record Result(List<string> Content, List<Rect> Rects);
    static Result Render(bool spacing = false, double padding = 0, double? leading = null,
        bool manyPages = false, bool singleLine = false, bool inherited = false,
        double? priorAfter = null, bool firstOnPage = false)
    {
        var doc = new Document();
        doc.Styles.Normal.Font.Name = "Arial";
        doc.Styles.Normal.Font.Size = 12;
        if (inherited)
        {
            doc.Styles.Normal.ParagraphFormat.SpaceBefore = Unit.FromPoint(12);
            doc.Styles.Normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(8);
        }
        var section = doc.AddSection();
        section.PageSetup.PageWidth = Unit.FromPoint(manyPages ? 180 : 400);
        section.PageSetup.PageHeight = Unit.FromPoint(manyPages ? 140 : 500);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(20);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromPoint(20);
        if (!firstOnPage)
        {
            var earlier = section.AddParagraph("Earlier paragraph.");
            if (priorAfter.HasValue) earlier.Format.SpaceAfter = Unit.FromPoint(priorAfter.Value);
        }
        var p = section.AddParagraph();
        if (!inherited)
        {
            p.Format.SpaceBefore = Unit.FromPoint(12);
            p.Format.SpaceAfter = Unit.FromPoint(8);
        }
        if (manyPages)
            p.AddHyperlink("https://example.org/long", HyperlinkType.Url)
                .AddText(string.Join(" ", Enumerable.Repeat("Alpha Bravo Charlie", 60)));
        else
        {
            p.AddHyperlink("https://example.org/first", HyperlinkType.Url).AddText("Alpha");
            p.AddText(" ");
            p.AddHyperlink("https://example.org/second", HyperlinkType.Url).AddText("Bravo");
            if (!singleLine)
            {
                p.AddLineBreak();
                p.AddHyperlink("https://example.org/middle", HyperlinkType.Url).AddText("Charlie");
                p.AddLineBreak();
                p.AddHyperlink("https://example.org/final", HyperlinkType.Url).AddText("Delta");
            }
        }
        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.DocumentRenderer.IncludeParagraphSpacingInTextLinkBounds = spacing;
        renderer.DocumentRenderer.TextLinkHorizontalPadding = padding;
        if (leading.HasValue) renderer.DocumentRenderer.TextLinkLeadingSpaceForParagraph = _ => leading;
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var content = Enumerable.Range(0, pdf.PageCount).Select(page => PdfFileHelper.GetPageContentStream(pdf, page)).ToList();
        using var saved = new MemoryStream();
        pdf.Save(saved, false);
        saved.Position = 0;
        using var reopened = PdfReader.Open(saved, PdfDocumentOpenMode.Modify);
        var rectangles = new List<Rect>();
        for (var page = 0; page < reopened.PageCount; page++)
            for (var index = 0; index < reopened.Pages[page].Annotations.Count; index++)
            {
                var r = reopened.Pages[page].Annotations[index].Rectangle;
                rectangles.Add(new Rect(page, r.X1, r.Y1, r.X2, r.Y2));
            }
        return new Result(content, rectangles);
    }
}
