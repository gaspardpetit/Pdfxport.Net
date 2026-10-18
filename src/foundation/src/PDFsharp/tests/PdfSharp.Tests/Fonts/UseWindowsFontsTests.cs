// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Fonts.Internal;
using PdfSharp.Pdf;
using PdfSharp.WPFonts;
using System.Buffers.Binary;
using PdfSharp.Snippets.Font;
using System.Runtime.InteropServices;
using Xunit;

namespace PdfSharp.Tests.Fonts
{
    [Collection("PDFsharp")]
    public class UseWindowsFontsTests : IDisposable
    {
        public UseWindowsFontsTests()
        {
            GlobalFontSettings.ResetFontManagement();
        }

        public void Dispose()
        {
            GlobalFontSettings.ResetFontManagement();
        }

        [Fact]
        public void UseWindowsFonts_not_set()
        {
            GlobalFontSettings.ResetFontManagement();
#if CORE
            // Core build fails on any platform.

            // Create a font.
            var fontCreator = () => new XFont("Times New Roman", 20, XFontStyleEx.BoldItalic);
            fontCreator.Should().Throw<InvalidOperationException>();

#elif GDI || WPF
            // Create a font.
            var font = new XFont("Times New Roman", 20, XFontStyleEx.BoldItalic);
            font.Should().NotBe(null);
#else
#error
#endif
        }

        [Fact]
        public void UseWindowsFontsUnderWindows_set()
        {
            GlobalFontSettings.ResetFontManagement();
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
#if CORE
            if (Capabilities.OperatingSystem.IsWindows)
            {
                // Create a font.
                var font = new XFont("Times New Roman", 20, XFontStyleEx.BoldItalic);
                font.Should().NotBe(null);
            }
            else
            {
                // Create a font.
                var fontCreator = () => new XFont("Times New Roman", 20, XFontStyleEx.BoldItalic);
                fontCreator.Should().Throw<InvalidOperationException>();
            }
#elif GDI || WPF
            // Create a font.
            var font = new XFont("Times New Roman", 20, XFontStyleEx.BoldItalic);
            font.Should().NotBe(null);
#else
#error
#endif
        }

        [Theory]
        [InlineData("SimSun", "simsun.ttc", "中", false)]
        [InlineData("MS Mincho", "msmincho.ttc", "日", false)]
        [InlineData("MingLiU-ExtB", "mingliub.ttc", "\U00020000", false)]
        [InlineData("MingLiU-ExtB", "mingliub.ttc", "\U00020000", true)]
        public void UseWindowsFontsUnderWindows_resolves_first_collection_face(
            string familyName, string fileName, string text, bool bold)
        {
            if (!Capabilities.OperatingSystem.IsWindows ||
                !File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), fileName)))
                return;
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
            var font = new XFont(familyName, 12, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
            font.Should().NotBe(null);
            var document = new PdfDocument();
            using (var graphics = XGraphics.FromPdfPage(document.AddPage()))
                graphics.DrawString(text, font, XBrushes.Black, 50, 100);
            using var stream = new MemoryStream();
            document.Save(stream);
            stream.Length.Should().BeGreaterThan(0);
        }

        [Fact]
        public void UseWindowsFontsUnderWindows_resolves_both_Cambria_faces()
        {
            if (!Capabilities.OperatingSystem.IsWindows ||
                !File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "cambria.ttc")))
                return;
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
            var regular = new XFont("Cambria", 12);
            var math = new XFont("Cambria Math", 12);
            var document = new PdfDocument();
            using (var graphics = XGraphics.FromPdfPage(document.AddPage()))
            {
                graphics.DrawString("ABC", regular, XBrushes.Black, 50, 100);
                graphics.DrawString("ABC", math, XBrushes.Black, 50, 130);
            }
            using var stream = new MemoryStream();
            document.Save(stream);
            stream.Length.Should().BeGreaterThan(0);
            var resolver = new WindowsPlatformFontResolver();
            resolver.ResolveTypeface("Cambria", false, false)!.FaceName.Should().Be("cambria");
            resolver.ResolveTypeface("Cambria Math", false, false)!.FaceName.Should().Be("cambria#1");
            var first = resolver.GetFont("cambria")!;
            var second = resolver.GetFont("cambria#1")!;
            first.Should().NotEqual(second);
        }

        [Fact]
        public void ExtractCollectionFace_rejects_invalid_directory()
        {
            var collection = new byte[16];
            Action extract = () => WindowsPlatformFontResolver.ExtractCollectionFace(collection, 0);
            extract.Should().Throw<InvalidDataException>();
        }

        [Fact]
        public void ExtractCollectionFace_preserves_embedded_font_tables()
        {
            var font = FontDataHelper.SegoeWP;
            var collection = new byte[font.Length + 16];
            "ttcf"u8.CopyTo(collection);
            BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(4, 4), 0x00010000);
            BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(8, 4), 1);
            BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(12, 4), 16);
            font.CopyTo(collection, 16);
            var count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4, 2));
            for (var index = 0; index < count; index++)
            {
                var record = collection.AsSpan(28 + index * 16, 16);
                var offset = BinaryPrimitives.ReadUInt32BigEndian(record.Slice(8, 4));
                BinaryPrimitives.WriteUInt32BigEndian(record.Slice(8, 4), offset + 16);
            }

            var extracted = WindowsPlatformFontResolver.ExtractCollectionFace(collection, 0);
            extracted.AsSpan(0, 4).ToArray().Should().Equal(font.AsSpan(0, 4).ToArray());
            BinaryPrimitives.ReadUInt16BigEndian(extracted.AsSpan(4, 2)).Should().Be(count);
            for (var index = 0; index < count; index++)
            {
                var sourceRecord = font.AsSpan(12 + index * 16, 16);
                var outputRecord = extracted.AsSpan(12 + index * 16, 16);
                outputRecord[..4].ToArray().Should().Equal(sourceRecord[..4].ToArray());
                var sourceOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(sourceRecord.Slice(8, 4));
                var outputOffset = (int)BinaryPrimitives.ReadUInt32BigEndian(outputRecord.Slice(8, 4));
                var length = (int)BinaryPrimitives.ReadUInt32BigEndian(sourceRecord.Slice(12, 4));
                if (sourceRecord[..4].SequenceEqual("head"u8)) continue;
                extracted.AsSpan(outputOffset, length).ToArray().Should().Equal(
                    font.AsSpan(sourceOffset, length).ToArray());
            }
            BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(36, 4), uint.MaxValue);
            Action extractInvalid = () => WindowsPlatformFontResolver.ExtractCollectionFace(collection, 0);
            extractInvalid.Should().Throw<InvalidDataException>();
        }

        [Fact]
        public void ExtractCollectionFace_selects_requested_face()
        {
            var regular = FontDataHelper.SegoeWP;
            var bold = FontDataHelper.SegoeWPBold;
            var secondOffset = 20 + regular.Length;
            var collection = new byte[secondOffset + bold.Length];
            "ttcf"u8.CopyTo(collection);
            BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(4, 4), 0x00010000);
            BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(8, 4), 2);
            BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(12, 4), 20);
            BinaryPrimitives.WriteUInt32BigEndian(collection.AsSpan(16, 4), (uint)secondOffset);
            CopyFace(regular, collection, 20);
            CopyFace(bold, collection, secondOffset);

            var first = WindowsPlatformFontResolver.ExtractCollectionFace(collection, 0);
            var second = WindowsPlatformFontResolver.ExtractCollectionFace(collection, 1);
            Table(first, "name").Should().Equal(Table(regular, "name"));
            Table(second, "name").Should().Equal(Table(bold, "name"));
            Table(second, "name").Should().NotEqual(Table(first, "name"));
            Action outOfRange = () => WindowsPlatformFontResolver.ExtractCollectionFace(collection, 2);
            outOfRange.Should().Throw<ArgumentOutOfRangeException>();

            static void CopyFace(byte[] font, byte[] collection, int destination)
            {
                font.CopyTo(collection, destination);
                var count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4, 2));
                for (var index = 0; index < count; index++)
                {
                    var record = collection.AsSpan(destination + 12 + index * 16, 16);
                    var offset = BinaryPrimitives.ReadUInt32BigEndian(record.Slice(8, 4));
                    BinaryPrimitives.WriteUInt32BigEndian(record.Slice(8, 4), offset + (uint)destination);
                }
            }

            static byte[] Table(byte[] font, string tag)
            {
                var count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4, 2));
                for (var index = 0; index < count; index++)
                {
                    var record = font.AsSpan(12 + index * 16, 16);
                    if (!record[..4].SequenceEqual(System.Text.Encoding.ASCII.GetBytes(tag))) continue;
                    var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(record.Slice(8, 4));
                    var length = (int)BinaryPrimitives.ReadUInt32BigEndian(record.Slice(12, 4));
                    return font.AsSpan(offset, length).ToArray();
                }
                throw new InvalidDataException($"Missing {tag} table.");
            }
        }

        [Fact]
        public void UseWindowsFontsUnderWsl2_set()
        {
            GlobalFontSettings.Reset();
            GlobalFontSettings.UseWindowsFontsUnderWsl2 = true;
#if CORE
            if (Capabilities.OperatingSystem.IsWsl2)
            {
                // Create a font.
                var font = new XFont("Times New Roman", 20, XFontStyleEx.BoldItalic);
                font.Should().NotBe(null);
            }
            else
            {
                // Create a font.
                var fontCreator = () => new XFont("Times New Roman", 20, XFontStyleEx.BoldItalic);
                fontCreator.Should().Throw<InvalidOperationException>();
            }
#elif GDI || WPF
            // Create a font.
            var font = new XFont("Times New Roman", 20, XFontStyleEx.BoldItalic);
            font.Should().NotBe(null);
#else
#error
#endif
        }

        [Fact]
        public void UseWindowsFontsUnderWindows_and_UseWindowsFontsUnderWsl2_set()
        {
            GlobalFontSettings.Reset();
            GlobalFontSettings.UseWindowsFontsUnderWindows = true;
            GlobalFontSettings.UseWindowsFontsUnderWsl2 = true;
#if CORE
            if (Capabilities.OperatingSystem.IsWindows ||
                Capabilities.OperatingSystem.IsWsl2)
            {
                // Create a font.
                var font = new XFont("Times New Roman", 20, XFontStyleEx.BoldItalic);
                font.Should().NotBe(null);
            }
            else
            {
                // Create a font.
                var fontCreator = () => new XFont("Times New Roman", 20, XFontStyleEx.BoldItalic);
                fontCreator.Should().Throw<InvalidOperationException>();
            }
#elif GDI || WPF
            // Create a font.
            var font = new XFont("Times New Roman", 20, XFontStyleEx.BoldItalic);
            font.Should().NotBe(null);
#else
#error
#endif
        }
    }
}
