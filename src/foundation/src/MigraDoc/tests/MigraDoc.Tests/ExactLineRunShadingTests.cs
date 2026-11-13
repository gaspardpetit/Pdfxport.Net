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
public class ExactLineRunShadingTests : IDisposable
{
    public ExactLineRunShadingTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(ParagraphAlignment.Left, 12)]
    [InlineData(ParagraphAlignment.Center, 12)]
    [InlineData(ParagraphAlignment.Right, 12)]
    [InlineData(ParagraphAlignment.Justify, 12)]
    [InlineData(ParagraphAlignment.Left, 30)]
    public void BackgroundUsesAcceptedLineBoxAndKeepsTextUnchanged(ParagraphAlignment alignment, double size)
    {
        var plain = Render(null, alignment: alignment, fontSize: size);
        var shaded = Render(_ => XColors.Red, alignment: alignment, fontSize: size);
        AssertText(shaded, plain);
        shaded.Rects.Should().HaveCount(4); // Alpha, rendered blank, Bravo, Charlie; line break excluded.
        foreach (var rect in shaded.Rects)
        {
            rect.Height.Should().BeApproximately(20, 0.001);
            rect.Color.Should().Be((1.0, 0.0, 0.0));
        }
        shaded.Rects[0].X.Should().BeApproximately(shaded.Text[0].X, 0.001);
        shaded.Rects[0].Width.Should().BeApproximately(Width("Alpha", size), 0.001);
        shaded.Rects[1].X.Should().BeApproximately(shaded.Rects[0].X + shaded.Rects[0].Width, 0.001);
        shaded.Rects[1].Width.Should().BeApproximately(
            shaded.Text[1].X - shaded.Text[0].X - shaded.Rects[0].Width, 0.001);
        shaded.Rects[2].X.Should().BeApproximately(shaded.Text[1].X, 0.001);
        Math.Abs(shaded.Rects[3].Y - shaded.Rects[0].Y).Should().BeApproximately(20, 0.001);
    }

    [Theory]
    [InlineData(0.8, 1.0)]
    [InlineData(1.2, 0.9)]
    public void BackgroundFollowsLayoutAdvanceRatherThanGlyphPaintScale(double layout, double paint)
    {
        var shaded = Render(_ => XColors.Red, layout: layout, paint: paint);
        AssertText(shaded, Render(null, layout: layout, paint: paint));
        shaded.Rects[0].Width.Should().BeApproximately(Width("Alpha", 12) * layout, 0.001);
    }

    [Theory]
    [InlineData(LineSpacingRule.Single)]
    [InlineData(LineSpacingRule.AtLeast)]
    [InlineData(LineSpacingRule.Multiple)]
    public void NonexactLinesDoNotInvokeSelector(LineSpacingRule rule)
    {
        var calls = 0;
        var selected = Render(_ => { calls++; return XColors.Red; }, rule: rule);
        calls.Should().Be(0);
        selected.Rects.Should().BeEmpty();
        selected.Content.Should().Be(Render(null, rule: rule).Content);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullOrEmptyColorKeepsDefaultStream(bool empty)
    {
        var selected = Render(_ => empty ? XColor.Empty : null);
        selected.Rects.Should().BeEmpty();
        selected.Content.Should().Be(Render(null).Content);
    }

    [Fact]
    public void SelectorReceivesTagsOnlyDuringRendering()
    {
        var seen = new List<object?>();
        var result = Render(tag => { seen.Add(tag); return Equals(tag, "Bravo") ? XColors.Red : null; });
        seen.Should().Equal("Alpha", "blank", "Bravo", "Charlie");
        result.Rects.Should().ContainSingle();
        result.Rects[0].X.Should().BeApproximately(result.Text[1].X, 0.001);
    }

    [Fact]
    public void FieldsAndControlCharactersAreNotSelected()
    {
        var seen = 0;
        var result = Render(_ => { seen++; return XColors.Red; }, controls: true);
        seen.Should().Be(0);
        result.Rects.Should().BeEmpty();
        result.Content.Should().Be(Render(null, controls: true).Content);
    }

    [Fact]
    public void PageContinuationsUseTheirAcceptedLineBoxes()
    {
        var plain = Render(null, manyPages: true);
        var shaded = Render(_ => XColors.Red, manyPages: true);
        shaded.PageCount.Should().Be(plain.PageCount).And.BeGreaterThan(1);
        AssertText(shaded, plain);
        shaded.Rects.Should().HaveCount(28);
        foreach (var rectangle in shaded.Rects)
            rectangle.Height.Should().BeApproximately(20, 0.001);
    }

    static void AssertText(Result actual, Result expected)
    {
        actual.Text.Should().HaveCount(expected.Text.Count);
        for (var index = 0; index < actual.Text.Count; index++)
        {
            var a = actual.Text[index];
            var b = expected.Text[index];
            a.Text.Should().Be(b.Text);
            a.X.Should().BeApproximately(b.X, 0.001);
            a.Y.Should().BeApproximately(b.Y, 0.001);
            a.Color.Should().Be(b.Color);
        }
    }
    static double Width(string text, double size)
    {
        using var gfx = XGraphics.CreateMeasureContext(new XSize(300, 300), XGraphicsUnit.Point, XPageDirection.Downwards);
        return gfx.MeasureString(text, new XFont("Arial", size)).Width;
    }
    record Paint(string Text, double X, double Y, (double R, double G, double B) Color);
    record Rect(double X, double Y, double Width, double Height, (double R, double G, double B) Color);
    record Result(string Content, List<Paint> Text, List<Rect> Rects, int PageCount);
    static Result Render(Func<object?, XColor?>? selector, ParagraphAlignment alignment = ParagraphAlignment.Left,
        double fontSize = 12, double layout = 1, double paint = 1, LineSpacingRule rule = LineSpacingRule.Exactly, bool controls = false, bool manyPages = false)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = fontSize;
        var section = document.AddSection();
        section.PageSetup.LeftMargin = Unit.FromPoint(20);
        if (manyPages)
        {
            section.PageSetup.PageHeight = Unit.FromPoint(120);
            section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromPoint(20);
        }
        var paragraph = section.AddParagraph();
        paragraph.Format.Alignment = alignment;
        paragraph.Format.LineSpacingRule = rule;
        paragraph.Format.LineSpacing = rule == LineSpacingRule.Multiple ? 1.5 : Unit.FromPoint(20);
        if (controls)
        {
            paragraph.AddPageField();
            paragraph.AddTab();
            paragraph.AddCharacter(SymbolName.Em);
        }
        else
        {
            paragraph.AddText("Alpha").Tag = "Alpha";
            paragraph.AddText(" ").Tag = "blank";
            paragraph.AddText("Bravo").Tag = "Bravo";
            paragraph.AddLineBreak();
            paragraph.AddText("Charlie").Tag = "Charlie";
            if (manyPages)
                for (var line = 0; line < 24; line++)
                {
                    paragraph.AddLineBreak();
                    paragraph.AddText("Alpha").Tag = "Alpha";
                }
        }
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.TextHorizontalScale = tag => Equals(tag, "Alpha") ? layout : null;
        renderer.DocumentRenderer.TextPaintHorizontalScale = (tag, _) => Equals(tag, "Alpha") ? paint : null;
        var calls = 0;
        renderer.DocumentRenderer.ExactLineRunShading = selector == null ? null : tag => { calls++; return selector(tag); };
        renderer.DocumentRenderer.PrepareDocument();
        calls.Should().Be(0);
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var text = new List<Paint>();
        var rectangles = new List<Rect>();
        var sx = 1.0; var sy = 1.0; var tx = 0.0; var ty = 0.0;
        var x = 0.0; var y = 0.0;
        var color = (R: 0.0, G: 0.0, B: 0.0);
        var stack = new Stack<(double Sx, double Sy, double Tx, double Ty, (double R, double G, double B) Color)>();
        foreach (var page in Enumerable.Range(0, pdf.PageCount))
        {
            sx = sy = 1; tx = ty = x = y = 0; color = (0, 0, 0); stack.Clear();
            foreach (var op in ContentReader.ReadContent(pdf.Pages[page]).OfType<COperator>())
            {
                double N(int index) => double.Parse(op.Operands[index].ToString()!, CultureInfo.InvariantCulture);
                switch (op.OpCode.Name)
                {
                    case "q": stack.Push((sx, sy, tx, ty, color)); break;
                    case "Q": (sx, sy, tx, ty, color) = stack.Pop(); break;
                    case "cm":
                        N(1).Should().Be(0); N(2).Should().Be(0);
                        tx += sx * N(4); ty += sy * N(5); sx *= N(0); sy *= N(3); break;
                    case "g": color = (N(0), N(0), N(0)); break;
                    case "rg": color = (N(0), N(1), N(2)); break;
                    case "BT": x = y = 0; break;
                    case "Td": x += N(0); y += N(1); break;
                    case "Tj": text.Add(new Paint(((CString)op.Operands[0]).Value, tx + sx * x, ty + sy * y, color)); break;
                    case "TJ": text.Add(new Paint(op.Operands[0].ToString()!, tx + sx * x, ty + sy * y, color)); break;
                    case "re": rectangles.Add(new Rect(tx + sx * N(0), ty + sy * N(1), Math.Abs(sx * N(2)), Math.Abs(sy * N(3)), color)); break;
                }
            }
        }
        return new Result(string.Concat(Enumerable.Range(0, pdf.PageCount)
            .Select(page => PdfFileHelper.GetPageContentStream(pdf, page))), text, rectangles, pdf.PageCount);
    }
}
