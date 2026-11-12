// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.
using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.Pdf.IO;
using PdfSharp.TestHelper;
using Xunit;
namespace MigraDoc.Tests;
[Collection("PDFsharp")]
public class TextLinkLeadingSpaceTests : IDisposable
{
    public TextLinkLeadingSpaceTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(0.5)]
    [InlineData(7.25)]
    [InlineData(24.0)]
    public void ExtendsOnlyTopOfFirstLineHitAreas(double space)
    {
        var original = Render(null);
        var selected = Render(space);
        selected.Calls.Should().Be(2);
        selected.Content.Should().Equal(original.Content);
        selected.Rects.Should().HaveCount(original.Rects.Count).And.HaveCount(3);
        for (var i = 0; i < original.Rects.Count; i++)
            AssertRect(selected.Rects[i], original.Rects[i], i < 2 ? space : 0);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-7.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidOrEmptySelectionKeepsBounds(double? space)
    {
        var original = Render(null);
        var selected = Render(space, installNullSelector: true);
        selected.Calls.Should().Be(2);
        selected.Content.Should().Equal(original.Content);
        selected.Rects.Should().Equal(original.Rects);
    }

    [Fact]
    public void DefaultDoesNotInvokeSelector() => Render(null).Calls.Should().Be(0);

    [Fact]
    public void PageContinuationsKeepTheirOriginalBounds()
    {
        var original = Render(null, manyPages: true);
        var selected = Render(9, manyPages: true);
        selected.Content.Should().HaveCountGreaterThan(1).And.Equal(original.Content);
        selected.Calls.Should().Be(1);
        selected.Rects.Should().HaveCount(original.Rects.Count);
        selected.Rects.Count(r => r.Page > 0).Should().BeGreaterThan(1);
        for (var i = 0; i < original.Rects.Count; i++)
            AssertRect(selected.Rects[i], original.Rects[i], i == 0 ? 9 : 0);
    }

    [Fact]
    public void LinkStartingOnLaterLineDoesNotInvokeSelector()
    {
        var selected = Render(9, laterOnly: true);
        selected.Calls.Should().Be(0);
        selected.Rects.Should().Equal(Render(null, laterOnly: true).Rects);
    }

    [Fact]
    public void EachParagraphSelectsItsOwnLeadingSpace()
    {
        var original = Render(null, twoParagraphs: true);
        var selected = Render(9, twoParagraphs: true);
        selected.Calls.Should().Be(2);
        selected.Content.Should().Equal(original.Content);
        selected.Rects.Should().HaveCount(2);
        AssertRect(selected.Rects[0], original.Rects[0], 9);
        AssertRect(selected.Rects[1], original.Rects[1], 18);
    }

    static void AssertRect(Rect actual, Rect expected, double extension)
    {
        actual.Page.Should().Be(expected.Page);
        actual.Left.Should().BeApproximately(expected.Left, 0.001);
        actual.Right.Should().BeApproximately(expected.Right, 0.001);
        actual.Bottom.Should().BeApproximately(expected.Bottom, 0.001);
        actual.Top.Should().BeApproximately(expected.Top + extension, 0.001);
    }
    record Rect(int Page, double Left, double Bottom, double Right, double Top);
    record Result(List<string> Content, List<Rect> Rects, int Calls);
    static Result Render(double? space, bool installNullSelector = false, bool manyPages = false,
        bool laterOnly = false, bool twoParagraphs = false)
    {
        var doc = new Document();
        doc.Styles.Normal.Font.Name = "Arial";
        doc.Styles.Normal.Font.Size = 12;
        var section = doc.AddSection();
        section.PageSetup.PageWidth = Unit.FromPoint(manyPages ? 180 : 400);
        section.PageSetup.PageHeight = Unit.FromPoint(manyPages ? 140 : 500);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(20);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromPoint(20);
        section.AddParagraph("Earlier paragraph.");
        var paragraph = section.AddParagraph();
        paragraph.Tag = "first";
        paragraph.Format.SpaceBefore = Unit.FromPoint(12);
        paragraph.Format.SpaceAfter = Unit.FromPoint(8);
        if (twoParagraphs)
        {
            paragraph.AddHyperlink("https://example.org/first", HyperlinkType.Url).AddText("Alpha");
            var second = section.AddParagraph();
            second.Tag = "second";
            second.AddHyperlink("https://example.org/second", HyperlinkType.Url).AddText("Bravo");
        }
        else if (manyPages)
            paragraph.AddHyperlink("https://example.org/long", HyperlinkType.Url)
                .AddText(string.Join(" ", Enumerable.Repeat("Alpha Bravo Charlie", 60)));
        else
        {
            if (laterOnly) paragraph.AddText("Unlinked first line");
            else
            {
                paragraph.AddHyperlink("https://example.org/first", HyperlinkType.Url).AddText("Alpha");
                paragraph.AddText(" ");
                paragraph.AddHyperlink("https://example.org/second", HyperlinkType.Url).AddText("Bravo");
            }
            paragraph.AddLineBreak();
            paragraph.AddHyperlink("https://example.org/later", HyperlinkType.Url).AddText("Charlie");
        }
        var renderer = new PdfDocumentRenderer { Document = doc };
        var calls = 0;
        if (space.HasValue || installNullSelector)
            renderer.DocumentRenderer.TextLinkLeadingSpaceForParagraph = p =>
            {
                p.Tag.Should().BeOneOf("first", "second");
                calls++;
                return Equals(p.Tag, "second") ? space * 2 : space;
            };
        renderer.DocumentRenderer.PrepareDocument();
        calls.Should().Be(0);
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var contents = Enumerable.Range(0, pdf.PageCount)
            .Select(page => PdfFileHelper.GetPageContentStream(pdf, page)).ToList();
        using var saved = new MemoryStream();
        pdf.Save(saved, false);
        saved.Position = 0;
        using var reopened = PdfReader.Open(saved, PdfDocumentOpenMode.Modify);
        var rectangles = new List<Rect>();
        for (var page = 0; page < reopened.PageCount; page++)
            for (var index = 0; index < reopened.Pages[page].Annotations.Count; index++)
            {
                var rect = reopened.Pages[page].Annotations[index].Rectangle;
                rectangles.Add(new Rect(page, rect.X1, rect.Y1, rect.X2, rect.Y2));
            }
        return new Result(contents, rectangles, calls);
    }
}
