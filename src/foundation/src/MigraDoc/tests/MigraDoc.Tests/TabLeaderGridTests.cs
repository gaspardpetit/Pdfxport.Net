// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.
using System.Globalization;
using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;
namespace MigraDoc.Tests;
[Collection("PDFsharp")]
public class TabLeaderGridTests : IDisposable
{
    public TabLeaderGridTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(TabLeader.Dots, ".")]
    [InlineData(TabLeader.Dashes, "-")]
    [InlineData(TabLeader.Lines, "_")]
    [InlineData(TabLeader.Heavy, "_")]
    [InlineData(TabLeader.MiddleDot, "·")]
    public void LeadersShareGridAcrossParagraphsAndLeaveTextUnchanged(TabLeader leader, string glyph)
    {
        var original = Render(null, leader);
        var grid = Render(10, leader);
        var leaders = grid.Runs.Where(r => r.Text == glyph).ToList();
        leaders.Count.Should().BeGreaterThan(10);
        foreach (var run in leaders)
            run.X.Should().BeApproximately(Math.Round(run.X / 10) * 10, 0.001);
        var plainText = original.Runs.Where(r => r.Text is "Alpha" or "B" or "1").ToList();
        grid.Runs.Where(r => r.Text is "Alpha" or "B" or "1").Should().Equal(plainText, SamePosition);
        // Every grid cell stays in its tab interval, not just the glyph origin.
        var groups = grid.Runs.GroupBy(r => r.Y);
        foreach (var line in groups)
        {
            var start = line.First();
            var end = line.Last();
            foreach (var run in line.Where(r => r.Text == glyph))
            {
                run.X.Should().BeGreaterThan(start.X);
                (run.X + 10).Should().BeLessThanOrEqualTo(end.X + 0.001);
            }
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(-2.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(double.Epsilon)]
    public void InvalidPitchFallsBackToExistingLayout(double? pitch)
    {
        Render(pitch).Content.Should().Be(Render(null).Content);
        Render(pitch, layout: true).Content.Should().Be(Render(null, layout: true).Content);
    }

    [Fact]
    public void ValidGridTakesPrecedenceOverLayout()
    {
        var actual = Render(10, layout: true);
        actual.LayoutCalls.Should().Be(0);
        actual.Content.Should().Be(Render(10).Content);
    }

    [Fact]
    public void OversizedPitchEmitsNoLeadersAndKeepsFollowingText()
    {
        var result = Render(double.MaxValue, layout: true);
        result.LayoutCalls.Should().Be(0);
        result.Runs.Should().HaveCount(4);
        result.Runs.Should().Equal(Render(null).Runs.Where(r => r.Text is "Alpha" or "B" or "1"), SamePosition);
    }

    [Fact]
    public void NoLeaderDoesNotInvokeSelector()
    {
        var result = Render(10, TabLeader.Spaces);
        result.GridCalls.Should().Be(0);
        result.Content.Should().Be(Render(null, TabLeader.Spaces).Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScriptFontIsPassedToSelector(bool subscript)
        => Render(10, script: subscript ? 1 : 2).GridCalls.Should().Be(2);

    [Fact]
    public void TightGridStillContainsTheLastGlyph()
    {
        var result = Render(2);
        using var gfx = XGraphics.CreateMeasureContext(new XSize(100, 100), XGraphicsUnit.Point, XPageDirection.Downwards);
        var width = gfx.MeasureString(".", new XFont("Arial", 12)).Width;
        width.Should().BeGreaterThan(2);
        foreach (var line in result.Runs.GroupBy(r => r.Y))
        {
            var dots = line.Where(r => r.Text == ".").ToList();
            dots.Should().NotBeEmpty();
            foreach (var dot in dots)
            {
                dot.X.Should().BeApproximately(Math.Round(dot.X / 2) * 2, 0.001);
                (dot.X + width).Should().BeLessThanOrEqualTo(line.Last().X + 0.001);
            }
        }
    }

    [Fact]
    public void NullSelectionFallsBackToExistingLayout()
    {
        var result = Render(null, layout: true, delegateNull: true);
        result.GridCalls.Should().Be(2);
        result.Content.Should().Be(Render(null, layout: true).Content);
    }

    [Fact]
    public void PenAlreadyOnGridUsesThatBoundary()
    {
        var result = Render(10, emptyLabels: true);
        result.Runs.First().Text.Should().Be(".");
        result.Runs.First().X.Should().Be(80);
    }

    static bool SamePosition(Run a, Run b) => a.Text == b.Text && Math.Abs(a.X - b.X) < 0.001 && Math.Abs(a.Y - b.Y) < 0.001;

    record Run(string Text, double X, double Y);
    record Result(string Content, List<Run> Runs, int GridCalls, int LayoutCalls);
    static Result Render(double? pitch, TabLeader leader = TabLeader.Dots, bool layout = false, int script = 0, bool delegateNull = false, bool emptyLabels = false)
    {
        var doc = new Document();
        doc.Styles.Normal.Font.Name = "Arial";
        doc.Styles.Normal.Font.Size = 12;
        var section = doc.AddSection();
        section.PageSetup.LeftMargin = Unit.FromPoint(emptyLabels ? 80 : 73);
        foreach (var label in new[] { "Alpha", "B" })
        {
            var p = section.AddParagraph();
            p.Format.LeftIndent = Unit.FromPoint(!emptyLabels && label == "B" ? 7 : 0);
            p.Format.TabStops.AddTabStop(Unit.FromPoint(180), TabAlignment.Right, leader);
            p.Format.Font.Subscript = script == 1;
            p.Format.Font.Superscript = script == 2;
            if (!emptyLabels) p.AddText(label);
            p.AddTab();
            p.AddText("1");
        }
        var renderer = new PdfDocumentRenderer { Document = doc };
        var gridCalls = 0;
        var layoutCalls = 0;
        if (pitch.HasValue || delegateNull)
            renderer.DocumentRenderer.TabLeaderGridPitch = (p, kind, font) =>
            {
                section.Elements.Cast<Paragraph>().Should().Contain(p);
                kind.Should().Be(leader);
                if (script != 0) font.Size.Should().BeLessThan(12);
                gridCalls++;
                return pitch;
            };
        if (layout)
            renderer.DocumentRenderer.TabLeaderLayout = (_, _, _) => { layoutCalls++; return (0.1, 3.5, 0.5); };
        renderer.DocumentRenderer.PrepareDocument();
        gridCalls.Should().Be(0);
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var content = PdfFileHelper.GetPageContentStream(pdf, 0);
        var x = 0.0;
        var y = 0.0;
        var runs = new List<Run>();
        foreach (var op in ContentReader.ReadContent(pdf.Pages[0]).OfType<COperator>())
        {
            if (op.OpCode.Name == "BT") x = y = 0;
            else if (op.OpCode.Name == "Td")
            {
                x += double.Parse(op.Operands[0].ToString()!, CultureInfo.InvariantCulture);
                y += double.Parse(op.Operands[1].ToString()!, CultureInfo.InvariantCulture);
            }
            else if (op.OpCode.Name == "Tj") runs.Add(new Run(((CString)op.Operands[0]).Value, x, y));
        }
        return new Result(content, runs, gridCalls, layoutCalls);
    }
}
