// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using MigraDoc.DocumentObjectModel.Visitors;
using Xunit;

namespace MigraDoc.DocumentObjectModel.Tests;

public class TextTagTests
{
    [Theory]
    [InlineData("plain", false)]
    [InlineData("one two", false)]
    [InlineData("one-two", false)]
    [InlineData("one\u00ADtwo", false)]
    [InlineData("one\u200Btwo", false)]
    [InlineData("one\u200Ctwo", false)]
    [InlineData("one\ttwo", false)]
    [InlineData(" one  two ", false)]
    [InlineData("plain", true)]
    [InlineData("one two", true)]
    [InlineData("one-two", true)]
    [InlineData("one\u00ADtwo", true)]
    [InlineData("one\u200Btwo", true)]
    [InlineData("one\u200Ctwo", true)]
    [InlineData("one\ttwo", true)]
    [InlineData(" one  two ", true)]
    public void PdfPreparationRetainsTagOnEveryReplacementText(string content, bool nested)
    {
        var document = new Document();
        var paragraph = document.AddSection().AddParagraph();
        var formatted = nested ? paragraph.AddFormattedText() : null;
        var before = formatted != null ? formatted.AddText("before") : paragraph.AddText("before");
        var source = new Text(content);
        (formatted != null ? formatted.Elements : paragraph.Elements).Add(source);
        var after = formatted != null ? formatted.AddText("after") : paragraph.AddText("after");
        var tag = new object();
        var neighborTag = new object();
        source.Tag = tag;
        after.Tag = neighborTag;
        if (formatted != null)
            formatted.Tag = new object();

        new PdfFlattenVisitor().Visit(document);

        var elements = formatted != null ? formatted.Elements : paragraph.Elements;
        var texts = elements.Cast<DocumentObject>().OfType<Text>().ToArray();
        texts.First().Content.Should().Be(before.Content);
        texts.First().Tag.Should().BeNull();
        texts.Last().Content.Should().Be(after.Content);
        texts.Last().Tag.Should().BeSameAs(neighborTag);
        var fragments = texts.Skip(1).Take(texts.Length - 2).ToArray();
        fragments.Should().NotBeEmpty();
        fragments.Should().OnlyContain(text => ReferenceEquals(text.Tag, tag));
        string.Concat(fragments.Select(text => text.Content)).Should().Be(content.Replace('\t', ' ').Replace("\u200B", "").Replace("\u200C", ""));
    }

    [Fact]
    public void UntaggedTextDoesNotAcquireItsContainersTag()
    {
        var document = new Document();
        var paragraph = document.AddSection().AddParagraph();
        var formatted = paragraph.AddFormattedText("one two");
        paragraph.Tag = new object();
        formatted.Tag = new object();

        new PdfFlattenVisitor().Visit(document);

        formatted.Elements.Cast<DocumentObject>().OfType<Text>()
            .Should().OnlyContain(text => text.Tag == null);
    }
}
