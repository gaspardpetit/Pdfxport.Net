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
public class UnequalColumnTests : IDisposable
{
    public UnequalColumnTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void AutomaticFlowUsesWidthsAndGapsInOrder(int geometry)
    {
        var document = Create(3, 38);
        var setup = document.LastSection.PageSetup;
        double[] widths = geometry == 1 ? new[] { 170.0 / 3, 170.0 / 3, 170.0 / 3 } : new[] { 40.0, 60.0, 70.0 };
        double[] gaps = geometry == 0 ? new[] { 15.0, 15.0 } : new[] { 10.0, 20.0 };
        setup.ColumnSpacing = Unit.FromPoint(15);
        if (geometry != 1) setup.ColumnWidthsPoints = widths;
        if (geometry != 0) setup.ColumnGapsPoints = gaps;
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        Enumerable.Range(1, 2).SelectMany(page => Texts(renderer, page)).Select(t => t.Text)
            .Should().Equal(Enumerable.Range(1, 38).Select(i => "Body" + i));
        var texts = Texts(renderer, 1);
        var infos = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!.OfType<ParagraphRenderInfo>().ToArray();
        var x = 20.0;
        for (var column = 0; column < 3; column++)
        {
            texts.Skip(column * 12).Take(12).Should().OnlyContain(t => Math.Abs(t.X - x) < 0.01);
            infos.Skip(column * 12).Take(12).Should().OnlyContain(i => Math.Abs(i.LayoutInfo.ContentArea.Width.Point - widths[column]) < 0.01);
            x += widths[column] + (column < 2 ? gaps[column] : 0);
        }
        Texts(renderer, 2)[0].X.Should().BeApproximately(20, 0.01);
    }

    [Fact]
    public void ParagraphContinuationUsesEachColumnsActualWidth()
    {
        var document = Create(2, 0);
        document.LastSection.PageSetup.ColumnWidthsPoints = new[] { 40.0, 140.0 };
        var p = document.LastSection.AddParagraph(string.Join(" ", Enumerable.Repeat("word", 100)));
        p.Format.WidowControl = false;
        var renderer = Render(document);
        var infos = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!.OfType<ParagraphRenderInfo>().ToArray();
        infos.Should().HaveCount(2);
        infos[0].LayoutInfo.ContentArea.Width.Point.Should().BeApproximately(40, 0.01);
        infos[1].LayoutInfo.ContentArea.Width.Point.Should().BeApproximately(140, 0.01);
        var texts = Texts(renderer, 1, "word");
        texts.Count(t => t.X >= 80).Should().BeGreaterThan(texts.Count(t => t.X < 80));
        string.Concat(Enumerable.Range(1, renderer.PageCount).SelectMany(page => AllTexts(renderer, page)).Select(t => t.Text))
            .Replace(" ", "").Should().Be(string.Concat(Enumerable.Repeat("word", 100)));
    }

    [Fact]
    public void ExplicitBreakAndMirroredMarginsUsePhysicalColumnGeometry()
    {
        var document = Create(2, 0);
        var section = document.LastSection;
        section.PageSetup.LeftMargin = Unit.FromPoint(30);
        section.PageSetup.RightMargin = Unit.FromPoint(10);
        section.PageSetup.MirrorMargins = true;
        section.PageSetup.ColumnWidthsPoints = new[] { 60.0, 120.0 };
        section.AddParagraph("Body1");
        section.AddColumnBreak();
        section.AddParagraph("Body2");
        section.AddColumnBreak();
        section.AddParagraph("Body3");
        section.AddColumnBreak();
        section.AddParagraph("Body4");
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        Texts(renderer, 1).Select(t => t.X).Should().Equal(30, 110);
        Texts(renderer, 2).Select(t => t.X).Should().Equal(10, 90);
    }

    [Fact]
    public void PartialSectionSetupInheritsGeometryButExplicitCountStartsANewDefinition()
    {
        var document = Create(2, 0);
        var setup = document.LastSection.PageSetup;
        setup.ColumnWidthsPoints = new[] { 60.0, 120.0 };
        setup.ColumnGapsPoints = new[] { 20.0 };
        document.LastSection.AddParagraph("First");
        var inherited = document.AddSection();
        inherited.PageSetup.TopMargin = Unit.FromPoint(20);
        inherited.AddColumnBreak();
        inherited.AddParagraph("Inherited");
        var reset = document.AddSection();
        reset.PageSetup.ColumnCount = 2;
        reset.AddColumnBreak();
        reset.AddParagraph("Reset");
        var renderer = Render(document);
        renderer.PageCount.Should().Be(3);
        Texts(renderer, 2, "Inherited").Single().X.Should().BeApproximately(100, 0.01);
        Texts(renderer, 3, "Reset").Single().X.Should().BeApproximately(130, 0.01);
        inherited.PageSetup.Values.ColumnWidthsPoints![0] = 30;
        setup.ColumnWidthsPoints.Should().Equal(60, 120);
    }

    [Fact]
    public void DdlAndDocumentClonePreserveGeometryWithInvariantDecimals()
    {
        var document = Create(2, 0);
        var setup = document.LastSection.PageSetup;
        setup.ColumnWidthsPoints = new[] { 60.25, 119.5 };
        setup.ColumnGapsPoints = new[] { 20.25 };
        document.LastSection.AddColumnBreak();
        document.LastSection.AddParagraph("After");
        var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("fr-CA");
            var ddl = DdlWriter.WriteToString(document);
            ddl.Should().Contain("60.25,119.5");
            var restored = DdlReader.DocumentFromString(ddl);
            foreach (var copy in new[] { restored, document.Clone() })
            {
                copy.LastSection.PageSetup.ColumnWidthsPoints.Should().Equal(60.25, 119.5);
                copy.LastSection.PageSetup.ColumnGapsPoints.Should().Equal(20.25);
                Texts(Render(copy), 1, "After").Single().X.Should().BeApproximately(100.5, 0.01);
            }
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previousCulture; }
    }

    [Fact]
    public void ArraysAreDefensivelyCopiedAndDeepCloned()
    {
        var setup = new PageSetup { ColumnCount = 2 };
        var widths = new[] { 60.0, 120.0 };
        var gaps = new[] { 20.0 };
        setup.ColumnWidthsPoints = widths;
        setup.ColumnGapsPoints = gaps;
        widths[0] = gaps[0] = 1;
        setup.ColumnWidthsPoints![0] = setup.ColumnGapsPoints![0] = 2;
        setup.ColumnWidthsPoints.Should().Equal(60, 120);
        setup.ColumnGapsPoints.Should().Equal(20);
        var copy = setup.Clone();
        copy.Values.ColumnWidthsPoints![0] = copy.Values.ColumnGapsPoints![0] = 3;
        setup.ColumnWidthsPoints.Should().Equal(60, 120);
        setup.ColumnGapsPoints.Should().Equal(20);
        setup.ColumnWidthsPoints = null;
        setup.ColumnGapsPoints = null;
        setup.ColumnWidthsPoints.Should().BeNull();
        setup.ColumnGapsPoints.Should().BeNull();
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, -1)]
    [InlineData(false, double.NaN)]
    [InlineData(false, double.PositiveInfinity)]
    [InlineData(true, -1)]
    [InlineData(true, double.NaN)]
    [InlineData(true, double.PositiveInfinity)]
    public void InvalidEntriesAreRejectedByApiAndRenderer(bool gap, double value)
    {
        var document = Create(2, 1);
        var setup = document.LastSection.PageSetup;
        Action assign = () => { if (gap) setup.ColumnGapsPoints = new[] { value }; else setup.ColumnWidthsPoints = new[] { value, 60.0 }; };
        assign.Should().Throw<ArgumentOutOfRangeException>();
        if (gap) setup.Values.ColumnGapsPoints = new[] { value };
        else setup.Values.ColumnWidthsPoints = new[] { value, 60.0 };
        Action render = () => Render(document);
        render.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidLengthsAreRejectedEvenAfterCountChanges(bool gap)
    {
        var document = Create(2, 1);
        var setup = document.LastSection.PageSetup;
        Action assign = () => { if (gap) setup.ColumnGapsPoints = Array.Empty<double>(); else setup.ColumnWidthsPoints = new[] { 60.0 }; };
        assign();
        Action invalidLength = () => Render(document.Clone());
        invalidLength.Should().Throw<ArgumentOutOfRangeException>();
        setup.ColumnWidthsPoints = new[] { 60.0, 120.0 };
        setup.ColumnGapsPoints = new[] { 20.0 };
        setup.ColumnCount = gap ? 1 : 3;
        if (gap) setup.ColumnWidthsPoints = null;
        Action render = () => Render(document);
        render.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeometryThatOverflowsBodyIsRejected(bool gapOnly)
    {
        var document = Create(2, 1);
        if (gapOnly) document.LastSection.PageSetup.ColumnGapsPoints = new[] { 200.0 };
        else document.LastSection.PageSetup.ColumnWidthsPoints = new[] { 100.0, 100.0 };
        Action render = () => Render(document);
        render.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void OneColumnAcceptsAnEmptyGapArrayAndUnusedBodyWidth()
    {
        var document = Create(1, 1);
        document.LastSection.PageSetup.ColumnWidthsPoints = new[] { 180.0 };
        document.LastSection.PageSetup.ColumnGapsPoints = Array.Empty<double>();
        var restored = DdlReader.DocumentFromString(DdlWriter.WriteToString(document));
        var renderer = Render(restored);
        renderer.DocumentRenderer.GetRenderInfoFromPage(1)!.OfType<ParagraphRenderInfo>().Single()
            .LayoutInfo.ContentArea.Width.Point.Should().BeApproximately(180, 0.01);
    }

    [Fact]
    public void TableAndHeaderKeepTheirRespectiveColumnAndMarginWidths()
    {
        var document = Create(2, 0);
        var section = document.LastSection;
        section.PageSetup.ColumnWidthsPoints = new[] { 60.0, 120.0 };
        section.Headers.Primary.AddParagraph("Header").Format.Alignment = ParagraphAlignment.Right;
        section.AddColumnBreak();
        var table = section.AddTable();
        table.LeftPadding = Unit.Zero;
        table.AddColumn(Unit.FromPoint(120));
        table.AddRow().Cells[0].AddParagraph("Cell");
        var renderer = Render(document);
        renderer.PageCount.Should().Be(1);
        var info = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!.Single(i => ReferenceEquals(i.DocumentObject, table));
        info.LayoutInfo.ContentArea.X.Point.Should().BeApproximately(100, 0.01);
        AllTexts(renderer, 1).Single(t => t.Text == "Header").X.Should().BeGreaterThan(180);
    }

    [Fact]
    public void FrozenDefaultSetupRejectsArrayAssignments()
    {
        Action widths = () => PageSetup.DefaultPageSetup.ColumnWidthsPoints = new[] { 100.0 };
        Action gaps = () => PageSetup.DefaultPageSetup.ColumnGapsPoints = Array.Empty<double>();
        widths.Should().Throw<InvalidOperationException>();
        gaps.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void GeometryCanBeConfiguredBeforeCountAndCanOverrideInheritedGaps()
    {
        var document = Create(2, 0);
        document.LastSection.PageSetup.ColumnWidthsPoints = new[] { 60.0, 120.0 };
        var section = document.AddSection();
        section.PageSetup.ColumnGapsPoints = new[] { 10.0 };
        section.AddColumnBreak();
        section.AddParagraph("After");
        var renderer = Render(document);
        Texts(renderer, 2, "After").Single().X.Should().BeApproximately(90, 0.01);
        var setup = new PageSetup { ColumnWidthsPoints = new[] { 60.0, 120.0 }, ColumnGapsPoints = new[] { 20.0 }, ColumnCount = 2 };
        setup.ColumnWidthsPoints.Should().Equal(60, 120);
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
