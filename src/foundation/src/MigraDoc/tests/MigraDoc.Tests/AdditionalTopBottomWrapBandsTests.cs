// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.
using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class AdditionalTopBottomWrapBandsTests : IDisposable
{
    public AdditionalTopBottomWrapBandsTests()
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
    public void DisjointBandsMatchSeparateExclusionsAndLeaveTextInGaps(bool frame)
    {
        var bands = new[] { (40.0, 10.0), (80.0, 10.0) };
        var result = Render(bands, frame: frame);
        result.Lines.Should().Equal(Render(bands, reference: true, frame: frame).Lines);
        result.Lines.Should().Contain(line => line.Page == 0 && line.Y > 40 && line.Y < 60);
        result.Lines.Should().Contain(line => line.Page == 0 && line.Y > 80 && line.Y < 100);
        result.Pages.Should().BeGreaterThan(1);
        result.Calls.Should().BeGreaterThan(0);
    }

    [Fact]
    public void OverlapDuplicatesAndOrderUseTheUnionOfBands()
    {
        var result = Render(new[] { (80.0, 10.0), (40.0, 10.0), (45.0, 10.0), (40.0, 10.0) });
        result.Lines.Should().Equal(Render(new[] { (40.0, 15.0), (80.0, 10.0) }, reference: true).Lines);
    }

    [Fact]
    public void NegativeOffsetsAreRelativeToThePositionedShape()
    {
        var bands = new[] { (-40.0, 10.0), (40.0, 10.0) };
        Render(bands, shapeTop: 40).Lines.Should().Equal(Render(bands, reference: true, shapeTop: 40).Lines);
    }

    [Fact]
    public void AdditionalBandsDoNotRepeatTheOrdinaryWrapDistances()
    {
        var bands = new[] { (40.0, 10.0), (80.0, 10.0) };
        Render(bands, distance: 4).Lines.Should().Equal(Render(bands, reference: true, distance: 4).Lines);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullAndEmptyResultsRetainOrdinaryExclusion(bool empty)
    {
        var result = Render(empty ? Array.Empty<(double, double)>() : null);
        result.Lines.Should().Equal(Render(null, reference: true).Lines);
    }

    [Theory]
    [InlineData(double.NaN, 10)]
    [InlineData(double.PositiveInfinity, 10)]
    [InlineData(double.NegativeInfinity, 10)]
    [InlineData(40, double.NaN)]
    [InlineData(40, double.PositiveInfinity)]
    [InlineData(40, double.NegativeInfinity)]
    [InlineData(40, 0)]
    [InlineData(40, -1)]
    [InlineData(double.MaxValue, double.MaxValue)]
    [InlineData(double.MaxValue, 1)]
    [InlineData(-double.MaxValue, 1)]
    public void InvalidBandsAreIgnoredWithoutDiscardingValidOnes(double top, double height)
    {
        Render(new[] { (top, height), (40.0, 10.0) }).Lines
            .Should().Equal(Render(new[] { (40.0, 10.0) }, reference: true).Lines);
    }

    [Theory]
    [InlineData(WrapStyle.None)]
    [InlineData(WrapStyle.Left)]
    [InlineData(WrapStyle.Right)]
    [InlineData(WrapStyle.BothSides)]
    [InlineData(WrapStyle.Largest)]
    [InlineData(WrapStyle.TopBottom)]
    public void OtherWrapStylesAndUnselectedShapesDoNotInvokeSelector(WrapStyle style)
    {
        var result = Render(new[] { (40.0, 10.0) }, style: style,
            exclude: style != WrapStyle.TopBottom);
        result.Calls.Should().Be(0);
        result.Lines.Should().Equal(Render(null, reference: true, style: style,
            exclude: style != WrapStyle.TopBottom).Lines);
    }

    [Fact]
    public void BandsBelowThePageDoNotCarryIntoFollowingPages()
    {
        Render(new[] { (500.0, 10.0) }).Lines.Should().Equal(Render(null, reference: true).Lines);
    }

    [Fact]
    public void DisabledByDefault() =>
        new DocumentRenderer(new Document()).AdditionalTopBottomWrapBands.Should().BeNull();

    record Line(int Page, string Text, double X, double Y);
    record Result(List<Line> Lines, int Pages, int Calls);
    static Result Render(IReadOnlyList<(double Top, double Height)>? bands, bool reference = false,
        bool frame = false, double shapeTop = 0, double distance = 0,
        WrapStyle style = WrapStyle.TopBottom, bool exclude = true)
    {
        var doc = new Document();
        doc.Styles.Normal.Font.Name = "Arial";
        doc.Styles.Normal.Font.Size = 8;
        var section = doc.AddSection();
        section.PageSetup.PageWidth = Unit.FromPoint(240);
        section.PageSetup.PageHeight = Unit.FromPoint(300);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromPoint(20);
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(20);
        Shape AddShape(double top, double height, bool useFrame, double clearance)
        {
            Shape shape = useFrame ? section.AddTextFrame() : section.AddImage(
                "base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==");
            shape.RelativeHorizontal = RelativeHorizontal.Margin;
            shape.RelativeVertical = RelativeVertical.Margin;
            shape.Left = Unit.FromPoint(0);
            shape.Top = Unit.FromPoint(top);
            shape.Width = Unit.FromPoint(40);
            shape.Height = Unit.FromPoint(height);
            shape.WrapFormat.Style = style;
            shape.WrapFormat.ExcludeFollowingText = exclude;
            shape.WrapFormat.DistanceTop = shape.WrapFormat.DistanceBottom = Unit.FromPoint(clearance);
            return shape;
        }
        var original = AddShape(shapeTop, 10, frame, distance);
        if (reference && bands != null)
            foreach (var (top, height) in bands) AddShape(shapeTop + distance + top, height, false, 0);
        var paragraph = section.AddParagraph();
        paragraph.Format.LineSpacingRule = LineSpacingRule.Exactly;
        paragraph.Format.LineSpacing = Unit.FromPoint(10);
        for (var index = 0; index < 36; index++)
        {
            if (index > 0) paragraph.AddLineBreak();
            paragraph.AddText($"Line{index:D2}");
        }
        var renderer = new PdfDocumentRenderer { Document = doc };
        var calls = 0;
        if (!reference)
            renderer.DocumentRenderer.AdditionalTopBottomWrapBands = shape =>
            {
                shape.Should().BeSameAs(original);
                calls++;
                return bands;
            };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var lines = new List<Line>();
        for (var page = 0; page < pdf.PageCount; page++)
        {
            var enumerator = PdfFileHelper.GetPageContentStreamEnumerator(pdf, page).Text;
            while (enumerator.MoveAndGetNext(true, out var text))
                if (text!.Text.StartsWith("Line", StringComparison.Ordinal))
                    lines.Add(new Line(page, text.Text, text.X, pdf.Pages[page].Height.Point - text.Y));
        }
        lines.Count.Should().Be(36);
        return new Result(lines, pdf.PageCount, calls);
    }
}
