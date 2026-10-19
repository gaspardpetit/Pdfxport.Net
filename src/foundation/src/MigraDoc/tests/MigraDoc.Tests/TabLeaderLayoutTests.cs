using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Quality;
using Xunit;
#if CORE
using PdfSharp.Fonts;
#endif

namespace MigraDoc.Tests
{
    [Collection("PDFsharp")]
    public class TabLeaderLayoutTests : IDisposable
    {
        public TabLeaderLayoutTests()
        {
            PdfSharpCore.ResetAll();
#if CORE
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
        }

        public void Dispose() => PdfSharpCore.ResetAll();

        [Fact]
        public void Custom_layout_draws_individual_leaders_without_changing_default()
        {
            var defaultContent = Render(false, out var defaultCalls, out _);
            var adjustedContent = Render(true, out var adjustedCalls, out var chapterWidth);

            defaultCalls.Should().Be(0);
            adjustedCalls.Should().BeGreaterThan(0);
            Count(defaultContent, " Tj").Should().Be(3);
            Count(adjustedContent, " Tj").Should().BeGreaterThan(3);
            var advances = Regex.Matches(adjustedContent,
                    @"(?<advance>-?\d+(?:\.\d+)?) 0 Td\r?\n\(\.\) Tj")
                .Cast<Match>()
                .Select(match => double.Parse(match.Groups["advance"].Value, CultureInfo.InvariantCulture))
                .ToArray();
            advances.Length.Should().BeGreaterThan(2);
            var pitch = advances[1];
            foreach (var advance in advances.Skip(1))
                advance.Should().BeApproximately(pitch, 0.001);
            (advances[0] - chapterWidth).Should().BeInRange(3.5, 3.5 + pitch);
            var numberAdvance = Regex.Match(adjustedContent,
                @"(?<advance>-?\d+(?:\.\d+)?) 0 Td\r?\n\(1\) Tj");
            numberAdvance.Success.Should().BeTrue();
            (double.Parse(numberAdvance.Groups["advance"].Value, CultureInfo.InvariantCulture) - pitch)
                .Should().BeApproximately(0.4, 0.001);
        }

        [Fact]
        public void Null_layout_retains_default_leader_rendering()
        {
            var defaultContent = Render(false, out _, out _);
            var delegatedContent = Render(true, out var calls, out _, true);

            calls.Should().BeGreaterThan(0);
            delegatedContent.Should().Be(defaultContent);
        }

        [Fact]
        public void Invalid_layout_retains_default_leader_rendering()
        {
            var defaultContent = Render(false, out _, out _);
            var delegatedContent = Render(true, out var calls, out _, invalidLayout: true);

            calls.Should().BeGreaterThan(0);
            delegatedContent.Should().Be(defaultContent);
        }

        [Theory]
        [InlineData(TabLeader.Dashes)]
        [InlineData(TabLeader.Lines)]
        [InlineData(TabLeader.Heavy)]
        [InlineData(TabLeader.MiddleDot)]
        public void Custom_layout_supports_other_leaders(TabLeader leader)
        {
            var defaultContent = Render(false, out _, out _, leader: leader);
            var adjustedContent = Render(true, out var calls, out _, leader: leader);

            calls.Should().BeGreaterThan(0);
            Count(adjustedContent, " Tj").Should().BeGreaterThan(Count(defaultContent, " Tj"));
        }

        static string Render(bool customize, out int calls, out double chapterWidth,
            bool useDefaultLayout = false, bool invalidLayout = false, TabLeader leader = TabLeader.Dots)
        {
            var document = new Document();
            var paragraph = document.AddSection().AddParagraph();
            paragraph.Format.TabStops.AddTabStop(Unit.FromPoint(250), TabAlignment.Right, leader);
            paragraph.AddText("Chapter");
            paragraph.AddTab();
            paragraph.AddText("1");
            var renderer = new PdfDocumentRenderer { Document = document };
            var callCount = 0;
            var measuredChapter = 0.0;
            if (customize)
                renderer.DocumentRenderer.TabLeaderLayout = (source, renderedLeader, font) =>
                {
                    source.Should().BeSameAs(paragraph);
                    callCount++;
                    using var graphics = XGraphics.CreateMeasureContext(new XSize(100, 100),
                        XGraphicsUnit.Point, XPageDirection.Downwards);
                    measuredChapter = graphics.MeasureString("Chapter", font).Width;
                    return !useDefaultLayout && renderedLeader == leader
                        ? invalidLayout ? (-1000, 3.5, 0.5) : (0.1, 3.5, 0.5)
                        : null;
                };
            renderer.RenderDocument();
            renderer.PdfDocument.Options.CompressContentStreams = false;
            using var stream = new MemoryStream();
            renderer.PdfDocument.Save(stream, false);
            calls = callCount;
            chapterWidth = measuredChapter;
            var content = ((PdfContent)((PdfReference)renderer.PdfDocument.Pages[0].Contents.Elements[0]).Value).Stream;
            return Encoding.GetEncoding(28591).GetString(content!.Value);
        }

        static int Count(string text, string value) => text.Split(new[] { value }, StringSplitOptions.None).Length - 1;
    }
}
