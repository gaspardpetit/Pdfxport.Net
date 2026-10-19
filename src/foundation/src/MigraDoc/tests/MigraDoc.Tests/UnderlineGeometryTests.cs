// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;
using FluentAssertions;

namespace MigraDoc.Tests
{
    [Collection("PDFsharp")]
    public class UnderlineGeometryTests : IDisposable
    {
        public UnderlineGeometryTests()
        {
            PdfSharpCore.ResetAll();
#if CORE
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
        }

        public void Dispose() => PdfSharpCore.ResetAll();

        [Fact]
        public void UnderlineGeometryCanBeConfiguredWithoutChangingTheDefault()
        {
            static (string Content, double Y) Render(double? widthFactor, double? offsetFactor,
                Func<Font, (double StrokeWidthFactor, double VerticalOffsetFactor)?>? geometry = null)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                var paragraph = document.AddSection().AddParagraph("underlined");
                paragraph.Format.Font.Size = 12;
                paragraph.Format.Font.Underline = Underline.Single;
                var renderer = new PdfDocumentRenderer { Document = document };
                renderer.DocumentRenderer.UnderlineStrokeWidthFactor = widthFactor;
                renderer.DocumentRenderer.UnderlineVerticalOffsetFactor = offsetFactor;
                renderer.DocumentRenderer.UnderlineGeometry = geometry;
                renderer.RenderDocument();

                var content = string.Concat(renderer.PdfDocument.Pages[0].Contents.Elements
                    .OfType<PdfSharp.Pdf.Advanced.PdfReference>()
                    .Select(reference => reference.Value)
                    .OfType<PdfSharp.Pdf.Advanced.PdfContent>()
                    .Where(stream => stream.Stream != null)
                    .Select(stream => Encoding.GetEncoding(28591).GetString(stream.Stream!.UnfilteredValue)));
                var line = Regex.Match(content,
                    @"(?m)[\d.]+ (?<y>[\d.]+) m\s+[\d.]+ \k<y> l\s+S");
                line.Success.Should().BeTrue();
                return (content, double.Parse(line.Groups["y"].Value, CultureInfo.InvariantCulture));
            }

            var original = Render(null, null);
            var configured = Render(0.025, 0.23);
            var overridden = Render(0.025, 0.23, font =>
                font.Name == "Arial" ? (0.1, 0.6) : null);
            var fallback = Render(0.025, 0.23, _ => null);

            original.Content.Should().Contain(".75 w");
            configured.Content.Should().Contain(".3 w");
            configured.Y.Should().NotBe(original.Y);
            overridden.Content.Should().Contain("1.2 w");
            overridden.Y.Should().NotBe(configured.Y);
            fallback.Should().Be(configured);
        }

        [Fact]
        public void UnderlineGeometryKeepsEachSegmentAtItsOwnFontOffset()
        {
            static double[] Render(double timesOffset)
            {
                var document = new Document();
                var paragraph = document.AddSection().AddParagraph();
                paragraph.Format.Font.Size = 12;
                paragraph.Format.Font.Underline = Underline.Single;
                paragraph.AddFormattedText("Arial").Font.Name = "Arial";
                paragraph.AddFormattedText("Times").Font.Name = "Times New Roman";
                var renderer = new PdfDocumentRenderer { Document = document };
                renderer.DocumentRenderer.UnderlineGeometry = font =>
                    (1.0 / 16, font.Name == "Arial" ? 0.2 : timesOffset);
                renderer.RenderDocument();

                var content = string.Concat(renderer.PdfDocument.Pages[0].Contents.Elements
                    .OfType<PdfSharp.Pdf.Advanced.PdfReference>()
                    .Select(reference => reference.Value)
                    .OfType<PdfSharp.Pdf.Advanced.PdfContent>()
                    .Where(stream => stream.Stream != null)
                    .Select(stream => Encoding.GetEncoding(28591).GetString(stream.Stream!.UnfilteredValue)));
                return Regex.Matches(content,
                    @"(?m)[\d.]+ (?<y>[\d.]+) m\s+[\d.]+ \k<y> l\s+S")
                    .Select(match => double.Parse(match.Groups["y"].Value, CultureInfo.InvariantCulture))
                    .ToArray();
            }

            var continuous = Render(0.2);
            continuous.Should().HaveCount(1);
            var split = Render(0.6);
            split.Should().HaveCount(2);
            split[0].Should().Be(continuous[0]);
            split[1].Should().NotBe(split[0]);
        }

        [Fact]
        public void UnderlineGeometryUsesPerFontStrokeWidthsAcrossRuns()
        {
            var document = new Document();
            var paragraph = document.AddSection().AddParagraph();
            paragraph.Format.Font.Size = 12;
            paragraph.Format.Font.Underline = Underline.Single;
            paragraph.AddFormattedText("Arial").Font.Name = "Arial";
            paragraph.AddFormattedText("Times").Font.Name = "Times New Roman";
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.DocumentRenderer.UnderlineGeometry = font =>
                (font.Name == "Arial" ? 0.025 : 0.05, 0.23);
            renderer.RenderDocument();

            var content = string.Concat(renderer.PdfDocument.Pages[0].Contents.Elements
                .OfType<PdfSharp.Pdf.Advanced.PdfReference>()
                .Select(reference => reference.Value)
                .OfType<PdfSharp.Pdf.Advanced.PdfContent>()
                .Where(stream => stream.Stream != null)
                .Select(stream => Encoding.GetEncoding(28591).GetString(stream.Stream!.UnfilteredValue)));
            content.Should().Contain(".3 w");
            content.Should().Contain(".6 w");
            Regex.Matches(content, @"(?m)[\d.]+ (?<y>[\d.]+) m\s+[\d.]+ \k<y> l\s+S")
                .Should().HaveCount(2);
        }

    }
}
