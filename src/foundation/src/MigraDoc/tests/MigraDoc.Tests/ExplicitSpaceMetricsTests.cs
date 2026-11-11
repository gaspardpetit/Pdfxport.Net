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
public class ExplicitSpaceMetricsTests : IDisposable
{
    public ExplicitSpaceMetricsTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }

    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(SymbolName.Blank, 1)]
    [InlineData(SymbolName.Blank, 3)]
    [InlineData(SymbolName.Em, 1)]
    [InlineData(SymbolName.Em, 3)]
    [InlineData(SymbolName.Em4, 1)]
    [InlineData(SymbolName.Em4, 3)]
    [InlineData(SymbolName.En, 1)]
    [InlineData(SymbolName.En, 3)]
    public void SelectedWidthIsTotalAndUsesSpaceTag(SymbolName symbol, int count)
    {
        var result = Render(12, symbol, count);
        result.Gap.Should().BeApproximately(12, 0.001);
        result.Calls.Should().BeGreaterThan(0);
        result.SameLine.Should().BeTrue();
    }

    [Fact]
    public void SelectedWidthControlsWrappingAsWellAsPaint()
    {
        var narrow = Render(2, narrow: true);
        var wide = Render(30, narrow: true);
        narrow.SameLine.Should().BeTrue();
        wide.SameLine.Should().BeFalse();
        narrow.Gap.Should().BeApproximately(2, 0.001);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonfiniteValuesRetainMeasuredWidth(double selected)
    {
        Render(selected).Content.Should().Be(Render(null).Content);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void NonpositiveWidthsAreClampedToZero(double selected)
    {
        Render(selected).Gap.Should().BeApproximately(0, 0.001);
    }

    [Fact]
    public void OrdinaryTextBlanksDoNotInvokeSelector()
    {
        var adjusted = Render(12, ordinary: true);
        adjusted.Calls.Should().Be(0);
        adjusted.Content.Should().Be(Render(null, ordinary: true).Content);
    }

    static Result Render(double? selected, SymbolName symbol = SymbolName.Blank, int count = 1,
        bool narrow = false, bool ordinary = false)
    {
        using var measure = XGraphics.CreateMeasureContext(new XSize(300, 300),
            XGraphicsUnit.Point, XPageDirection.Downwards);
        var font = new XFont("Arial", 12);
        var alphaWidth = measure.MeasureString("Alpha", font).Width;
        var naturalSpace = symbol switch
        {
            SymbolName.Em => measure.MeasureString("m", font).Width,
            SymbolName.Em4 => measure.MeasureString("m", font).Width / 4,
            SymbolName.En => measure.MeasureString("n", font).Width,
            _ => measure.MeasureString(" ", font).Width
        } * count;
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        if (narrow)
        {
            section.PageSetup.PageWidth = Unit.FromPoint(alphaWidth +
                measure.MeasureString("Bravo", font).Width + 10 + 40);
            section.PageSetup.LeftMargin = Unit.FromPoint(20);
            section.PageSetup.RightMargin = Unit.FromPoint(20);
        }
        var paragraph = section.AddParagraph();
        var tag = new object();
        if (ordinary)
            paragraph.AddText("Alpha Bravo");
        else
        {
            paragraph.AddText("Alpha");
            paragraph.AddCharacter(symbol, count).Tag = tag;
            paragraph.AddText("Bravo");
        }
        var renderer = new PdfDocumentRenderer { Document = document };
        int calls = 0;
        renderer.DocumentRenderer.SpaceWidthFromTextMetrics = (actualTag, measured, actualCount) =>
        {
            actualTag.Should().BeSameAs(tag);
            actualCount.Should().Be(count);
            measured.Should().BeApproximately(naturalSpace, 0.001);
            calls++;
            return selected;
        };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        pdf.PageCount.Should().Be(1);
        var reader = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        var positions = new List<(string Text, double X, double Y)>();
        while (reader.MoveAndGetNext(true, out var item))
            positions.Add((item!.Text, item.X, item.Y));
        var alpha = positions.First(item => item.Text == "Alpha");
        var bravo = positions.First(item => item.Text == "Bravo");
        return new Result(bravo.X - alpha.X - alphaWidth, alpha.Y == bravo.Y, calls,
            PdfFileHelper.GetPageContentStream(pdf, 0));
    }

    record Result(double Gap, bool SameLine, int Calls, string Content);
}
