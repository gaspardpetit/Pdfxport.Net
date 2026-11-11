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
public class TaggedLeadingKerningTests : IDisposable
{
    public TaggedLeadingKerningTests()
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
        var baseline = Render(null, install: false, overrun: 0.7);
        var selected = Render(-1.4, overrun: 0.7);
        baseline.FirstSameLine.Should().BeFalse();
        selected.FirstSameLine.Should().BeTrue();
        selected.SecondSameLine.Should().BeFalse();
        selected.TaggedCalls.Should().BeGreaterThan(0);
    }

    [Theory]
    [InlineData(-1.4)]
    [InlineData(2.0)]
    public void TaggedAdjustmentMovesOnlySelectedWord(double adjustment)
    {
        var baseline = Render(null, install: false);
        var selected = Render(adjustment);
        selected.FirstX.Should().BeApproximately(baseline.FirstX + adjustment, 0.001);
        selected.SecondX.Should().BeApproximately(baseline.SecondX, 0.001);
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
    public void InvalidOrZeroSelectionSuppressesLegacyAdjustment(double value)
    {
        var selected = Render(value, legacy: -1.4, tagBoth: true);
        selected.Content.Should().Be(Render(null, install: false).Content);
        selected.LegacyCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Alpha")]
    [InlineData("Alpha\u00A0")]
    public void IneligibleBoundaryDoesNotInvokeSelector(string prefix)
    {
        var selected = Render(-1.4, prefix: prefix);
        selected.TaggedCalls.Should().Be(0);
        selected.Content.Should().Be(Render(null, install: false, prefix: prefix).Content);
    }

    [Fact]
    public void WrappedWordRetainsLineStartPosition()
    {
        var baseline = Render(null, install: false, overrun: 5);
        var selected = Render(-1.4, overrun: 5);
        selected.FirstSameLine.Should().BeFalse();
        selected.FirstX.Should().BeApproximately(baseline.FirstX, 0.001);
    }

    static Result Render(double? selected, double? legacy = null, bool install = true,
        double? overrun = null, bool tagBoth = false, string prefix = "Alpha ")
    {
        using var measure = XGraphics.CreateMeasureContext(new XSize(300, 300),
            XGraphicsUnit.Point, XPageDirection.Downwards);
        var font = new XFont("Arial", 12);
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        if (overrun != null)
        {
            section.PageSetup.PageWidth = Unit.FromPoint(measure.MeasureString(prefix + "Bravo", font).Width - overrun.Value + 40);
            section.PageSetup.LeftMargin = Unit.FromPoint(20);
            section.PageSetup.RightMargin = Unit.FromPoint(20);
        }
        var tag = new object();
        var paragraphs = new List<Paragraph>();
        for (int i = 0; i < 2; i++)
        {
            var paragraph = section.AddParagraph();
            paragraphs.Add(paragraph);
            if (prefix.Length > 0) paragraph.AddText(prefix);
            paragraph.AddFormattedText().AddText("Bravo").Tag = i == 0 || tagBoth ? tag : null;
        }
        var renderer = new PdfDocumentRenderer { Document = document };
        int taggedCalls = 0, legacyCalls = 0;
        if (install)
            renderer.DocumentRenderer.LeadingKerningAfterBlankWithTextTag = (paragraph, actualTag, word, actualFont) =>
            {
                if (word != "Bravo" || !ReferenceEquals(actualTag, tag)) return null;
                paragraphs.Should().Contain(paragraph);
                actualFont.Size.Should().Be(12);
                taggedCalls++;
                return selected;
            };
        if (legacy != null)
            renderer.DocumentRenderer.LeadingKerningAfterBlank = (_, word, _) =>
            {
                if (word != "Bravo") return null;
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
        var words = positions.Where(item => item.Text == "Bravo").ToArray();
        var prefixes = positions.Where(item => item.Text == "Alpha").ToArray();
        return new Result(words[0].X, words[1].X,
            prefixes.Length == 2 && prefixes[0].Y == words[0].Y,
            prefixes.Length == 2 && prefixes[1].Y == words[1].Y,
            taggedCalls, legacyCalls, PdfFileHelper.GetPageContentStream(pdf, 0));
    }

    record Result(double FirstX, double SecondX, bool FirstSameLine, bool SecondSameLine,
        int TaggedCalls, int LegacyCalls, string Content);
}
