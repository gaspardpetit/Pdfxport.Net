// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using System.Text;
using FluentAssertions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfSharp.Tests.IO;

public class SaveToSourceStreamTests
{
    [Fact]
    public void SavingToTheSourceStreamReplacesItsOriginalContent()
    {
        using var pdf = CreateDocument();
        using var modified = PdfReader.Open(pdf, PdfDocumentOpenMode.Modify);
        modified.Info.Title = "Updated";

        modified.Save(pdf, false);

        var saved = Encoding.Latin1.GetString(pdf.ToArray());
        saved.Should().StartWith("%PDF-");
        saved.IndexOf("%PDF-", 1, StringComparison.Ordinal).Should().Be(-1);
        using var reopened = PdfReader.Open(new MemoryStream(pdf.ToArray()), PdfDocumentOpenMode.Import);
        reopened.PageCount.Should().Be(1);
        reopened.Info.Title.Should().Be("Updated");
    }

    [Fact]
    public void FailedSaveLeavesTheSourceStreamIntact()
    {
        using var pdf = CreateDocument();
        var original = pdf.ToArray();
        using var modified = PdfReader.Open(pdf, PdfDocumentOpenMode.Modify);
        modified.Pages.RemoveAt(0);

        modified.Invoking(doc => doc.Save(pdf, false)).Should().Throw<InvalidOperationException>();

        pdf.ToArray().Should().Equal(original);
    }

    [Fact]
    public void SavingToAnotherStreamPreservesTheCallersPosition()
    {
        using var pdf = CreateDocument();
        using var modified = PdfReader.Open(pdf, PdfDocumentOpenMode.Modify);
        using var output = new MemoryStream();
        output.Write(Encoding.ASCII.GetBytes("prefix"));

        modified.Save(output, false);

        Encoding.ASCII.GetString(output.ToArray(), 0, 11).Should().Be("prefix%PDF-");
    }

    static MemoryStream CreateDocument()
    {
        using var document = new PdfDocument();
        document.AddPage();
        var stream = new MemoryStream();
        document.Save(stream, false);
        return stream;
    }
}
