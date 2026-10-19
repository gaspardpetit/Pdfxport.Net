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
public class InlineImageOffsetTests : IDisposable
{
    public InlineImageOffsetTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }

    public void Dispose() => PdfSharpCore.ResetAll();

    [Fact]
    public void InlineImageOffsetMovesOnlyTheImagePaint()
    {
        var normal = Render(null);
        var shifted = Render(2.5);
        (normal.ImageBottom - shifted.ImageBottom).Should().BeApproximately(2.5, 0.0002);
        normal.OtherContent.Should().Be(shifted.OtherContent);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void NonFiniteInlineImageOffsetPreservesDefaultContent(double offset)
    {
        Render(offset).OtherContent.Should().Be(Render(null).OtherContent);
        Render(offset).ImageBottom.Should().Be(Render(null).ImageBottom);
    }

    static (double ImageBottom, string OtherContent) Render(double? offset)
    {
        var document = new Document();
        document.Styles.Normal.Font.Name = "Arial";
        var section = document.AddSection();
        section.AddParagraph("Before");
        var paragraph = section.AddParagraph();
        paragraph.Format.LineSpacingRule = LineSpacingRule.AtLeast;
        paragraph.Format.LineSpacing = Unit.FromPoint(60);
        var path = IOUtility.GetAssetsPath(@"pdfsharp\images\samples\png\color8A.png")!;
        var image = paragraph.AddImage(path);
        image.Width = Unit.FromPoint(72);
        image.Height = Unit.FromPoint(48);
        section.AddParagraph("After");
        var renderer = new PdfDocumentRenderer { Document = document };
        if (offset is not null)
            renderer.DocumentRenderer.InlineImageVerticalOffset = candidate =>
                ReferenceEquals(candidate, image) ? offset : null;
        renderer.RenderDocument();
        var content = PdfFileHelper.GetPageContentStream(renderer.PdfDocument, 0);
        var imageTransform = Regex.Match(content,
            @"(?<bottom>-?[\d.]+) cm /I\d+ Do");
        imageTransform.Success.Should().BeTrue();
        return (double.Parse(imageTransform.Groups["bottom"].Value,
                    CultureInfo.InvariantCulture),
                content.Replace(imageTransform.Value, "IMAGE"));
    }
}
