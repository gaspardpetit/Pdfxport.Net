using System.Globalization;
using System.Text.RegularExpressions;
using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.Snippets.Font;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class ImageOnlyCellHeightTests : IDisposable
{
    public ImageOnlyCellHeightTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }

    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(10, 48)]
    [InlineData(20, 48)]
    public void TallImageOnlyRowsUseImageHeight(double fontSize, double imageHeight)
    {
        var images = Render(true, fontSize, imageHeight, LineSpacingRule.Single);
        (images[0] - images[1]).Should().BeApproximately(imageHeight, 0.001);
    }

    [Theory]
    [InlineData(LineSpacingRule.Single, 48)]
    [InlineData(LineSpacingRule.Single, 5)]
    [InlineData(LineSpacingRule.AtLeast, 48)]
    [InlineData(LineSpacingRule.AtLeast, 5)]
    [InlineData(LineSpacingRule.Exactly, 48)]
    [InlineData(LineSpacingRule.Exactly, 5)]
    public void FirstImageRetainsItsBodyParagraphPaintPosition(LineSpacingRule rule, double height)
    {
        var table = Render(true, 10, height, rule);
        var body = Render(false, 10, height, rule);
        table[0].Should().BeApproximately(body[0], 0.001);
        if (rule != LineSpacingRule.Single || height == 5)
            (table[0] - table[1]).Should().BeApproximately(body[0] - body[1], 0.001);
    }

    [Theory]
    [InlineData(40)]
    [InlineData(49)]
    [InlineData(60)]
    public void MinimumSpacingCompensatesOnlyRemovedHeight(double minimum)
    {
        var table = Render(true, 10, 48, LineSpacingRule.AtLeast, minimum);
        var body = Render(false, 10, 48, LineSpacingRule.AtLeast, minimum);
        (table[0] - table[1]).Should().BeApproximately(Math.Max(48, minimum), 0.001);
        table[0].Should().BeApproximately(body[0], 0.001);
    }

    [Theory]
    [InlineData(LineSpacingRule.Single)]
    [InlineData(LineSpacingRule.Exactly)]
    [InlineData(LineSpacingRule.AtLeast)]
    public void MixedTextAndImageCellRetainsBodyLineMetrics(LineSpacingRule rule)
    {
        Render(true, 10, 48, rule, mixedText: true)
            .Should().Equal(Render(false, 10, 48, rule, mixedText: true));
    }

    [Theory]
    [InlineData(LineSpacingRule.Single, 40, 40)]
    [InlineData(LineSpacingRule.Single, 49, 49)]
    [InlineData(LineSpacingRule.Single, 60, 60)]
    [InlineData(LineSpacingRule.Single, 60, 40)]
    [InlineData(LineSpacingRule.Single, 40, 60)]
    [InlineData(LineSpacingRule.AtLeast, 40, 40)]
    [InlineData(LineSpacingRule.AtLeast, 49, 49)]
    [InlineData(LineSpacingRule.AtLeast, 60, 60)]
    public void MinimumFontMetricsPreserveImagePaintPosition(LineSpacingRule rule,
        double lineHeight, double inherentSpace)
    {
        var table = Render(true, 10, 48, rule, 49, minimumMetrics: (lineHeight, inherentSpace));
        var body = Render(false, 10, 48, rule, 49, minimumMetrics: (lineHeight, inherentSpace));
        table[0].Should().BeApproximately(body[0], 0.001);
        var expectedPitch = Math.Max(rule == LineSpacingRule.AtLeast ? 49 : 48, lineHeight);
        (table[0] - table[1]).Should().BeApproximately(expectedPitch, 0.001);
    }

    [Theory]
    [InlineData(double.NaN, 60)]
    [InlineData(60, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, 60)]
    [InlineData(0, 60)]
    [InlineData(60, -1)]
    public void InvalidMinimumFontMetricsPreserveDefaultPaint(double lineHeight, double inherentSpace)
    {
        Render(true, 10, 48, LineSpacingRule.Single, minimumMetrics: (lineHeight, inherentSpace))
            .Should().Equal(Render(true, 10, 48, LineSpacingRule.Single));
    }

    static double[] Render(bool inTable, double fontSize, double imageHeight, LineSpacingRule rule,
        double spacing = 80, bool mixedText = false,
        (double LineHeight, double InherentLineSpace)? minimumMetrics = null)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        document.Styles.Normal.Font.Size = fontSize;
        document.Styles.Normal.ParagraphFormat.LineSpacingRule = rule;
        document.Styles.Normal.ParagraphFormat.LineSpacing = Unit.FromPoint(spacing);
        var section = document.AddSection();
        var table = inTable ? section.AddTable() : null;
        if (table != null)
        {
            table.AddColumn(Unit.FromPoint(200));
            table.TopPadding = table.BottomPadding = Unit.FromPoint(0);
            table.LeftPadding = table.RightPadding = Unit.FromPoint(0);
        }
        for (var index = 0; index < 2; index++)
        {
            var paragraph = table != null ? table.AddRow()[0].AddParagraph() : section.AddParagraph();
            var image = paragraph.AddImage(IOUtility.GetAssetsPath(@"pdfsharp\images\samples\png\color8A.png")!);
            image.Width = Unit.FromPoint(40);
            image.Height = Unit.FromPoint(imageHeight);
            if (mixedText)
                paragraph.AddText(" text");
        }
        var renderer = new PdfDocumentRenderer { Document = document };
        if (minimumMetrics is not null)
            renderer.DocumentRenderer.MinimumLineMetricsForFont = (_, _) => minimumMetrics;
        renderer.RenderDocument();
        renderer.PageCount.Should().Be(1);
        var content = PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0);
        var matches = Regex.Matches(content, @"(?<bottom>-?[\d.]+) cm /I\d+ Do");
        matches.Count.Should().Be(2);
        return matches.Select(match => double.Parse(match.Groups["bottom"].Value,
            CultureInfo.InvariantCulture)).ToArray();
    }
}
