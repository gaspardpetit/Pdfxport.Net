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
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AllBlankGlyphSubsetHasValidTableDirectory(bool cidFont)
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
            }, cidFont);
            var bytes = subset.OTFontSource.Bytes;
            var tableCount = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(4, 2));
            var records = new List<(string Tag, int Offset, int Length)>();
            for (var index = 0; index < tableCount; index++)
            {
                var record = bytes.AsSpan(12 + 16 * index, 16);
                records.Add((Encoding.ASCII.GetString(record[..4]),
                    checked((int)BinaryPrimitives.ReadUInt32BigEndian(record.Slice(8, 4))),
                    checked((int)BinaryPrimitives.ReadUInt32BigEndian(record.Slice(12, 4)))));
            }

            records.Should().ContainSingle(record => record.Tag == "glyf");
            var glyf = records.Single(record => record.Tag == "glyf");
            glyf.Length.Should().Be(1);
            bytes[glyf.Offset].Should().Be(0);

            for (var index = 0; index < records.Count; index++)
            {
                var record = records[index];
                record.Length.Should().BeGreaterThan(0);
                record.Offset.Should().BeGreaterOrEqualTo(12 + 16 * tableCount);
                (record.Offset % 4).Should().Be(0);
                ((long)record.Offset + record.Length).Should().BeLessOrEqualTo(bytes.Length);
                if (index > 0)
                    StringComparer.Ordinal.Compare(records[index - 1].Tag, record.Tag).Should().BeLessThan(0);
            }

            var byOffset = records.OrderBy(record => record.Offset).ToArray();
            for (var index = 1; index < byOffset.Length; index++)
            {
                var preceding = byOffset[index - 1];
                byOffset[index].Offset.Should().BeGreaterOrEqualTo(
                    preceding.Offset + (preceding.Length + 3 & ~3));
            }

            var head = records.Single(record => record.Tag == "head");
            var maxp = records.Single(record => record.Tag == "maxp");
            var loca = records.Single(record => record.Tag == "loca");
            var glyphCount = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(maxp.Offset + 4, 2));
            var locationFormat = BinaryPrimitives.ReadInt16BigEndian(bytes.AsSpan(head.Offset + 50, 2));
            locationFormat.Should().BeOneOf((short)0, (short)1);
            var entrySize = locationFormat == 0 ? 2 : 4;
            loca.Length.Should().BeGreaterOrEqualTo((glyphCount + 1) * entrySize);
            var lastLocation = loca.Offset + glyphCount * entrySize;
            var glyphDataEnd = entrySize == 2
                ? BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(lastLocation, 2)) * 2L
                : BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(lastLocation, 4));
            glyphDataEnd.Should().Be(0);
            glyphDataEnd.Should().BeLessOrEqualTo(glyf.Length);
        }
    }
}
