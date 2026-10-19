using FluentAssertions;
using MigraDoc.DocumentObjectModel;
using Xunit;

namespace MigraDoc.Tests
{
    public class InsertedRowCacheTests
    {
        [Fact]
        public void InsertingClonedRowsUpdatesTheirTableAndCellCaches()
        {
            var document = new Document();
            var table = document.AddSection().AddTable();
            table.AddColumn();
            table.AddColumn();
            var original = table.AddRow();
            table.AddRow();

            var first = original.Clone();
            var second = original.Clone();
            table.Rows.RemoveObjectAt(0);
            table.Rows.InsertObject(0, first);
            table.Rows.InsertObject(1, second);

            table.Rows[0].Should().BeSameAs(first);
            table.Rows[1].Should().BeSameAs(second);
            first.Index.Should().Be(0);
            second.Index.Should().Be(1);
            table.Rows[2].Index.Should().Be(2);
            foreach (var row in new[] { first, second })
            {
                row.Table.Should().BeSameAs(table);
                for (var column = 0; column < row.Cells.Count; column++)
                {
                    row.Cells[column].Row.Should().BeSameAs(row);
                    row.Cells[column].Column.Should().BeSameAs(table.Columns[column]);
                }
            }
        }
    }
}
