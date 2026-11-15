# Equal-width body columns

Set the column count and gap on a section's page setup:

```csharp
var section = document.AddSection();
section.PageSetup.ColumnCount = 2;
section.PageSetup.ColumnSpacing = Unit.FromCentimeter(0.6);
section.AddParagraph("Text flows through the first column, then the second.");
```

The available page-margin width is divided equally after subtracting the gaps.
Columns fill left to right, from the body top to its bottom, before creating
another physical page. The final page is unbalanced. The default is one column
with zero spacing. A count must be positive; spacing must be finite and
nonnegative, and the gaps must leave positive column width.

Paragraph continuation, keep-with-next, and table pagination use the next column
as the next layout area. Tables align within that area. Explicit page breaks and
paragraph `PageBreakBefore` skip the remaining columns and start a physical page.
Section breaks retain their page-start semantics. Settings inherit from the
preceding section, and an explicit count of one or gap of zero resets the
corresponding setting. Settings survive cloning and DDL serialization.

Headers and footers retain the complete margin width. Page-relative and
page-margin-relative shapes retain those references; area-relative content uses
the current column. Mirrored margins, footer clearance, body-bottom reservations,
page numbering, and section page counts apply to physical pages.
