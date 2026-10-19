// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using System.Buffers.Binary;
using FluentAssertions;
using PdfSharp.Internal.Imaging;
using Xunit;

namespace PdfSharp.Tests.Drawing
{
    public class BmpOrientationTests
    {
        [Theory]
        [InlineData(false, 24)]
        [InlineData(true, 24)]
        [InlineData(false, 32)]
        [InlineData(true, 32)]
        [InlineData(false, 8)]
        [InlineData(true, 8)]
        public void Bmp_rows_follow_the_signed_height(bool topDown, int bitCount)
        {
            var bitmap = CreateBitmap(topDown, bitCount);

            ToBeNamed.TryImportImageBmp(bitmap, out var imported).Should().BeTrue();
            var image = imported.Should().BeOfType<ImportedBmpImage>().Subject;
            var pixels = image.GetPdfImageData();
            if (bitCount is 24 or 32)
                pixels.Should().Equal(new byte[] { 0, 0, 255, 0, 0, 255, 255, 0, 0, 255, 0, 0 });
            else
                pixels.Should().Equal(new byte[] { 0, 0, 1, 1 });
        }

        static byte[] CreateBitmap(bool topDown, int bitCount)
        {
            const int width = 2;
            const int height = 2;
            var stride = (width * bitCount + 31) / 32 * 4;
            var pixelOffset = 54 + (bitCount == 8 ? 8 : 0);
            var bitmap = new byte[pixelOffset + stride * height];
            bitmap[0] = (byte)'B';
            bitmap[1] = (byte)'M';
            BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(2), bitmap.Length);
            BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(10), pixelOffset);
            BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(14), 40);
            BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(18), width);
            BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(22), topDown ? -height : height);
            BinaryPrimitives.WriteInt16LittleEndian(bitmap.AsSpan(26), 1);
            BinaryPrimitives.WriteInt16LittleEndian(bitmap.AsSpan(28), (short)bitCount);
            BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(34), stride * height);
            if (bitCount == 8)
            {
                BinaryPrimitives.WriteInt32LittleEndian(bitmap.AsSpan(46), 2);
                bitmap[54] = 255; // Blue.
                bitmap[60] = 255; // Red.
                bitmap[57] = bitmap[61] = 255; // Opaque palette entries.
            }
            for (var fileRow = 0; fileRow < height; fileRow++)
            {
                var blue = (topDown ? fileRow : height - 1 - fileRow) == 0;
                var offset = pixelOffset + fileRow * stride;
                for (var column = 0; column < width; column++)
                    if (bitCount is 24 or 32)
                    {
                        var pixel = offset + column * (bitCount / 8);
                        bitmap[pixel + (blue ? 0 : 2)] = 255;
                        if (bitCount == 32)
                            bitmap[pixel + 3] = 255;
                    }
                    else
                        bitmap[offset + column] = (byte)(blue ? 0 : 1);
            }
            return bitmap;
        }
    }
}
