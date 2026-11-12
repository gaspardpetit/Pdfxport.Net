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
public class RenderBoundaryKerningTests : IDisposable
{
    public RenderBoundaryKerningTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(-2)]
    [InlineData(3)]
    public void AdjustmentMovesSelectedLeafAndFollowingText(double adjustment)
    {
        var original = Render(null);
        var selected = Render(adjustment);
        selected.PrefixX.Should().BeApproximately(original.PrefixX, .001);
        selected.WordX.Should().BeApproximately(original.WordX + adjustment, .001);
        selected.FollowingX.Should().BeApproximately(original.FollowingX + adjustment, .001);
        selected.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(0)]
    public void InvalidOrZeroAdjustmentPreservesOutput(double adjustment)
        => Render(adjustment).Content.Should().Be(Render(null).Content);

    [Theory]
    [InlineData(-5)]
    [InlineData(5)]
    public void PaintAdjustmentCannotChangeLineBreaks(double adjustment)
    {
        var original = Render(null, wrap: true);
        var selected = Render(adjustment, wrap: true);
        selected.WordY.Should().BeApproximately(original.WordY, .001);
        selected.WordX.Should().BeApproximately(original.WordX, .001);
        selected.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("start")]
    [InlineData("untagged")]
    [InlineData("size")]
    [InlineData("family")]
    [InlineData("bold")]
    [InlineData("subscript")]
    [InlineData("superscript")]
    [InlineData("tab")]
    [InlineData("linebreak")]
    [InlineData("surrogate")]
    public void IneligibleBoundaryDoesNotInvokeSelector(string mode)
    {
        var selected = Render(-2, mode: mode);
        selected.Calls.Should().Be(0);
        selected.Content.Should().Be(Render(null, mode: mode).Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryBlankBoundaryCanBeAdjusted(bool character)
    {
        var mode = character ? "character" : "space";
        var original = Render(null, mode: mode);
        var selected = Render(-2, mode: mode);
        selected.WordX.Should().BeApproximately(original.WordX - 2, .001);
        selected.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(-2)]
    public void LeadingAfterBlankAdjustmentTakesPrecedence(double leading)
    {
        var selected = Render(-5, mode: "space", leading: leading);
        var original = Render(null, mode: "space", leading: leading);
        selected.Content.Should().Be(original.Content);
        selected.Calls.Should().Be(0);
    }

    [Fact]
    public void ZeroLeadingAfterBlankAllowsBoundaryAdjustment()
    {
        var original = Render(null, mode: "space", leading: 0);
        Render(-2, mode: "space", leading: 0).WordX.Should().BeApproximately(original.WordX - 2, .001);
    }

    [Fact]
    public void EmptyTextDoesNotBreakVisibleBoundary()
    {
        var selected = Render(-2, mode: "empty");
        selected.Calls.Should().Be(1);
        selected.Content.Should().Be(Render(-2).Content);
    }

    [Theory]
    [InlineData(.5, ParagraphAlignment.Center)]
    [InlineData(2, ParagraphAlignment.Right)]
    public void BoundaryAdjustmentRemainsInPointsWithScaledAlignedText(double layout, ParagraphAlignment alignment)
    {
        var original = Render(null, layout: layout, alignment: alignment);
        var selected = Render(2, layout: layout, alignment: alignment);
        selected.PrefixX.Should().BeApproximately(original.PrefixX, .001);
        selected.FollowingX.Should().BeApproximately(original.FollowingX + 2, .001);
    }

    static Result Render(double? adjustment, string mode = "adjacent", bool wrap = false, double? leading = null,
        double layout = 1, ParagraphAlignment alignment = ParagraphAlignment.Left)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        section.PageSetup.LeftMargin = Unit.FromPoint(20);
        section.PageSetup.RightMargin = Unit.FromPoint(20);
        if (wrap)
        {
            using var measure = XGraphics.CreateMeasureContext(new XSize(300, 300), XGraphicsUnit.Point, XPageDirection.Downwards);
            section.PageSetup.PageWidth = Unit.FromPoint(40 + measure.MeasureString("Alpha Bravo", new XFont("Arial", 12)).Width - 1);
            mode = "space";
        }
        var paragraph = section.AddParagraph();
        paragraph.Format.Alignment = alignment;
        var priorTag = new object();
        var currentTag = new object();
        if (mode != "start") paragraph.AddText("Alpha").Tag = priorTag;
        switch (mode)
        {
            case "space": paragraph.AddText(" ").Tag = priorTag; break;
            case "character": paragraph.AddCharacter(SymbolName.Blank); break;
            case "tab": paragraph.AddTab(); break;
            case "linebreak": paragraph.AddLineBreak(); break;
            case "empty": paragraph.Elements.Add(new Text("")); break;
            case "surrogate": paragraph.AddText("\U0001F600"); break;
        }
        var run = paragraph.AddFormattedText();
        switch (mode)
        {
            case "size": run.Font.Size = 14; break;
            case "family": run.Font.Name = "Times New Roman"; break;
            case "bold": run.Font.Bold = true; break;
            case "subscript": run.Font.Subscript = true; break;
            case "superscript": run.Font.Superscript = true; break;
        }
        run.AddText("Bravo").Tag = mode == "untagged" ? null : currentTag;
        paragraph.AddText(" Charlie");
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.TextHorizontalScale = tag => ReferenceEquals(tag, currentTag) ? layout : null;
        var calls = 0;
        renderer.DocumentRenderer.RenderOnlyTextBoundaryKerning = (actualParagraph, previous, current, left, right, font) =>
        {
            ReferenceEquals(current, currentTag).Should().BeTrue();
            actualParagraph.Should().BeSameAs(paragraph);
            if (mode == "character") previous.Should().BeNull();
            else previous.Should().BeSameAs(priorTag);
            left.Should().Be(mode is "space" or "character" ? ' ' : 'a');
            right.Should().Be('B');
            font.Size.Should().Be(12);
            calls++;
            return adjustment;
        };
        if (leading != null)
            renderer.DocumentRenderer.LeadingKerningAfterBlankWithTextTag = (_, _, _, _) => leading;
        renderer.DocumentRenderer.PrepareDocument(renderer.PdfDocument.RenderEvents);
        calls.Should().Be(0, "paint-only selectors must not run during formatting");
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var reader = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        var positions = new List<(string Text, double X, double Y)>();
        while (reader.MoveAndGetNext(true, out var item)) positions.Add((item!.Text, item.X, item.Y));
        var word = positions.First(item => item.Text == "Bravo");
        var prefix = positions.FirstOrDefault(item => item.Text == "Alpha");
        var following = positions.First(item => item.Text == "Charlie");
        return new Result(prefix.X, word.X, word.Y, following.X, calls, PdfFileHelper.GetPageContentStream(pdf, 0));
    }
    record Result(double PrefixX, double WordX, double WordY, double FollowingX, int Calls, string Content);
}
