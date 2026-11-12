// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.
using System.Globalization;
using FluentAssertions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
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
public class TextPairAdvanceTests : IDisposable
{
    public TextPairAdvanceTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    public static IEnumerable<object[]> Cases()
    {
        foreach (var alignment in new[] { ParagraphAlignment.Left, ParagraphAlignment.Center, ParagraphAlignment.Right })
        foreach (var scales in new[] { (1.0, 1.0), (.5, 2.0), (2.0, .5) })
        foreach (var script in new[] { "normal", "super", "sub" })
            yield return new object[] { alignment, scales.Item1, scales.Item2, script };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void PairAdjustmentsUsePointUnitsForFittingAndPaint(ParagraphAlignment alignment, double layout, double paint, string script)
    {
        var original = Render(new[] { .001, .001 }, alignment, layout, paint, script);
        var selected = Render(new[] { 2.0, -1.0 }, alignment, layout, paint, script);
        original.Text.Should().HaveCount(4);
        selected.Text.Should().HaveCount(4);
        var delta = 1 - .002;
        var origin = alignment == ParagraphAlignment.Center ? -delta / 2 : alignment == ParagraphAlignment.Right ? -delta : 0;
        var offsets = new[] { origin, origin + 2 - .001, origin + delta, origin + delta };
        for (var index = 0; index < 4; index++)
        {
            selected.Text[index].X.Should().BeApproximately(original.Text[index].X + offsets[index], .003);
            selected.Text[index].Y.Should().BeApproximately(original.Text[index].Y, .003);
        }
    }

    [Fact]
    public void PositiveAdjustmentMovesWholeWordToNextLine()
    {
        Wrap(null, -1).Should().BeTrue();
        Wrap(2, -1).Should().BeFalse();
    }
    [Fact]
    public void NegativeAdjustmentFitsWholeWordOnCurrentLine()
    {
        Wrap(null, .7).Should().BeFalse();
        Wrap(-1.4, .7).Should().BeTrue();
    }

    [Theory]
    [InlineData("zero")]
    [InlineData("length")]
    [InlineData("nan")]
    [InlineData("positiveInfinity")]
    [InlineData("negativeInfinity")]
    [InlineData("negativeWidth")]
    public void InvalidOrZeroSelectionsPreserveOutput(string kind)
    {
        var values = kind switch
        {
            "zero" => new[] { 0.0, 0.0 },
            "length" => new[] { 1.0 },
            "nan" => new[] { double.NaN, 1 },
            "positiveInfinity" => new[] { double.PositiveInfinity, 1 },
            "negativeInfinity" => new[] { double.NegativeInfinity, 1 },
            _ => new[] { -1000.0, 0 },
        };
        Render(values).Content.Should().Be(Render(null).Content);
    }

    [Fact]
    public void SurrogateInternalSlotIsIgnoredWithoutMutatingSelection()
    {
        var values = new[] { 1.0, 1000, 2 };
        var selected = Render(values, word: "A\U0001F600C");
        var original = Render(new[] { .001, 0.0, .001 }, word: "A\U0001F600C");
        values.Should().Equal(1, 1000, 2);
        selected.Text.Should().HaveCount(4);
        selected.Text[1].X.Should().BeApproximately(original.Text[1].X + .999, .003);
        selected.Text[2].X.Should().BeApproximately(original.Text[2].X + 2.998, .003);
        selected.Text[3].X.Should().BeApproximately(original.Text[3].X + 2.998, .003);
    }

    [Fact]
    public void TextEventsReceiveWholeRun()
    {
        var seen = new List<string>();
        var resolved = new List<int[]>();
        var selected = Render(new[] { 1.0, 2 }, configure: renderer =>
        {
            renderer.PdfDocument.RenderEvents.PrepareTextEvent += (_, args) =>
            {
                seen.Add(args.Text);
                if (args.Text == "ABC") args.Text = "XYZ";
            };
            renderer.PdfDocument.RenderEvents.RenderTextEvent += (_, args) =>
                resolved.Add(args.CodePointGlyphIndexPairs.Select(pair => pair.CodePoint).ToArray());
        });
        seen.Should().Contain("ABC");
        resolved.Should().Contain(points => points.SequenceEqual(new[] { 88, 89, 90 }));
        selected.Text.Should().HaveCount(4);
    }

    [Fact]
    public void GlyphCountChangingTextEventsAreRejectedExplicitly()
    {
        Action draw = () => Render(new[] { 1.0, 2 }, configure: renderer =>
            renderer.PdfDocument.RenderEvents.PrepareTextEvent += (_, args) =>
            {
                if (args.Text == "ABC") args.Text = "X";
            });
        draw.Should().Throw<NotSupportedException>();
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(-1, -2)]
    public void PairAdvancesComposeWithTrailingSelectors(double trailing, double renderTrailing)
    {
        var original = Render(new[] { .001, .001 }, trailing: trailing, renderTrailing: renderTrailing);
        var selected = Render(new[] { 2.0, -1 }, trailing: trailing, renderTrailing: renderTrailing);
        selected.Text[2].X.Should().BeApproximately(original.Text[2].X + .998, .003);
        selected.Text[3].X.Should().BeApproximately(original.Text[3].X + .998, .003);
    }

    [Theory]
    [InlineData(false, 1, 1)]
    [InlineData(false, .5, 2)]
    [InlineData(false, 2, .5)]
    [InlineData(true, 1, 1)]
    [InlineData(true, .5, 2)]
    [InlineData(true, 2, .5)]
    public void SubstitutionsUseResolvedNaturalAdvances(bool renderEvent, double layout, double paint)
    {
        var values = new[] { 1.0, 2 };
        var expected = Render(values, word: "WWW", layout: layout, paint: paint);
        var actual = Render(values, layout: layout, paint: paint, configure: renderer =>
        {
            if (!renderEvent)
                renderer.PdfDocument.RenderEvents.PrepareTextEvent += (_, args) =>
                {
                    if (args.Text == "ABC") args.Text = "WWW";
                };
            else
                renderer.PdfDocument.RenderEvents.RenderTextEvent += (_, args) =>
                {
                    if (args.CodePointGlyphIndexPairs.Select(pair => pair.CodePoint).SequenceEqual(new[] { 65, 66, 67 }))
                    {
                        for (var index = 0; index < 3; index++) args.CodePointGlyphIndexPairs[index].CodePoint = 87;
                        args.ReevaluateGlyphIndices = true;
                    }
                };
        });
        actual.Text.Should().HaveCount(expected.Text.Count);
        for (var index = 0; index < expected.Text.Count; index++)
            actual.Text[index].X.Should().BeApproximately(expected.Text[index].X, .003);
    }

    [Theory]
    [InlineData(1, -2)]
    [InlineData(1, 2)]
    [InlineData(.5, -2)]
    [InlineData(.5, 2)]
    [InlineData(2, -2)]
    [InlineData(2, 2)]
    public void DecimalTabIncludesAdvancesUpToSeparator(double layout, double adjustment)
    {
        var document = new Document();
        document.Culture = CultureInfo.GetCultureInfo("en-US");
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(20);
        section.PageSetup.PageWidth = Unit.FromPoint(500);
        var paragraph = section.AddParagraph();
        paragraph.Format.TabStops.AddTabStop(Unit.FromPoint(100), TabAlignment.Decimal);
        paragraph.AddTab();
        paragraph.AddText("12.34").Tag = "number";
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.TextPairAdvances = (_, text, _) =>
        {
            text.Should().Be("12.34");
            return new[] { adjustment, adjustment / 2, 3.0, 4.0 };
        };
        renderer.DocumentRenderer.TextHorizontalScale = _ => layout;
        renderer.DocumentRenderer.TextPaintHorizontalScale = (_, _) => 1.5;
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var origins = ReadOrigins(pdf);
        origins.Should().HaveCount(5);
        origins[2].X.Should().BeApproximately(120, .003);
    }

    static bool Wrap(double? adjustment, double overrun)
    {
        using var gfx = XGraphics.CreateMeasureContext(new XSize(300, 300), XGraphicsUnit.Point, XPageDirection.Downwards);
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(20);
        section.PageSetup.PageWidth = Unit.FromPoint(40 + gfx.MeasureString("Alpha Bravo", new XFont("Arial", 12)).Width - overrun);
        var paragraph = section.AddParagraph();
        paragraph.AddText("Alpha ");
        paragraph.AddText("Bravo").Tag = "target";
        var renderer = new PdfDocumentRenderer { Document = document };
        if (adjustment.HasValue)
            renderer.DocumentRenderer.TextPairAdvances = (tag, word, _) => Equals(tag, "target") ? new[] { 0.0, adjustment.Value, 0, 0 } : null;
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var positions = ReadOrigins(pdf);
        return positions[^1].Y == positions[0].Y;
    }

    static Result Render(double[]? values, ParagraphAlignment alignment = ParagraphAlignment.Left,
        double layout = 1, double paint = 1, string script = "normal", string word = "ABC",
        double trailing = 0, double renderTrailing = 0, Action<PdfDocumentRenderer>? configure = null)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = 12;
        var section = document.AddSection();
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(20);
        section.PageSetup.PageWidth = Unit.FromPoint(400);
        var paragraph = section.AddParagraph();
        paragraph.Format.Alignment = alignment;
        var run = paragraph.AddFormattedText();
        run.Font.Superscript = script == "super";
        run.Font.Subscript = script == "sub";
        run.AddText(word).Tag = "target";
        paragraph.AddText("D");
        var renderer = new PdfDocumentRenderer { Document = document };
        renderer.DocumentRenderer.TextPairAdvances = (tag, text, font) =>
        {
            if (!Equals(tag, "target")) return null;
            text.Should().Be(word);
            font.Size.Should().Be(12);
            return values;
        };
        renderer.DocumentRenderer.TextHorizontalScale = tag => Equals(tag, "target") ? layout : null;
        renderer.DocumentRenderer.TextPaintHorizontalScale = (tag, _) => Equals(tag, "target") ? paint : null;
        renderer.DocumentRenderer.TrailingGlyphKerningWithTextTag = (_, tag, _, _) => Equals(tag, "target") ? trailing : null;
        renderer.DocumentRenderer.RenderOnlyTrailingGlyphKerning = (_, text, _) => text == word ? renderTrailing : null;
        configure?.Invoke(renderer);
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        return new Result(ReadOrigins(pdf), PdfFileHelper.GetPageContentStream(pdf, 0));
    }

    // Inspect emitted PDF operators rather than the legacy Tj-only text reader.
    static List<(string Text, double X, double Y)> ReadOrigins(PdfDocument document)
    {
        using var stream = new MemoryStream();
        document.Save(stream, false);
        stream.Position = 0;
        using var restored = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        var page = restored.Pages[0];
        var fonts = page.Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!;
        PdfDictionary? font = null;
        var size = 0.0;
        var lineX = 0.0;
        var lineY = 0.0;
        var transform = (Sx: 1.0, Sy: 1.0, X: 0.0, Y: 0.0);
        var stack = new Stack<(double Sx, double Sy, double X, double Y)>();
        var result = new List<(string Text, double X, double Y)>();
        foreach (var op in ContentReader.ReadContent(page).OfType<COperator>())
        {
            switch (op.OpCode.Name)
            {
                case "q": stack.Push(transform); break;
                case "Q": transform = stack.Pop(); break;
                case "cm":
                    Number(op.Operands[1]).Should().Be(0);
                    Number(op.Operands[2]).Should().Be(0);
                    transform = (transform.Sx * Number(op.Operands[0]), transform.Sy * Number(op.Operands[3]),
                        transform.X + transform.Sx * Number(op.Operands[4]), transform.Y + transform.Sy * Number(op.Operands[5]));
                    break;
                case "BT": lineX = lineY = 0; break;
                case "Tf": font = fonts.Elements.GetDictionary(op.Operands[0].ToString()!)!; size = Number(op.Operands[1]); break;
                case "Td": lineX += Number(op.Operands[0]); lineY += Number(op.Operands[1]); break;
                case "Tj": result.Add(("run", transform.X + transform.Sx * lineX, transform.Y + transform.Sy * lineY)); break;
                case "TJ":
                    var x = lineX;
                    foreach (var item in (CArray)op.Operands[0])
                    {
                        if (item is CString text)
                        {
                            var cid = font!.Elements.GetName("/Subtype") == "/Type0";
                            for (var index = 0; index < text.Value.Length; index += cid ? 2 : 1)
                            {
                                var code = cid ? text.Value[index] * 256 + text.Value[index + 1] : text.Value[index];
                                result.Add(("glyph", transform.X + transform.Sx * x, transform.Y + transform.Sy * lineY));
                                x += Width(font, code, cid) * size / 1000;
                            }
                        }
                        else x -= Number(item) * size / 1000;
                    }
                    break;
            }
        }
        return result;
    }
    static double Number(CObject value) => double.Parse(value.ToString()!, CultureInfo.InvariantCulture);
    static double Width(PdfDictionary font, int code, bool cid)
    {
        if (!cid) return font.Elements.GetArray("/Widths")!.Elements.GetReal(code - font.Elements.GetInteger("/FirstChar"));
        var descendant = font.Elements.GetArray("/DescendantFonts")!.Elements.GetDictionary(0)!;
        var entries = descendant.Elements.GetArray("/W")!;
        for (var index = 0; index < entries.Elements.Count; index += 2)
        {
            var first = entries.Elements.GetInteger(index);
            var widths = entries.Elements.GetArray(index + 1)!;
            if (code >= first && code < first + widths.Elements.Count) return widths.Elements.GetReal(code - first);
        }
        return descendant.Elements.ContainsKey("/DW") ? descendant.Elements.GetReal("/DW") : 1000;
    }
    record Result(List<(string Text, double X, double Y)> Text, string Content);
}
