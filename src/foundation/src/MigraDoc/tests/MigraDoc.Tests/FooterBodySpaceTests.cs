// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

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
public class FooterBodySpaceTests : IDisposable
{
    public FooterBodySpaceTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }

    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(6, 20, 10, 2, 7)]
    [InlineData(1, 20, 10, 1, 10)]
    [InlineData(6, 80, 10, 2, 6)]
    [InlineData(0, 20, 80, 1, 10)]
    public void FooterReservesOnlySpaceAboveBottomMargin(int footerLines, double bottomMargin,
        double footerDistance, int expectedPages, int firstPageBodyLines)
    {
        var document = CreateDocument();
        var section = document.LastSection;
        section.PageSetup.BottomMargin = Unit.FromPoint(bottomMargin);
        section.PageSetup.FooterDistance = Unit.FromPoint(footerDistance);
        AddFooter(section.Footers.Primary, footerLines);
        AddBody(section, 10);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        renderer.PageCount.Should().Be(expectedPages);
        BodyLines(renderer, 1).Should().Be(firstPageBodyLines);
        Enumerable.Range(1, renderer.PageCount).Sum(page => BodyLines(renderer, page)).Should().Be(10);
        if (footerLines > 0)
            AssertNoOverlap(renderer);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void UsesFirstEvenAndPrimaryFooterForLogicalPageNumber(int startingNumber)
    {
        var document = CreateDocument();
        var section = document.LastSection;
        section.PageSetup.StartingNumber = startingNumber;
        section.PageSetup.DifferentFirstPageHeaderFooter = true;
        section.PageSetup.OddAndEvenPagesHeaderFooter = true;
        AddFooter(section.Footers.FirstPage, 6);
        AddFooter(section.Footers.EvenPage, 4);
        AddFooter(section.Footers.Primary, 2);
        AddBody(section, 23);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        renderer.PageCount.Should().Be(3);
        var expected = startingNumber == 1 ? new[] { 7, 9, 7 } : new[] { 7, 11, 5 };
        Enumerable.Range(1, 3).Select(page => BodyLines(renderer, page)).Should().Equal(expected);
        AssertNoOverlap(renderer);
    }

    [Fact]
    public void PagePositionedFooterFrameDoesNotReserveFlowingBodySpace()
    {
        var document = CreateDocument();
        var section = document.LastSection;
        var frame = section.Footers.Primary.AddTextFrame();
        frame.RelativeVertical = RelativeVertical.Page;
        frame.Top = Unit.FromPoint(0);
        frame.Height = Unit.FromPoint(80);
        frame.Width = Unit.FromPoint(100);
        frame.AddParagraph("Positioned");
        AddBody(section, 10);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        renderer.PageCount.Should().Be(1);
        BodyLines(renderer, 1).Should().Be(10);
    }

    [Theory]
    [InlineData(true, 7)]
    [InlineData(false, 9)]
    public void MixedPositionedAndFlowingFooterUsesRenderedOffset(bool frameFirst, int firstPageBodyLines)
    {
        var document = CreateDocument();
        var section = document.LastSection;
        if (!frameFirst)
            AddFooter(section.Footers.Primary, 6);
        var frame = section.Footers.Primary.AddTextFrame();
        frame.RelativeVertical = RelativeVertical.Page;
        frame.Top = Unit.FromPoint(100);
        frame.Height = Unit.FromPoint(80);
        frame.Width = Unit.FromPoint(100);
        frame.AddParagraph("Positioned");
        if (frameFirst)
            AddFooter(section.Footers.Primary, 6);
        AddBody(section, 10);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        renderer.PageCount.Should().Be(2);
        BodyLines(renderer, 1).Should().Be(firstPageBodyLines);
        Enumerable.Range(1, renderer.PageCount).Sum(page => BodyLines(renderer, page)).Should().Be(10);
        AssertNoOverlap(renderer);
    }

    static int BodyLines(PdfDocumentRenderer renderer, int page)
    {
        var stream = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, page - 1);
        var count = 0;
        while (stream.Text.MoveAndGetNext(true, out var text))
            if (text!.Text.StartsWith("Body", StringComparison.Ordinal))
                count++;
        return count;
    }

    static void AssertNoOverlap(PdfDocumentRenderer renderer)
    {
        for (var page = 1; page <= renderer.PageCount; page++)
        {
            var stream = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, page - 1);
            var body = new List<double>();
            var footer = new List<double>();
            while (stream.Text.MoveAndGetNext(true, out var text))
            {
                if (text!.Text.StartsWith("Body", StringComparison.Ordinal)) body.Add(text.Y);
                if (text.Text.StartsWith("Footer", StringComparison.Ordinal)) footer.Add(text.Y);
            }
            body.Min().Should().BeGreaterThan(footer.Max());
        }
    }

    static void AddBody(Section section, int count)
    {
        for (var index = 1; index <= count; index++)
            section.AddParagraph("Body" + index);
    }

    static void AddFooter(HeaderFooter footer, int lines)
    {
        for (var index = 1; index <= lines; index++)
            footer.AddParagraph("Footer" + index);
    }

    static Document CreateDocument()
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 8;
        var format = document.Styles.Normal.ParagraphFormat;
        format.LineSpacingRule = LineSpacingRule.Exactly;
        format.LineSpacing = Unit.FromPoint(10);
        format.SpaceBefore = format.SpaceAfter = Unit.FromPoint(0);
        var section = document.AddSection();
        section.PageSetup.PageHeight = Unit.FromPoint(160);
        section.PageSetup.PageWidth = Unit.FromPoint(240);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromPoint(20);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(20);
        section.PageSetup.FooterDistance = Unit.FromPoint(10);
        return document;
    }
}
