// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using System.Globalization;
using FluentAssertions;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Quality;
using Xunit;

namespace PdfSharp.Tests.Drawing;

[Collection("PDFsharp")]
public class PositionedGlyphTests : IDisposable
{
    public PositionedGlyphTests()
    {
        PdfSharpCore.ResetAll();
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(PdfFontEncoding.WinAnsi, 12, "en-US")]
    [InlineData(PdfFontEncoding.Unicode, 12, "en-US")]
    [InlineData(PdfFontEncoding.WinAnsi, 24, "fr-FR")]
    [InlineData(PdfFontEncoding.Unicode, 24, "fr-FR")]
    public void SavedPdfReconstructsRequestedOriginsAndDecorationWidth(PdfFontEncoding encoding, double size, string culture)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            using var document = new PdfDocument();
            var page = document.AddPage();
            var font = new XFont("Arial", size, XFontStyleEx.Underline, new XPdfFontOptions(encoding));
            var advances = new[] { 3.25, 0.0, -2.0 };
            using (var graphics = XGraphics.FromPdfPage(page))
                graphics.DrawStringWithGlyphAdvances("Wi €", font, XBrushes.Black, 72, 100, advances);
            using var stream = new MemoryStream();
            document.Save(stream, false);
            stream.Position = 0;
            using var restored = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
            var operators = ContentReader.ReadContent(restored.Pages[0]).OfType<COperator>().ToArray();
            var array = (CArray)operators.Single(op => op.OpCode.Name == "TJ").Operands[0];
            array.Should().HaveCount(7);
            var fonts = restored.Pages[0].Elements.GetDictionary("/Resources")!.Elements.GetDictionary("/Font")!;
            var pdfFont = fonts.Elements.GetDictionary(fonts.Elements.Keys.Single())!;
            var codes = new[] { 87, 105, 32, 128 };
            var text = "Wi €";
            var widths = new double[4];
            for (int index = 0; index < 4; index++)
            {
                var glyph = font.OpenTypeDescriptor.BmpCodepointToGlyphIndex(text[index]);
                widths[index] = ReadWidth(pdfFont, encoding, codes[index], glyph) * size / 1000;
                if (index < 3)
                {
                    var adjustment = Number(array[index * 2 + 1]);
                    (widths[index] - adjustment * size / 1000).Should().BeApproximately(advances[index], 0.00001);
                }
            }
            var underline = operators.Single(op => op.OpCode.Name == "re");
            Number(underline.Operands[2]).Should().BeApproximately(advances.Sum() + widths[3], 0.001);
        }
        finally { CultureInfo.CurrentCulture = originalCulture; }
    }

    [Fact]
    public void SupplementaryScalarsAndSymbolGlyphsUseCidStrings()
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        var emoji = new XFont(UnitTestFontResolver.EmojiFont, 12, XFontStyleEx.Regular,
            new XPdfFontOptions(PdfFontEncoding.Unicode));
        var symbol = new XFont(UnitTestFontResolver.SymbolFont, 12);
        using (var graphics = XGraphics.FromPdfPage(page))
        {
            graphics.DrawStringWithGlyphAdvances("\U0001F600A", emoji, XBrushes.Black, 72, 100, new[] { 12.0 });
            graphics.DrawStringWithGlyphAdvances("AB", symbol, XBrushes.Black, 72, 120, new[] { 6.0 });
        }
        var runs = ContentReader.ReadContent(page).OfType<COperator>().Where(op => op.OpCode.Name == "TJ").ToArray();
        runs.Should().HaveCount(2);
        foreach (var run in runs)
            ((CArray)run.Operands[0]).Should().HaveCount(3);
        var symbolArray = (CArray)runs[1].Operands[0];
        var expectedGlyph = symbol.OpenTypeDescriptor.BmpCodepointToGlyphIndex(symbol.OpenTypeDescriptor.RemapSymbolChar('A'));
        var value = ((CString)symbolArray[0]).Value;
        ((value[0] << 8) | value[1]).Should().Be(expectedGlyph);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void WrongAdvanceCountDoesNotPreventSubsequentDrawing(int count)
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        var font = new XFont("Arial", 12);
        using (var graphics = XGraphics.FromPdfPage(page))
        {
            Action draw = () => graphics.DrawStringWithGlyphAdvances("AB", font, XBrushes.Black, 72, 100, new double[count]);
            draw.Should().Throw<ArgumentException>().WithParameterName("advances");
            graphics.DrawString("OK", font, XBrushes.Black, 72, 100);
        }
        ContentReader.ReadContent(page).OfType<COperator>().Count(op => op.OpCode.Name == "Tj").Should().Be(1);
        ContentReader.ReadContent(page).OfType<COperator>().Should().NotContain(op => op.OpCode.Name == "TJ");
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonfiniteAdvancesAreRejected(double advance)
    {
        using var document = new PdfDocument();
        using var graphics = XGraphics.FromPdfPage(document.AddPage());
        Action draw = () => graphics.DrawStringWithGlyphAdvances("AB", new XFont("Arial", 12), XBrushes.Black, 0, 0, new[] { advance });
        draw.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("advances");
    }

    [Fact]
    public void EmptySingleScalarAndRendererRequirementsAreExplicit()
    {
        var font = new XFont("Arial", 12);
        using var measure = XGraphics.CreateMeasureContext(new XSize(100, 100), XGraphicsUnit.Point, XPageDirection.Downwards);
        measure.DrawStringWithGlyphAdvances("", font, XBrushes.Black, 0, 0, Array.Empty<double>());
        Action noRenderer = () => measure.DrawStringWithGlyphAdvances("A", font, XBrushes.Black, 0, 0, Array.Empty<double>());
        noRenderer.Should().Throw<InvalidOperationException>();
        using var document = new PdfDocument();
        var page = document.AddPage();
        using (var graphics = XGraphics.FromPdfPage(page))
            graphics.DrawStringWithGlyphAdvances("A", font, XBrushes.Black, 0, 0, Array.Empty<double>());
        ((CArray)ContentReader.ReadContent(page).OfType<COperator>().Single(op => op.OpCode.Name == "TJ").Operands[0]).Should().HaveCount(1);
    }

    [Fact]
    public void InvalidSurrogatesAndNullArgumentsAreRejected()
    {
        using var document = new PdfDocument();
        using var graphics = XGraphics.FromPdfPage(document.AddPage());
        var font = new XFont("Arial", 12);
        Action text = () => graphics.DrawStringWithGlyphAdvances(null!, font, XBrushes.Black, 0, 0, Array.Empty<double>());
        text.Should().Throw<ArgumentNullException>().WithParameterName("text");
        Action noFont = () => graphics.DrawStringWithGlyphAdvances("A", null!, XBrushes.Black, 0, 0, Array.Empty<double>());
        noFont.Should().Throw<ArgumentNullException>().WithParameterName("font");
        Action brush = () => graphics.DrawStringWithGlyphAdvances("A", font, null!, 0, 0, Array.Empty<double>());
        brush.Should().Throw<ArgumentNullException>().WithParameterName("brush");
        Action advances = () => graphics.DrawStringWithGlyphAdvances("A", font, XBrushes.Black, 0, 0, null!);
        advances.Should().Throw<ArgumentNullException>().WithParameterName("advances");
        foreach (var invalid in new[] { "\uD800", "\uDC00", "\uD800A" })
        {
            Action draw = () => graphics.DrawStringWithGlyphAdvances(invalid, font, XBrushes.Black, 0, 0, Array.Empty<double>());
            draw.Should().Throw<ArgumentException>().WithParameterName("text");
        }
    }

    [Fact]
    public void ItalicSimulationCanSwitchBetweenPositionedAndOrdinaryRuns()
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        var regular = new XFont("Arial", 12);
        var italic = new XFont("Arial", 12, XFontStyleEx.Italic, XPdfFontOptions.AutomaticEncoding, XStyleSimulations.ItalicSimulation);
        using (var graphics = XGraphics.FromPdfPage(page))
        {
            var y = 100;
            foreach (var font in new[] { italic, italic, regular, regular })
                graphics.DrawStringWithGlyphAdvances("AB", font, XBrushes.Black, 72, y += 20, new[] { 8.0 });
            graphics.DrawString("End", regular, XBrushes.Black, 72, y + 20);
        }
        var operators = ContentReader.ReadContent(page).OfType<COperator>().ToArray();
        operators.Count(op => op.OpCode.Name == "TJ").Should().Be(4);
        operators.Count(op => op.OpCode.Name == "Tj").Should().Be(1);
    }

    [Fact]
    public void PrepareTextCountChangesAreRejectedBeforeDrawing()
    {
        using var document = new PdfDocument();
        document.RenderEvents.PrepareTextEvent += (_, args) => args.Text = "A";
        var page = document.AddPage();
        using (var graphics = XGraphics.FromPdfPage(page))
        {
            Action draw = () => graphics.DrawStringWithGlyphAdvances("AB", new XFont("Arial", 12), XBrushes.Black, 0, 0, new[] { 8.0 });
            draw.Should().Throw<NotSupportedException>();
        }
        ContentReader.ReadContent(page).OfType<COperator>().Should().NotContain(op => op.OpCode.Name == "TJ");
    }

    [Fact]
    public void GlyphReplacementUsesCidEncodingAndCallerAdvancesAreSnapshotted()
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        var font = new XFont("Arial", 12, XFontStyleEx.Regular, XPdfFontOptions.WinAnsiDefault);
        var replacement = font.OpenTypeDescriptor.BmpCodepointToGlyphIndex('B');
        var advances = new[] { 7.0 };
        document.RenderEvents.RenderTextEvent += (_, args) =>
        {
            args.CodePointGlyphIndexPairs[0].GlyphIndex = replacement;
            advances[0] = 999; // Caller mutation during an event must not change this draw.
        };
        using (var graphics = XGraphics.FromPdfPage(page))
            graphics.DrawStringWithGlyphAdvances("AA", font, XBrushes.Black, 72, 100, advances);
        var array = (CArray)ContentReader.ReadContent(page).OfType<COperator>().Single(op => op.OpCode.Name == "TJ").Operands[0];
        var value = ((CString)array[0]).Value;
        ((value[0] << 8) | value[1]).Should().Be(replacement);
        (font.OpenTypeDescriptor.GlyphIndexToEmWidth(replacement, 12) - Number(array[1]) * 12 / 1000)
            .Should().BeApproximately(7, 0.00001);
    }

    [Fact]
    public void RenderTextGlyphCountChangesAreRejected()
    {
        using var document = new PdfDocument();
        document.RenderEvents.RenderTextEvent += (_, args) =>
            args.CodePointGlyphIndexPairs = args.CodePointGlyphIndexPairs.Take(1).ToArray();
        using var graphics = XGraphics.FromPdfPage(document.AddPage());
        Action draw = () => graphics.DrawStringWithGlyphAdvances("AB", new XFont("Arial", 12), XBrushes.Black, 0, 0, new[] { 8.0 });
        draw.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void ColorGlyphLayersAreRejectedWithoutPaintingAPartialRun()
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        var emoji = new XFont(UnitTestFontResolver.EmojiFont, 12, XFontStyleEx.Regular,
            new XPdfFontOptions(PdfFontEncoding.Unicode, PdfFontColoredGlyphs.Version0));
        using (var graphics = XGraphics.FromPdfPage(page))
        {
            Action draw = () => graphics.DrawStringWithGlyphAdvances("\U0001F600A", emoji, XBrushes.Black, 72, 100, new[] { 12.0 });
            draw.Should().Throw<NotSupportedException>();
            graphics.DrawString("OK", new XFont("Arial", 12), XBrushes.Black, 72, 100);
        }
        var operators = ContentReader.ReadContent(page).OfType<COperator>().ToArray();
        operators.Should().NotContain(op => op.OpCode.Name == "TJ");
        operators.Count(op => op.OpCode.Name == "Tj").Should().Be(1);
    }

    static double Number(CObject value) => double.Parse(value.ToString()!, CultureInfo.InvariantCulture);

    static double ReadWidth(PdfDictionary font, PdfFontEncoding encoding, int ansiCode, ushort glyph)
    {
        if (encoding == PdfFontEncoding.WinAnsi)
            return font.Elements.GetArray("/Widths")!.Elements.GetReal(ansiCode - font.Elements.GetInteger("/FirstChar"));
        var descendant = font.Elements.GetArray("/DescendantFonts")!.Elements.GetDictionary(0)!;
        var entries = descendant.Elements.GetArray("/W")!;
        for (int index = 0; index < entries.Elements.Count; index += 2)
        {
            var first = entries.Elements.GetInteger(index);
            var widths = entries.Elements.GetArray(index + 1)!;
            if (glyph >= first && glyph < first + widths.Elements.Count)
                return widths.Elements.GetReal(glyph - first);
        }
        throw new InvalidOperationException("Used glyph must have an emitted width.");
    }
}
