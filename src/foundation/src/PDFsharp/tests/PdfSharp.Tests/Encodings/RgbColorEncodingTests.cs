// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using System.Globalization;
using FluentAssertions;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Internal;
using PdfSharp.TestHelper;
using Xunit;

namespace PdfSharp.Tests.Encodings
{
    [Collection("PDFsharp")]
    public class RgbColorEncodingTests
    {
        [Theory]
        [InlineData(19, 24, 33, ".0745 .0941 .129")]
        [InlineData(31, 37, 53, ".122 .145 .208")]
        [InlineData(1, 2, 3, ".00392 .00784 .0118")]
        [InlineData(0, 128, 255, "0 .502 1")]
        public void Rgb_channels_use_three_significant_digits(int red, int green, int blue, string expected)
        {
            PdfEncoders.ToString(XColor.FromArgb(red, green, blue), PdfColorMode.Rgb)
                .Should().Be(expected);
            PdfEncoders.ToString(XColor.FromArgb(red, green, blue), PdfColorMode.Undefined)
                .Should().Be(expected);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        public void EveryByteChannelHasSmallRelativeErrorAndRoundTrips(int channel)
        {
            for (var value = 0; value <= 255; value++)
            {
                var channels = new int[3];
                channels[channel] = value;
                var encoded = PdfEncoders.ToString(XColor.FromArgb(channels[0], channels[1], channels[2]),
                    PdfColorMode.Rgb).Split(' ');
                encoded.Should().HaveCount(3);
                for (var index = 0; index < 3; index++)
                {
                    var actual = double.Parse(encoded[index], CultureInfo.InvariantCulture);
                    actual.Should().BeInRange(0, 1);
                    Math.Round(actual * 255).Should().Be(channels[index]);
                    if (channels[index] > 0)
                    {
                        var expected = channels[index] / 255.0;
                        (Math.Abs(actual - expected) / expected).Should().BeLessThan(0.005,
                            "three significant digits should keep relative error below 0.5% for channel {0}", value);
                    }
                }
            }
        }

        [Theory]
        [InlineData(PdfColorMode.Cmyk)]
        [InlineData(PdfColorMode.Undefined)]
        public void CmykRetainsThreeDecimalPlaces(PdfColorMode mode)
        {
            PdfEncoders.ToString(XColor.FromCmyk(0.12345, 0.23456, 0.34567, 0.45678), mode)
                .Should().Be(".123 .235 .346 .457");
        }

        [Theory]
        [InlineData("fr-FR")]
        [InlineData("de-DE")]
        public void ColorComponentsRemainInvariantUnderCommaDecimalCulture(string culture)
        {
            var saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
                PdfEncoders.ToString(XColor.FromArgb(1, 19, 255), PdfColorMode.Rgb)
                    .Should().Be(".00392 .0745 1");
                PdfEncoders.ToString(XColor.FromCmyk(0.12345, 0.23456, 0.34567, 0.45678), PdfColorMode.Cmyk)
                    .Should().Be(".123 .235 .346 .457");
                PdfEncoders.ToString(0.12345).Should().Be(".123");
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        [Fact]
        public void StrokeAndFillUsePreciseRgbComponentsInPdfContent()
        {
            using var document = new PdfDocument();
            var page = document.AddPage();
            var color = XColor.FromArgb(1, 19, 255);
            using (var graphics = XGraphics.FromPdfPage(page))
                graphics.DrawRectangle(new XPen(color), new XSolidBrush(color), 10, 10, 20, 20);

            var content = PdfFileHelper.GetPageContentStream(document, 0);
            content.Should().Contain(".00392 .0745 1 RG");
            content.Should().Contain(".00392 .0745 1 rg");
        }
    }
}
