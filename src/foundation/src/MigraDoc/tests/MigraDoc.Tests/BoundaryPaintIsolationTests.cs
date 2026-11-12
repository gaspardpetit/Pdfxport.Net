// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.
using System.Globalization;
using System.Text.RegularExpressions;
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
public class BoundaryPaintIsolationTests : IDisposable
{
    public BoundaryPaintIsolationTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    public static IEnumerable<object[]> Cases()
    {
        foreach (var alignment in new[] { ParagraphAlignment.Left, ParagraphAlignment.Center, ParagraphAlignment.Right, ParagraphAlignment.Justify })
        foreach (var tab in new[] { TabAlignment.Left, TabAlignment.Center, TabAlignment.Right, TabAlignment.Decimal })
        foreach (var shift in new[] { -12.0, 12.0 })
            yield return new object[] { alignment, tab, shift };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void PaintShiftLeavesSpacingAndFollowingLineUnchanged(ParagraphAlignment alignment, TabAlignment tab, double shift)
    {
        var original = Render(alignment, tab, 0);
        var selected = Render(alignment, tab, shift);
        selected.Text.Should().HaveCount(original.Text.Count);
        var firstY = original.Text[0].Y;
        for (var index = 0; index < original.Text.Count; index++)
        {
            var expected = original.Text[index];
            var actual = selected.Text[index];
            var delta = expected.Y == firstY && expected.Text != "A" ? shift : 0;
            actual.Text.Should().Be(expected.Text);
            actual.X.Should().BeApproximately(expected.X + delta, .002, expected.Text);
            actual.Y.Should().BeApproximately(expected.Y, .002);
        }
        selected.Image.X.Should().BeApproximately(original.Image.X + shift, .002);
        selected.Image.Y.Should().BeApproximately(original.Image.Y, .002);
        selected.Image.Width.Should().Be(original.Image.Width);
        selected.Image.Height.Should().Be(original.Image.Height);
        original.Links.Should().HaveCount(2);
        selected.Links.Should().HaveCount(original.Links.Count);
        for (var index = 0; index < original.Links.Count; index++)
        {
            selected.Links[index].Left.Should().BeApproximately(original.Links[index].Left + shift, .002);
            selected.Links[index].Right.Should().BeApproximately(original.Links[index].Right + shift, .002);
            selected.Links[index].Bottom.Should().BeApproximately(original.Links[index].Bottom, .002);
            selected.Links[index].Top.Should().BeApproximately(original.Links[index].Top, .002);
        }
        original.Underlines.Should().NotBeEmpty();
        selected.Underlines.Should().HaveCount(original.Underlines.Count);
        for (var index = 0; index < original.Underlines.Count; index++)
        {
            selected.Underlines[index].Left.Should().BeApproximately(original.Underlines[index].Left + shift, .002);
            selected.Underlines[index].Right.Should().BeApproximately(original.Underlines[index].Right + shift, .002);
        }
    }

    [Theory]
    [InlineData(-12)]
    [InlineData(12)]
    public void PaintOffsetResetsAtFloatSegmentsAndLineStarts(double shift)
    {
        (List<(string Text, double X, double Y)> Text, List<object?> Calls) Draw(double adjustment)
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            document.Styles.Normal.Font.Size = 12;
            var section = document.AddSection();
            section.PageSetup.PageWidth = Unit.FromPoint(612);
            section.PageSetup.PageHeight = Unit.FromPoint(400);
            section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(72);
            section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromPoint(72);
            var image = section.AddImage("base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==");
            image.RelativeHorizontal = MigraDoc.DocumentObjectModel.Shapes.RelativeHorizontal.Margin;
            image.RelativeVertical = MigraDoc.DocumentObjectModel.Shapes.RelativeVertical.Margin;
            image.Left = Unit.FromPoint(100);
            image.Top = Unit.FromPoint(0);
            image.Width = Unit.FromPoint(120);
            image.Height = Unit.FromPoint(90);
            image.WrapFormat.Style = MigraDoc.DocumentObjectModel.Shapes.WrapStyle.BothSides;
            image.WrapFormat.DistanceLeft = image.WrapFormat.DistanceRight = Unit.FromPoint(9);
            var paragraph = section.AddParagraph();
            for (var index = 0; index < 20; index++)
            {
                var word = $"W{index:D2}";
                paragraph.AddText(word).Tag = word;
                paragraph.AddText(" ");
            }
            var calls = new List<object?>();
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.DocumentRenderer.RenderOnlyTextBoundaryKerning = (_, _, tag, _, _, _) =>
            {
                calls.Add(tag);
                return Equals(tag, "W01") ? adjustment : null;
            };
            renderer.RenderDocument();
            using var pdf = renderer.PdfDocument;
            var reader = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
            var text = new List<(string Text, double X, double Y)>();
            while (reader.MoveAndGetNext(true, out var item)) text.Add((item!.Text, item.X, item.Y));
            return (text, calls);
        }
        var original = Draw(0);
        var selected = Draw(shift);
        var firstY = original.Text[0].Y;
        original.Text.Count(item => item.Y == firstY && item.X < 172).Should().BeGreaterThan(1);
        original.Text.Should().Contain(item => item.Y == firstY && item.X > 292);
        selected.Calls.Should().Equal(original.Calls);
        selected.Text.Should().HaveCount(original.Text.Count);
        for (var index = 0; index < original.Text.Count; index++)
        {
            var expected = original.Text[index];
            var delta = expected.Y == firstY && expected.X < 172 && expected.Text != "W00" ? shift : 0;
            selected.Text[index].Text.Should().Be(expected.Text);
            selected.Text[index].X.Should().BeApproximately(expected.X + delta, .002);
            selected.Text[index].Y.Should().BeApproximately(expected.Y, .002);
        }
    }

    static Result Render(ParagraphAlignment alignment, TabAlignment tab, double shift)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        section.PageSetup.PageWidth = Unit.FromPoint(500);
        section.PageSetup.LeftMargin = Unit.FromPoint(20);
        section.PageSetup.RightMargin = Unit.FromPoint(20);
        var paragraph = section.AddParagraph();
        paragraph.Format.Alignment = alignment;
        paragraph.Format.TabStops.AddTabStop(Unit.FromPoint(100), tab, TabLeader.Dots);
        paragraph.AddText("A");
        paragraph.AddText("B").Tag = "B";
        paragraph.AddTab();
        var link = paragraph.AddHyperlink("https://example.org", HyperlinkType.Url);
        link.Font.Underline = Underline.Single;
        link.AddText("C");
        paragraph.AddText(" ");
        var run = paragraph.AddFormattedText();
        run.Font.Underline = Underline.Single;
        run.AddText("D").Tag = "D";
        paragraph.AddText(" ");
        paragraph.AddPageField();
        const string pixel = "base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==";
        var image = paragraph.AddImage(pixel);
        image.Width = Unit.FromPoint(12);
        image.Height = Unit.FromPoint(8);
        paragraph.AddText("E");
        paragraph.AddLineBreak();
        paragraph.AddText("tail ");
        paragraph.AddPageField();
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.RenderOnlyTextBoundaryKerning = (_, _, tag, _, _, _) => Equals(tag, "B") ? shift : null;
        renderer.DocumentRenderer.LeadingKerningAfterBlankWithTextTag = (_, tag, _, _) => Equals(tag, "D") ? 2 : null;
        if (tab == TabAlignment.Decimal)
            renderer.DocumentRenderer.TabLeaderLayout = (_, _, _) => (1.0, 2.0, 3.0);
        XRect imagePaint = default;
        renderer.DocumentRenderer.ImageWebLink = (_, rect) => { imagePaint = rect; return "https://example.org/image"; };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        pdf.PageCount.Should().Be(1);
        var reader = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        var text = new List<(string Text, double X, double Y)>();
        while (reader.MoveAndGetNext(true, out var item)) text.Add((item!.Text, item.X, item.Y));
        text.Should().Contain(item => item.Text == "tail");
        var links = Enumerable.Range(0, pdf.Pages[0].Annotations.Count)
            .Select(index => pdf.Pages[0].Annotations[index].Rectangle)
            .Select(rect => (rect.X1, rect.X2, rect.Y1, rect.Y2)).ToList();
        var content = PdfFileHelper.GetPageContentStream(pdf, 0);
        var underlines = Regex.Matches(content,
                @"(?m)(?<x1>[-\d.]+) (?<y1>[-\d.]+) m\s+(?<x2>[-\d.]+) (?<y2>[-\d.]+) l\s+S")
            .Where(match => match.Groups["y1"].Value == match.Groups["y2"].Value)
            .Select(match => (double.Parse(match.Groups["x1"].Value, CultureInfo.InvariantCulture),
                double.Parse(match.Groups["x2"].Value, CultureInfo.InvariantCulture))).ToList();
        return new Result(text, imagePaint, links, underlines);
    }

    record Result(List<(string Text, double X, double Y)> Text, XRect Image,
        List<(double Left, double Right, double Bottom, double Top)> Links,
        List<(double Left, double Right)> Underlines);
}
