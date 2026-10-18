// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Fields;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.Rendering;
using MigraDoc.RtfRendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Logging;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;
using FluentAssertions;

namespace MigraDoc.Tests
{
    [Collection("PDFsharp")]
    public class TextTests : IDisposable
    {
        [Fact]
        public void TextWidthAdjustmentIsScopedToParagraph()
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var section = document.AddSection();
            section.PageSetup.PageWidth = Unit.FromPoint(200);
            section.PageSetup.LeftMargin = Unit.FromPoint(65);
            section.PageSetup.RightMargin = Unit.FromPoint(65);
            var adjusted = section.AddParagraph("Longword marker");
            var unchanged = section.AddParagraph("Longword marker");
            var widened = section.AddParagraph("Tiny words");
            var widenedControl = section.AddParagraph("Tiny words");
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.DocumentRenderer.TextWidthAdjustment = (paragraph, word, _) =>
                ReferenceEquals(paragraph, adjusted) && word == "marker" ? -30 :
                ReferenceEquals(paragraph, widened) && word == "words" ? 30 : null;

            renderer.RenderDocument();

            var paragraphs = renderer.DocumentRenderer.FormattedDocument!.GetRenderInfos(1)!
                .OfType<ParagraphRenderInfo>().ToArray();
            double Height(Paragraph paragraph) => paragraphs.Single(info => ReferenceEquals(info.DocumentObject, paragraph))
                .LayoutInfo.ContentArea.Height.Point;
            Height(adjusted).Should().BeLessThan(Height(unchanged));
            Height(widened).Should().BeGreaterThan(Height(widenedControl));
        }

        [Fact]
        public void TextWidthAdjustmentDoesNotChangePaintedAdvances()
        {
            string Render(double? adjustment)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                var paragraph = document.AddSection().AddParagraph("before after");
                var renderer = new PdfDocumentRenderer { Document = document };
                if (adjustment is not null)
                    renderer.DocumentRenderer.TextWidthAdjustment = (candidate, word, _) =>
                        ReferenceEquals(candidate, paragraph) && word == "before" ? adjustment : null;
                renderer.RenderDocument();
                return PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0);
            }

            Render(-2).Should().Be(Render(null));
            Render(double.NaN).Should().Be(Render(null));
            Render(double.PositiveInfinity).Should().Be(Render(null));
            Render(-1000).Should().Be(Render(null));
        }

        [Fact]
        public void TextWidthAdjustmentForSuperscriptUsesFinalPointWidth()
        {
            double Height(double contentWidth, double adjustment)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                var section = document.AddSection();
                section.PageSetup.PageWidth = Unit.FromPoint(contentWidth + 40);
                section.PageSetup.LeftMargin = Unit.FromPoint(20);
                section.PageSetup.RightMargin = Unit.FromPoint(20);
                var paragraph = section.AddParagraph();
                paragraph.AddText("before ");
                paragraph.AddFormattedText("marker").Font.Superscript = true;
                var renderer = new PdfDocumentRenderer { Document = document };
                renderer.DocumentRenderer.TextWidthAdjustment = (_, word, _) =>
                    word == "marker" ? adjustment : null;
                renderer.RenderDocument();
                return renderer.DocumentRenderer.FormattedDocument!.GetRenderInfos(1)!
                    .OfType<ParagraphRenderInfo>().Single().LayoutInfo.ContentArea.Height.Point;
            }

            double MinimumWidth(double adjustment)
            {
                var oneLineHeight = Height(120, adjustment);
                var lower = 30.0;
                var upper = 120.0;
                for (var iteration = 0; iteration < 12; iteration++)
                {
                    var middle = (lower + upper) / 2;
                    if (Height(middle, adjustment) > oneLineHeight + 1)
                        lower = middle;
                    else
                        upper = middle;
                }
                return upper;
            }

            (MinimumWidth(5) - MinimumWidth(0)).Should().BeApproximately(5, 0.1);
        }

        [Fact]
        public void JustifiedFinalWordCanUseContractedSpaces()
        {
            (int PageCount, double[] Positions, string[] PageContents) Render(
                Func<Paragraph, double?>? shrinkFactor)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                document.Styles.Normal.Font.Size = 12;
                var section = document.AddSection();
                section.PageSetup.PageWidth = Unit.FromPoint(95);
                section.PageSetup.PageHeight = Unit.FromPoint(40);
                section.PageSetup.LeftMargin = Unit.FromPoint(10);
                section.PageSetup.RightMargin = Unit.FromPoint(10);
                section.PageSetup.TopMargin = Unit.FromPoint(10);
                section.PageSetup.BottomMargin = Unit.FromPoint(10);
                var paragraph = section.AddParagraph("a a a a a a a a");
                paragraph.Format.Alignment = ParagraphAlignment.Justify;
                var renderer = new PdfDocumentRenderer { Document = document };
                renderer.DocumentRenderer.JustifiedSpaceShrinkFactorForParagraph = shrinkFactor;
                renderer.RenderDocument();
                var streamEnumerator = PdfFileHelper.GetPageContentStreamEnumerator(renderer.PdfDocument, 0);
                var positions = new List<double>();
                while (streamEnumerator.Text.MoveAndGetNext(true, out var textInfo))
                    positions.Add(textInfo!.X);
                var pageContents = Enumerable.Range(0, renderer.PdfDocument.PageCount)
                    .Select(index => PdfFileHelper.GetPageContentStream(renderer.PdfDocument, index)).ToArray();
                return (renderer.PdfDocument.PageCount, positions.ToArray(), pageContents);
            }

            var defaultRender = Render(null);
            var unchangedRender = Render(_ => 1);
            var nullRender = Render(_ => null);
            var contractedRender = Render(_ => 0.75);
            defaultRender.PageCount.Should().Be(2);
            unchangedRender.PageCount.Should().Be(2);
            nullRender.PageCount.Should().Be(2);
            contractedRender.PageCount.Should().Be(1);
            unchangedRender.Positions.Should().Equal(defaultRender.Positions);
            unchangedRender.PageContents.Should().Equal(defaultRender.PageContents);
            nullRender.PageContents.Should().Equal(defaultRender.PageContents);
            contractedRender.Positions.Should().HaveCount(defaultRender.Positions.Length + 1);
            (contractedRender.Positions[1] - contractedRender.Positions[0]).Should()
                .BeLessThan(defaultRender.Positions[1] - defaultRender.Positions[0]);
            contractedRender.Positions[^1].Should().BeApproximately(defaultRender.Positions[^1], 0.01);
        }

        [Fact]
        public void TextFrameLayersSurviveDdlAndControlPagePaintOrder()
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var section = document.AddSection();
            section.AddParagraph("BODYMARKER");
            TextFrame AddFrame(string marker, TextFrameLayer layer)
            {
                var frame = section.AddTextFrame();
                frame.Width = Unit.FromPoint(180);
                frame.Height = Unit.FromPoint(60);
                frame.Layer = layer;
                frame.AddParagraph(marker);
                return frame;
            }

            var behind = AddFrame("BEHINDMARKER", TextFrameLayer.BehindContent);
            AddFrame("NORMALMARKER", TextFrameLayer.Normal);
            behind.Clone().Layer.Should().Be(TextFrameLayer.BehindContent);

            var ddl = MigraDoc.DocumentObjectModel.IO.DdlWriter.WriteToString(document);
            var restored = MigraDoc.DocumentObjectModel.IO.DdlReader.DocumentFromString(ddl);
            restored.Sections[0].Elements.OfType<TextFrame>().Select(frame => frame.Layer)
                .Should().Equal(TextFrameLayer.BehindContent, TextFrameLayer.Normal);

            var renderer = new PdfDocumentRenderer { Document = restored };
            renderer.DocumentRenderer.TextFrameBehindContent = _ => true;
            renderer.RenderDocument();
            var content = PageContent(renderer, 0);
            content.IndexOf("BEHINDMARKER", StringComparison.Ordinal)
                .Should().BeLessThan(content.IndexOf("BODYMARKER", StringComparison.Ordinal));
            content.IndexOf("BODYMARKER", StringComparison.Ordinal)
                .Should().BeLessThan(content.IndexOf("NORMALMARKER", StringComparison.Ordinal));
        }

        [Fact]
        public void TextFrameRotationSurvivesCloneAndDdl()
        {
            var document = new Document();
            var frame = document.AddSection().AddTextFrame();
            frame.Rotation = 30;

            frame.Clone().Rotation.Should().Be(30);
            var ddl = MigraDoc.DocumentObjectModel.IO.DdlWriter.WriteToString(document);
            var restored = MigraDoc.DocumentObjectModel.IO.DdlReader.DocumentFromString(ddl);
            restored.Sections[0].Elements.OfType<TextFrame>().Single().Rotation.Should().Be(30);
        }

        [Fact]
        public void FramePaginationAndTopBottomExclusionSurviveCloneAndDdl()
        {
            var document = new Document();
            var frame = document.AddSection().AddTextFrame();
            frame.PaginateContent = true;
            frame.WrapFormat.Style = WrapStyle.TopBottom;
            frame.WrapFormat.ExcludeFollowingText = true;

            var clone = frame.Clone();
            clone.PaginateContent.Should().BeTrue();
            clone.WrapFormat.ExcludeFollowingText.Should().BeTrue();

            var ddl = MigraDoc.DocumentObjectModel.IO.DdlWriter.WriteToString(document);
            var restored = MigraDoc.DocumentObjectModel.IO.DdlReader.DocumentFromString(ddl);
            var restoredFrame = restored.Sections[0].Elements.OfType<TextFrame>().Single();
            restoredFrame.PaginateContent.Should().BeTrue();
            restoredFrame.WrapFormat.ExcludeFollowingText.Should().BeTrue();
        }

        [Fact]
        public void TextFrameRotationTransformsCompleteFrame()
        {
            string RenderContent(double? angle)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                var section = document.AddSection();
                var frame = section.AddTextFrame();
                frame.Width = Unit.FromPoint(100);
                frame.Height = Unit.FromPoint(50);
                frame.Rotation = angle ?? 0;
                frame.RelativeVertical = MigraDoc.DocumentObjectModel.Shapes.RelativeVertical.Page;
                frame.Left = Unit.FromPoint(100);
                frame.Top = Unit.FromPoint(100);
                frame.FillFormat.Visible = true;
                frame.FillFormat.Color = Colors.Yellow;
                frame.LineFormat.Visible = true;
                frame.AddParagraph("Rotated frame");
                var renderer = new PdfDocumentRenderer { Document = document };
                renderer.PdfDocument.Options.CompressContentStreams = false;
                renderer.RenderDocument();
                var content = renderer.PdfDocument.Pages[0].Contents.CreateSingleContent();
                return Encoding.ASCII.GetString(content.Stream?.Value ?? throw new InvalidOperationException());
            }

            var rotated = RenderContent(30);
            var unrotated = RenderContent(null);
            var rotation = rotated.IndexOf(".8660254 -.5 .5 .8660254", StringComparison.Ordinal);
            rotation.Should().BeGreaterThanOrEqualTo(0);
            var fill = rotated.IndexOf("\nf\n", rotation, StringComparison.Ordinal);
            fill.Should().BeGreaterThan(rotation);
            var text = rotated.IndexOf("(Rotated) Tj", fill, StringComparison.Ordinal);
            text.Should().BeGreaterThan(fill);
            var border = rotated.IndexOf("\nS\n", text, StringComparison.Ordinal);
            border.Should().BeGreaterThan(text);
            var restore = rotated.IndexOf("\nQ\n", border, StringComparison.Ordinal);
            restore.Should().BeGreaterThan(border);
            unrotated.Should().NotContain(".8660254 -.5 .5 .8660254");
        }

        public TextTests()
        {
            PdfSharpCore.ResetAll();
#if CORE
            GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
        }

        public void Dispose()
        {
            PdfSharpCore.ResetAll();
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void SelectedTextFrameRendersBeforePageContent(bool setSelector, bool selectFrame)
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var section = document.AddSection();
            section.AddParagraph("BODYMARKER");
            var frame = section.AddTextFrame();
            frame.RelativeVertical = MigraDoc.DocumentObjectModel.Shapes.RelativeVertical.Page;
            frame.Top = Unit.FromPoint(72);
            frame.Left = Unit.FromPoint(72);
            frame.Width = Unit.FromPoint(180);
            frame.Height = Unit.FromPoint(60);
            frame.AddParagraph("FRAMEMARKER");
            var renderer = new PdfDocumentRenderer { Document = document };
            if (setSelector)
                renderer.DocumentRenderer.TextFrameBehindContent = candidate =>
                    selectFrame && ReferenceEquals(candidate, frame);

            renderer.RenderDocument();

            renderer.PdfDocument.PageCount.Should().Be(1);
            var content = PageContent(renderer, 0);
            var bodyIndex = content.IndexOf("BODYMARKER", StringComparison.Ordinal);
            var frameIndex = content.IndexOf("FRAMEMARKER", StringComparison.Ordinal);
            bodyIndex.Should().BeGreaterThanOrEqualTo(0);
            frameIndex.Should().BeGreaterThanOrEqualTo(0);
            (frameIndex < bodyIndex).Should().Be(selectFrame);
        }

        [Fact]
        public void AboveContentTextFrameRendersAfterBodyAndBehindContentFrame()
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var section = document.AddSection();
            var above = section.AddTextFrame();
            above.Width = Unit.FromPoint(180);
            above.Height = Unit.FromPoint(60);
            above.Layer = MigraDoc.DocumentObjectModel.Shapes.TextFrameLayer.AboveContent;
            above.AddParagraph("ABOVEMARKER");
            var normal = section.AddTextFrame();
            normal.Width = Unit.FromPoint(180);
            normal.Height = Unit.FromPoint(60);
            normal.AddParagraph("NORMALMARKER");
            section.AddParagraph("BODYMARKER");
            var behind = section.AddTextFrame();
            behind.Width = Unit.FromPoint(180);
            behind.Height = Unit.FromPoint(60);
            behind.Layer = MigraDoc.DocumentObjectModel.Shapes.TextFrameLayer.BehindContent;
            behind.AddParagraph("BEHINDMARKER");

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();

            var content = PageContent(renderer, 0);
            var behindIndex = content.IndexOf("BEHINDMARKER", StringComparison.Ordinal);
            var normalIndex = content.IndexOf("NORMALMARKER", StringComparison.Ordinal);
            var bodyIndex = content.IndexOf("BODYMARKER", StringComparison.Ordinal);
            var aboveIndex = content.IndexOf("ABOVEMARKER", StringComparison.Ordinal);
            behindIndex.Should().BeGreaterThanOrEqualTo(0);
            normalIndex.Should().BeGreaterThan(behindIndex);
            bodyIndex.Should().BeGreaterThan(normalIndex);
            aboveIndex.Should().BeGreaterThan(bodyIndex);
            content.Split(new[] { "ABOVEMARKER" }, StringSplitOptions.None).Length.Should().Be(2);
        }

        [Fact]
        public void SelectedTextFramesKeepTheirOrderBeforeUnselectedContent()
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var section = document.AddSection();
            section.AddParagraph("BODYMARKER");
            var foreground = section.AddTextFrame();
            foreground.Width = Unit.FromPoint(180);
            foreground.Height = Unit.FromPoint(60);
            foreground.AddParagraph("FOREGROUNDMARKER");
            var firstBehind = section.AddTextFrame();
            firstBehind.Width = Unit.FromPoint(180);
            firstBehind.Height = Unit.FromPoint(60);
            firstBehind.AddParagraph("FIRSTBEHINDMARKER");
            var secondBehind = section.AddTextFrame();
            secondBehind.Width = Unit.FromPoint(180);
            secondBehind.Height = Unit.FromPoint(60);
            secondBehind.AddParagraph("SECONDBEHINDMARKER");
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.DocumentRenderer.TextFrameBehindContent = candidate =>
                ReferenceEquals(candidate, firstBehind) || ReferenceEquals(candidate, secondBehind);

            renderer.RenderDocument();

            renderer.PdfDocument.PageCount.Should().Be(1);
            var content = PageContent(renderer, 0);
            var firstIndex = content.IndexOf("FIRSTBEHINDMARKER", StringComparison.Ordinal);
            var secondIndex = content.IndexOf("SECONDBEHINDMARKER", StringComparison.Ordinal);
            var bodyIndex = content.IndexOf("BODYMARKER", StringComparison.Ordinal);
            var foregroundIndex = content.IndexOf("FOREGROUNDMARKER", StringComparison.Ordinal);
            firstIndex.Should().BeGreaterThanOrEqualTo(0);
            secondIndex.Should().BeGreaterThan(firstIndex);
            bodyIndex.Should().BeGreaterThan(secondIndex);
            foregroundIndex.Should().BeGreaterThan(bodyIndex);
        }

        [Fact]
        public void SelectedTextFrameOnSecondPageRendersAfterHeaderBeforeBody()
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var section = document.AddSection();
            section.Headers.Primary.AddParagraph("HEADERMARKER");
            section.AddParagraph("FIRSTPAGEMARKER");
            section.AddPageBreak();
            section.AddParagraph("SECONDPAGEMARKER");
            var frame = section.AddTextFrame();
            frame.RelativeVertical = MigraDoc.DocumentObjectModel.Shapes.RelativeVertical.Page;
            frame.Top = Unit.FromPoint(72);
            frame.Left = Unit.FromPoint(72);
            frame.Width = Unit.FromPoint(180);
            frame.Height = Unit.FromPoint(60);
            frame.AddParagraph("FRAMEMARKER");
            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.DocumentRenderer.TextFrameBehindContent = candidate => ReferenceEquals(candidate, frame);

            renderer.RenderDocument();

            renderer.PdfDocument.PageCount.Should().Be(2);
            var firstPage = PageContent(renderer, 0);
            var secondPage = PageContent(renderer, 1);
            firstPage.Should().Contain("FIRSTPAGEMARKER").And.NotContain("FRAMEMARKER");
            var headerIndex = secondPage.IndexOf("HEADERMARKER", StringComparison.Ordinal);
            var frameIndex = secondPage.IndexOf("FRAMEMARKER", StringComparison.Ordinal);
            var bodyIndex = secondPage.IndexOf("SECONDPAGEMARKER", StringComparison.Ordinal);
            headerIndex.Should().BeGreaterThanOrEqualTo(0);
            frameIndex.Should().BeGreaterThan(headerIndex);
            bodyIndex.Should().BeGreaterThan(frameIndex);
        }

        private static string PageContent(PdfDocumentRenderer renderer, int page)
            => string.Concat(renderer.PdfDocument.Pages[page].Contents.Elements
                .OfType<PdfSharp.Pdf.Advanced.PdfReference>()
                .Select(reference => reference.Value)
                .OfType<PdfSharp.Pdf.Advanced.PdfContent>()
                .Where(stream => stream.Stream != null)
                .Select(stream => Encoding.GetEncoding(28591).GetString(stream.Stream!.UnfilteredValue)));

        [Fact]
        public void SelectedPositionedTextFrameContinuesTableOnNextPage()
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var section = document.AddSection();
            section.PageSetup.TopMargin = Unit.FromPoint(72);
            section.PageSetup.BottomMargin = Unit.FromPoint(72);
            var frame = section.AddTextFrame();
            frame.RelativeHorizontal = RelativeHorizontal.Margin;
            frame.RelativeVertical = RelativeVertical.Margin;
            frame.Left = Unit.FromPoint(144);
            frame.Top = Unit.FromPoint(36);
            frame.Width = Unit.FromPoint(144);
            frame.WrapFormat.Style = WrapStyle.TopBottom;
            var table = frame.AddTable();
            table.AddColumn(Unit.FromPoint(144));
            for (var rowNumber = 1; rowNumber <= 60; rowNumber++)
                table.AddRow().Cells[0].AddParagraph($"Row {rowNumber}");

            var ordinary = new PdfDocumentRenderer { Document = (Document)document.Clone() };
            ordinary.RenderDocument();
            var renderer = new PdfDocumentRenderer { Document = document };
            frame.PaginateContent = true;
            renderer.RenderDocument();

            ordinary.PdfDocument.PageCount.Should().Be(1);
            renderer.PdfDocument.PageCount.Should().BeGreaterThan(1);
            var contents = Enumerable.Range(0, renderer.PdfDocument.PageCount)
                .Select(pageIndex => PdfFileHelper.GetPageContentStream(renderer.PdfDocument, pageIndex))
                .ToArray();
            contents.SelectMany(content => Regex.Matches(content, @"\(Row\) Tj").Cast<Match>())
                .Should().HaveCount(60);
            var rowNumbers = contents.SelectMany(content =>
                    Regex.Matches(content, @"\((\d+)\) Tj").Cast<Match>())
                .Select(match => int.Parse(match.Groups[1].Value))
                .ToArray();
            rowNumbers.Should().Equal(Enumerable.Range(1, 60));
        }

        [Fact]
        public void UnselectedPositionedTextFrameRetainsDefaultPageContent()
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var frame = document.AddSection().AddTextFrame();
            frame.Width = Unit.FromPoint(144);
            frame.AddParagraph("Ordinary frame");

            var unselectedDocument = (Document)document.Clone();
            var ordinary = new PdfDocumentRenderer { Document = document };
            ordinary.RenderDocument();
            var unselected = new PdfDocumentRenderer { Document = unselectedDocument };
            ((TextFrame)unselectedDocument.Sections[0].Elements[0]).PaginateContent = false;
            unselected.RenderDocument();

            unselected.PdfDocument.PageCount.Should().Be(ordinary.PdfDocument.PageCount);
            PdfFileHelper.GetPageContentStream(unselected.PdfDocument, 0)
                .Should().Be(PdfFileHelper.GetPageContentStream(ordinary.PdfDocument, 0));
        }

        [Fact]
        public void SelectedTopBottomImageMovesFollowingTextBelowItsBand()
        {
            static (double Top, string Content) Render(bool enableExclusion)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                var section = document.AddSection();
                section.PageSetup.TopMargin = Unit.FromPoint(72);
                section.AddParagraph("Before image");
                var image = section.AddImage(
                    "base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==");
                image.RelativeHorizontal = RelativeHorizontal.Margin;
                image.RelativeVertical = RelativeVertical.Margin;
                image.Top = Unit.FromPoint(30);
                image.Width = Unit.FromPoint(120);
                image.Height = Unit.FromPoint(90);
                image.WrapFormat.Style = WrapStyle.TopBottom;
                section.AddParagraph(string.Concat(Enumerable.Repeat(
                    "Words flowing around the image. ", 35)));
                var following = section.AddParagraph("Following image");
                var renderer = new PdfDocumentRenderer { Document = document };
                image.WrapFormat.ExcludeFollowingText = enableExclusion;
                renderer.RenderDocument();
                var top = renderer.DocumentRenderer.GetRenderInfoFromPage(1)!
                    .Single(info => ReferenceEquals(info.DocumentObject, following))
                    .LayoutInfo.ContentArea.Y.Point;
                var content = PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0);
                return (top, content);
            }

            var normal = Render(false);
            var selected = Render(true);

            selected.Top.Should().BeGreaterThan(normal.Top + 50);
            selected.Content.Should().Contain("Following");
            normal.Content.Should().Contain("Following");
        }

        [Fact]
        public void UnderlineAcrossSmallerFormattedSpaceUsesOneStroke()
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var paragraph = document.AddSection().AddParagraph();
            paragraph.Format.Font.Size = 12;
            paragraph.Format.Font.Underline = Underline.Single;
            paragraph.AddText("before");
            paragraph.AddFormattedText(" ").Font.Size = 8;
            paragraph.AddText("after");

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();

            var content = string.Concat(renderer.PdfDocument.Pages[0].Contents.Elements
                .OfType<PdfSharp.Pdf.Advanced.PdfReference>()
                .Select(reference => reference.Value)
                .OfType<PdfSharp.Pdf.Advanced.PdfContent>()
                .Where(stream => stream.Stream != null)
                .Select(stream => Encoding.Latin1.GetString(stream.Stream!.UnfilteredValue)));
            var underlines = Regex.Matches(content,
                @"(?m)(?<x1>[\d.]+) (?<y1>[\d.]+) m\s+(?<x2>[\d.]+) (?<y2>[\d.]+) l\s+S")
                .Where(match => match.Groups["y1"].Value == match.Groups["y2"].Value &&
                    match.Groups["x1"].Value != match.Groups["x2"].Value).ToArray();

            underlines.Should().ContainSingle();
            content.Should().Contain(".75 w");
        }

        [Theory]
        [InlineData(WrapStyle.Left, 72)]
        [InlineData(WrapStyle.Right, 301)]
        [InlineData(WrapStyle.Largest, 301)]
        public void PositionedImageExcludesTextOnSelectedSide(WrapStyle style, double expectedLeft)
        {
            var content = RenderSideWrappedImage(style);
            var lineStarts = new List<double>();
            var horizontal = 0.0;
            var vertical = 0.0;
            foreach (var line in content.Split('\n'))
            {
                if (line.Trim() == "BT")
                {
                    horizontal = 0;
                    vertical = 0;
                }
                var move = Regex.Match(line, @"^(?<dx>-?\d+(?:\.\d+)?) (?<dy>-?\d+(?:\.\d+)?) Td$");
                if (!move.Success)
                    continue;
                horizontal += double.Parse(move.Groups["dx"].Value, CultureInfo.InvariantCulture);
                vertical += double.Parse(move.Groups["dy"].Value, CultureInfo.InvariantCulture);
                if (vertical is > 550 and < 620 &&
                    double.Parse(move.Groups["dy"].Value, CultureInfo.InvariantCulture) != 0)
                    lineStarts.Add(horizontal);
            }
            lineStarts.Should().HaveCountGreaterOrEqualTo(2);
            lineStarts.Should().OnlyContain(position => Math.Abs(position - expectedLeft) < 0.1);
        }

        [Fact]
        public void PositionedImageUsesBothSidesOnSameLine()
        {
            var content = RenderSideWrappedImage(WrapStyle.BothSides);
            var horizontal = 0.0;
            var vertical = 0.0;
            var rightIntervalStarts = new List<double>();
            foreach (var line in content.Split('\n'))
            {
                if (line.Trim() == "BT")
                {
                    horizontal = 0;
                    vertical = 0;
                }
                var move = Regex.Match(line, @"^(?<dx>-?\d+(?:\.\d+)?) (?<dy>-?\d+(?:\.\d+)?) Td$");
                if (!move.Success)
                    continue;
                var deltaX = double.Parse(move.Groups["dx"].Value, CultureInfo.InvariantCulture);
                var deltaY = double.Parse(move.Groups["dy"].Value, CultureInfo.InvariantCulture);
                horizontal += deltaX;
                vertical += deltaY;
                if (vertical is > 550 and < 620 && deltaY == 0 && deltaX > 100)
                    rightIntervalStarts.Add(horizontal);
            }
            rightIntervalStarts.Should().HaveCountGreaterOrEqualTo(2);
            rightIntervalStarts.Should().OnlyContain(position => Math.Abs(position - 301) < 0.1);
        }

        [Fact]
        public void TwoPositionedImagesUseThreeIntervalsOnSameLine()
        {
            var content = RenderSideWrappedImage(WrapStyle.BothSides, secondImage: true);
            var horizontal = 0.0;
            var vertical = 0.0;
            var intervalStarts = new List<double>();
            foreach (var line in content.Split('\n'))
            {
                if (line.Trim() == "BT")
                {
                    horizontal = 0;
                    vertical = 0;
                }
                var move = Regex.Match(line, @"^(?<dx>-?\d+(?:\.\d+)?) (?<dy>-?\d+(?:\.\d+)?) Td$");
                if (!move.Success)
                    continue;
                var deltaX = double.Parse(move.Groups["dx"].Value, CultureInfo.InvariantCulture);
                var deltaY = double.Parse(move.Groups["dy"].Value, CultureInfo.InvariantCulture);
                horizontal += deltaX;
                vertical += deltaY;
                if (vertical is > 550 and < 620 && deltaY == 0 && deltaX > 100)
                    intervalStarts.Add(horizontal);
            }
            intervalStarts.Should().Contain(position => Math.Abs(position - 301) < 0.1);
            intervalStarts.Should().Contain(position => Math.Abs(position - 501) < 0.1);
        }

        [Fact]
        public void ConflictingSideWrappedImagesSkipOccupiedVerticalBand()
        {
            var content = RenderSideWrappedImage(WrapStyle.Left, secondImage: true,
                secondStyle: WrapStyle.Right);
            var verticalMoves = Regex.Matches(content,
                @"(?m)^-?\d+(?:\.\d+)? (?<vertical>-?\d+(?:\.\d+)?) Td$")
                .Select(match => double.Parse(match.Groups["vertical"].Value,
                    CultureInfo.InvariantCulture));

            verticalMoves.Should().Contain(move => move < -90);
        }

        [Fact]
        public void SideWrapSupportPreservesOrdinaryTableTextOrigin()
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var section = document.AddSection();
            section.PageSetup.LeftMargin = Unit.FromPoint(72);
            var table = section.AddTable();
            table.AddColumn(Unit.FromPoint(180));
            table.AddRow().Cells[0].AddParagraph("Table text");

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var content = string.Concat(renderer.PdfDocument.Pages[0].Contents.Elements
                .OfType<PdfSharp.Pdf.Advanced.PdfReference>()
                .Select(reference => reference.Value)
                .OfType<PdfSharp.Pdf.Advanced.PdfContent>()
                .Where(stream => stream.Stream != null)
                .Select(stream => Encoding.Latin1.GetString(stream.Stream!.UnfilteredValue)));
            var firstMove = Regex.Match(content,
                @"(?m)^(?<horizontal>-?\d+(?:\.\d+)?) -?\d+(?:\.\d+)? Td$");

            firstMove.Success.Should().BeTrue();
            double.Parse(firstMove.Groups["horizontal"].Value, CultureInfo.InvariantCulture)
                .Should().BeGreaterThan(70);
        }

        [Fact]
        public void PositionedImageJustifiesWithinBothSideIntervals()
        {
            var content = RenderSideWrappedImage(WrapStyle.BothSides, justify: true);
            var horizontal = 0.0;
            var vertical = 0.0;
            var wordPositions = new List<double>();
            foreach (var line in content.Split('\n'))
            {
                if (line.Trim() == "BT")
                {
                    horizontal = 0;
                    vertical = 0;
                }
                var move = Regex.Match(line, @"^(?<dx>-?\d+(?:\.\d+)?) (?<dy>-?\d+(?:\.\d+)?) Td$");
                if (!move.Success)
                    continue;
                horizontal += double.Parse(move.Groups["dx"].Value, CultureInfo.InvariantCulture);
                vertical += double.Parse(move.Groups["dy"].Value, CultureInfo.InvariantCulture);
                if (vertical is > 550 and < 620)
                    wordPositions.Add(horizontal);
            }
            wordPositions.Should().Contain(position => Math.Abs(position - 72) < 0.1);
            wordPositions.Should().Contain(position => Math.Abs(position - 301) < 0.1);
            wordPositions.Should().OnlyContain(position => position <= 540);
        }

        [Fact]
        public void PositionedImageDoesNotExcludeTextOnFollowingPage()
        {
            var content = RenderSideWrappedImage(WrapStyle.Right, pageIndex: 1,
                imageTop: 550, repetitions: 150);
            var firstLine = Regex.Match(content, @"(?m)^(?<horizontal>-?\d+(?:\.\d+)?) (?<vertical>-?\d+(?:\.\d+)?) Td$");

            firstLine.Success.Should().BeTrue();
            double.Parse(firstLine.Groups["horizontal"].Value, CultureInfo.InvariantCulture)
                .Should().BeApproximately(72, 0.1);
        }

        [Theory]
        [InlineData(144, 366)]
        [InlineData(0, 222)]
        public void PositionedTableFrameUsesBothSideIntervals(double frameLeft, double expectedRight)
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            document.Styles.Normal.Font.Size = 12;
            var section = document.AddSection();
            section.PageSetup.PageWidth = Unit.FromPoint(612);
            section.PageSetup.PageHeight = Unit.FromPoint(792);
            section.PageSetup.LeftMargin = Unit.FromPoint(72);
            section.PageSetup.RightMargin = Unit.FromPoint(72);
            section.PageSetup.TopMargin = Unit.FromPoint(72);
            section.PageSetup.BottomMargin = Unit.FromPoint(72);
            section.AddParagraph("Before floating table");
            var frame = section.AddTextFrame();
            frame.RelativeHorizontal = RelativeHorizontal.Margin;
            frame.RelativeVertical = RelativeVertical.Margin;
            frame.Left = Unit.FromPoint(frameLeft);
            frame.Top = Unit.FromPoint(36);
            frame.Width = Unit.FromPoint(144);
            frame.WrapFormat.Style = WrapStyle.BothSides;
            frame.WrapFormat.DistanceLeft = Unit.FromPoint(6);
            frame.WrapFormat.DistanceRight = Unit.FromPoint(6);
            var table = frame.AddTable();
            table.AddColumn(Unit.FromPoint(144));
            table.Borders.Width = Unit.FromPoint(1);
            table.AddRow().Cells[0].AddParagraph("Floating cell");
            table.AddRow().Cells[0].AddParagraph("Second cell");
            table.AddRow().Cells[0].AddParagraph("Third cell");
            section.AddParagraph(string.Concat(Enumerable.Repeat(
                "Following text flows beside the floating table. ", 18)));

            var renderer = new PdfDocumentRenderer { Document = document };
            renderer.RenderDocument();
            var content = string.Concat(renderer.PdfDocument.Pages[0].Contents.Elements
                .OfType<PdfSharp.Pdf.Advanced.PdfReference>()
                .Select(reference => reference.Value)
                .OfType<PdfSharp.Pdf.Advanced.PdfContent>()
                .Where(stream => stream.Stream != null)
                .Select(stream => Encoding.Latin1.GetString(stream.Stream!.UnfilteredValue)));

            var horizontal = 0.0;
            var vertical = 0.0;
            var rightIntervalStarts = new List<double>();
            foreach (var line in content.Split('\n'))
            {
                if (line.Trim() == "BT")
                {
                    horizontal = 0;
                    vertical = 0;
                }
                var move = Regex.Match(line, @"^(?<dx>-?\d+(?:\.\d+)?) (?<dy>-?\d+(?:\.\d+)?) Td$");
                if (!move.Success)
                    continue;
                var deltaX = double.Parse(move.Groups["dx"].Value, CultureInfo.InvariantCulture);
                var deltaY = double.Parse(move.Groups["dy"].Value, CultureInfo.InvariantCulture);
                horizontal += deltaX;
                vertical += deltaY;
                if (vertical is > 620 and < 690 &&
                    (frameLeft == 0 ? deltaY != 0 : deltaY == 0 && deltaX > 100))
                    rightIntervalStarts.Add(horizontal);
            }
            rightIntervalStarts.Count(position => Math.Abs(position - expectedRight) < 0.1)
                .Should().BeGreaterOrEqualTo(3);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void PositionedImageContourVariesSideIntervalsByLine(bool notched)
        {
            var contour = notched
                ? new[] { new PdfSharp.Drawing.XPoint(0, 0), new PdfSharp.Drawing.XPoint(1, 0),
                    new PdfSharp.Drawing.XPoint(1, .2), new PdfSharp.Drawing.XPoint(.56, .2),
                    new PdfSharp.Drawing.XPoint(.56, .75), new PdfSharp.Drawing.XPoint(1, .75),
                    new PdfSharp.Drawing.XPoint(1, 1), new PdfSharp.Drawing.XPoint(0, 1) }
                : DiamondContour();
            var content = RenderSideWrappedImage(WrapStyle.BothSides, contour: contour);
            var horizontal = 0.0;
            var vertical = 0.0;
            var rightIntervalStarts = new List<double>();
            foreach (var line in content.Split('\n'))
            {
                if (line.Trim() == "BT")
                {
                    horizontal = 0;
                    vertical = 0;
                }
                var move = Regex.Match(line, @"^(?<dx>-?\d+(?:\.\d+)?) (?<dy>-?\d+(?:\.\d+)?) Td$");
                if (!move.Success)
                    continue;
                var deltaX = double.Parse(move.Groups["dx"].Value, CultureInfo.InvariantCulture);
                var deltaY = double.Parse(move.Groups["dy"].Value, CultureInfo.InvariantCulture);
                horizontal += deltaX;
                vertical += deltaY;
                if (vertical is > 520 and < 630 && deltaY == 0 && deltaX > 100)
                    rightIntervalStarts.Add(horizontal);
            }
            rightIntervalStarts.Should().HaveCountGreaterOrEqualTo(3);
            rightIntervalStarts.Max().Should().BeGreaterThan(rightIntervalStarts.Min() +
                (notched ? 40 : 20));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void InvalidContourBandHeightsRetainDefaultIntervals(double bandHeight)
        {
            var ordinary = RenderSideWrappedImage(WrapStyle.BothSides, contour: DiamondContour());
            RenderSideWrappedImage(WrapStyle.BothSides, contour: DiamondContour(),
                contourBandHeight: bandHeight).Should().Be(ordinary);
        }

        [Fact]
        public void PositionedImageContourReceivesMixedLineFontHeights()
        {
            var fontHeights = new List<double>();
            RenderSideWrappedImage(WrapStyle.BothSides, contour: DiamondContour(), mixedFont: true,
                imageTop: 0,
                contourLineFontBandHeight: (height, fontHeight) =>
                {
                    if (fontHeight > 0)
                        fontHeights.Add(fontHeight);
                    return Math.Min(height, fontHeight + .25);
                });

            fontHeights.Should().NotBeEmpty();
            fontHeights.Max().Should().BeGreaterThan(fontHeights.Min() + 5);
        }

        [Fact]
        public void PositionedImageNaturalFontFitCanAdmitTerminalLine()
        {
            var ordinary = RenderSideWrappedImage(WrapStyle.BothSides, contour: DiamondContour(),
                pageHeight: 300, oneAndHalfSpacing: true);
            var adjusted = RenderSideWrappedImage(WrapStyle.BothSides, contour: DiamondContour(),
                pageHeight: 300, oneAndHalfSpacing: true,
                lineFitHeight: (_, fontHeight) => fontHeight);

            adjusted.Should().NotBe(ordinary);
            adjusted.Length.Should().BeGreaterThan(ordinary.Length);
        }

        [Fact]
        public void InvalidPositionedImageLineFitHeightsRetainDefaultContent()
        {
            var ordinary = RenderSideWrappedImage(WrapStyle.BothSides, contour: DiamondContour(),
                pageHeight: 300, oneAndHalfSpacing: true);
            foreach (var height in new[] { 0.0, -1.0, 100.0, double.NaN,
                         double.PositiveInfinity })
                RenderSideWrappedImage(WrapStyle.BothSides, contour: DiamondContour(),
                    pageHeight: 300, oneAndHalfSpacing: true,
                    lineFitHeight: (_, _) => height).Should().Be(ordinary);
        }

        static PdfSharp.Drawing.XPoint[] DiamondContour() =>
        [
            new(.5, 0), new(1, .5), new(.5, 1), new(0, .5)
        ];

        static string RenderSideWrappedImage(WrapStyle style, bool justify = false,
            int pageIndex = 0, double imageTop = 100, int repetitions = 35,
            bool secondImage = false, WrapStyle? secondStyle = null,
            IReadOnlyList<PdfSharp.Drawing.XPoint>? contour = null,
            double? contourBandHeight = null,
            Func<double, double, double?>? contourLineFontBandHeight = null,
            Func<double, double, double?>? lineFitHeight = null,
            bool mixedFont = false, double pageHeight = 792,
            bool oneAndHalfSpacing = false)
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            document.Styles.Normal.Font.Size = 12;
            var section = document.AddSection();
            section.PageSetup.PageWidth = Unit.FromPoint(612);
            section.PageSetup.PageHeight = Unit.FromPoint(pageHeight);
            section.PageSetup.LeftMargin = Unit.FromPoint(72);
            section.PageSetup.RightMargin = Unit.FromPoint(72);
            section.PageSetup.TopMargin = Unit.FromPoint(72);
            section.PageSetup.BottomMargin = Unit.FromPoint(72);
            section.AddParagraph("Before image");
            var image = section.AddImage(
                "base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==");
            image.RelativeHorizontal = RelativeHorizontal.Margin;
            image.RelativeVertical = RelativeVertical.Margin;
            image.Left = Unit.FromPoint(100);
            image.Top = Unit.FromPoint(imageTop);
            image.Width = Unit.FromPoint(120);
            image.Height = Unit.FromPoint(90);
            image.WrapFormat.Style = style;
            image.WrapFormat.DistanceLeft = Unit.FromPoint(9);
            image.WrapFormat.DistanceRight = Unit.FromPoint(9);
            if (secondImage)
            {
                var nextImage = section.AddImage(
                    "base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==");
                nextImage.RelativeHorizontal = RelativeHorizontal.Margin;
                nextImage.RelativeVertical = RelativeVertical.Margin;
                nextImage.Left = Unit.FromPoint(300);
                nextImage.Top = Unit.FromPoint(imageTop);
                nextImage.Width = Unit.FromPoint(120);
                nextImage.Height = Unit.FromPoint(90);
                nextImage.WrapFormat.Style = secondStyle ?? style;
                nextImage.WrapFormat.DistanceLeft = Unit.FromPoint(9);
                nextImage.WrapFormat.DistanceRight = Unit.FromPoint(9);
            }
            var paragraph = section.AddParagraph();
            if (mixedFont)
            {
                paragraph.AddFormattedText(string.Concat(Enumerable.Repeat(
                    "Words flowing around the image. ", 3))).Size = 9;
                paragraph.AddFormattedText(string.Concat(Enumerable.Repeat(
                    "Words flowing around the image. ", repetitions))).Size = 18;
            }
            else
                paragraph.AddText(string.Concat(Enumerable.Repeat(
                    "Words flowing around the image. ", repetitions)));
            if (justify)
                paragraph.Format.Alignment = ParagraphAlignment.Justify;
            if (oneAndHalfSpacing)
                paragraph.Format.LineSpacingRule = LineSpacingRule.OnePtFive;

            var renderer = new PdfDocumentRenderer { Document = document };
            if (contour != null)
                renderer.DocumentRenderer.WrapContour = shape => shape == image ? contour : null;
            if (contourBandHeight != null)
                renderer.DocumentRenderer.WrapContourBandHeight = (shape, _) =>
                    shape == image ? contourBandHeight : null;
            if (contourLineFontBandHeight != null)
                renderer.DocumentRenderer.WrapContourLineFontBandHeight = (shape, height, fontHeight) =>
                    shape == image ? contourLineFontBandHeight(height, fontHeight) : null;
            if (lineFitHeight != null)
                renderer.DocumentRenderer.WrapLineFitHeight = (shape, height, fontHeight) =>
                    shape == image ? lineFitHeight(height, fontHeight) : null;
            renderer.RenderDocument();
            return string.Concat(renderer.PdfDocument.Pages[pageIndex].Contents.Elements
                .OfType<PdfSharp.Pdf.Advanced.PdfReference>()
                .Select(reference => reference.Value)
                .OfType<PdfSharp.Pdf.Advanced.PdfContent>()
                .Where(stream => stream.Stream != null)
                .Select(stream => Encoding.Latin1.GetString(stream.Stream!.UnfilteredValue)));
        }

        [Fact]
        public void Surrogate_Pairs_Test()
        {
            // Create a MigraDoc document.
            var document = CreateDocument();

            // ----- Unicode encoding in MigraDoc is demonstrated here. -----

            //// A flag indicating whether to create a Unicode PDF or a WinAnsi PDF file.
            //// This setting applies to all fonts used in the PDF document.
            //// This setting has no effect on the RTF renderer.
            //const bool unicode = false;

            // Create a renderer for the MigraDoc document.
            var pdfRenderer = new PdfDocumentRenderer()
            {
                // Associate the MigraDoc document with a renderer.
                Document = document
            };

            // Layout and render document to PDF.
            pdfRenderer.RenderDocument();

            // Save the document…
            var filename = PdfFileUtility.GetTempPdfFullFileName("unittests/migradoc/text/HelloEmoji");
            pdfRenderer.PdfDocument.Save(filename);
            // … and start a viewer.
            PdfFileUtility.ShowDocumentIfDebugging(filename);

#if DEBUG___
            MigraDoc.DocumentObjectModel.IO.DdlWriter dw = new MigraDoc.DocumentObjectModel.IO.DdlWriter(filename + "_2.mdddl");
            dw.WriteDocument(document);
            dw.Close();
#endif
        }

        /// <summary>
        /// Creates an absolutely minimalistic document.
        /// </summary>
        static Document CreateDocument()
        {
            // Create a new MigraDoc document.
            var document = new Document();

            // Add a section to the document.
            var section = document.AddSection();

            // Add a paragraph to the section.
            var paragraph = section.AddParagraph();

            // Set font color.
            //paragraph.Format.Font.Color = Color.FromCmyk(100, 30, 20, 50);
            paragraph.Format.Font.Color = Colors.DarkBlue;

            // Add some text to the paragraph.
            paragraph.AddFormattedText("Hello, World!", TextFormat.Bold);

            // Create the primary footer.
            var footer = section.Footers.Primary;

            // Add content to footer.
            paragraph = footer.AddParagraph();
            paragraph.Add(new DateField { Format = "yyyy/MM/dd HH:mm:ss" });
            paragraph.Format.Alignment = ParagraphAlignment.Center;

            return document;
        }

        [Fact]
        public void Document_with_No_Break_Hyphen()
        {
            // Create a new MigraDoc document.
            var document = new Document();

            var section = document.AddSection();
            var paragraph = section.AddParagraph("No\u2011break\u2011hyphen-Test No\u2011break\u2011hyphen-Test No\u2011break\u2011hyphen-Test No\u2011break\u2011hyphen-Test 12345 " +
                                                 "No\u2011break\u2011hyphen-Test No\u2011break\u2011hyphen-Test No\u2011break\u2011hyphen-Test No\u2011break\u2011hyphen-Test 12345 " +
                                                 "No\u2011break\u2011hyphen-Test No\u2011break\u2011hyphen-Test No\u2011break\u2011hyphen-Test No\u2011break\u2011hyphen-Test 12345 ");
            paragraph.Format.Font.Bold = true;

            var filename = IOUtility.GetTempFullFileName("DocumentWithNoBreakHyphen", null);

            var pdfFilename = filename + ".pdf";
            var pdfRenderer = new PdfDocumentRenderer { Document = document };

            var hasReplaced = false;

            pdfRenderer.PdfDocument.RenderEvents.RenderTextEvent += (sender, args) =>
                {
                    var length = args.CodePointGlyphIndexPairs.Length;
                    for (var idx = 0; idx < length; idx++)
                    {
                        ref var item = ref args.CodePointGlyphIndexPairs[idx];
                        if (item is { GlyphIndex: 0, CodePoint: '\u2011' })
                        {
                            item.CodePoint = '-';
                            args.ReevaluateGlyphIndices = true;
                            hasReplaced = true;
                        }
                    }
                };
            pdfRenderer.RenderDocument();

            hasReplaced.Should().BeTrue();

            var pdfDocument = pdfRenderer.PdfDocument;
            pdfDocument.Options.CompressContentStreams = false;
            pdfRenderer.Save(pdfFilename);

            PdfFileUtility.ShowDocumentIfDebugging(pdfFilename);

            var rtfRenderer = new RtfDocumentRenderer();
            rtfRenderer.Render(document, filename + ".rtf", Environment.CurrentDirectory);


            // Analyze the drawn text in the PDF’s content stream.
            var streamEnumerator = PdfFileHelper.GetPageContentStreamEnumerator(pdfDocument, 0);

            streamEnumerator.Text.MoveAndGetNext(true, out var textInfo).Should().BeTrue();

            double lineYPosition = 0;

            var lastObjectEndsWithHyphen = false;
            var pageContainsHyphenBreak = false;

            do
            {
                var isNewLine = !textInfo!.IsAtYPosition(lineYPosition);
                if (isNewLine)
                    lineYPosition = textInfo.Y;

                var text = textInfo.Text;
                text.Should().NotBeNull();

                var isHex = textInfo.IsHex;
                if (isHex)
                {
                    var glyphIds = PdfFileHelper.GetHexStringAsGlyphIndices(text);
                    glyphIds.Should().NotContain("0", "no char (and no no-break hyphen) should be converted to an invalid glyph (\"0\")");
                }
                isHex.Should().BeFalse("for strings without not available characters we expect literal strings here for further investigation");

                var length = text.Length;
                var endsWithHyphen = text[length - 1] == '-';
                if (endsWithHyphen)
                    text[length - 2].Should().Be('n', "in text objects ending with a hyphen, the char before should be an 'n', because the other hyphens where no-break hyphens");

                if (isNewLine && lastObjectEndsWithHyphen)
                {
                    pageContainsHyphenBreak = true;
                    text.First().Should().Be('T', "a new line after a hyphen should continue with a 'T', because the other hyphens where no-break hyphens");
                }

                lastObjectEndsWithHyphen = endsWithHyphen;
            } while (streamEnumerator.Text.MoveAndGetNext(true, out textInfo));

            lastObjectEndsWithHyphen.Should().Be(false, "each line ending with a hyphen should be followed by another line");
            pageContainsHyphenBreak.Should().Be(true, "test should contain a line break at a hyphen to show that breaks occur at hyphens while not occurring at no-break hyphens");
        }

        [Fact]
        public void Document_with_No_Break_Hyphen_before_Tabs()
        {
            LogHost.Factory = LoggerFactory.Create(builder => builder.AddConsole());

            // Create a new MigraDoc document.
            var document = new Document();

            var style = document.Styles[StyleNames.Normal];
            style!.Font.Name = "Arial";

            style = document.Styles[StyleNames.Heading1];
            style!.Font.Size = 20;
            style.ParagraphFormat.TabStops.ClearAll();
            style.ParagraphFormat.TabStops.AddTabStop(Unit.FromCentimeter(2));

            style = document.Styles[StyleNames.Heading2];
            style!.Font.Size = 16;
            style.ParagraphFormat.TabStops.ClearAll();
            style.ParagraphFormat.TabStops.AddTabStop(Unit.FromCentimeter(2));

            var section = document.AddSection();

            var paragraph = section.AddParagraph("A");
            paragraph.Style = StyleNames.Heading1;
            paragraph.AddTab();
            paragraph.AddText("Heading level 1 without any hyphen");

            paragraph = section.AddParagraph("A-1");
            paragraph.AddTab();
            paragraph.AddText("Heading level 2 with usual hyphen");
            paragraph.Style = StyleNames.Heading2;

            paragraph = section.AddParagraph("A\u20111");
            paragraph.AddTab();
            paragraph.AddText("Heading level 2 with no-break hyphen replaced by hyphen character");
            paragraph.Style = StyleNames.Heading2;

            var filename = IOUtility.GetTempFullFileName("DocumentWithNoBreakHyphenBeforeTabs", null);

            var pdfFilename = filename + ".pdf";
            var pdfRenderer = new PdfDocumentRenderer { Document = document };

            pdfRenderer.PdfDocument.RenderEvents.RenderTextEvent += (sender, args) =>
            {
                var length = args.CodePointGlyphIndexPairs.Length;
                for (var idx = 0; idx < length; idx++)
                {
                    ref var item = ref args.CodePointGlyphIndexPairs[idx];
                    if (item is { CodePoint: '\u2011', GlyphIndex: 0 })
                    {
                        item.CodePoint = '-';
                        item.GlyphIndex = GlyphHelper.GlyphIndexFromCodePoint('-', args.Font);
                    }
                }
            };

            pdfRenderer.RenderDocument();

            var pdfDocument = pdfRenderer.PdfDocument;
            pdfDocument.Options.CompressContentStreams = false;
            pdfRenderer.Save(pdfFilename);

            PdfFileUtility.ShowDocumentIfDebugging(pdfFilename);

            var rtfRenderer = new RtfDocumentRenderer();
            rtfRenderer.Render(document, filename + ".rtf", Environment.CurrentDirectory);

            // Analyze the drawn text in the PDF’s content stream.
            var streamEnumerator = PdfFileHelper.GetPageContentStreamEnumerator(pdfDocument, 0);

            // Ensure that all Heading text objects are positioned at the same x value.
            streamEnumerator.Text.MoveAndGetNext(x => x.Text == "Heading", true, out var textInfo).Should().BeTrue();
            var firstHeadingTabX = textInfo!.X;

            streamEnumerator.Text.MoveAndGetNext(x => x.Text == "Heading", true, out textInfo).Should().BeTrue();
            textInfo!.IsAtXPosition(firstHeadingTabX).Should().BeTrue();

            streamEnumerator.Text.MoveAndGetNext(x => x.Text == "Heading", true, out textInfo).Should().BeTrue();
            textInfo!.IsAtXPosition(firstHeadingTabX).Should().BeTrue();
        }

        [Fact]
        public void DecimalTabulatorTest()
        {
            var cultureInfos = new[] { null, CultureInfo.GetCultureInfo("en-us"), CultureInfo.GetCultureInfo("de-de") };

            // Numbers separated by page breaks to let all numbers of a page start at the same x position.
            // For easier analysis, '1' should and should only occur at the first digit of each number.
            var numbersByPage = new List<List<double>>
            {
                new() { 1, 1.3, 1.33 },
                new() { 1234, 1234.4, 1234.44 },
                new() { .3 },
                new() { -1, -1.3, -1.33 },
                new() { -1234, -1234.4, -1234.44 },
                new() { -.3 }
            };

            var units = new[] { null, "%", " %", "mm", " mm" };

            // Simulate references to footnotes by superscript text numbers.
            var appendReferences = new[]
            {
                new Action<Paragraph>(_ => {}),
                // For numbers without decimal separator that are directly followed by a FormattedText containing a superscript number, e.g. "1⁹", Word interprets the superscript text as an exponent.
                // As a result the superscript is positioned before the decimal tabstop in RTF. The PDF renderer positions the superscript text at the decimal tabstop instead,
                // as it can currently take only a single Text object for alignment position calculation.
                // As the intention of the superscript text is not clear, none of these positions is wrong.
                // For alignment before the decimal tabstop as an exponent in PDF, use superscript digit unicode characters instead of FormattedText.
                // For alignment at the decimal tabstop as footnote reference in RTF, insert e.g. a Hair Space as first character of the superscript.
                new Action<Paragraph>(p => p.AddFormattedText("9").Superscript = true),
                new Action<Paragraph>(p =>
                {
                    var t = (Text)p.Elements.LastObject!;
                    t.Content += " ";
                    p.AddFormattedText("9").Superscript = true;
                })
            };

            var filenamePattern = IOUtility.GetTempFullFileName("DecimalTabulator{0}", "{1}");

            foreach (var cultureInfo in cultureInfos)
            {
                // Create a new MigraDoc document.
                var document = new Document { Culture = cultureInfo };

                var section = document.AddSection();

                for (var pageIdx = 0; pageIdx < numbersByPage.Count; pageIdx++)
                {
                    if (pageIdx != 0)
                        section.AddPageBreak();

                    var p = section.AddParagraph();
                    p.AddTab();
                    p.AddText(0.0.ToString("0.0", cultureInfo));
                    p.Format.TabStops.AddTabStop(Unit.FromCentimeter(3), TabAlignment.Decimal);

                    foreach (var number in numbersByPage[pageIdx])
                    {
                        // "#,###.##" is intended here to check if numbers with and without a digit before the decimal separator are aligned correctly at the decimal tabulator.
                        // Only the numbers in numbersByPage are formatted here, so the result won’t be an empty string, as it would be if '0' would be formatted with it.
                        var numberStr = number.ToString("#,###.##", cultureInfo);

                        foreach (var unit in units)
                        {
                            var numberAndUnitStr = numberStr + unit;

                            foreach (var appendReference in appendReferences)
                            {
                                p = section.AddParagraph();
                                p.AddTab();
                                p.AddText(numberAndUnitStr);
                                appendReference(p);
                                p.Format.TabStops.AddTabStop(Unit.FromCentimeter(3), TabAlignment.Decimal);
                            }
                        }
                    }
                }

                var pdfFilename = String.Format(filenamePattern, "-" + (cultureInfo?.Name ?? "CurrentCulture"), "pdf");
                var pdfRenderer = new PdfDocumentRenderer { Document = document };
                pdfRenderer.RenderDocument();

                var pdfDocument = pdfRenderer.PdfDocument;
                pdfDocument.Options.CompressContentStreams = false;
                pdfRenderer.Save(pdfFilename);

                PdfFileUtility.ShowDocumentIfDebugging(pdfFilename);

                // In RTF, decimal tabstop alignment is done in the viewing application and always depends on the system regional settings,
                // as culture or separators cannot be saved in rtf. Therefore, we don’t need to assign cultureInfo and output RTF only once.
                if (cultureInfo == null)
                {
                    var rtfFilename = String.Format(filenamePattern, null, "rtf");
                    var rtfRenderer = new RtfDocumentRenderer();
                    rtfRenderer.Render(document, rtfFilename, Environment.CurrentDirectory);
                }


                // Analyze the drawn text in the PDF’s content stream.

                // The horizontal position of the first line ("0.0").
                var firstLineX = 150.344;
                // The horizontal position of the other lines.
                var expectedSecondLineXOffsetsByPage = new[] { 0, -19.4629, 5.5615, -3.3301, -22.793, 2.2314 };

                for (var pageIdx = 0; pageIdx < expectedSecondLineXOffsetsByPage.Length; pageIdx++)
                {
                    var pageNr = pageIdx + 1;
                    var expectedSecondLineXOffset = expectedSecondLineXOffsetsByPage[pageIdx];
                    var expectedOtherLinesX = firstLineX + expectedSecondLineXOffset;

                    var expectedLineCount = pageNr % 3 == 0 ? 16 : 46; // Page 3 and 6 have fewer lines.
                    var lineCount = 0;
                    double lineYPosition = 0;
                    var lineHeight = 11.499;

                    var streamEnumerator = PdfFileHelper.GetPageContentStreamEnumerator(pdfDocument, pageIdx);
                    while (streamEnumerator.Text.MoveAndGetNext(true, out var textInfo))
                    {
                        var isNewLine = lineCount == 0 || textInfo!.IsAtYPosition(lineYPosition - lineHeight);
                        if (isNewLine)
                        {
                            lineCount++;
                            lineYPosition = textInfo!.Y;

                            if (lineCount == 1)
                            {
                                var isAtFirstLineX = textInfo.IsAtXPosition(firstLineX);
                                isAtFirstLineX.Should().BeTrue($"first text line’s (\"0.0\") horizontal offset should be '{firstLineX}' instead of '{textInfo.X}'");
                                continue;
                            }

                            var isAtOtherLinesX = textInfo.IsAtXPosition(expectedOtherLinesX);
                            isAtOtherLinesX.Should().BeTrue($"the other line’s horizontal offset should be '{expectedOtherLinesX}' instead of '{textInfo.X}'");
                        }
                    }

                    lineCount.Should().Be(expectedLineCount,
                        $"the expected number of text lines on page {pageNr} is {expectedLineCount}");
                }
            }
        }

        [Fact]
        public void FooterLayoutTest()
        {
            var document = new Document();
            var section = document.AddSection();
            section.PageSetup.PageWidth = "210mm";
            section.PageSetup.PageHeight = "297mm";
            section.PageSetup.TopMargin = 0;
            section.PageSetup.BottomMargin = Unit.FromCentimeter(3);
            section.PageSetup.FooterDistance = 0;
            section.PageSetup.HeaderDistance = 0;
            //for (int i = 0; i < 77; i++)
            //    Section.AddParagraph("paragraph " + i);
            var par1 = section.Footers.Primary.AddParagraph("Unexpected blank space after footer text 1");
            par1.Format.SpaceBefore = Unit.FromCentimeter(2);
            par1.Format.SpaceAfter = 0;
            //par1 = Section.Footers.Primary.AddParagraph("Unexpected blank space after footer text 2");
            //par1 = Section.Footers.Primary.AddParagraph("Unexpected blank space after footer text 3");
            var pdfRenderer = new PdfDocumentRenderer
            {
                Document = document,
                PdfDocument =
                {
                    // PageLayout = PdfPageLayout.SinglePage
                }
            };

            // Layout and render document to PDF.
            pdfRenderer.RenderDocument();

            // Save the document…
            var filename = PdfFileUtility.GetTempPdfFullFileName("unittests/migradoc/footer/FooterTest");
            pdfRenderer.PdfDocument.Save(filename);
            //// … and start a viewer.
            //Process.Sta/rt(new ProcessStartInfo(filename) { UseShellExecute = true });
        }

        [Theory]
        [InlineData(TextOrientation.Horizontal)]
        [InlineData(TextOrientation.Downward)]
        [InlineData(TextOrientation.Upward)]
        public void SelectedTextFrameClipsContentWithoutChangingTheFrame(TextOrientation orientation)
        {
            string Render(bool? clip)
            {
                var document = new Document();
                document.Styles.Normal.Font.Name = "Arial";
                var frame = document.AddSection().AddTextFrame();
                frame.Width = Unit.FromPoint(120);
                frame.Height = Unit.FromPoint(45);
                frame.Orientation = orientation;
                frame.LineFormat.Visible = true;
                frame.AddParagraph("Text that continues beyond the fixed frame height " +
                    "and should only be clipped when selected.");
                var renderer = new PdfDocumentRenderer { Document = document };
                if (clip is bool selected)
                    renderer.DocumentRenderer.ClipTextFrameContent = candidate => selected && ReferenceEquals(candidate, frame);
                renderer.RenderDocument();
                renderer.PdfDocument.PageCount.Should().Be(1);
                return string.Concat(renderer.PdfDocument.Pages[0].Contents.Elements
                    .OfType<PdfSharp.Pdf.Advanced.PdfReference>()
                    .Select(reference => reference.Value)
                    .OfType<PdfSharp.Pdf.Advanced.PdfContent>()
                    .Where(stream => stream.Stream != null)
                    .Select(stream => Encoding.Latin1.GetString(stream.Stream!.UnfilteredValue)));
            }

            var normal = Render(null);
            var unselected = Render(false);
            var clipped = Render(true);
            unselected.Should().Be(normal);
            normal.Should().NotContain("W* n");
            clipped.Should().Contain("W* n");
            clipped.Should().Contain("(Text) Tj");
            Regex.Matches(clipped, @"(?m)^-?[\d.]+ -?[\d.]+ [\d.]+ [\d.]+ re$").Count
                .Should().BeGreaterThan(0);
            Regex.IsMatch(clipped, @"W\* n(?s:.*?)Q(?s:.*?)re\s+S").Should().BeTrue();
        }

        [Fact]
        public void TextFrameClipContentSurvivesDdlAndOverridesLegacySelector()
        {
            var document = new Document();
            document.Styles.Normal.Font.Name = "Arial";
            var frame = document.AddSection().AddTextFrame();
            frame.Width = Unit.FromPoint(120);
            frame.Height = Unit.FromPoint(45);
            frame.AddParagraph("Clipped text");
            frame.ClipContent = true;

            frame.Clone().ClipContent.Should().BeTrue();
            var ddl = MigraDoc.DocumentObjectModel.IO.DdlWriter.WriteToString(document);
            var restored = MigraDoc.DocumentObjectModel.IO.DdlReader.DocumentFromString(ddl);
            var restoredFrame = restored.Sections[0].Elements.OfType<TextFrame>().Single();
            restoredFrame.ClipContent.Should().BeTrue();

            var renderer = new PdfDocumentRenderer { Document = restored };
            renderer.DocumentRenderer.ClipTextFrameContent = _ => false;
            renderer.RenderDocument();
            var content = string.Concat(renderer.PdfDocument.Pages[0].Contents.Elements
                .OfType<PdfSharp.Pdf.Advanced.PdfReference>()
                .Select(reference => reference.Value)
                .OfType<PdfSharp.Pdf.Advanced.PdfContent>()
                .Where(stream => stream.Stream != null)
                .Select(stream => Encoding.Latin1.GetString(stream.Stream!.UnfilteredValue)));
            content.Should().Contain("W* n");
        }
    }
}
