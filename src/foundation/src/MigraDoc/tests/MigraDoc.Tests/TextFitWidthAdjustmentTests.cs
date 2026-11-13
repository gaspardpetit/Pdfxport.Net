// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.
using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.Rendering;
using PdfSharp.Diagnostics;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Quality;
using PdfSharp.TestHelper;
using Xunit;

namespace MigraDoc.Tests;

[Collection("PDFsharp")]
public class TextFitWidthAdjustmentTests : IDisposable
{
    public TextFitWidthAdjustmentTests()
    {
        PdfSharpCore.ResetAll();
#if CORE
        GlobalFontSettings.FontResolver = new UnitTestFontResolver();
#endif
    }
    public void Dispose() => PdfSharpCore.ResetAll();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignedAdjustmentsAccumulateAcrossWords(bool positive)
    {
        var width = Width("Alpha Bravo") + (positive ? 1 : -1);
        var ordinary = Render(width);
        var adjusted = Render(width, adjust: (_, _, _) => positive ? .6 : -.6);
        SameLine(ordinary, "Alpha", "Bravo").Should().Be(positive);
        SameLine(adjusted, "Alpha", "Bravo").Should().Be(!positive);
        if (!positive)
            adjusted.Should().Equal(Render(width + 20));
    }

    [Fact]
    public void JoinedFormattingRunsUseEveryFragmentsAdjustment()
    {
        var width = Width("Alpha Bravo") - 1;
        SameLine(Render(width, split: true), "Alpha", "vo").Should().BeFalse();
        var result = Render(width, split: true, adjust: (_, _, _) => -.4);
        SameLine(result, "Alpha", "vo").Should().BeTrue();
        result.Should().Equal(Render(width + 20, split: true));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0.0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-1000.0)]
    public void InvalidOrEmptyAdjustmentsPreserveOutput(double? value)
    {
        var width = Width("Alpha Bravo") - 1;
        Render(width, adjust: (_, _, _) => value).Should().Equal(Render(width));
    }

    [Theory]
    [InlineData(ParagraphAlignment.Left)]
    [InlineData(ParagraphAlignment.Right)]
    [InlineData(ParagraphAlignment.Center)]
    [InlineData(ParagraphAlignment.Justify)]
    public void PaintingAndAlignmentUseMeasuredAdvances(ParagraphAlignment alignment)
    {
        var width = Width("Alpha Bravo") + 1;
        Render(width, suffix: " Delta", alignment: alignment, adjust: (_, _, _) => -.2)
            .Should().Equal(Render(width, suffix: " Delta", alignment: alignment));
    }

    [Fact]
    public void CallbackReceivesLeafTagAndEffectiveFontButNoOrdinaryBlanks()
    {
        var calls = new List<string>();
        Render(Width("Alpha Bravo") + 20, split: true, adjust: (tag, text, font) =>
        {
            text.Should().NotBe(" ");
            tag.Should().Be(text);
            font.Size.Should().Be(12);
            calls.Add(text);
            return null;
        });
        calls.Should().Contain(new[] { "Alpha", "Bra", "vo" });
    }

    [Fact]
    public void ExplicitLineBreakResetsAccumulatedAdjustment()
    {
        var width = Width("Alpha Bravo") - 1;
        var result = Render(width, prefixBreak: true, adjust: (_, text, _) => text == "Prefix" ? -10 : null);
        SameLine(result, "Alpha", "Bravo").Should().BeFalse();
        result.Should().Equal(Render(width, prefixBreak: true));
    }

    [Fact]
    public void AutomaticLineBreakResetsAccumulatedAdjustment()
    {
        var width = Width("Alpha Bravo") - 1;
        var result = Render(width, prefixWrap: true, adjust: (_, text, _) => text == "Prefix" ? -5 : null);
        SameLine(result, "Alpha", "Bravo").Should().BeFalse();
        result.Should().Equal(Render(width, prefixWrap: true));
    }

    [Fact]
    public void LeftTabDiscardsThePrecedingAdjustment()
    {
        var width = 120 + Width("Alpha Bravo") - 1;
        Render(width, tab: TabAlignment.Left, adjust: (_, text, _) => text == "Prefix" ? -10 : null)
            .Should().Equal(Render(width, tab: TabAlignment.Left));
    }

    [Fact]
    public void LeftTabProbeRestoresAdjustmentsBeforeActualFormatting()
    {
        var width = 120 + Width("Alpha Bravo") + 1;
        var result = Render(width, tab: TabAlignment.Left,
            adjust: (_, text, _) => text == "Prefix" ? null : .4);
        SameLine(result, "Alpha", "Bravo").Should().BeTrue();
        result.Should().Equal(Render(width, tab: TabAlignment.Left));
    }

    [Theory]
    [InlineData(TabAlignment.Left)]
    [InlineData(TabAlignment.Right)]
    [InlineData(TabAlignment.Center)]
    [InlineData(TabAlignment.Decimal)]
    public void TabProbingDoesNotAccumulateAdjustmentsTwice(TabAlignment tab)
    {
        var width = 120 + Width("Alpha Bravo") + 20;
        Render(width, tab: tab, adjust: (_, _, _) => -.2)
            .Should().Equal(Render(width, tab: tab));
    }

    [Fact]
    public void SoftHyphenProbingRestoresFittingCursor()
    {
        var width = Width("Alpha Bravo") + 1;
        Render(width, softHyphen: true, adjust: (_, text, _) => text == "Alpha" ? null : .4)
            .Should().Equal(Render(width, softHyphen: true));
    }

    [Fact]
    public void SideExclusionTransitionStartsAFreshInterval()
    {
        var width = 60 + 40 + Width("Alpha Bravo") - 1;
        var ordinary = Render(width, wrap: true);
        Render(width, wrap: true, adjust: (_, text, _) => text == "Prefix" ? -2 : null)
            .Should().Equal(ordinary);
        ordinary.Single(w => w.Text == "Alpha").X.Should().BeGreaterThan(100);
    }

    [Theory]
    [InlineData(ParagraphAlignment.Right, false)]
    [InlineData(ParagraphAlignment.Right, true)]
    [InlineData(ParagraphAlignment.Center, false)]
    [InlineData(ParagraphAlignment.Center, true)]
    [InlineData(ParagraphAlignment.Justify, false)]
    [InlineData(ParagraphAlignment.Justify, true)]
    public void FieldRemeasurementKeepsFullMeasuredLineWidths(ParagraphAlignment alignment, bool nonfinal)
    {
        var width = Width("1 Alpha Bravo") - 1;
        var suffix = nonfinal ? " Delta" : "";
        var literal = Render(width, pageField: false, suffix: suffix, alignment: alignment,
            adjust: (_, _, _) => -1);
        var field = Render(width, pageField: true, suffix: suffix, alignment: alignment,
            adjust: (_, _, _) => -1);
        SameLine(field, "Alpha", "Bravo").Should().BeTrue();
        field.Should().Equal(literal);
    }

    [Theory]
    [InlineData(ParagraphAlignment.Right)]
    [InlineData(ParagraphAlignment.Center)]
    [InlineData(ParagraphAlignment.Justify)]
    public void FieldRemeasurementIncludesBlanksAfterNaturallyOverflowingWords(ParagraphAlignment alignment)
    {
        var width = Width("1 Alpha Bravo") - 1;
        double? Adjust(object? tag, string text, XFont font) =>
            text == "Bravo" ? -Width("Bravo") + 1 : -1;
        Render(width, pageField: true, suffix: " Echo", alignment: alignment, adjust: Adjust)
            .Should().Equal(Render(width, pageField: false, suffix: " Echo", alignment: alignment, adjust: Adjust));
    }

    [Fact]
    public void RemeasuredTabProbeStopsAtTheStoredLineEnd()
    {
        var result = Render(120, pageField: true, tab: TabAlignment.Right,
            suffix: " Delta", adjust: (_, _, _) => -1);
        SameLine(result, "Alpha", "Bravo").Should().BeTrue();
        SameLine(result, "Bravo", "Delta").Should().BeFalse();
        (result.Single(w => w.Text == "Bravo").X + Width("Bravo"))
            .Should().BeApproximately(140, .001);
    }

    [Theory]
    [InlineData(ParagraphAlignment.Right, false, false)]
    [InlineData(ParagraphAlignment.Center, false, false)]
    [InlineData(ParagraphAlignment.Justify, false, false)]
    [InlineData(ParagraphAlignment.Right, true, false)]
    [InlineData(ParagraphAlignment.Center, true, false)]
    [InlineData(ParagraphAlignment.Justify, true, false)]
    [InlineData(ParagraphAlignment.Right, false, true)]
    [InlineData(ParagraphAlignment.Center, false, true)]
    [InlineData(ParagraphAlignment.Justify, false, true)]
    [InlineData(ParagraphAlignment.Right, true, true)]
    [InlineData(ParagraphAlignment.Center, true, true)]
    [InlineData(ParagraphAlignment.Justify, true, true)]
    public void RemeasurementUsesStoredSoftHyphenVisibility(
        ParagraphAlignment alignment, bool terminal, bool adjusted)
    {
        var width = Width(terminal ? "1 Alpha Bra-" : "1 Alpha Bravo") + 1;
        Func<object?, string, XFont, double?>? adjust = adjusted ? (_, _, _) => -.2 : null;
        var field = Render(width, pageField: true, softHyphen: true,
            suffix: " Delta", alignment: alignment, adjust: adjust);
        var literal = Render(width, pageField: false, softHyphen: true,
            suffix: " Delta", alignment: alignment, adjust: adjust);
        field.Any(w => w.Text == "-").Should().Be(terminal);
        field.Should().Equal(literal);
    }

    [Theory]
    [InlineData(TabAlignment.Right, false)]
    [InlineData(TabAlignment.Right, true)]
    [InlineData(TabAlignment.Center, false)]
    [InlineData(TabAlignment.Center, true)]
    public void TabRemeasurementStopsAtIgnoredTrailingBlank(TabAlignment tab, bool adjusted)
    {
        var textWidth = Width("1 Alpha Bravo");
        var rightEdge = 120 + (tab == TabAlignment.Center ? textWidth / 2 : 0);
        var result = Render(rightEdge + Width(" ") * 1.5, pageField: true, tab: tab,
            suffix: " Delta", prefixScale: tab == TabAlignment.Center ? 2 : 1,
            adjust: adjusted ? (_, _, _) => -.2 : null);
        SameLine(result, "Alpha", "Bravo").Should().BeTrue();
        SameLine(result, "Bravo", "Delta").Should().BeFalse();
        (result.Single(w => w.Text == "Bravo").X + Width("Bravo"))
            .Should().BeApproximately(20 + rightEdge, .001);
    }

    [Fact]
    public void DefaultIsDisabled() =>
        new DocumentRenderer(new Document()).TextFitWidthAdjustment.Should().BeNull();

    record Word(string Text, double X, double Y);
    static bool SameLine(List<Word> words, string first, string last) =>
        Math.Abs(words.Single(w => w.Text == first).Y - words.Single(w => w.Text == last).Y) < .001;

    static double Width(string text)
    {
        using var gfx = XGraphics.CreateMeasureContext(new XSize(300, 300), XGraphicsUnit.Point, XPageDirection.Downwards);
        return gfx.MeasureString(text, new XFont("Arial", 12)).Width;
    }

    static List<Word> Render(double bodyWidth,
        Func<object?, string, XFont, double?>? adjust = null, bool split = false, string suffix = "",
        ParagraphAlignment alignment = ParagraphAlignment.Left, bool prefixBreak = false, bool prefixWrap = false,
        TabAlignment? tab = null, bool softHyphen = false, bool wrap = false, bool? pageField = null, double prefixScale = 1)
    {
        var doc = new Document();
        doc.Styles.Normal.Font.Name = "Arial";
        doc.Styles.Normal.Font.Size = 12;
        var section = doc.AddSection();
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromPoint(20);
        section.PageSetup.TopMargin = Unit.FromPoint(20);
        section.PageSetup.PageWidth = Unit.FromPoint(40 + bodyWidth);
        if (wrap)
        {
            var image = section.AddImage("base64:iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4//8/AwAI/AL+p5qgoAAAAABJRU5ErkJggg==");
            image.RelativeHorizontal = RelativeHorizontal.Margin;
            image.RelativeVertical = RelativeVertical.Margin;
            image.Left = Unit.FromPoint(60);
            image.Top = Unit.FromPoint(0);
            image.Width = Unit.FromPoint(40);
            image.Height = Unit.FromPoint(90);
            image.WrapFormat.Style = WrapStyle.BothSides;
        }
        var paragraph = section.AddParagraph();
        paragraph.Format.Alignment = alignment;
        if (prefixBreak || prefixWrap || tab.HasValue || wrap)
        {
            paragraph.AddText("Prefix").Tag = "Prefix";
            if (prefixBreak) paragraph.AddLineBreak();
            else if (tab.HasValue)
            {
                paragraph.Format.AddTabStop(Unit.FromPoint(120), tab.Value);
                paragraph.AddTab();
            }
            else paragraph.AddText(" ");
        }
        if (pageField.HasValue)
        {
            if (pageField.Value) paragraph.AddPageField();
            else paragraph.AddText("1");
            paragraph.AddText(" ");
        }
        paragraph.AddText("Alpha").Tag = "Alpha";
        paragraph.AddText(" ");
        if (split || softHyphen)
        {
            paragraph.AddFormattedText("").AddText("Bra").Tag = "Bra";
            if (softHyphen) paragraph.AddText("\u00AD");
            paragraph.AddFormattedText("").AddText("vo").Tag = "vo";
        }
        else paragraph.AddText("Bravo").Tag = "Bravo";
        if (suffix.Length > 0) paragraph.AddText(suffix);
        var renderer = new PdfDocumentRenderer { Document = doc };
        if (prefixWrap || prefixScale != 1)
            renderer.DocumentRenderer.TextHorizontalScale = tag => Equals(tag, "Prefix")
                ? prefixWrap ? (bodyWidth - Width(" ") / 2) / Width("Prefix") : prefixScale : null;
        if (adjust != null)
            renderer.DocumentRenderer.TextFitWidthAdjustment = (source, tag, text, font) =>
            {
                source.Should().BeSameAs(paragraph);
                return adjust(tag, text, font);
            };
        renderer.RenderDocument();
        using var pdf = renderer.PdfDocument;
        var enumerator = PdfFileHelper.GetPageContentStreamEnumerator(pdf, 0).Text;
        var words = new List<Word>();
        while (enumerator.MoveAndGetNext(true, out var text)) words.Add(new Word(text!.Text, text.X, text.Y));
        return words;
    }
}
