using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.IO;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using MigraDoc.RtfRendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class ContinuousSectionTests : IDisposable
{
    public ContinuousSectionTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void SectionsContinueBelowExistingFlowAndBalanceOnlyTheirOwnContent(int columns, bool balance)
    {
        var document = Create(2);
        var section = Next(document, columns);
        section.PageSetup.BalanceColumns = balance;
        for (var i = 1; i <= 8; i++) section.AddParagraph("Next" + i);
        var renderer = Render(document);
        renderer.PageCount.Should().Be(1);
        var before = Texts(renderer, 1, "Before");
        var after = Texts(renderer, 1, "Next");
        after.Should().HaveCount(8);
        after[0].Y.Should().BeApproximately(before[1].Y + 10, 0.01);
        after.Take(balance ? 4 : 8).Should().OnlyContain(t => Math.Abs(t.X - 20) < 0.01);
        if (balance)
        {
            after.Skip(4).Should().OnlyContain(t => Math.Abs(t.X - 130) < 0.01);
            after[4].Y.Should().BeApproximately(after[0].Y, 0.01);
        }
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 1)]
    public void OverflowResumesAtTheTopOfTheNextPage(int columns, int remaining)
    {
        var document = Create(10);
        var section = Next(document, columns);
        for (var i = 1; i <= 5; i++) section.AddParagraph("Next" + i);
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        Texts(renderer, 1, "Next").Should().HaveCount(columns * 2);
        var second = Texts(renderer, 2, "Next");
        second.Should().HaveCount(remaining);
        second[0].X.Should().BeApproximately(20, 0.01);
        second[0].Y.Should().BeApproximately(Texts(renderer, 1, "Before")[0].Y, 0.01);
    }

    [Fact]
    public void SharedPageRetainsItsHeaderFooterAndBodyBottom()
    {
        var document = Create(8);
        document.LastSection.Headers.Primary.AddParagraph("OldHeader");
        document.LastSection.Footers.Primary.AddParagraph("OldFooter");
        var section = Next(document);
        section.PageSetup.BottomMargin = Unit.FromPoint(70);
        section.Headers.Primary.AddParagraph("NewHeader");
        section.Footers.Primary.AddParagraph("NewFooter");
        for (var i = 1; i <= 8; i++) section.AddParagraph("Next" + i);
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        Texts(renderer, 1, "Next").Should().HaveCount(4);
        Texts(renderer, 2, "Next").Should().HaveCount(4);
        Texts(renderer, 1, "Old").Select(t => t.Text).Should().BeEquivalentTo("OldHeader", "OldFooter");
        Texts(renderer, 1, "New").Should().BeEmpty();
        Texts(renderer, 2, "New").Select(t => t.Text).Should().BeEquivalentTo("NewHeader", "NewFooter");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FirstElementPageBreakAdvancesToANewPhysicalPage(bool paragraphBreak)
    {
        var document = Create(1);
        var section = Next(document, 2);
        if (!paragraphBreak) section.AddPageBreak();
        var paragraph = section.AddParagraph("Next1");
        paragraph.Format.PageBreakBefore = paragraphBreak;
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        Texts(renderer, 1, "Next").Should().BeEmpty();
        Texts(renderer, 2, "Next").Single().X.Should().BeApproximately(20, 0.01);
    }

    [Fact]
    public void KeepTogetherCannotUseOversizedFallbackInRemainingColumns()
    {
        var document = Create(10);
        var section = Next(document, 2);
        var p = section.AddParagraph("Next1");
        p.Format.KeepTogether = true;
        for (var i = 2; i <= 4; i++) { p.AddLineBreak(); p.AddText("Next" + i); }
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        Texts(renderer, 1, "Next").Should().BeEmpty();
        Texts(renderer, 2, "Next").Should().HaveCount(4);
    }

    [Fact]
    public void FieldsInASharedSectionReceivePageAndSectionTotals()
    {
        var document = Create(1);
        var section = Next(document);
        var p = section.AddParagraph("Fields");
        p.AddPageField(); p.AddText("/"); p.AddNumPagesField();
        p.AddText("/"); p.AddSectionField(); p.AddText("/"); p.AddSectionPagesField();
        var renderer = Render(document);
        string.Concat(Texts(renderer, 1).Select(t => t.Text)).Should().Contain("Fields1/1/2/1");
    }

    [Fact]
    public void DifferentPageDimensionsStartANewPage()
    {
        var document = Create(1);
        Next(document).PageSetup.PageWidth = Unit.FromPoint(300);
        document.LastSection.AddParagraph("Next1");
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        renderer.PdfDocument.Pages[1].Width.Point.Should().BeApproximately(300, 0.01);
    }

    [Fact]
    public void DdlCloneAndRtfPreserveContinuousSectionStart()
    {
        var document = Create(1);
        Next(document).AddParagraph("Next1");
        var restored = DdlReader.DocumentFromString(DdlWriter.WriteToString(document));
        foreach (var candidate in new[] { restored, document.Clone() })
        {
            candidate.LastSection.PageSetup.SectionStart.Should().Be(BreakType.BreakContinuous);
            Render(candidate).PageCount.Should().Be(1);
        }
        var rtf = new RtfDocumentRenderer().RenderToString(document, "");
        rtf.Should().Contain("\\sbknone");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NewSectionsFirstOwnedPageUsesItsFirstPageStoryAndFooterHeight(bool balanced)
    {
        var document = Create(10);
        var section = Next(document, 2);
        section.PageSetup.BalanceColumns = balanced;
        section.PageSetup.DifferentFirstPageHeaderFooter = true;
        section.Headers.FirstPage.AddParagraph("FirstHeader");
        var footer = section.Footers.FirstPage.AddParagraph("FirstFooter");
        footer.Format.LineSpacingRule = LineSpacingRule.Exactly;
        footer.Format.LineSpacing = Unit.FromPoint(60);
        section.Footers.Primary.AddParagraph("PrimaryFooter");
        for (var i = 1; i <= 24; i++) section.AddParagraph("Next" + i);
        var renderer = Render(document);
        renderer.PageCount.Should().Be(3);
        Texts(renderer, 1, "First").Should().BeEmpty();
        Texts(renderer, 2, "First").Select(t => t.Text).Should().BeEquivalentTo("FirstHeader", "FirstFooter");
        Texts(renderer, 2, "Next").Should().HaveCount(14);
        Texts(renderer, 3, "Primary").Single().Text.Should().Be("PrimaryFooter");
        Enumerable.Range(1, 3).SelectMany(page => Texts(renderer, page, "Next"))
            .Select(t => t.Text).Should().Equal(Enumerable.Range(1, 24).Select(i => "Next" + i));
    }

    [Fact]
    public void SeveralContinuousSectionsKeepTheirOwnFieldContexts()
    {
        var document = Create(1);
        for (var i = 2; i <= 4; i++)
        {
            var p = Next(document).AddParagraph("Section");
            p.AddSectionField(); p.AddText("of"); p.AddNumPagesField();
        }
        var renderer = Render(document);
        renderer.PageCount.Should().Be(1);
        string.Concat(Texts(renderer, 1).Select(t => t.Text)).Should()
            .Contain("Section2of1Section3of1Section4of1");
    }

    [Fact]
    public void FullPrecedingPageAdvancesWithoutLosingTheNewSectionsFirstElement()
    {
        var document = Create(12);
        Next(document, 2).AddParagraph("Next1");
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        Texts(renderer, 1, "Next").Should().BeEmpty();
        Texts(renderer, 2, "Next").Single().Y.Should()
            .BeApproximately(Texts(renderer, 1, "Before")[0].Y, 0.01);
    }

    [Fact]
    public void DefaultSectionStartStillOpensANewPage()
    {
        var document = Create(1);
        var section = document.AddSection();
        section.PageSetup = document.Sections[0].PageSetup.Clone();
        section.AddParagraph("Next1");
        Render(document).PageCount.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NumberingRestartWaitsWhenNoContentFitsOnTheSharedPage(bool pageBreak)
    {
        var document = Create(10);
        var section = Next(document);
        section.PageSetup.StartingNumber = 7;
        if (pageBreak) section.AddPageBreak();
        var p = section.AddParagraph("Number");
        p.AddPageField();
        p.Format.KeepTogether = true;
        for (var i = 0; i < 3; i++) { p.AddLineBreak(); p.AddText("Line"); }
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        string.Concat(Texts(renderer, 2).Select(t => t.Text)).Should().Contain("Number7");
    }

    [Theory]
    [InlineData(12, false, 1)]
    [InlineData(10, true, 1)]
    [InlineData(10, false, 2)]
    public void SectionTotalsCountOnlyPagesReceivingContent(int precedingLines, bool keepTogether, int pages)
    {
        var document = Create(precedingLines);
        var p = Next(document).AddParagraph("Pages");
        p.AddSectionPagesField();
        p.Format.KeepTogether = keepTogether;
        for (var i = 0; i < 3; i++) { p.AddLineBreak(); p.AddText("Line"); }
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        string.Concat(Enumerable.Range(1, 2).SelectMany(page => Texts(renderer, page)).Select(t => t.Text))
            .Should().Contain("Pages" + pages);
    }

    [Fact]
    public void WidowRepairLooksAheadToTheFullNextPhysicalPage()
    {
        var document = Create(10);
        var p = Next(document, 2).AddParagraph("Line1");
        p.AddLineBreak(); p.AddText("Line2"); p.AddLineBreak(); p.AddText("Line3");
        p.Format.WidowControl = true;
        var renderer = Render(document);
        renderer.PageCount.Should().Be(1);
        var lines = Texts(renderer, 1, "Line");
        lines.Select(t => t.Text).Should().Equal("Line1", "Line2", "Line3");
        lines.Count(t => Math.Abs(t.X - 20) < 0.01).Should().Be(1);
        lines.Count(t => Math.Abs(t.X - 130) < 0.01).Should().Be(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void PreparedBodyFragmentsUseTheirOwnSectionFields(int kind)
    {
        var document = Create(1);
        var section = Next(document);
        section.PageSetup.StartingNumber = 7;
        Paragraph p;
        DocumentObject target;
        if (kind == 1)
        {
            var frame = section.AddTextFrame();
            frame.Width = Unit.FromPoint(180); frame.Height = Unit.FromPoint(20);
            p = frame.AddParagraph("Fields"); target = frame;
        }
        else if (kind == 2)
        {
            var table = section.AddTable(); table.AddColumn(Unit.FromPoint(180));
            p = table.AddRow().Cells[0].AddParagraph("Fields"); target = table;
        }
        else { p = section.AddParagraph("Fields"); target = p; }
        p.AddPageField(); p.AddText("/"); p.AddSectionField();
        p.AddText("/"); p.AddSectionPagesField(); p.AddText("/"); p.AddNumPagesField();
        var renderer = new DocumentRenderer(document);
        renderer.DeferTextFramePaint = _ => kind == 1;
        renderer.PrepareDocument();
        renderer.FormattedDocument!.PageCount.Should().Be(1);
        var fragment = renderer.GetRenderInfoFromPage(1)!.Single(info => ReferenceEquals(info.DocumentObject, target));
        using var pdf = new PdfDocument();
        var page = pdf.AddPage(); page.Width = 240; page.Height = 160;
        using (var gfx = XGraphics.FromPdfPage(page)) renderer.RenderPreparedObject(gfx, fragment, 1, 0);
        var stream = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0);
        var texts = new List<string>();
        while (stream.Text.MoveAndGetNext(true, out var item)) texts.Add(item!.Text);
        string.Concat(texts).Should().Be("Fields7/2/1/1");
        renderer.CurrentRenderPage.Should().Be(0);
    }

    [Theory]
    [InlineData(1, false, 50)]
    [InlineData(2, false, 50)]
    [InlineData(2, true, 50)]
    [InlineData(2, false, 10)]
    public void TableRowsUseFullBodyHeightForTheirOversizedLimit(int columns, bool balance, double rowHeight)
    {
        var document = Create(10);
        var section = Next(document, columns);
        section.PageSetup.BalanceColumns = balance;
        var table = section.AddTable(); table.AddColumn(Unit.FromPoint(60));
        var row = table.AddRow(); row.HeightRule = RowHeightRule.Exactly;
        row.Height = Unit.FromPoint(rowHeight);
        row.Cells[0].AddParagraph("Row");
        var renderer = Render(document);
        var tablePage = rowHeight > 20 ? 2 : 1;
        renderer.PageCount.Should().Be(tablePage);
        if (tablePage == 2) Texts(renderer, 1, "Row").Should().BeEmpty();
        Texts(renderer, tablePage, "Row").Should().HaveCount(1);
        var layout = renderer.DocumentRenderer.GetRenderInfoFromPage(tablePage)!
            .Single(info => ReferenceEquals(info.DocumentObject, table)).LayoutInfo.ContentArea;
        layout.Height.Point.Should().BeApproximately(rowHeight, 0.01);
        (layout.Y.Point + layout.Height.Point).Should().BeLessThanOrEqualTo(140.01);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BalancingDoesNotShrinkATableRowsMeasuredHeight(bool balance)
    {
        var document = Create(0);
        var section = document.LastSection;
        section.PageSetup.ColumnCount = 2;
        section.PageSetup.BalanceColumns = balance;
        var table = section.AddTable(); table.AddColumn(Unit.FromPoint(60));
        var row = table.AddRow(); row.HeightRule = RowHeightRule.Exactly;
        row.Height = Unit.FromPoint(50);
        row.Cells[0].AddParagraph("Row");
        var renderer = Render(document);
        renderer.PageCount.Should().Be(1);
        renderer.DocumentRenderer.GetRenderInfoFromPage(1)!.Single().LayoutInfo.ContentArea.Height.Point
            .Should().BeApproximately(50, 0.01);
        Texts(renderer, 1, "Row").Should().HaveCount(1);
    }

    static Document Create(int paragraphs)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 8;
        var format = document.Styles.Normal.ParagraphFormat;
        format.LineSpacingRule = LineSpacingRule.Exactly;
        format.LineSpacing = Unit.FromPoint(10);
        format.SpaceBefore = format.SpaceAfter = Unit.Zero;
        var s = document.AddSection();
        s.PageSetup.PageWidth = Unit.FromPoint(240);
        s.PageSetup.PageHeight = Unit.FromPoint(160);
        s.PageSetup.TopMargin = s.PageSetup.BottomMargin = Unit.FromPoint(20);
        s.PageSetup.LeftMargin = s.PageSetup.RightMargin = Unit.FromPoint(20);
        s.PageSetup.HeaderDistance = s.PageSetup.FooterDistance = Unit.FromPoint(10);
        for (var i = 1; i <= paragraphs; i++) s.AddParagraph("Before" + i);
        return document;
    }

    static Section Next(Document document, int columns = 1)
    {
        var previous = document.LastSection;
        var section = document.AddSection();
        section.PageSetup = previous.PageSetup.Clone();
        section.PageSetup.SectionStart = BreakType.BreakContinuous;
        section.PageSetup.ColumnCount = columns;
        section.PageSetup.ColumnSpacing = Unit.FromPoint(20);
        return section;
    }

    static PdfDocumentRenderer Render(Document document)
    {
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        return renderer;
    }

    static (string Text, double X, double Y)[] Texts(PdfDocumentRenderer renderer, int page, string prefix = "")
    {
        var stream = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, page - 1);
        var texts = new List<(string, double, double)>();
        while (stream.Text.MoveAndGetNext(true, out var item))
            if (item!.Text.StartsWith(prefix, StringComparison.Ordinal))
                texts.Add((item.Text, item.X, renderer.PdfDocument.Pages[page - 1].Height.Point - item.Y));
        return texts.ToArray();
    }
}
