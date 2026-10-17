using System.Buffers.Binary;
using System.Text;
using FluentAssertions;
using PdfSharp.Internal.OpenType;
using Xunit;

namespace PdfSharp.Tests.Fonts
{
    [Collection("PDFsharp")]
    public class BlankGlyphSubsetTests
    {
        [Fact]
        public void AllBlankGlyphSubsetHasNonemptyGlyfTable()
        {
            // This small generated font has only .notdef and space, both without outlines.
            var path = Path.Combine(AppContext.BaseDirectory, "Fonts", "Fixtures", "BlankGlyphFont.ttf");
            var source = OpenTypeFontSource.GetOrCreateFrom(File.ReadAllBytes(path));
            var face = source.OTFontFace ?? OpenTypeFontFace.GetOrCreateFrom(source);
            face.SetFontEmbedding(OpenTypeFontEmbedding.TryComputeSubset);
            face.glyf.GetGlyphSize(0).Should().Be(0);
            face.glyf.GetGlyphSize(1).Should().Be(0);

            var subset = face.CreateFontSubset(new Dictionary<ushort, object?>
            {
                [0] = null,
                [1] = null,
            }, cidFont: false);
            var bytes = subset.OTFontSource.Bytes;
            var tableCount = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4, 2));
            for (var index = 0; index < tableCount; index++)
            {
                var record = bytes.AsSpan(12 + 16 * index, 16);
                if (Encoding.ASCII.GetString(record[..4]) != "glyf")
                    continue;

                BinaryPrimitives.ReadUInt32BigEndian(record.Slice(12, 4)).Should().BeGreaterThan(0);
                return;
            }

            throw new InvalidOperationException("The subset has no glyf table.");
        }
    }
}
