using FluentAssertions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfSharp.Tests.Pdf
{
    [Collection("PDFsharp")]
    public class NamedDestinationTests
    {
        [Fact]
        public void ImportedPageDestinationUsesTheFinalPageReference()
        {
            using var original = new PdfDocument();
            original.AddPage();
            original.AddPage();
            using var sourceStream = new MemoryStream();
            original.Save(sourceStream, false);
            sourceStream.Position = 0;

            using var source = PdfReader.Open(sourceStream, PdfDocumentOpenMode.Import);
            using var result = new PdfDocument();
            foreach (var page in source.Pages)
                result.AddPage(page);
            result.AddNamedDestination("second", 2,
                PdfNamedDestinationParameters.CreateVerticalPosition(400));
            var liveDestination = result.Internals.Catalog.Names!.NameTree!.GetValue("second", true)
                .Should().BeOfType<PdfArray>().Subject;
            liveDestination.Elements[0].Should().BeSameAs(result.Pages[1].Reference);
            using var resultStream = new MemoryStream();
            result.Save(resultStream, false);
            resultStream.Position = 0;

            using var saved = PdfReader.Open(resultStream, PdfDocumentOpenMode.Import);
            var destination = saved.Internals.Catalog.Names!.NameTree!.GetValue("second", true)
                .Should().BeOfType<PdfArray>().Subject;
            var pageReference = destination.Elements[0].Should().BeOfType<PdfReference>().Subject;
            pageReference.ObjectID.Should().Be(saved.Pages[1].Reference!.ObjectID);
        }
    }
}
