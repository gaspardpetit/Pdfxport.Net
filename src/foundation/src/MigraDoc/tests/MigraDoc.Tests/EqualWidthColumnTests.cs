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
public class EqualWidthColumnTests : IDisposable
{
    public EqualWidthColumnTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void ColumnsFillInOrderWithoutLosingOrDuplicatingContent(int columns)
    {
        var document = Create(columns, columns * 12 + 2);
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        var first = Texts(renderer, 1);
        first.Select(t => t.Text).Should().Equal(Enumerable.Range(1, columns * 12).Select(i => "Body" + i));
        Texts(renderer, 2).Select(t => t.Text).Should().Equal("Body" + (columns * 12 + 1), "Body" + (columns * 12 + 2));
        var width = (200.0 - 20 * (columns - 1)) / columns;
        for (var column = 0; column < columns; column++)
        {
            first.Skip(column * 12).Take(12).Select(t => t.X).Should()
                .OnlyContain(x => Math.Abs(x - (20 + column * (width + 20))) < 0.01);
            first[column * 12].Y.Should().BeApproximately(first[0].Y, 0.01);
        }
        renderer.DocumentRenderer.GetRenderInfoFromPage(1)!.OfType<ParagraphRenderInfo>()
            .Select(info => info.LayoutInfo.ContentArea.Width.Point).Should()
            .OnlyContain(w => Math.Abs(w - width) < 0.01);
    }

    [Fact]
    public void AParagraphCanContinueThroughColumnsAndOntoTheNextPage()
    {
        var document = Create(2, 0);
        var paragraph = document.LastSection.AddParagraph();
        paragraph.Format.WidowControl = false;
        for (var i = 1; i <= 30; i++)
        {
            if (i > 1) paragraph.AddLineBreak();
            paragraph.AddText("Body" + i);
        }
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        Texts(renderer, 1).Should().HaveCount(24);
        Enumerable.Range(1, 2).SelectMany(page => Texts(renderer, page)).Select(t => t.Text)
            .Should().Equal(Enumerable.Range(1, 30).Select(i => "Body" + i));
        Texts(renderer, 1)[12].X.Should().BeApproximately(130, 0.01);
        Texts(renderer, 2)[0].X.Should().BeApproximately(20, 0.01);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 13)]
    [InlineData(true, 13)]
    public void ExplicitPageBreaksSkipRemainingColumns(bool paragraphBreak, int before)
    {
        var document = Create(3, before);
        if (!paragraphBreak) document.LastSection.AddPageBreak();
        var after = document.LastSection.AddParagraph("After");
        after.Format.PageBreakBefore = paragraphBreak;
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        Texts(renderer, 1).Should().HaveCount(before);
        Texts(renderer, 2, "After").Single().X.Should().BeApproximately(20, 0.01);
    }

    [Fact]
    public void PageBreakBeforeAtStartOfSecondColumnStillStartsAPhysicalPage()
    {
        var document = Create(2, 12);
        var after = document.LastSection.AddParagraph("After");
        after.Format.PageBreakBefore = true;
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        Texts(renderer, 2, "After").Should().ContainSingle();
    }

    [Fact]
    public void SectionsStartOnANewPageAndInheritColumnSettings()
    {
        var document = Create(2, 1);
        var section = document.AddSection();
        for (var i = 1; i <= 13; i++) section.AddParagraph("Next" + i);
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        Texts(renderer, 1).Should().ContainSingle();
        var next = Texts(renderer, 2, "Next");
        next.Should().HaveCount(13);
        next[0].X.Should().BeApproximately(20, 0.01);
        next[12].X.Should().BeApproximately(130, 0.01);
    }

    [Fact]
    public void HeadersFootersAndPageFieldsUsePhysicalPages()
    {
        var document = Create(2, 26);
        var section = document.LastSection;
        var header = section.Headers.Primary.AddParagraph("Header");
        header.Format.Alignment = ParagraphAlignment.Right;
        var footer = section.Footers.Primary.AddParagraph("Footer");
        footer.AddPageField();
        footer.AddText("of");
        footer.AddNumPagesField();
        footer.AddText("section");
        footer.AddSectionPagesField();
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        for (var page = 1; page <= 2; page++)
        {
            var stream = AllTexts(renderer, page);
            stream.Count(t => t.Text == "Header").Should().Be(1);
            stream.Single(t => t.Text == "Header").X.Should().BeGreaterThan(180);
            string.Concat(stream.Where(t => t.Text.StartsWith("Footer") || t.Y > 130).Select(t => t.Text))
                .Should().Contain("Footer" + page + "of2section2");
        }
    }

    [Fact]
    public void MirroredMarginsAndReservationsApplyPerPhysicalPage()
    {
        var document = Create(2, 42);
        var setup = document.LastSection.PageSetup;
        setup.LeftMargin = Unit.FromPoint(30);
        setup.RightMargin = Unit.FromPoint(10);
        setup.MirrorMargins = true;
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.BodyBottomReservation = page => page == 1 ? 20 : 0;
        renderer.RenderDocument();
        renderer.PageCount.Should().Be(2);
        Texts(renderer, 1).Should().HaveCount(20);
        Texts(renderer, 2).Should().HaveCount(22);
        Texts(renderer, 1)[0].X.Should().BeApproximately(30, 0.01);
        Texts(renderer, 1)[10].X.Should().BeApproximately(140, 0.01);
        Texts(renderer, 2)[0].X.Should().BeApproximately(10, 0.01);
        Texts(renderer, 2)[12].X.Should().BeApproximately(120, 0.01);
    }

    [Fact]
    public void KeepWithNextMovesToTheNextColumn()
    {
        var document = Create(2, 11);
        document.LastSection.AddParagraph("Heading").Format.KeepWithNext = true;
        document.LastSection.AddParagraph("Following");
        var renderer = Render(document);
        renderer.PageCount.Should().Be(1);
        Texts(renderer, 1, "Heading").Single().X.Should().BeApproximately(130, 0.01);
        Texts(renderer, 1, "Following").Single().X.Should().BeApproximately(130, 0.01);
    }

    [Theory]
    [InlineData(RowAlignment.Left, 0)]
    [InlineData(RowAlignment.Center, 25)]
    [InlineData(RowAlignment.Right, 50)]
    public void TablesAlignWithinTheCurrentColumn(RowAlignment alignment, double offset)
    {
        var document = Create(2, 12);
        var table = document.LastSection.AddTable();
        table.AddColumn(Unit.FromPoint(40));
        table.LeftPadding = table.RightPadding = Unit.Zero;
        table.Rows.Alignment = alignment;
        table.AddRow().Cells[0].AddParagraph("Cell");
        var renderer = Render(document);
        renderer.PageCount.Should().Be(1);
        var info = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!.Single(i => i.DocumentObject == table);
        info.LayoutInfo.ContentArea.X.Point.Should().BeApproximately(130 + offset, 0.01);
        Texts(renderer, 1, "Cell").Single().X.Should().BeApproximately(130 + offset, 0.01);
    }

    [Fact]
    public void ColumnSettingsSurviveCloneAndDdlAndDefaultsStayUnspecified()
    {
        var document = Create(2, 1);
        var setup = document.LastSection.PageSetup;
        setup.Clone().ColumnCount.Should().Be(2);
        setup.Clone().ColumnSpacing.Point.Should().Be(20);
        var restored = DdlReader.DocumentFromString(DdlWriter.WriteToString(document));
        restored.LastSection.PageSetup.ColumnCount.Should().Be(2);
        restored.LastSection.PageSetup.ColumnSpacing.Point.Should().Be(20);
        Render(restored).PageCount.Should().Be(1);
        var defaults = new PageSetup();
        defaults.ColumnCount.Should().Be(1);
        defaults.ColumnSpacing.Point.Should().Be(0);
        defaults.Values.ColumnCount.Should().BeNull();
        defaults.Values.ColumnSpacing.Should().BeNull();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public void InvalidCountsAreRejected(int count)
    {
        Action set = () => new PageSetup().ColumnCount = count;
        set.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void InvalidSpacingIsRejected(double spacing)
    {
        Action set = () => new PageSetup().ColumnSpacing = Unit.FromPoint(spacing);
        set.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(100)]
    [InlineData(110)]
    public void GapsCannotConsumeTheWholeBodyWidth(double spacing)
    {
        var document = Create(3, 1);
        document.LastSection.PageSetup.ColumnSpacing = Unit.FromPoint(spacing);
        Action render = () => Render(document);
        render.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("ColumnSpacing");
    }

    [Theory]
    [InlineData(null, null, 130)]
    [InlineData(2, 0d, 120)]
    [InlineData(1, null, 20)]
    public void PartialPageSetupInheritsColumnsButRespectsExplicitResets(int? count, double? spacing, double lastX)
    {
        var document = Create(2, 1);
        var section = document.AddSection();
        section.PageSetup.TopMargin = Unit.FromPoint(20);
        if (count is { } c) section.PageSetup.ColumnCount = c;
        if (spacing is { } gap) section.PageSetup.ColumnSpacing = Unit.FromPoint(gap);
        for (var i = 1; i <= 13; i++) section.AddParagraph("Next" + i);
        var renderer = Render(document);
        var page = count == 1 ? 3 : 2;
        Texts(renderer, page, "Next").Last().X.Should().BeApproximately(lastX, 0.01);
        section.PageSetup.ColumnSpacing.Point.Should().Be(spacing ?? 20);
    }

    [Fact]
    public void OddPageSectionBreakSkipsAPhysicalPageRatherThanAColumn()
    {
        var document = Create(2, 1);
        var section = document.AddSection();
        section.PageSetup.SectionStart = BreakType.BreakOddPage;
        section.AddParagraph("Next");
        var renderer = Render(document);
        renderer.PageCount.Should().Be(3);
        Texts(renderer, 2, "Next").Should().BeEmpty();
        Texts(renderer, 3, "Next").Single().X.Should().BeApproximately(20, 0.01);
    }

    [Fact]
    public void PageNumbersRemainTheSameAcrossColumnsAfterARestart()
    {
        var document = Create(2, 0);
        document.LastSection.PageSetup.StartingNumber = 9;
        for (var i = 0; i < 26; i++)
        {
            var p = document.LastSection.AddParagraph("Body");
            p.AddPageField();
        }
        var renderer = Render(document);
        var pageOne = string.Concat(AllTexts(renderer, 1).Select(t => t.Text));
        var pageTwo = string.Concat(AllTexts(renderer, 2).Select(t => t.Text));
        pageOne.Should().Be(string.Concat(Enumerable.Repeat("Body9", 24)));
        pageTwo.Should().Be("Body10Body10");
    }

    [Fact]
    public void PaginatedTableRepeatsItsHeaderInEachColumn()
    {
        var document = Create(2, 0);
        var table = document.LastSection.AddTable();
        table.AddColumn(Unit.FromPoint(60));
        table.TopPadding = table.BottomPadding = Unit.Zero;
        table.AddRow().Cells[0].AddParagraph("Header");
        table.Rows[0].HeadingFormat = true;
        for (var i = 1; i <= 25; i++) table.AddRow().Cells[0].AddParagraph("Body" + i);
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        AllTexts(renderer, 1).Count(t => t.Text == "Header").Should().Be(2);
        AllTexts(renderer, 2).Count(t => t.Text == "Header").Should().Be(1);
        Enumerable.Range(1, 2).SelectMany(page => Texts(renderer, page)).Select(t => t.Text)
            .Should().Equal(Enumerable.Range(1, 25).Select(i => "Body" + i));
    }

    public static IEnumerable<object[]> ShapePositions()
    {
        foreach (var before in new[] { 13, 25, 49 })
        foreach (var reference in new[] { RelativeHorizontal.Margin, RelativeHorizontal.Column, RelativeHorizontal.Page })
        {
            yield return new object[] { before, reference, null!, 0d };
            yield return new object[] { before, reference, 0d, 5d };
            yield return new object[] { before, reference, 7d, 0d };
            yield return new object[] { before, reference, null!, 5d };
        }
    }

    [Theory]
    [MemberData(nameof(ShapePositions))]
    public void NearAlignedShapesKeepTheirHorizontalReference(int before, RelativeHorizontal reference,
        double? left, double wrapDistance)
    {
        var document = Create(3, before);
        var section = document.LastSection;
        section.PageSetup.LeftMargin = Unit.FromPoint(30);
        section.PageSetup.RightMargin = Unit.FromPoint(10);
        section.PageSetup.MirrorMargins = true;
        var frame = section.AddTextFrame();
        frame.RelativeHorizontal = reference;
        frame.RelativeVertical = RelativeVertical.Page;
        frame.Top = Unit.FromPoint(30);
        if (left is { } offset) frame.Left = Unit.FromPoint(offset);
        frame.WrapFormat.DistanceLeft = Unit.FromPoint(wrapDistance);
        frame.Width = Unit.FromPoint(30);
        frame.Height = Unit.FromPoint(10);
        frame.AddParagraph("Frame");
        var renderer = Render(document);
        var page = before / 36 + 1;
        var column = before % 36 / 12;
        var marginOrigin = page % 2 == 0 ? 10 : 30;
        var columnOrigin = marginOrigin + column * (160.0 / 3 + 20);
        var expected = reference switch
        {
            RelativeHorizontal.Margin => marginOrigin + (left ?? wrapDistance),
            RelativeHorizontal.Column => columnOrigin + (left ?? wrapDistance),
            _ => wrapDistance != 0 ? wrapDistance : left ?? 0
        };
        renderer.PageCount.Should().Be(page);
        var info = renderer.DocumentRenderer.GetRenderInfoFromPage(page)!
            .Single(i => ReferenceEquals(i.DocumentObject, frame));
        info.LayoutInfo.ContentArea.X.Point.Should().BeApproximately(expected, 0.01);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShapeWrapPersistsAcrossColumnsButClearsOnTheNextPage(bool splitParagraph)
    {
        var document = Create(2, 0);
        AddWrappingFrame(document.LastSection, 90, 20, 60, 30);
        AddBodyLines(document.LastSection, 30, splitParagraph);
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        var first = Texts(renderer, 1);
        first.Should().HaveCount(24);
        first[12].X.Should().BeApproximately(150, 0.01);
        first[14].X.Should().BeApproximately(150, 0.01);
        first[15].X.Should().BeApproximately(130, 0.01);
        Texts(renderer, 2)[0].X.Should().BeApproximately(20, 0.01);
        Enumerable.Range(1, 2).SelectMany(page => Texts(renderer, page)).Select(t => t.Text)
            .Should().Equal(Enumerable.Range(1, 30).Select(i => "Body" + i));
    }

    [Fact]
    public void MultipleShapesAndTheirContoursAreRetainedAcrossColumns()
    {
        var document = Create(2, 0);
        var firstShape = AddWrappingFrame(document.LastSection, 90, 20, 100, 20);
        AddWrappingFrame(document.LastSection, 155, 40, 15, 20);
        firstShape.WrapFormat.DistanceRight = Unit.FromPoint(2);
        AddBodyLines(document.LastSection, 27, false);
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.WrapContour = shape => ReferenceEquals(shape, firstShape)
            ? new[] { new XPoint(0, 0), new XPoint(0.75, 0), new XPoint(0.75, 1), new XPoint(0, 1) }
            : null;
        renderer.RenderDocument();
        var first = Texts(renderer, 1);
        first[12].X.Should().BeApproximately(167, 0.01);
        first[13].X.Should().BeApproximately(167, 0.01);
        first[14].X.Should().BeApproximately(170, 0.01);
        first[15].X.Should().BeApproximately(170, 0.01);
        first[16].X.Should().BeApproximately(130, 0.01);
        Texts(renderer, 2)[0].X.Should().BeApproximately(20, 0.01);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullyBlockedColumnUsesRetainedWrapBands(bool keepTogether)
    {
        var document = Create(2, 0);
        AddWrappingFrame(document.LastSection, 120, 20, 110, 30);
        for (var i = 1; i <= 12; i++) document.LastSection.AddParagraph("Body" + i);
        var next = document.LastSection.AddParagraph("After");
        next.Format.KeepTogether = keepTogether;
        next.AddLineBreak();
        next.AddText("Second");
        var renderer = Render(document);
        renderer.PageCount.Should().Be(1);
        var info = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!
            .OfType<ParagraphRenderInfo>().Single(i => ReferenceEquals(i.DocumentObject, next));
        info.LayoutInfo.ContentArea.X.Point.Should().BeApproximately(130, 0.01);
        // The fragment includes the leading 30pt gap in its flow rectangle;
        // its painted first line still starts below the blocked band.
        info.LayoutInfo.ContentArea.Y.Point.Should().BeApproximately(20, 0.01);
        info.LayoutInfo.ContentArea.Height.Point.Should().BeApproximately(50, 0.01);
        var unwrapped = Render(Create(2, 1));
        Texts(renderer, 1, "After").Single().Y.Should()
            .BeApproximately(Texts(unwrapped, 1)[0].Y + 30, 0.01);
    }

    [Fact]
    public void ExplicitPageAndSectionBreaksClearShapeWrap()
    {
        var document = Create(2, 0);
        AddWrappingFrame(document.LastSection, 0, 20, 240, 40);
        document.LastSection.AddParagraph("Before");
        document.LastSection.AddPageBreak();
        document.LastSection.AddParagraph("After");
        document.AddSection().AddParagraph("Next");
        var renderer = Render(document);
        renderer.PageCount.Should().Be(3);
        var after = renderer.DocumentRenderer.GetRenderInfoFromPage(2)!.OfType<ParagraphRenderInfo>().Single();
        var next = renderer.DocumentRenderer.GetRenderInfoFromPage(3)!.OfType<ParagraphRenderInfo>().Single();
        after.LayoutInfo.ContentArea.Y.Point.Should().BeApproximately(20, 0.01);
        next.LayoutInfo.ContentArea.Y.Point.Should().BeApproximately(20, 0.01);
    }

    [Fact]
    public void AdditionalTopBottomBandsPersistWithoutReinvokingTheCallback()
    {
        var document = Create(2, 0);
        var frame = AddWrappingFrame(document.LastSection, 0, 20, 240, 10);
        frame.WrapFormat.Style = WrapStyle.TopBottom;
        frame.WrapFormat.ExcludeFollowingText = true;
        AddBodyLines(document.LastSection, 22, true);
        var renderer = new PdfDocumentRenderer { Document = document };
        var calls = 0;
        renderer.DocumentRenderer.AdditionalTopBottomWrapBands = _ =>
        {
            calls++;
            return new[] { (40d, 10d) };
        };
        renderer.RenderDocument();
        renderer.PageCount.Should().Be(2);
        var first = Texts(renderer, 1);
        first.Should().HaveCount(20);
        first[10].X.Should().BeApproximately(130, 0.01);
        first[10].Y.Should().BeApproximately(first[0].Y, 0.01);
        first[13].Y.Should().BeApproximately(first[10].Y + 40, 0.01);
        var unwrapped = Render(Create(2, 1));
        Texts(renderer, 2)[0].Y.Should().BeApproximately(Texts(unwrapped, 1)[0].Y, 0.01);
        calls.Should().Be(1);
    }

    static TextFrame AddWrappingFrame(Section section, double left, double top, double width, double height)
    {
        var frame = section.AddTextFrame();
        frame.RelativeHorizontal = RelativeHorizontal.Page;
        frame.RelativeVertical = RelativeVertical.Page;
        frame.Left = Unit.FromPoint(left);
        frame.Top = Unit.FromPoint(top);
        frame.Width = Unit.FromPoint(width);
        frame.Height = Unit.FromPoint(height);
        frame.WrapFormat.Style = WrapStyle.BothSides;
        return frame;
    }

    static void AddBodyLines(Section section, int count, bool splitParagraph)
    {
        var paragraph = splitParagraph ? section.AddParagraph() : null;
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
