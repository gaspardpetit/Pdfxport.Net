// PDFsharp - A .NET library for processing PDF
// See the LICENSE file in the solution root for more information.

using FluentAssertions;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using Xunit;

namespace PdfSharp.Tests.IO
{
    public class ModificationDateTests
    {
        static readonly DateTimeOffset OriginalDate = new(2001, 2, 3, 4, 5, 6, TimeSpan.Zero);

        [Fact]
        public void OpeningForModificationPreservesTheStoredDate()
        {
            var bytes = CreateDocument(OriginalDate);
            using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);

            document.Info.ModificationDate.Should().Be(OriginalDate);
        }

        [Fact]
        public void OpeningAnUndatedDocumentDoesNotSetAModificationDate()
        {
            var bytes = CreateDocument(null);
            using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);

            document.Info.ModificationDate.Should().BeNull();
        }

        [Fact]
        public void SavingAnImportedDocumentSetsTheModificationDate()
        {
            var bytes = CreateDocument(OriginalDate);
            using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);
            var beforeSave = DateTimeOffset.Now;

            using var output = new MemoryStream();
            document.Save(output, false);

            document.Info.ModificationDate.Should().BeOnOrAfter(beforeSave).And.BeOnOrBefore(DateTimeOffset.Now);
            using var saved = PdfReader.Open(new MemoryStream(output.ToArray()), PdfDocumentOpenMode.Import);
            saved.Info.ModificationDate.Should().BeCloseTo(document.Info.ModificationDate!.Value, TimeSpan.FromSeconds(1));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SavingPreservesAnExplicitModificationDate(bool clearDate)
        {
            var bytes = CreateDocument(OriginalDate);
            using var document = PdfReader.Open(new MemoryStream(bytes), PdfDocumentOpenMode.Modify);
            var chosenDate = clearDate ? (DateTimeOffset?)null : new DateTimeOffset(1999, 12, 31, 23, 59, 58, TimeSpan.Zero);
            document.Info.ModificationDate = chosenDate;

            using var output = new MemoryStream();
            document.Save(output, false);

            document.Info.ModificationDate.Should().Be(chosenDate);
        }

        [Fact]
        public void SavingANewDocumentDoesNotSetAModificationDate()
        {
            using var document = new PdfDocument();
            document.AddPage();
            using var output = new MemoryStream();

            document.Save(output, false);

            document.Info.ModificationDate.Should().BeNull();
        }

        static byte[] CreateDocument(DateTimeOffset? modificationDate)
        {
            using var document = new PdfDocument();
            document.AddPage();
            document.Info.ModificationDate = modificationDate;
            using var output = new MemoryStream();
            document.Save(output, false);
            return output.ToArray();
        }
    }
}
