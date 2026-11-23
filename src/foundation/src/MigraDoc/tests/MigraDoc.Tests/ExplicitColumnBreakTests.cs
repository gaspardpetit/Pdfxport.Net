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
public class ExplicitColumnBreakTests : IDisposable
{
    public ExplicitColumnBreakTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(1, 0, 2, 20)]
    [InlineData(2, 0, 1, 130)]
    [InlineData(3, 0, 1, 20 + 220.0 / 3)]
    [InlineData(2, 1, 1, 130)]
    [InlineData(2, 12, 1, 130)]
    [InlineData(2, 13, 2, 20)]
    [InlineData(2, 24, 2, 20)]
    public void BreakAdvancesExactlyOneColumn(int columns, int before, int page, double x)
    {
        var document = Create(columns, before);
        document.LastSection.AddColumnBreak();
        document.LastSection.AddParagraph("After");
        var renderer = Render(document);
        renderer.PageCount.Should().Be(page);
        var all = Enumerable.Range(1, page).SelectMany(i => AllTexts(renderer, i)).ToArray();
        all.Select(t => t.Text).Should().Equal(Enumerable.Range(1, before).Select(i => "Body" + i).Append("After"));
        var after = Texts(renderer, page, "After").Single();
        after.X.Should().BeApproximately(x, 0.01);
        var reference = Render(Create(columns, 1));
        after.Y.Should().BeApproximately(Texts(reference, 1).Single().Y, 0.01);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void ConsecutiveLeadingBreaksConsumeEmptyColumns(int breaks)
    {
        var document = Create(2, 0);
        for (var i = 0; i < breaks; i++) document.LastSection.AddColumnBreak();
        document.LastSection.AddParagraph("After");
        var renderer = Render(document);
        var page = breaks / 2 + 1;
        renderer.PageCount.Should().Be(page);
        var after = Texts(renderer, page, "After").Single();
        after.X.Should().BeApproximately(breaks % 2 == 0 ? 20 : 130, 0.01);
        Enumerable.Range(1, page - 1).SelectMany(i => AllTexts(renderer, i)).Should().BeEmpty();
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    public void TrailingBreakTerminatesWithoutRepeating(int columns, int pages)
    {
        var document = Create(columns, 1);
        document.LastSection.AddColumnBreak();
        var renderer = Render(document);
        renderer.PageCount.Should().Be(pages);
        Enumerable.Range(1, pages).SelectMany(i => AllTexts(renderer, i)).Select(t => t.Text).Should().Equal("Body1");
    }

    [Fact]
    public void ColumnBreakThenPageBreakStillEndsThePhysicalPage()
    {
        var document = Create(3, 1);
        document.LastSection.AddColumnBreak();
        document.LastSection.AddPageBreak();
        document.LastSection.AddParagraph("After");
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        Texts(renderer, 2, "After").Single().X.Should().BeApproximately(20, 0.01);
    }

    [Fact]
    public void FollowingParagraphCanContinueThroughAutomaticColumns()
    {
        var document = Create(2, 1);
        document.LastSection.AddColumnBreak();
        var paragraph = document.LastSection.AddParagraph();
        paragraph.Format.WidowControl = false;
        for (var i = 1; i <= 14; i++)
        {
            if (i > 1) paragraph.AddLineBreak();
            paragraph.AddText("After" + i);
        }
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        Texts(renderer, 1, "After").Should().HaveCount(12);
        Texts(renderer, 2, "After").Should().HaveCount(2);
        Texts(renderer, 1, "After").Should().OnlyContain(t => Math.Abs(t.X - 130) < 0.01);
        Texts(renderer, 2, "After").Should().OnlyContain(t => Math.Abs(t.X - 20) < 0.01);
    }

    [Fact]
    public void DdlAndClonePreserveBothBreakKindsAndTheirLayout()
    {
        var document = Create(2, 1);
        document.LastSection.Elements.AddColumnBreak();
        document.LastSection.AddParagraph("Middle");
        document.LastSection.AddPageBreak();
        document.LastSection.AddParagraph("After");
        var breaks = document.LastSection.Elements.OfType<PageBreak>().ToArray();
        breaks.Select(b => b.IsColumnBreak).Should().Equal(true, false);
        breaks[0].Clone().IsColumnBreak.Should().BeTrue();
        new PageBreak().IsColumnBreak.Should().BeFalse();
        var ddl = DdlWriter.WriteToString(document);
        ddl.Should().Contain("\\columnbreak").And.Contain("\\pagebreak");
        var restored = DdlReader.DocumentFromString(ddl);
        restored.LastSection.Elements.OfType<PageBreak>().Select(b => b.IsColumnBreak).Should().Equal(true, false);
        foreach (var candidate in new[] { document, restored, document.Clone() })
        {
            var renderer = Render(candidate);
            renderer.PageCount.Should().Be(2);
            Texts(renderer, 1, "Middle").Single().X.Should().BeApproximately(130, 0.01);
            Texts(renderer, 2, "After").Single().X.Should().BeApproximately(20, 0.01);
        }
    }

    [Fact]
    public void LeadingBreakInDdlIsRecognizedAsASectionElement()
    {
        var document = Create(2, 0);
        document.LastSection.AddColumnBreak();
        document.LastSection.AddParagraph("After");
        var restored = DdlReader.DocumentFromString(DdlWriter.WriteToString(document));
        restored.LastSection.Elements[0].Should().BeOfType<PageBreak>().Which.IsColumnBreak.Should().BeTrue();
        Texts(Render(restored), 1, "After").Single().X.Should().BeApproximately(130, 0.01);
    }

    [Theory]
    [InlineData(false, "page")]
    [InlineData(true, "column")]
    public void RtfPreservesTheBreakKind(bool column, string control)
    {
        var document = Create(1, 1);
        document.LastSection.Elements.Add(new PageBreak { IsColumnBreak = column });
        document.LastSection.AddParagraph("After");
        var rtf = new MigraDoc.RtfRendering.RtfDocumentRenderer().RenderToString(document, ".");
        System.Text.RegularExpressions.Regex.IsMatch(rtf, @"\\" + control + @"(?![a-zA-Z])").Should().BeTrue();
    }

    [Fact]
    public void PhysicalPageFieldsIncludeEmptyPagesSkippedByColumnBreaks()
    {
        var document = Create(2, 0);
        var section = document.LastSection;
        section.Headers.Primary.AddParagraph("Header");
        var footer = section.Footers.Primary.AddParagraph("Footer");
        footer.AddPageField();
        footer.AddText("of");
        footer.AddNumPagesField();
        footer.AddText("section");
        footer.AddSectionPagesField();
        section.AddColumnBreak();
        section.AddColumnBreak();
        section.AddParagraph("After");
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        for (var page = 1; page <= 2; page++)
        {
            var texts = AllTexts(renderer, page);
            texts.Count(t => t.Text == "Header").Should().Be(1);
            string.Concat(texts.Where(t => t.Text.StartsWith("Footer") || t.Y > 130).Select(t => t.Text))
                .Should().Contain("Footer" + page + "of2section2");
        }
    }

    [Fact]
    public void ColumnBreakRetainsPageWrapButPhysicalPageAdvanceClearsIt()
    {
        var document = Create(2, 0);
        var section = document.LastSection;
        var frame = section.AddTextFrame();
        frame.RelativeHorizontal = RelativeHorizontal.Page;
        frame.RelativeVertical = RelativeVertical.Page;
        frame.Left = Unit.FromPoint(120);
        frame.Top = Unit.FromPoint(20);
        frame.Width = Unit.FromPoint(110);
        frame.Height = Unit.FromPoint(30);
        frame.WrapFormat.Style = WrapStyle.BothSides;
        section.AddParagraph("Body1");
        section.AddColumnBreak();
        section.AddParagraph("Middle");
        section.AddColumnBreak();
        section.AddParagraph("After");
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        var reference = Render(Create(2, 1));
        var baseline = Texts(reference, 1).Single().Y;
        Texts(renderer, 1, "Middle").Single().Y.Should().BeApproximately(baseline + 30, 0.01);
        Texts(renderer, 2, "After").Single().Y.Should().BeApproximately(baseline, 0.01);
    }

    [Theory]
    [InlineData(1, false, 3, 20)]
    [InlineData(1, true, 2, 20)]
    [InlineData(2, false, 2, 20)]
    [InlineData(2, true, 1, 130)]
    [InlineData(3, true, 1, 20 + 220.0 / 3)]
    public void OptInReusesWholeParagraphOverflow(int columns, bool coalesce, int pages, double x)
    {
        var document = Create(columns, 12);
        document.LastSection.AddParagraph("Overflow");
        document.LastSection.Elements.Add(new PageBreak
        {
            IsColumnBreak = true,
            CoalesceWithPrecedingParagraphOverflow = coalesce
        });
        document.LastSection.AddParagraph("After");
        var renderer = Render(document);
        renderer.PageCount.Should().Be(pages);
        Texts(renderer, pages, "After").Single().X.Should().BeApproximately(x, 0.01);
        Enumerable.Range(1, pages).SelectMany(i => AllTexts(renderer, i))
            .Count(t => t.Text == "Overflow").Should().Be(1);
        if (coalesce)
            Texts(renderer, pages, "After").Single().Y.Should().BeApproximately(
                Texts(renderer, pages, "Overflow").Single().Y + 10, 0.01);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyWholeParagraphOverflowCoalesces(bool keepTogether)
    {
        var document = Create(2, 11);
        var paragraph = document.LastSection.AddParagraph("Start");
        paragraph.Format.WidowControl = false;
        paragraph.Format.KeepTogether = keepTogether;
        paragraph.AddLineBreak();
        paragraph.AddText("End");
        document.LastSection.Elements.Add(new PageBreak
        {
            IsColumnBreak = true,
            CoalesceWithPrecedingParagraphOverflow = true
        });
        document.LastSection.AddParagraph("After");
        var renderer = Render(document);
        // KeepTogether moves the whole two-line paragraph and permits coalescing;
        // otherwise the paragraph spans columns and the explicit break still advances.
        renderer.PageCount.Should().Be(keepTogether ? 1 : 2);
        Texts(renderer, keepTogether ? 1 : 2, "After").Single().X
            .Should().BeApproximately(keepTogether ? 130 : 20, 0.01);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(12, true)]
    public void BreakWithoutAutomaticWholeParagraphOverflowStillAdvances(int before, bool explicitPage)
    {
        var document = Create(2, before);
        var paragraph = document.LastSection.AddParagraph("Before");
        paragraph.Format.PageBreakBefore = explicitPage;
        document.LastSection.Elements.Add(new PageBreak
        {
            IsColumnBreak = true,
            CoalesceWithPrecedingParagraphOverflow = true
        });
        document.LastSection.AddParagraph("After");
        var renderer = Render(document);
        renderer.PageCount.Should().Be(explicitPage ? 2 : 1);
        Texts(renderer, explicitPage ? 2 : 1, "After").Single().X.Should().BeApproximately(130, 0.01);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReusedTransitionIsImmediateAndConsumedOnce(bool interveningParagraph)
    {
        var document = Create(2, 12);
        document.LastSection.AddParagraph("Overflow");
        if (interveningParagraph) document.LastSection.AddParagraph("Intervening");
        for (var i = 0; i < (interveningParagraph ? 1 : 2); i++)
            document.LastSection.Elements.Add(new PageBreak
            {
                IsColumnBreak = true,
                CoalesceWithPrecedingParagraphOverflow = true
            });
        document.LastSection.AddParagraph("After");
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        Texts(renderer, 2, "After").Single().X.Should().BeApproximately(20, 0.01);
    }

    [Fact]
    public void CoalescedTerminalBreakDoesNotCreateAnEmptyPage()
    {
        var document = Create(2, 12);
        document.LastSection.AddParagraph("Overflow");
        document.LastSection.Elements.Add(new PageBreak
        {
            IsColumnBreak = true,
            CoalesceWithPrecedingParagraphOverflow = true
        });
        var renderer = Render(document);
        renderer.PageCount.Should().Be(1);
        Texts(renderer, 1, "Overflow").Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OverflowOptionSurvivesDdlAndCloning(bool enabled)
    {
        var document = Create(2, 12);
        document.LastSection.AddParagraph("Overflow");
        document.LastSection.Elements.Add(new PageBreak
        {
            IsColumnBreak = true,
            CoalesceWithPrecedingParagraphOverflow = enabled
        });
        document.LastSection.AddParagraph("After");
        var ddl = DdlWriter.WriteToString(document);
        ddl.Should().Contain("CoalesceWithPrecedingParagraphOverflow");
        foreach (var candidate in new[] { document.Clone(), DdlReader.DocumentFromString(ddl) })
        {
            candidate.LastSection.Elements.OfType<PageBreak>().Single()
                .CoalesceWithPrecedingParagraphOverflow.Should().Be(enabled);
            Render(candidate).PageCount.Should().Be(enabled ? 1 : 2);
        }
        new PageBreak().CoalesceWithPrecedingParagraphOverflow.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CoalescingPreservesFollowingSpacingAndEmptyParagraph(bool empty)
    {
        var document = Create(2, 12);
        document.LastSection.AddParagraph(empty ? "" : "Overflow");
        document.LastSection.Elements.Add(new PageBreak
        {
            IsColumnBreak = true,
            CoalesceWithPrecedingParagraphOverflow = true
        });
        document.LastSection.AddParagraph("After").Format.SpaceBefore = Unit.FromPoint(17);
        var renderer = Render(document);
        renderer.PageCount.Should().Be(1);
        var baseline = Texts(Render(Create(2, 1)), 1).Single().Y;
        Texts(renderer, 1, "After").Single().Y.Should().BeApproximately(baseline + 27, 0.01);
    }

    [Fact]
    public void ParagraphThatSplitsAfterInitialOverflowDoesNotCoalesce()
    {
        var document = Create(2, 12);
        var paragraph = document.LastSection.AddParagraph("Start");
        paragraph.Format.WidowControl = false;
        for (var i = 1; i < 14; i++)
        {
            paragraph.AddLineBreak();
            paragraph.AddText("Part" + i);
        }
        document.LastSection.Elements.Add(new PageBreak
        {
            IsColumnBreak = true,
            CoalesceWithPrecedingParagraphOverflow = true
        });
        document.LastSection.AddParagraph("After");
        var renderer = Render(document);
        renderer.PageCount.Should().Be(2);
        Texts(renderer, 2, "After").Single().X.Should().BeApproximately(130, 0.01);
    }

    [Fact]
    public void OptionDoesNotSuppressPhysicalPageBreak()
    {
        var document = Create(2, 12);
        document.LastSection.AddParagraph("Overflow");
        document.LastSection.Elements.Add(new PageBreak
        {
            CoalesceWithPrecedingParagraphOverflow = true
        });
        document.LastSection.AddParagraph("After");
        var restored = DdlReader.DocumentFromString(DdlWriter.WriteToString(document));
        foreach (var candidate in new[] { document, restored })
        {
            candidate.LastSection.Elements.OfType<PageBreak>().Single()
                .CoalesceWithPrecedingParagraphOverflow.Should().BeTrue();
            var renderer = Render(candidate);
            renderer.PageCount.Should().Be(2);
            Texts(renderer, 2, "After").Single().X.Should().BeApproximately(20, 0.01);
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
