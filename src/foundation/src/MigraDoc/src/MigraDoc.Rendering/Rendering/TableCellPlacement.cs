using PdfSharp.Drawing;

namespace MigraDoc.Rendering
{
    /// <summary>
    /// Resolved table geometry in points, supplied to a cell placement callback.
    /// Rectangles use page coordinates with Y increasing downward.
    /// </summary>
    public readonly record struct TableCellPlacementContext(
        XRect GridRect,
        XRect DefaultPaintRect,
        double ColumnWidth,
        double RowPitch,
        double RowTopBand,
        double RowBottomBand,
        double LeftBand,
        double RightBand,
        double TopBand,
        double BottomBand,
        double LeftPadding,
        double RowTopNarrowestBand);

    /// <summary>
    /// Optional cell paint and content placement in page points.
    /// Null members retain their default placement.
    /// </summary>
    public readonly record struct TableCellPlacement(
        XRect? PaintRect = null,
        XRect? ContentRect = null,
        double? ContentLeftPadding = null);
}
