// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using MigraDoc.DocumentObjectModel.Shapes;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class CenteredFrameTrailingSpaceTests : IDisposable
{
    public CenteredFrameTrailingSpaceTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(0, 30, 15)]
    [InlineData(10, 30, 10)]
    [InlineData(30, 30, 0)]
    [InlineData(30, 10, 0)]
    public void SelectedTrailingSpaceCountsOnlyTheUncountedAmount(double counted, double selected, double shift)
    {
        var baseline = Render(null, after: counted);
        var result = Render(selected, after: counted);
        result.TranslationY.Should().BeApproximately(baseline.TranslationY - shift, 0.001);
        result.Rectangles.Should().Equal(baseline.Rectangles);
        result.FollowingY.Should().BeApproximately(baseline.FollowingY, 0.001);
        result.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidValuesPreserveDefaultPainting(double selected)
    {
        Render(selected).Content.Should().Be(Render(null).Content);
    }

    [Fact]
    public void OversizedTrailingSpaceDoesNotTranslateAboveTheFrameTop()
    {
        var selected = Render(1000);
        var topAligned = Render(null, centered: false);
        selected.TranslationY.Should().BeApproximately(topAligned.TranslationY, 0.001);
        selected.Rectangles.Should().Equal(topAligned.Rectangles);
    }

    [Fact]
    public void NoncenteredFramesDoNotInvokePolicy()
    {
        var result = Render(30, centered: false);
        result.Calls.Should().Be(0);
        result.Content.Should().Be(Render(null, centered: false).Content);
    }

    [Fact]
    public void EmptyFramesDoNotInvokePolicy()
    {
        var result = Render(30, empty: true);
        result.Calls.Should().Be(0);
        result.Content.Should().Be(Render(null, empty: true).Content);
    }

    static Result Render(double? selected, double after = 0, bool centered = true, bool empty = false)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        var frame = section.AddTextFrame();
        frame.Width = Unit.FromPoint(180);
        frame.Height = Unit.FromPoint(120);
        frame.CenterContentVertically = centered;
        frame.LineFormat.Visible = true;
        if (!empty) frame.AddParagraph("Centered").Format.SpaceAfter = Unit.FromPoint(after);
        section.AddParagraph("Following");
        var renderer = new PdfDocumentRenderer { Document = document };
        int calls = 0;
        renderer.DocumentRenderer.CenteredTextFrameTrailingSpace = actual =>
        {
            actual.Should().BeSameAs(frame);
            calls++;
            return selected;
        };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        pdf.PageCount.Should().Be(1);
        var content = PdfFileHelper.GetPageContentStream(pdf, 0);
        var rectangles = Regex.Matches(content, @"(?m)^-?[\d.]+ -?[\d.]+ [\d.]+ [\d.]+ re$")
            .Cast<Match>().Select(match => match.Value).ToArray();
        var translations = Regex.Matches(content, @"(?m)^1 0 0 1 (?<x>-?[\d.]+) (?<y>-?[\d.]+) cm$");
        var translationY = translations.Count > 0
            ? double.Parse(translations[^1].Groups["y"].Value, CultureInfo.InvariantCulture) : 0;
        var text = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        double followingY = 0;
        while (text.MoveAndGetNext(true, out var item))
            if (item!.Text == "Following") followingY = item.Y;
        followingY.Should().BeGreaterThan(0);
        return new Result(content, translationY, rectangles, followingY, calls);
    }

    record Result(string Content, double TranslationY, string[] Rectangles, double FollowingY, int Calls);
}
