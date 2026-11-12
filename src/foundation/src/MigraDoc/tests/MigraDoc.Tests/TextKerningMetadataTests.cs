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
public class TextKerningMetadataTests : IDisposable
{
    public TextKerningMetadataTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData("en-US", 0.0, "0")]
    [InlineData("fr-FR", 10.12567, "10.12567")]
    [InlineData("en-US", double.MaxValue, "1.7976931348623157E+308")]
    [InlineData("fr-FR", -1.0, "None")]
    [InlineData("en-US", double.NaN, "None")]
    [InlineData("en-US", double.PositiveInfinity, "None")]
    [InlineData("en-US", double.NegativeInfinity, "None")]
    [InlineData("en-US", null, "None")]
    public void MetadataIsBalancedAndLeavesDrawingUnchanged(string culture, double? threshold, string expected)
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            var plain = Render(null);
            var marked = Render(_ => threshold);
            Strip(marked).Should().Be(plain);
            var starts = Regex.Matches(marked, @"(?m)^% MD-Kern (?!End)([^\r\n]+)");
            starts.Should().HaveCount(2);
            foreach (Match match in starts) match.Groups[1].Value.Should().Be(expected);
            Regex.Matches(marked, @"(?m)^% MD-Kern End").Count.Should().Be(2);
            // Each scope must enclose a text paint, without overlapping another scope.
            foreach (var scope in marked.Split("% MD-Kern " + expected).Skip(1))
            {
                var end = scope.IndexOf("% MD-Kern End", StringComparison.Ordinal);
                end.Should().BeGreaterThan(0);
                scope[..end].Should().MatchRegex(@"\bT[Jj]\b");
                scope[..end].Should().NotContain("% MD-Kern");
            }
        }
        finally { CultureInfo.CurrentCulture = saved; }
    }

    [Fact]
    public void DefaultEmitsNoMetadata() => Render(null).Should().NotContain("MD-Kern");

    [Fact]
    public void SelectorReceivesTextTagsOnlyDuringRendering()
    {
        var seen = new List<object?>();
        var output = Render(tag => { seen.Add(tag); return Equals(tag, "first") ? 0 : 12; });
        seen.Should().Equal("first", "second");
        output.Should().Contain("% MD-Kern 0").And.Contain("% MD-Kern 12");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MetadataComposesWithSplitGlyphDrawing(bool pairAdjustments)
    {
        var marked = Render(_ => 8, pairAdjustments, !pairAdjustments);
        Strip(marked).Should().Be(Render(null, pairAdjustments, !pairAdjustments));
        Regex.Matches(marked, @"(?m)^% MD-Kern End").Count.Should().Be(2);
    }

    static string Strip(string value) => Regex.Replace(value, @"(?m)^% MD-Kern [^\r\n]+\r?\n", "");
    static string Render(Func<object?, double?>? selector, bool pairs = false, bool scale = false)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var paragraph = document.AddSection().AddParagraph();
        paragraph.AddText("Alpha").Tag = "first";
        paragraph.AddText(" ");
        paragraph.AddPageField();
        paragraph.AddTab();
        paragraph.AddText("Bravo").Tag = "second";
        var renderer = new PdfDocumentRenderer { Document = document };
        var dr = renderer.DocumentRenderer;
        dr.PrepareDocument(); // The metadata selector must never be needed for formatting.
        var calls = 0;
        dr.TextKerningMinimumSizePoints = selector is null ? null : tag => { calls++; return selector(tag); };
        dr.PrepareDocument();
        calls.Should().Be(0);
        if (pairs) dr.TextPairAdvances = (_, text, _) => Enumerable.Repeat(0.25, text.Length - 1).ToArray();
        if (scale) dr.TextPaintHorizontalScale = (_, _) => 0.8;
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        return string.Concat(Enumerable.Range(0, pdf.PageCount).Select(page => PdfFileHelper.GetPageContentStream(pdf, page)));
    }
}
