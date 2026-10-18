// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

#if WPF
using System.IO;
#endif
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Quality;
using Xunit;

namespace PdfSharp.Tests.Fonts
{
    [Collection("PDFsharp")]
    public class FontEmbeddingTests : IDisposable
    {
        public FontEmbeddingTests()
        {
            GlobalFontSettings.ResetFontManagement();
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
        }

        public void Dispose()
        {
            GlobalFontSettings.ResetFontManagement();
        }

        [Fact]
        public void Embedded_font_descriptor_has_negative_descent()
        {
            var document = new PdfDocument();
            var page = document.AddPage();
            var font = new XFont(UnitTestFontResolver.ArialFont, 12);
            using (var graphics = XGraphics.FromPdfPage(page))
                graphics.DrawString("Hello", font, XBrushes.Black, 50, 100);

            using var stream = new MemoryStream();
            document.Save(stream, false);
            var pdf = Encoding.ASCII.GetString(stream.ToArray());
            var descent = Regex.Match(pdf, @"/Descent\s+(-?\d+)");

            descent.Success.Should().BeTrue();
            int.Parse(descent.Groups[1].Value).Should().BeLessThan(0);
        }

        [Fact]
        public void Supplementary_code_point_has_UTF16BE_ToUnicode_mapping()
        {
            var document = new PdfDocument();
            document.Options.CompressContentStreams = false;
            var page = document.AddPage();
            var font = new XFont(UnitTestFontResolver.EmojiFont, 12, XFontStyleEx.Regular,
                new XPdfFontOptions(PdfFontEncoding.Unicode));
            using (var graphics = XGraphics.FromPdfPage(page))
                graphics.DrawString("\ud83d\ude00", font, XBrushes.Black, 50, 100);

            using var stream = new MemoryStream();
            document.Save(stream, false);
            var pdf = Encoding.ASCII.GetString(stream.ToArray());

            pdf.Should().Contain("<D83DDE00>");
        }

        [Fact]
        public void Embed_font_subset()
        {
            var font1 = new XFont("Arial", 10, XFontStyleEx.Regular, new(PdfFontEncoding.WinAnsi, PdfFontEmbedding.TryComputeSubset));
            var font2 = new XFont("Arial", 10, XFontStyleEx.Regular, new(PdfFontEncoding.Unicode, PdfFontEmbedding.TryComputeSubset));

            var doc = new PdfDocument();
            var page = doc.AddPage();
            var gfx = XGraphics.FromPdfPage(page);

            gfx.DrawString("Hello, World!", font1, XBrushes.Black, 50, 100);
            gfx.DrawString("Hello, World!", font2, XBrushes.Black, 50, 150);

            gfx.DrawString("Köln† \u20AC € \u00a9©†   (Köln† \u20ac \u20ac \u00a9\u00a9†)", font1, XBrushes.Black, 50, 200);

            var fileName = PdfFileUtility.GetTempPdfFullFileName("PDFsharp/UnitTest/Fonts/FontEmbeddingTests/" + nameof(FontEmbeddingTests));
            doc.Save(fileName);
            var info = new FileInfo(fileName);
            var size = info.Length;

            size.Should().BeLessThan(35_000);
        }

        [Fact]
        public void Embed_font_complete_file()
        {
            var font = new XFont("Arial", 10, XFontStyleEx.Regular, new(PdfFontEmbedding.EmbedCompleteFontFile));
            var font1 = new XFont("Arial", 10, XFontStyleEx.Regular, new(PdfFontEncoding.WinAnsi, PdfFontEmbedding.EmbedCompleteFontFile));
            var font2 = new XFont("Arial", 10, XFontStyleEx.Regular, new(PdfFontEncoding.Unicode, PdfFontEmbedding.EmbedCompleteFontFile));

            var doc = new PdfDocument();
            var page = doc.AddPage();
            var gfx = XGraphics.FromPdfPage(page);

            gfx.DrawString("Hello, World!", font1, XBrushes.Black, 50, 100);
            gfx.DrawString("Hello, World!", font2, XBrushes.Black, 50, 150);
            gfx.DrawString("Köln† \u20AC € \u00a9©†   (Köln† \u20ac \u20ac \u00a9\u00a9†)", font1, XBrushes.Black, 50, 200);

            var fileName = PdfFileUtility.GetTempPdfFullFileName("PDFsharp/UnitTest/Fonts/FontEmbeddingTests/" + nameof(Embed_font_complete_file));
            doc.Save(fileName);
            var info = new FileInfo(fileName);
            var size = info.Length;

            size.Should().BeGreaterThan(500_000);
        }

        [Fact]
        public void Embed_Segoe_UI_Emoji_file()
        {
            //const int SmilingFaceWithHearts = 0x_0001_F970;  // 😍
            //const int RedRose = 0x_0001_F339;  // 🌹

            var fontEmoji = new XFont("Segoe UI Emoji", 10, XFontStyleEx.Regular, new(PdfFontEncoding.Unicode, PdfFontEmbedding.EmbedCompleteFontFile));

            var doc = new PdfDocument();
            var page = doc.AddPage();
            var gfx = XGraphics.FromPdfPage(page);

            gfx.DrawString("🌹 😍 \ud83c\udf39 \ud83d\ude0d", fontEmoji, XBrushes.Black, 50, 100);

            var fileName = PdfFileUtility.GetTempPdfFullFileName("PDFsharp/UnitTest/Fonts/FontEmbeddingTests/" + nameof(Embed_Segoe_UI_Emoji_file));
            doc.Save(fileName);
            var info = new FileInfo(fileName);
            var size = info.Length;
            var count = FontHelperTests.CountGlyphs(fontEmoji);

            size.Should().BeGreaterThan(1_000_000);
        }
    }
}
