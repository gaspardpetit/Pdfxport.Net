// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class BodyBottomReservationTests : IDisposable
{
    public BodyBottomReservationTests()
    {
        PdfSharpCore.ResetAll();
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(-1, 12, 12, 2)]
    [InlineData(0, 12, 12, 2)]
    [InlineData(1, 10, 12, 4)]
    [InlineData(2, 12, 10, 4)]
    public void ReservationIsPageLocalAndPreservesAllBodyText(int reservedPage, int first, int second, int third)
    {
        var renderer = new PdfDocumentRenderer { Document = CreateDocument(26) };
        if (reservedPage >= 0)
            renderer.DocumentRenderer.BodyBottomReservation = page => page == reservedPage ? 20 : 0;
        renderer.RenderDocument();
        renderer.PageCount.Should().Be(3);
        Enumerable.Range(1, 3).Select(page => BodyTexts(renderer, page).Length).Should().Equal(first, second, third);
        Enumerable.Range(1, 3).SelectMany(page => BodyTexts(renderer, page))
            .Should().Equal(Enumerable.Range(1, 26).Select(index => "Body" + index));
    }

    [Fact]
    public void CallbackUsesPhysicalPageDespiteDisplayedPageRestart()
    {
        var document = CreateDocument(14);
        document.LastSection.PageSetup.StartingNumber = 9;
        var seen = new List<int>();
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.BodyBottomReservation = page =>
        {
            seen.Add(page);
            return page == 1 ? 20 : 0;
        };
        renderer.RenderDocument();
        BodyTexts(renderer, 1).Should().HaveCount(10);
        BodyTexts(renderer, 2).Should().HaveCount(4);
        seen.Should().Contain(1).And.Contain(2).And.NotContain(9);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1)]
    [InlineData(120)]
    [InlineData(200)]
    public void InvalidReservationsFailBeforeLayout(double value)
    {
        var renderer = new DocumentRenderer(CreateDocument(1)) { BodyBottomReservation = _ => value };
        Action prepare = () => renderer.PrepareDocument();
        prepare.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("BodyBottomReservation");
    }

    [Fact]
    public void PreparingAgainUsesUpdatedReservations()
    {
        var renderer = new DocumentRenderer(CreateDocument(14)) { BodyBottomReservation = page => page == 1 ? 20 : 0 };
        renderer.PrepareDocument();
        renderer.GetDocumentObjectsFromPage(1).Should().HaveCount(10);
        renderer.BodyBottomReservation = null;
        renderer.PrepareDocument();
        renderer.GetDocumentObjectsFromPage(1).Should().HaveCount(12);
    }

    [Fact]
    public void ReservationAddsToFooterClearanceWithoutMovingFooter()
    {
        (int Count, double FooterY) Render(double reservation)
        {
            var document = CreateDocument(10);
            for (var index = 0; index < 6; index++)
                document.LastSection.Footers.Primary.AddParagraph("Footer" + index);
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.DocumentRenderer.BodyBottomReservation = _ => reservation;
            renderer.RenderDocument();
            var stream = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0);
            stream.Text.MoveAndGetNext(info => info.Text == "Footer0", true, out var footer).Should().BeTrue();
            return (BodyTexts(renderer, 1).Length, footer!.Y);
        }
        var baseline = Render(0);
        var reserved = Render(20);
        baseline.Count.Should().Be(7);
        reserved.Count.Should().Be(5);
        reserved.FooterY.Should().Be(baseline.FooterY);
    }

    static string[] BodyTexts(PdfDocumentRenderer renderer, int page)
    {
        var stream = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, page - 1);
        var text = new List<string>();
        while (stream.Text.MoveAndGetNext(true, out var item))
            if (item!.Text.StartsWith("Body", StringComparison.Ordinal)) text.Add(item.Text);
        return text.ToArray();
    }
    static Document CreateDocument(int paragraphs)
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
        for (var index = 1; index <= paragraphs; index++) section.AddParagraph("Body" + index);
        return document;
    }
}
