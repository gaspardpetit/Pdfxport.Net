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
public class TextHorizontalScaleTests : IDisposable
{
    public TextHorizontalScaleTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(.5)]
    [InlineData(2)]
    public void ScalingChangesAdvanceAndEmitsHorizontalPaintTransform(double scale)
    {
        var original = Render(null);
        var selected = Render(scale);
        selected.FollowingX.Should().BeApproximately(original.FollowingX + (scale - 1) * Width("Bravo"), .001);
        selected.FollowingY.Should().BeApproximately(original.FollowingY, .001);
        // TextGetter reports text coordinates before graphics transforms. Check
        // the PDF matrix as well as the following unscaled leaf's actual advance.
        selected.Content.Should().Contain(scale == .5 ? ".5 0 0 1" : "2 0 0 1");
    }

    [Theory]
    [InlineData(.5, 1, true)]
    [InlineData(2, -1, false)]
    public void ScalingParticipatesInLineFitting(double scale, double overrun, bool fits)
    {
        var selected = Render(scale, overrun: overrun, suffix: false);
        (Math.Abs(selected.WordY - selected.FirstY) < .001).Should().Be(fits);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(1)]
    public void InvalidOrUnitScalePreservesOutput(double scale)
        => Render(scale).Content.Should().Be(Render(null).Content);

    [Theory]
    [InlineData(.5)]
    [InlineData(2)]
    public void OutgoingAdvanceRetainsPointValueAfterScaling(double scale)
    {
        Render(scale, trailing: 2).FollowingX.Should().BeApproximately(Render(scale).FollowingX + 2, .001);
    }

    [Theory]
    [InlineData(.5, 0)]
    [InlineData(.5, 1)]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    public void SameFontBlanksUseTheirOwnScale(double scale, int selectedBlank)
    {
        (double First, double Last) RenderBlanks(double? selected)
        {
            var document = NewDocument(out var section);
            var paragraph = section.AddParagraph();
            paragraph.Format.Alignment = ParagraphAlignment.Right;
            var tag = new object();
            paragraph.AddText("Alpha");
            paragraph.Elements.Add(new Text(" ") { Tag = selectedBlank == 0 ? tag : null });
            paragraph.AddText("Bravo");
            paragraph.Elements.Add(new Text(" ") { Tag = selectedBlank == 1 ? tag : null });
            paragraph.AddText("Charlie");
            var renderer = new PdfDocumentRenderer { Document = document };
            if (selected != null)
                renderer.DocumentRenderer.TextHorizontalScale = actual => ReferenceEquals(actual, tag) ? selected : null;
            renderer.RenderDocument();
            using var pdf = renderer.PdfDocument;
            var reader = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
            var positions = new List<(string Text, double X)>();
            while (reader.MoveAndGetNext(true, out var item)) positions.Add((item!.Text, item.X));
            return (positions.First(item => item.Text == "Alpha").X, positions.First(item => item.Text == "Charlie").X);
        }
        var original = RenderBlanks(null);
        var selected = RenderBlanks(scale);
        selected.First.Should().BeApproximately(original.First - (scale - 1) * Width(" "), .001);
        selected.Last.Should().BeApproximately(original.Last, .001);
    }

    [Theory]
    [InlineData(.5)]
    [InlineData(2)]
    public void KerningRetainsPointValueAfterScaling(double scale)
    {
        Render(scale, kerning: -1).FollowingX.Should().BeApproximately(Render(scale).FollowingX - 1, .001);
    }

    [Theory]
    [InlineData(.5)]
    [InlineData(2)]
    public void DecimalTabUsesScaledPrefixWidth(double scale)
    {
        var document = NewDocument(out var section);
        document.Culture = System.Globalization.CultureInfo.GetCultureInfo("en-US");
        var paragraph = section.AddParagraph();
        paragraph.Format.TabStops.AddTabStop(Unit.FromPoint(100), TabAlignment.Decimal);
        paragraph.AddTab();
        var tag = new object();
        paragraph.Elements.Add(new Text("12.34") { Tag = tag });
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.TextHorizontalScale = actual => ReferenceEquals(actual, tag) ? scale : null;
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var content = PdfFileHelper.GetPageContentStream(pdf, 0);
        var matrix = System.Text.RegularExpressions.Regex.Match(content,
            @"(?m)^([\d.]+) 0 0 1 ([\d.]+) 0 cm");
        matrix.Success.Should().BeTrue();
        var origin = double.Parse(matrix.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture);
        (origin + Width("12") * scale).Should().BeApproximately(120, .001);
    }

    static Result Render(double? scale, double? overrun = null, bool suffix = true, double? trailing = null, double? kerning = null)
    {
        var document = NewDocument(out var section);
        if (overrun != null)
            section.PageSetup.PageWidth = Unit.FromPoint(40 + Width("Alpha Bravo") - overrun.Value);
        var paragraph = section.AddParagraph();
        paragraph.AddText("Alpha ");
        var tag = new object();
        paragraph.Elements.Add(new Text("Bravo") { Tag = tag });
        if (suffix) paragraph.AddText(" Charlie");
        var renderer = new PdfDocumentRenderer { Document = document };
        if (scale != null)
            renderer.DocumentRenderer.TextHorizontalScale = actual => ReferenceEquals(actual, tag) ? scale : null;
        if (trailing != null)
            renderer.DocumentRenderer.TextTrailingAdvance = actual => ReferenceEquals(actual, tag) ? trailing : null;
        if (kerning != null)
            renderer.DocumentRenderer.TrailingGlyphKerningWithTextTag = (_, actual, _, _) => ReferenceEquals(actual, tag) ? kerning : null;
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var reader = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        var positions = new List<(string Text, double X, double Y)>();
        while (reader.MoveAndGetNext(true, out var item)) positions.Add((item!.Text, item.X, item.Y));
        var first = positions.First(item => item.Text == "Alpha");
        var word = positions.First(item => item.Text is "Bravo" or "Brav");
        var following = positions.FirstOrDefault(item => item.Text == "Charlie");
        return new Result(first.Y, word.Y, following.X, following.Y, PdfFileHelper.GetPageContentStream(pdf, 0));
    }

    static Document NewDocument(out Section section)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        section = document.AddSection();
        section.PageSetup.LeftMargin = Unit.FromPoint(20);
        section.PageSetup.RightMargin = Unit.FromPoint(20);
        return document;
    }
    static double Width(string text)
    {
        using var gfx = XGraphics.CreateMeasureContext(new XSize(300, 300), XGraphicsUnit.Point, XPageDirection.Downwards);
        return gfx.MeasureString(text, new XFont("Arial", 12)).Width;
    }
    record Result(double FirstY, double WordY, double FollowingX, double FollowingY, string Content);
}
