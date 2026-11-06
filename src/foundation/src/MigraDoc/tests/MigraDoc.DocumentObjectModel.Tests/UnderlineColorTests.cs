// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel.IO;
using MigraDoc.DocumentObjectModel.Visitors;
using Xunit;

namespace MigraDoc.DocumentObjectModel.Tests;

public class UnderlineColorTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DerivedStylePreservesDistinctCmykInkSeparationsThroughDdl(bool rgbOverride)
    {
        var baseColor = new Color(100.0, 100.0, 100.0, 0.0);
        var overrideColor = rgbOverride ? Colors.Black : new Color(0.0, 0.0, 0.0, 100.0);
        baseColor.Argb.Should().Be(overrideColor.Argb);
        baseColor.Should().NotBe(overrideColor);
        var document = new Document();
        document.Styles.AddStyle("BaseUnderline", StyleNames.Normal).Font.UnderlineColor = baseColor;
        document.Styles.AddStyle("DerivedUnderline", "BaseUnderline").Font.UnderlineColor = overrideColor;
        var restored = DdlReader.DocumentFromString(DdlWriter.WriteToString(document))!;
        restored.Styles["DerivedUnderline"]!.Font.Values.UnderlineColor.Should().Be(overrideColor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferenceAwareApplyFontPreservesDistinctCmykInkSeparations(bool rgbOverride)
    {
        var reference = new Font { UnderlineColor = new Color(100.0, 100.0, 100.0, 0.0) };
        var source = new Font { UnderlineColor = rgbOverride ? Colors.Black : new Color(0.0, 0.0, 0.0, 100.0) };
        source.UnderlineColor.Argb.Should().Be(reference.UnderlineColor.Argb);
        var target = reference.Clone();
        target.ApplyFont(source, reference);
        target.UnderlineColor.Should().Be(source.UnderlineColor);
    }

    [Fact]
    public void EmptyAndCloneValuesRetainIndependentColor()
    {
        var font = new Font();
        font.UnderlineColor.IsEmpty.Should().BeTrue();
        font.UnderlineColor = Colors.Blue;
        var clone = font.Clone();
        font.UnderlineColor = Color.Empty;
        font.Values.UnderlineColor.Should().BeNull();
        clone.UnderlineColor.Should().Be(Colors.Blue);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyFontCopiesColorAndIgnoresUnsetSource(bool referenceAware)
    {
        var source = new Font { UnderlineColor = Colors.Blue };
        var target = new Font();
        if (referenceAware)
            target.ApplyFont(source, new Font());
        else
            target.ApplyFont(source);
        target.UnderlineColor.Should().Be(Colors.Blue);
        target.ApplyFont(new Font());
        target.UnderlineColor.Should().Be(Colors.Blue);
    }

    [Fact]
    public void ColorOnlyFormattedTextSurvivesDdlRoundtrip()
    {
        var document = new Document();
        var child = document.AddSection().AddParagraph().AddFormattedText("text");
        child.Font.UnderlineColor = Colors.Blue;
        var ddl = DdlWriter.WriteToString(document);
        ddl.Should().Contain("UnderlineColor");
        var restored = DdlReader.DocumentFromString(ddl)!;
        ((FormattedText)restored.LastSection.LastParagraph!.Elements[0]).Font.UnderlineColor
            .Should().Be(Colors.Blue);
    }

    [Fact]
    public void NestedTextInheritsColorButExplicitColorOverridesIt()
    {
        var document = new Document();
        var paragraph = document.AddSection().AddParagraph();
        paragraph.Format.Font.UnderlineColor = Colors.Blue;
        var inherited = paragraph.AddFormattedText("inherited");
        var explicitColor = paragraph.AddFormattedText("explicit");
        explicitColor.Font.UnderlineColor = Colors.Green;
        new PdfFlattenVisitor().Visit(document);
        inherited.Font.UnderlineColor.Should().Be(Colors.Blue);
        explicitColor.Font.UnderlineColor.Should().Be(Colors.Green);
    }
}
