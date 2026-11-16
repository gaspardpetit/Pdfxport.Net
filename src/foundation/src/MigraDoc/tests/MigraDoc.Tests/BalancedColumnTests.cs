using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.IO;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class BalancedColumnTests : IDisposable
{
    public BalancedColumnTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(2, 8, 4)]
    [InlineData(3, 15, 5)]
    [InlineData(2, 36, 6)]
    [InlineData(3, 51, 5)]
    public void OnlyFinalPageUsesACommonShorterColumnHeight(int columns, int paragraphs, int perFinalColumn)
    {
        var document = Create(columns, paragraphs);
        document.LastSection.PageSetup.BalanceColumns = true;
        var renderer = Render(document);
        var pages = (paragraphs + columns * 12 - 1) / (columns * 12);
        renderer.PageCount.Should().Be(pages);
        Enumerable.Range(1, pages).SelectMany(page => Texts(renderer, page)).Select(t => t.Text)
            .Should().Equal(Enumerable.Range(1, paragraphs).Select(i => "Body" + i));
        if (pages > 1)
        {
            var first = Texts(renderer, 1);
            first.Should().HaveCount(columns * 12);
            first.Take(12).Should().OnlyContain(t => Math.Abs(t.X - 20) < 0.01);
        }
        var final = Texts(renderer, pages);
        var width = (200.0 - 20 * (columns - 1)) / columns;
        for (var column = 0; column < columns; column++)
            final.Skip(column * perFinalColumn).Take(perFinalColumn).Should()
                .OnlyContain(t => Math.Abs(t.X - (20 + column * (width + 20))) < 0.01);
    }

    [Fact]
    public void OptOutRetainsFullHeightFlow()
    {
        var document = Create(2, 8);
        var renderer = Render(document);
        Texts(renderer, 1).Should().HaveCount(8).And.OnlyContain(t => Math.Abs(t.X - 20) < 0.01);
    }

    [Fact]
    public void ParagraphCanContinueAcrossBalancedColumns()
    {
        var document = Create(2, 0);
        document.LastSection.PageSetup.BalanceColumns = true;
        var p = document.LastSection.AddParagraph();
        p.Format.WidowControl = false;
        for (var i = 1; i <= 8; i++)
        {
            if (i > 1) p.AddLineBreak();
            p.AddText("Body" + i);
        }
        var renderer = Render(document);
        Texts(renderer, 1).Select(t => t.Text).Should().Equal(Enumerable.Range(1, 8).Select(i => "Body" + i));
        Texts(renderer, 1).Skip(4).Should().OnlyContain(t => Math.Abs(t.X - 130) < 0.01);
    }

    [Fact]
    public void SpeculationDoesNotAdvanceListNumbersOrEmitExtraProgress()
    {
        var document = Create(2, 8);
        document.LastSection.PageSetup.BalanceColumns = true;
        foreach (var p in document.LastSection.Elements.OfType<Paragraph>())
        {
            p.Format.ListInfo.ListType = ListType.NumberList1;
            p.Format.ListInfo.ContinuePreviousList = true;
        }
        var plain = document.Clone();
        plain.LastSection.PageSetup.BalanceColumns = false;
        plain.LastSection.PageSetup.PageHeight = Unit.FromPoint(80);
        var baseline = new PdfDocumentRenderer { Document = plain };
        var expectedProgress = 0;
        baseline.DocumentRenderer.PrepareDocumentProgress += (_, _) => expectedProgress++;
        baseline.RenderDocument();
        var expected = AllTexts(baseline, 1).Select(t => t.Text).ToArray();
        var renderer = new PdfDocumentRenderer { Document = document };
        var progress = 0;
        renderer.DocumentRenderer.PrepareDocumentProgress += (_, _) => progress++;
        renderer.RenderDocument();
        AllTexts(renderer, 1).Select(t => t.Text).Should().Equal(expected);
        progress.Should().Be(expectedProgress);
    }

    [Fact]
    public void HeadersFootersAndFieldsRetainPhysicalPageSemantics()
    {
        var document = Create(2, 36);
        var section = document.LastSection;
        section.PageSetup.BalanceColumns = true;
        section.Headers.Primary.AddParagraph("Header").Format.Alignment = ParagraphAlignment.Right;
        var footer = section.Footers.Primary.AddParagraph("Footer");
        footer.AddPageField();
        footer.AddText("of");
        footer.AddNumPagesField();
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        for (var page = 1; page <= 2; page++)
        {
            var texts = AllTexts(renderer, page);
            texts.Count(t => t.Text == "Header").Should().Be(1);
            texts.Single(t => t.Text == "Header").X.Should().BeGreaterThan(180);
            string.Concat(texts.Where(t => t.Text.StartsWith("Footer") || t.Y > 130).Select(t => t.Text))
                .Should().Contain("Footer" + page + "of2");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FinalPageHeightUsesTheNewSectionsFooterState(bool differentFirstPage)
    {
        var document = Create(2, 1);
        document.LastSection.PageSetup.StartingNumber = 2;
        var section = document.AddSection();
        section.PageSetup = document.Sections[0].PageSetup.Clone();
        section.PageSetup.BalanceColumns = true;
        section.PageSetup.StartingNumber = 2;
        section.PageSetup.FooterDistance = Unit.FromPoint(10);
        section.PageSetup.OddAndEvenPagesHeaderFooter = !differentFirstPage;
        section.PageSetup.DifferentFirstPageHeaderFooter = differentFirstPage;
        var tallFooter = section.Footers.Primary.AddParagraph("TallFooter");
        tallFooter.Format.LineSpacingRule = LineSpacingRule.Exactly;
        tallFooter.Format.LineSpacing = Unit.FromPoint(100);
        var shortFooter = differentFirstPage ? section.Footers.FirstPage : section.Footers.EvenPage;
        shortFooter.AddParagraph("ShortFooter");
        for (var i = 1; i <= 18; i++) section.AddParagraph("Next" + i);

        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        var texts = Texts(renderer, 2, "Next");
        texts.Select(t => t.Text).Should().Equal(Enumerable.Range(1, 18).Select(i => "Next" + i));
        texts.Take(9).Should().OnlyContain(t => Math.Abs(t.X - 20) < 0.01);
        texts.Skip(9).Should().OnlyContain(t => Math.Abs(t.X - 130) < 0.01);
        AllTexts(renderer, 2).Should().Contain(t => t.Text == "ShortFooter");
        AllTexts(renderer, 2).Should().NotContain(t => t.Text == "TallFooter");
    }

    [Fact]
    public void DdlCloneInheritanceAndExplicitFalsePreserveTheOption()
    {
        var document = Create(2, 8);
        document.LastSection.PageSetup.BalanceColumns = true;
        var inherited = document.AddSection();
        inherited.PageSetup.TopMargin = Unit.FromPoint(20);
        for (var i = 1; i <= 8; i++) inherited.AddParagraph("Next" + i);
        var disabled = document.AddSection();
        disabled.PageSetup.BalanceColumns = false;
        for (var i = 1; i <= 8; i++) disabled.AddParagraph("Last" + i);
        var restored = DdlReader.DocumentFromString(DdlWriter.WriteToString(document));
        var clone = document.Clone();
        foreach (var candidate in new[] { document, restored, clone })
        {
            var renderer = Render(candidate);
            renderer.PageCount.Should().Be(3);
            Texts(renderer, 1)[4].X.Should().BeApproximately(130, 0.01);
            Texts(renderer, 2, "Next")[4].X.Should().BeApproximately(130, 0.01);
            Texts(renderer, 3, "Last").Should().OnlyContain(t => Math.Abs(t.X - 20) < 0.01);
        }
    }

    [Fact]
    public void UnequalColumnsRetainTheirWidthsWhenBalanced()
    {
        var document = Create(2, 8);
        var setup = document.LastSection.PageSetup;
        setup.ColumnWidthsPoints = new[] { 60.0, 120.0 };
        setup.BalanceColumns = true;
        var renderer = Render(document);
        Texts(renderer, 1).Take(4).Should().OnlyContain(t => Math.Abs(t.X - 20) < 0.01);
        Texts(renderer, 1).Skip(4).Should().OnlyContain(t => Math.Abs(t.X - 100) < 0.01);
    }

    [Fact]
    public void KeepTogetherParagraphCannotExploitOversizedFallbackInAShortProbe()
    {
        var document = Create(2, 1);
        document.LastSection.PageSetup.BalanceColumns = true;
        var p = document.LastSection.AddParagraph("After");
        p.Format.KeepTogether = true;
        p.AddLineBreak();
        p.AddText("Second");
        var renderer = Render(document);
        renderer.PageCount.Should().Be(1);
        var after = Texts(renderer, 1, "After").Single();
        after.X.Should().BeApproximately(130, 0.01);
        var second = Texts(renderer, 1, "Second").Single();
        (second.Y - after.Y).Should().BeApproximately(10, 0.01);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExplicitBreaksKeepTheirPageOrColumnMeaning(bool pageBreak)
    {
        var document = Create(2, 1);
        var section = document.LastSection;
        section.PageSetup.BalanceColumns = true;
        if (pageBreak) section.AddPageBreak(); else section.AddColumnBreak();
        for (var i = 1; i <= 4; i++) section.AddParagraph("After" + i);
        var renderer = Render(document);
        renderer.PageCount.Should().Be(pageBreak ? 2 : 1);
        var after = Texts(renderer, pageBreak ? 2 : 1, "After");
        after.Should().HaveCount(4);
        after[0].X.Should().BeApproximately(pageBreak ? 20 : 130, 0.01);
        if (!pageBreak) after.Should().OnlyContain(t => Math.Abs(t.X - 130) < 0.01);
    }

    [Fact]
    public void OversizedKeepTogetherContentRetainsTheOrdinaryFallback()
    {
        var document = Create(2, 0);
        document.LastSection.PageSetup.BalanceColumns = true;
        var p = document.LastSection.AddParagraph("Body1");
        p.Format.KeepTogether = true;
        p.Format.LineSpacing = Unit.FromPoint(140);
        var plain = document.Clone();
        plain.LastSection.PageSetup.BalanceColumns = false;
        var expected = Render(plain);
        var renderer = Render(document);
        renderer.PageCount.Should().Be(expected.PageCount);
        AllTexts(renderer, 1).Should().Equal(AllTexts(expected, 1));
    }

    [Fact]
    public void ProbesPreserveBeforeSpacingOnParagraphsThatStartInsideAColumn()
    {
        var document = Create(2, 0);
        var section = document.LastSection;
        section.PageSetup.BalanceColumns = true;
        var paragraphs = Enumerable.Range(1, 6).Select(i => section.AddParagraph("Body" + i)).ToArray();
        foreach (var p in paragraphs) p.Format.SpaceBefore = Unit.FromPoint(5);
        var renderer = Render(document);
        var texts = Texts(renderer, 1);
        texts.Should().HaveCount(6);
        texts.Take(3).Should().OnlyContain(t => Math.Abs(t.X - 20) < 0.01);
        texts.Skip(3).Should().OnlyContain(t => Math.Abs(t.X - 130) < 0.01);
        (texts[1].Y - texts[0].Y).Should().BeApproximately(15, 0.01);
        (texts[4].Y - texts[3].Y).Should().BeApproximately(15, 0.01);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(18)]
    public void OneColumnRetainsOrdinaryLayout(int paragraphs)
    {
        var document = Create(1, paragraphs);
        document.LastSection.PageSetup.BalanceColumns = true;
        var plain = document.Clone();
        plain.LastSection.PageSetup.BalanceColumns = false;
        var expected = Render(plain);
        var renderer = Render(document);
        renderer.PageCount.Should().Be(expected.PageCount);
        for (var page = 1; page <= renderer.PageCount; page++)
            AllTexts(renderer, page).Should().Equal(AllTexts(expected, page));
    }

    [Fact]
    public void WholeTablesCanFlowIntoBalancedColumns()
    {
        var document = Create(2, 0);
        var section = document.LastSection;
        section.PageSetup.BalanceColumns = true;
        for (var i = 1; i <= 2; i++)
        {
            var table = section.AddTable();
            table.LeftPadding = Unit.Zero;
            table.AddColumn(Unit.FromPoint(90));
            var row = table.AddRow();
            row.HeightRule = RowHeightRule.Exactly;
            row.Height = Unit.FromPoint(20);
            row.Cells[0].AddParagraph("Cell" + i);
        }
        var renderer = Render(document);
        renderer.PageCount.Should().Be(1);
        var infos = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!.Where(i => i.DocumentObject is Table).ToArray();
        infos.Should().HaveCount(2);
        infos[0].LayoutInfo.ContentArea.X.Point.Should().BeApproximately(20, 0.01);
        infos[1].LayoutInfo.ContentArea.X.Point.Should().BeApproximately(130, 0.01);
        Texts(renderer, 1, "Cell").Select(t => t.Text).Should().Equal("Cell1", "Cell2");
    }

    [Fact]
    public void BalancedColumnsRetainPhysicalPageWrapExclusions()
    {
        var document = Create(2, 0);
        var section = document.LastSection;
        section.PageSetup.BalanceColumns = true;
        var frame = section.AddTextFrame();
        frame.RelativeHorizontal = RelativeHorizontal.Page;
        frame.RelativeVertical = RelativeVertical.Page;
        frame.Left = Unit.FromPoint(120);
        frame.Top = Unit.FromPoint(20);
        frame.Width = Unit.FromPoint(110);
        frame.Height = Unit.FromPoint(30);
        frame.WrapFormat.Style = WrapStyle.BothSides;
        for (var i = 1; i <= 8; i++) section.AddParagraph("Body" + i);
        var renderer = Render(document);
        renderer.PageCount.Should().Be(1);
        var texts = Texts(renderer, 1);
        texts.Select(t => t.Text).Should().Equal(Enumerable.Range(1, 8).Select(i => "Body" + i));
        texts.Take(6).Should().OnlyContain(t => Math.Abs(t.X - 20) < 0.01);
        texts.Skip(6).Should().OnlyContain(t => Math.Abs(t.X - 130) < 0.01);
        texts[6].Y.Should().BeGreaterThan(texts[0].Y + 29);
    }

    static PdfDocumentRenderer Render(Document document)
    {
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        return renderer;
    }

    static (string Text, double X, double Y)[] Texts(PdfDocumentRenderer renderer, int page, string prefix = "Body")
        => AllTexts(renderer, page).Where(t => t.Text.StartsWith(prefix, StringComparison.Ordinal)).ToArray();

    static (string Text, double X, double Y)[] AllTexts(PdfDocumentRenderer renderer, int page)
    {
        var stream = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, page - 1);
        var texts = new List<(string, double, double)>();
        while (stream.Text.MoveAndGetNext(true, out var item))
            texts.Add((item!.Text, item.X, renderer.PdfDocument.Pages[page - 1].Height.Point - item.Y));
        return texts.ToArray();
    }

    static Document Create(int columns, int paragraphs)
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
        for (var i = 1; i <= paragraphs; i++) section.AddParagraph("Body" + i);
        return document;
    }
}
