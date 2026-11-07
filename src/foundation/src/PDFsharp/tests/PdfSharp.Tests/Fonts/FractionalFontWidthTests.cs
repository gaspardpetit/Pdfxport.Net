// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using System.Globalization;
using FluentAssertions;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Quality;
using Xunit;

namespace PdfSharp.Tests.Fonts;

[Collection("PDFsharp")]
public class FractionalFontWidthTests : IDisposable
{
    public FractionalFontWidthTests()
    {
        PdfSharpCore.ResetAll();
        GlobalFontSettings.FontResolver = new FixtureFontResolver();
    }

    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(PdfFontEncoding.WinAnsi, PdfFontEmbedding.TryComputeSubset, "en-US")]
    [InlineData(PdfFontEncoding.Unicode, PdfFontEmbedding.TryComputeSubset, "en-US")]
    [InlineData(PdfFontEncoding.WinAnsi, PdfFontEmbedding.EmbedCompleteFontFile, "en-US")]
    [InlineData(PdfFontEncoding.Unicode, PdfFontEmbedding.EmbedCompleteFontFile, "en-US")]
    [InlineData(PdfFontEncoding.WinAnsi, PdfFontEmbedding.TryComputeSubset, "fr-FR")]
    [InlineData(PdfFontEncoding.Unicode, PdfFontEmbedding.TryComputeSubset, "fr-FR")]
    [InlineData(PdfFontEncoding.WinAnsi, PdfFontEmbedding.EmbedCompleteFontFile, "fr-FR")]
    [InlineData(PdfFontEncoding.Unicode, PdfFontEmbedding.EmbedCompleteFontFile, "fr-FR")]
    public void SavedWidthsMatchFontProgramAndMeasuredText(PdfFontEncoding encoding,
        PdfFontEmbedding embedding, string culture)
    {
        var savedCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CheckWidths(UnitTestFontResolver.ArialFont, "Wi €", new[] { 87, 105, 32, 128 },
                encoding, embedding, true);
        }
        finally
        {
            CultureInfo.CurrentCulture = savedCulture;
        }
    }

    [Theory]
    [InlineData(PdfFontEncoding.WinAnsi)]
    [InlineData(PdfFontEncoding.Unicode)]
    public void IntegralWidthsAndSharedLastHorizontalMetricArePreserved(PdfFontEncoding encoding)
    {
        CheckWidths("BlankGlyph", " ", new[] { 32 }, encoding,
            PdfFontEmbedding.TryComputeSubset, false);
    }

    [Theory]
    [InlineData(PdfFontEncoding.WinAnsi)]
    [InlineData(PdfFontEncoding.Unicode)]
    public void SymbolWidthsUseTheRemappedGlyphs(PdfFontEncoding encoding)
    {
        CheckWidths(UnitTestFontResolver.SymbolFont, "ABC", new[] { 65, 66, 67 }, encoding,
            PdfFontEmbedding.TryComputeSubset, false);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(127, 62)]
    [InlineData(128, 63)]
    [InlineData(129, 63)]
    [InlineData(65535, 32000)]
    public void IntegerWidthsRoundToNearestWithMidpointsAwayFromZero(ushort advance, int expected)
    {
        var font = new XFont(UnitTestFontResolver.ArialFont, 12);
        var descriptor = font.OpenTypeDescriptor;
        descriptor.UnitsPerEm.Should().Be(2048);
        var metric = descriptor.FontFace.hmtx.HorzMetrics[0];
        var original = metric.advanceWidth;
        try
        {
            // Exercise exact normalization boundaries independently of the fixture's glyph repertoire.
            metric.advanceWidth = advance;
            descriptor.GlyphIndexToPdfWidth(0).Should().Be(expected);
        }
        finally
        {
            metric.advanceWidth = original;
        }
    }

    static void CheckWidths(string family, string text, int[] ansiCodes, PdfFontEncoding encoding,
        PdfFontEmbedding embedding, bool expectFraction)
    {
        using var document = new PdfDocument();
        var font = new XFont(family, 12, XFontStyleEx.Regular, new XPdfFontOptions(encoding, embedding));
        var descriptor = font.OpenTypeDescriptor;
        var face = descriptor.FontFace;
        var page = document.AddPage();
        double measured;
        using (var graphics = XGraphics.FromPdfPage(page))
        {
            measured = graphics.MeasureString(text, font).Width;
            graphics.DrawString(text, font, XBrushes.Black, 50, 100);
        }

        using var stream = new MemoryStream();
        document.Save(stream, false);
        stream.Position = 0;
        using var restored = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        var resources = restored.Pages[0].Elements.GetDictionary("/Resources")!;
        var fonts = resources.Elements.GetDictionary("/Font")!;
        var pdfFont = fonts.Elements.GetDictionary(fonts.Elements.Keys.Single())!;
        double totalWidth = 0;
        bool hasFraction = false;
        for (var index = 0; index < text.Length; index++)
        {
            var character = descriptor.IsSymbolFont ? descriptor.RemapSymbolChar(text[index]) : text[index];
            var glyph = descriptor.BmpCodepointToGlyphIndex(character);
            var metric = Math.Min(glyph, face.hhea.numberOfHMetrics - 1);
            var expected = face.hmtx.HorzMetrics[metric].advanceWidth * 1000.0 / descriptor.UnitsPerEm;
            hasFraction |= Math.Abs(expected - Math.Truncate(expected)) > 0.001;
            // Symbol fonts use CID encoding even when WinAnsi was requested.
            var actual = ReadWidth(pdfFont, descriptor.IsSymbolFont ? PdfFontEncoding.Unicode : encoding,
                ansiCodes[index], glyph);
            actual.Should().BeApproximately(expected, 0.001);
            totalWidth += actual;
            // The integer API rounds independently of the fractional PDF metadata.
            descriptor.GlyphIndexToPdfWidth(glyph).Should().Be(
                (int)Math.Round(expected, MidpointRounding.AwayFromZero));
            if (encoding == PdfFontEncoding.WinAnsi && !descriptor.IsSymbolFont)
                descriptor.Widths[ansiCodes[index]].Should().Be(
                    (int)Math.Round(expected, MidpointRounding.AwayFromZero));
            if (family == "BlankGlyph")
            {
                descriptor.UnitsPerEm.Should().Be(1000);
                glyph.Should().BeGreaterOrEqualTo(face.hhea.numberOfHMetrics);
                actual.Should().Be(Math.Truncate(actual));
            }
        }
        if (expectFraction)
            hasFraction.Should().BeTrue("the regression must exercise non-integral normalized widths");
        (totalWidth * font.Size / 1000).Should().BeApproximately(measured, 0.0001);
    }

    static double ReadWidth(PdfDictionary font, PdfFontEncoding encoding, int ansiCode, ushort glyph)
    {
        if (encoding == PdfFontEncoding.WinAnsi)
        {
            font.Elements.GetName("/Subtype").Should().Be("/TrueType");
            var widths = font.Elements.GetArray("/Widths")!;
            widths.Elements.Count.Should().Be(224);
            return widths.Elements.GetReal(ansiCode - font.Elements.GetInteger("/FirstChar"));
        }

        font.Elements.GetName("/Subtype").Should().Be("/Type0");
        var descendant = font.Elements.GetArray("/DescendantFonts")!.Elements.GetDictionary(0)!;
        var entries = descendant.Elements.GetArray("/W")!;
        for (var index = 0; index < entries.Elements.Count; index += 2)
        {
            var first = entries.Elements.GetInteger(index);
            var widths = entries.Elements.GetArray(index + 1)!;
            if (glyph >= first && glyph < first + widths.Elements.Count)
                return widths.Elements.GetReal(glyph - first);
        }
        throw new InvalidOperationException("The emitted CID font must contain the used glyph width.");
    }

    sealed class FixtureFontResolver : IFontResolver
    {
        readonly UnitTestFontResolver _fallback = new();

        public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
            => familyName == "BlankGlyph" ? new FontResolverInfo("BlankGlyph")
                : _fallback.ResolveTypeface(familyName, bold, italic);

        public byte[]? GetFont(string faceName)
            => faceName == "BlankGlyph" ? File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,
                "Fonts", "Fixtures", "BlankGlyphFont.ttf")) : _fallback.GetFont(faceName);
    }
}
