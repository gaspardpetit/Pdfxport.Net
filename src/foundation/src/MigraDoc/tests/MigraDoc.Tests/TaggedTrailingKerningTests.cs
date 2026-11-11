// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class TaggedTrailingKerningTests : IDisposable
{
    const string ClosingWord = "authors.\u2019";

    public TaggedTrailingKerningTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }

    public void Dispose() => PdfSharpCore.ResetAll();

    [Fact]
    public void TagDistinguishesIdenticalWordsDuringLineFitting()
    {
        var baseline = Render(null, install: false, narrow: true);
        var selected = Render(-1.4, narrow: true);
        baseline.FirstSameLine.Should().BeFalse();
        selected.FirstSameLine.Should().BeTrue();
        selected.SecondSameLine.Should().BeFalse();
        selected.TaggedCalls.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TagSelectedAdjustmentPositionsFinalGlyph()
    {
        var baseline = Render(null, install: false);
        var selected = Render(-1.4);
        selected.MarkX.Should().BeApproximately(baseline.MarkX - 1.4, 0.001);
    }

    [Fact]
    public void NullSelectionFallsBackToExistingSelector()
    {
        Render(null, legacy: -1.4).Content.Should()
            .Be(Render(null, legacy: -1.4, install: false).Content);
    }

    [Fact]
    public void TaggedSelectionTakesPrecedence()
    {
        var selected = Render(-1.4, legacy: 5, tagBoth: true);
        selected.Content.Should().Be(Render(null, legacy: -1.4, install: false).Content);
        selected.LegacyCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0)]
    public void InvalidOrZeroSelectionDisablesAdjustmentWithoutFallback(double value)
    {
        var selected = Render(value, legacy: -1.4, tagBoth: true);
        selected.Content.Should().Be(Render(null, install: false).Content);
        selected.LegacyCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    public void TaggedAdjustmentUsesExistingScriptScaling(int script)
    {
        Render(-1.4, tagBoth: true, script: script).Content.Should()
            .Be(Render(null, legacy: -1.4, install: false, script: script).Content);
    }

    [Fact]
    public void SingleCharacterWordDoesNotInvokeTaggedSelector()
    {
        var selected = Render(-1.4, word: "x");
        selected.TaggedCalls.Should().Be(0);
        selected.Content.Should().Be(Render(null, install: false, word: "x").Content);
    }

    static Result Render(double? selected, double? legacy = null, bool install = true,
        bool narrow = false, bool tagBoth = false, int script = 0, string word = ClosingWord)
    {
        using var measure = XGraphics.CreateMeasureContext(new XSize(300, 300),
            XGraphicsUnit.Point, XPageDirection.Downwards);
        var font = new XFont("Arial", 12);
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        if (narrow)
        {
            section.PageSetup.PageWidth = Unit.FromPoint(measure.MeasureString("of " + word, font).Width - 0.7 + 40);
            section.PageSetup.LeftMargin = Unit.FromPoint(20);
            section.PageSetup.RightMargin = Unit.FromPoint(20);
        }
        var tag = new object();
        var paragraphs = new List<Paragraph>();
        for (int i = 0; i < 2; i++)
        {
            var paragraph = section.AddParagraph();
            paragraphs.Add(paragraph);
            paragraph.AddText("of ");
            var formatted = paragraph.AddFormattedText();
            formatted.Font.Subscript = script < 0;
            formatted.Font.Superscript = script > 0;
            formatted.AddText(word).Tag = i == 0 || tagBoth ? tag : null;
        }
        var renderer = new PdfDocumentRenderer { Document = document };
        int taggedCalls = 0, legacyCalls = 0;
        if (install)
            renderer.DocumentRenderer.TrailingGlyphKerningWithTextTag = (paragraph, actualTag, text, _) =>
            {
                if (text != word || !ReferenceEquals(actualTag, tag)) return null;
                paragraphs.Should().Contain(paragraph);
                taggedCalls++;
                return selected;
            };
        if (legacy != null)
            renderer.DocumentRenderer.TrailingGlyphKerning = (_, text, _) =>
            {
                if (text != word) return null;
                legacyCalls++;
                return legacy;
            };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        pdf.PageCount.Should().Be(1);
        var reader = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        var positions = new List<(string Text, double X, double Y)>();
        while (reader.MoveAndGetNext(true, out var item))
            positions.Add((item!.Text, item.X, item.Y));
        var prefixes = positions.Where(item => item.Text == "of").ToArray();
        var start = word.Length == 1 ? word : word[..^1];
        var words = positions.Where(item => item.Text.StartsWith(start, StringComparison.Ordinal)).ToArray();
        var mark = positions.FirstOrDefault(item => item.Y == words[0].Y && item.X > words[0].X);
        var markX = mark.Text != null ? mark.X : words[0].X + measure.MeasureString(start, font).Width;
        return new Result(prefixes[0].Y == words[0].Y, prefixes[1].Y == words[1].Y,
            markX, taggedCalls, legacyCalls, PdfFileHelper.GetPageContentStream(pdf, 0));
    }

    record Result(bool FirstSameLine, bool SecondSameLine, double MarkX,
        int TaggedCalls, int LegacyCalls, string Content);
}
