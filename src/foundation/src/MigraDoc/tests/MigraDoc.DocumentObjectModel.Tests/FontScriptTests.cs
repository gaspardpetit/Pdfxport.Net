// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel.IO;
using MigraDoc.DocumentObjectModel.Visitors;
using Xunit;

namespace MigraDoc.DocumentObjectModel.Tests;

public class FontScriptTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeactivatingOppositeScriptKeepsActiveScript(bool superscript)
    {
        var font = new Font();
        SetScript(font, superscript, true);
        SetScript(font, !superscript, false);
        AssertScript(font, superscript);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActivatingOppositeScriptLeavesOnlyTheLastScriptActive(bool superscript)
    {
        var font = new Font();
        SetScript(font, !superscript, true);
        SetScript(font, superscript, true);
        AssertScript(font, superscript);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DisablingScriptDoesNotCreateAnOppositeOverride(bool superscript)
    {
        var font = new Font();
        SetScript(font, superscript, false);
        (superscript ? font.Values.Subscript : font.Values.Superscript).Should().BeNull();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ChildScriptOverridesOppositeInheritedScript(bool superscript, bool useFontOverload)
    {
        var document = new Document();
        var paragraph = document.AddSection().AddParagraph();
        SetScript(paragraph.Format.Font, !superscript, true);
        var font = new Font();
        SetScript(font, superscript, true);
        var child = useFontOverload ? paragraph.AddFormattedText(font) : paragraph.AddFormattedText();
        if (!useFontOverload)
            SetScript(child.Font, superscript, true);
        child.AddText("script");

        new PdfFlattenVisitor().Visit(document);

        AssertScript(child.Font, superscript);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CloneAndDdlRoundtripPreserveExplicitScriptOverrides(bool superscript)
    {
        var document = new Document();
        var child = document.AddSection().AddParagraph().AddFormattedText("script");
        SetScript(child.Font, superscript, true);

        var clone = child.Font.Clone();
        AssertOverrides(clone, superscript);
        var restored = DdlReader.DocumentFromString(DdlWriter.WriteToString(document))
            ?? throw new InvalidOperationException("The document must survive DDL roundtrip.");
        var restoredChild = restored.LastSection.LastParagraph!.Elements[0] as FormattedText
            ?? throw new InvalidOperationException("The formatted text must survive DDL roundtrip.");
        AssertOverrides(restoredChild.Font, superscript);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyFontCopiesActiveScriptAndOppositeOverride(bool superscript)
    {
        var source = new Font();
        SetScript(source, superscript, true);
        var target = new Font();

        target.ApplyFont(source);

        AssertOverrides(target, superscript);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ApplyFontCanDisableOppositeScriptWithoutClearingActiveScript(bool superscript)
    {
        var target = new Font();
        SetScript(target, superscript, true);
        var source = new Font();
        SetScript(source, !superscript, false);

        target.ApplyFont(source);

        AssertScript(target, superscript);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ReferenceAwareApplyFontCopiesSourceScriptsIntoAnUnsetTarget(bool superscript, bool useReference)
    {
        var source = new Font();
        SetScript(source, superscript, true);
        var reference = new Font();
        SetScript(reference, !superscript, true);
        var target = new Font();

        target.ApplyFont(source, useReference ? reference : null);

        AssertOverrides(target, superscript);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferenceAwareApplyFontSkipsMatchingScriptProperties(bool superscript)
    {
        var source = new Font();
        SetScript(source, superscript, true);
        var target = new Font();

        target.ApplyFont(source, source.Clone());

        target.Values.Superscript.Should().BeNull();
        target.Values.Subscript.Should().BeNull();
    }

    static void SetScript(Font font, bool superscript, bool value)
    {
        if (superscript)
            font.Superscript = value;
        else
            font.Subscript = value;
    }

    static void AssertScript(Font font, bool superscript)
    {
        font.Superscript.Should().Be(superscript);
        font.Subscript.Should().Be(!superscript);
    }

    static void AssertOverrides(Font font, bool superscript)
    {
        AssertScript(font, superscript);
        font.Values.Superscript.Should().Be(superscript);
        font.Values.Subscript.Should().Be(!superscript);
    }
}
