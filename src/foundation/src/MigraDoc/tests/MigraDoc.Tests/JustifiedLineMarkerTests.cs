// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.
using System.Globalization;
using System.Text.RegularExpressions;
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
public class JustifiedLineMarkerTests : IDisposable
{
    public JustifiedLineMarkerTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();
    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    public void MarkersLeaveDrawingUnchangedAcrossPages(string culture)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var original = Render(false, manyLines: true);
            var marked = Render(true, manyLines: true);
            marked.Count.Should().Be(original.Count).And.BeGreaterThan(1);
            for (var page = 0; page < marked.Count; page++) Strip(marked[page]).Should().Be(original[page]);
            var all = string.Concat(marked);
            Regex.Matches(all, @"% MD-JustifiedLine last=1").Count.Should().Be(1);
            Regex.Matches(all, @"% MD-JustifiedLine last=0").Count.Should().BeGreaterThan(1);
            all.Should().Contain("% MD-JustifiedGap");
            foreach (var page in marked.Take(marked.Count - 1)) page.Should().NotContain("last=1");
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }
    [Theory]
    [InlineData(ParagraphAlignment.Left)]
    [InlineData(ParagraphAlignment.Center)]
    [InlineData(ParagraphAlignment.Right)]
    public void NonjustifiedParagraphsEmitNoMarkers(ParagraphAlignment alignment)
        => Render(true, alignment: alignment).Should().Equal(Render(false, alignment: alignment));
    [Fact]
    public void DefaultIsDisabled() => Render(null).Should().Equal(Render(false));
    [Fact]
    public void FinalLineHasNoGapMarkers()
    {
        var contents = Render(true, singleLine: true);
        contents.Should().ContainSingle();
        contents[0].Should().Contain("% MD-JustifiedLine last=1").And.NotContain("% MD-JustifiedGap");
        Strip(contents[0]).Should().Be(Render(false, singleLine: true)[0]);
    }
    [Fact]
    public void ExplicitBreakKeepsFollowingLineFinal()
    {
        var lines = string.Concat(Render(true)).Split("% MD-JustifiedLine", StringSplitOptions.None);
        lines.Should().HaveCount(3);
        lines[1].Should().StartWith(" last=0");
        lines[2].Should().StartWith(" last=1");
        lines[0].Should().Contain("% MD-JustifiedGap");
        lines[1].Should().NotContain("% MD-JustifiedGap");
    }
    static string Strip(string value) => Regex.Replace(value, @"(?m)^% MD-Justified(?:Gap|Line last=[01])\r?\n", "");
    static List<string> Render(bool? enabled, bool manyLines = false, bool singleLine = false,
        ParagraphAlignment alignment = ParagraphAlignment.Justify)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        section.PageSetup.PageWidth = Unit.FromPoint(200);
        section.PageSetup.PageHeight = Unit.FromPoint(manyLines ? 120 : 500);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(20);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromPoint(20);
        var paragraph = section.AddParagraph();
        paragraph.Format.Alignment = alignment;
        if (manyLines) paragraph.AddText(string.Join(" ", Enumerable.Repeat("Alpha Bravo Charlie", 20)));
        else
        {
            paragraph.AddText("Alpha Bravo");
            if (!singleLine) { paragraph.AddLineBreak(); paragraph.AddText("Charlie Delta"); }
        }
        var renderer = new PdfDocumentRenderer { Document = document };
        if (enabled.HasValue) renderer.DocumentRenderer.EmitJustifiedLineGeometry = enabled.Value;
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        return Enumerable.Range(0, pdf.PageCount).Select(page => PdfFileHelper.GetPageContentStream(pdf, page)).ToList();
    }
}
