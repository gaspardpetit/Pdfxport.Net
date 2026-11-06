// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel.IO;
using MigraDoc.DocumentObjectModel.Visitors;
using Xunit;

namespace MigraDoc.DocumentObjectModel.Tests;

public class UnderlineColorTests
{
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
