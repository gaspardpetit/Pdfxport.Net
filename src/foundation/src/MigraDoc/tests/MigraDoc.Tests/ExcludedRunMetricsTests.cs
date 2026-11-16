// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.IO;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class ExcludedRunMetricsTests : IDisposable
{
    public ExcludedRunMetricsTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExcludedRunKeepsItsWidthWithoutEnlargingLine(bool first)
    {
        var normal = Render(12, null, first);
        var large = Render(36, null, first);
        var excluded = Render(36, true, first);
        excluded.Heights.Should().Equal(normal.Heights);
        excluded.Runs.Select(r => r.Y).Should().Equal(normal.Runs.Select(r => r.Y));
        excluded.Runs.Select(r => r.X).Should().Equal(large.Runs.Select(r => r.X));
        large.Heights.Sum().Should().BeGreaterThan(excluded.Heights.Sum());
        excluded.Runs.Select(r => r.Text).Should().Equal(large.Runs.Select(r => r.Text));
        Render(36, false, first).Runs.Should().Equal(large.Runs);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LeaderTabKeepsItsFontWithoutEnlargingLine(bool first)
    {
        var normal = Render(12, null, first, tab: true);
        var large = Render(36, null, first, tab: true);
        var excluded = Render(36, true, first, tab: true);
        excluded.Heights.Should().Equal(normal.Heights);
        large.Heights.Sum().Should().BeGreaterThan(excluded.Heights.Sum());
        excluded.Runs.Where(r => r.Text == "after").Should().Equal(
            normal.Runs.Where(r => r.Text == "after"));
        var dots = string.Concat(excluded.Runs.Select(r => r.Text).Where(t => t.All(c => c == '.')));
        dots.Length.Should().BeGreaterThan(0);
        dots.Should().Be(string.Concat(large.Runs.Select(r => r.Text).Where(t => t.All(c => c == '.'))));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void NestedRunsInheritAndCanExplicitlyOverride(bool? child, bool excluded)
    {
        var actual = Render(36, true, true, nested: true, child: child);
        var reference = Render(excluded ? 12 : 36, null, true);
        actual.Heights.Should().Equal(reference.Heights);
    }

    [Fact]
    public void ExclusionDoesNotRemoveInlineImageMetrics()
    {
        var normal = Render(36, null, true, image: true);
        var excluded = Render(36, true, true, image: true);
        excluded.Heights.Should().Equal(normal.Heights);
        excluded.Runs.Should().Equal(normal.Runs);
    }

    [Fact]
    public void ExclusionControlsPaginationWithoutLosingText()
    {
        var normal = Render(12, null, true, lines: 8, smallPage: true);
        var large = Render(36, null, true, lines: 8, smallPage: true);
        var excluded = Render(36, true, true, lines: 8, smallPage: true);
        excluded.Pages.Should().Be(normal.Pages);
        excluded.Pages.Should().BeLessThan(large.Pages);
        excluded.Runs.Select(r => r.Text).Should().Equal(normal.Runs.Select(r => r.Text));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    public void CloneAndDdlPreserveLocalOverride(bool? excluded, int fontKind)
    {
        var doc = Create();
        var span = doc.Sections[0].AddParagraph().AddFormattedText("marker");
        span.Values.ExcludeFromLineMetrics = excluded;
        if (fontKind == 1) span.Bold = true;
        if (fontKind == 2) span.Size = Unit.FromPoint(24);
        if (fontKind == 3) span.Style = StyleNames.Hyperlink;
        span.Clone().Values.ExcludeFromLineMetrics.Should().Be(excluded);
        var clone = DdlReader.DocumentFromString(DdlWriter.WriteToString(doc))!;
        if (excluded is not null || fontKind != 0)
        {
            var paragraph = clone.Sections[0].Elements[0].Should().BeOfType<Paragraph>().Subject;
            var restored = paragraph.Elements[0].Should().BeOfType<FormattedText>().Subject;
            restored.Values.ExcludeFromLineMetrics.Should().Be(excluded);
            if (fontKind == 1) restored.Bold.Should().BeTrue();
            if (fontKind == 2) restored.Size.Point.Should().Be(24);
            if (fontKind == 3) restored.Style.Should().Be(StyleNames.Hyperlink);
        }
    }

    record Result(int Pages, List<double> Heights, List<(string Text, double X, double Y)> Runs);
    static Result Render(double size, bool? excluded, bool first, bool nested = false,
        bool? child = null, int lines = 2, bool smallPage = false, bool tab = false, bool image = false)
    {
        var document = Create();
        var section = document.Sections[0];
        if (smallPage)
        {
            section.PageSetup.PageHeight = Unit.FromPoint(120);
            section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromPoint(25);
        }
        var p = section.AddParagraph();
        p.Format.WidowControl = false;
        if (tab) p.Format.TabStops.AddTabStop(Unit.FromPoint(100), TabAlignment.Left, TabLeader.Dots);
        for (int i = 0; i < lines; i++)
        {
            if (i > 0) p.AddLineBreak();
            if (!first) p.AddText("before");
            var span = p.AddFormattedText();
            span.Values.ExcludeFromLineMetrics = excluded;
            if (nested)
            {
                span = span.AddFormattedText();
                span.Values.ExcludeFromLineMetrics = child;
            }
            span.Size = Unit.FromPoint(size);
            if (image)
            {
                var picture = span.AddImage("base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==");
                picture.Width = Unit.FromPoint(10);
                picture.Height = Unit.FromPoint(40);
            }
            else if (tab) span.AddTab();
            else span.AddText("X");
            p.AddText("after");
        }
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var heights = new List<double>();
        var runs = new List<(string, double, double)>();
        for (int page = 0; page < pdf.PageCount; page++)
        {
            heights.AddRange(renderer.DocumentRenderer.GetRenderInfoFromPage(page + 1)!
                .Select(info => info.LayoutInfo.ContentArea.Height.Point));
            var reader = PdfFileHelper.GetPageContentStreamEnumerator(pdf, page).Text;
            while (reader.MoveAndGetNext(true, out var item))
                runs.Add((item!.Text, item.X, item.Y));
        }
        return new Result(pdf.PageCount, heights, runs);
    }
    static Document Create()
    {
        var doc = new Document();
        doc.Styles.Normal.Font.Name = "Arial";
        doc.Styles.Normal.Font.Size = 12;
        doc.AddSection();
        return doc;
    }
}
