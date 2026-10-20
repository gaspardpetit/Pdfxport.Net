// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using MigraDoc.DocumentObjectModel.Internals;
using PdfSharp.Drawing;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Fields;
using MigraDoc.DocumentObjectModel.Visitors;
using MigraDoc.DocumentObjectModel.Tables;

namespace MigraDoc.Rendering
{
    /// <summary>
    /// Renders a table to an XGraphics object.
    /// </summary>
    class TableRenderer : Renderer
    {
        internal TableRenderer(XGraphics gfx, Table documentObject, FieldInfos? fieldInfos)
            : base(gfx, documentObject, fieldInfos)
        {
            _table = documentObject;
        }

        internal TableRenderer(XGraphics gfx, RenderInfo renderInfo, FieldInfos? fieldInfos)
            : base(gfx, renderInfo, fieldInfos)
        {
            _table = (Table)_renderInfo.DocumentObject;
        }

        internal override LayoutInfo InitialLayoutInfo
        {
            get
            {
                var layoutInfo = new LayoutInfo
                {
                    KeepTogether = _table.KeepTogether,
                    KeepWithNext = false,
                    MarginBottom = 0,
                    MarginLeft = 0,
                    MarginTop = 0,
                    MarginRight = 0
                };
                return layoutInfo;
            }
        }

        void InitRendering()
        {
            _horizontalBoundaryWidths.Clear();
            var formatInfo = (TableFormatInfo)_renderInfo.FormatInfo;
            _bottomBorderMap = formatInfo.BottomBorderMap ?? NRT.ThrowOnNull<Dictionary<int, XUnitPt>>();
            //_connectedRowsMap = formatInfo.ConnectedRowsMap ?? NRT.ThrowOnNull<Dictionary<int, int>>();
            _connectedRowsMap = formatInfo.ConnectedRowsMap ?? NRT.ThrowOnNull<int[]>();
            _minMergedCellRowMap = formatInfo.MinMergedCellRowMap ?? NRT.ThrowOnNull<int[]>();
            _formattedCells = formatInfo.FormattedCells ?? NRT.ThrowOnNull<Dictionary<Cell, FormattedCell>>();

            _currRow = formatInfo.StartRow;
            _startRow = formatInfo.StartRow;
            _endRow = formatInfo.EndRow;

            _mergedCells = formatInfo.MergedCells ?? NRT.ThrowOnNull<MergedCellList>();
            _lastHeaderRow = formatInfo.LastHeaderRow;
            _startX = _renderInfo.LayoutInfo.ContentArea.X;
            _startY = _renderInfo.LayoutInfo.ContentArea.Y;
        }

        void RenderHeaderRows()
        {
            if (_lastHeaderRow < 0)
                return;

            var headerCells = new List<Cell>();
            int count = _mergedCells.Count;
            for (var index = 0; index < count; index++)
            {
                var cell = _mergedCells[index];
                Debug.Assert(cell != null, nameof(cell) + " != null");
                Debug.Assert(cell.Row != null, "cell.Row != null");
                if (cell.Row.Index <= _lastHeaderRow)
                    headerCells.Add(cell);
                else
                    break; // Exit the loop when we hit the first non-header cell. _mergedCells is sorted.
            }
            // _startRow is the index of the first content row on the page. Add this index as header insertion row index to _mergedCells, 
            // to enable considering repeated headers on PDF effective border determination in MergedCellList.
            _mergedCells.AddHeaderInsertionRowIndex(_startRow);
            _horizontalBoundaryWidths.Clear();
            var firstBodyCells = new List<Cell>();
            for (var index = 0; index < count; index++)
            {
                var cell = _mergedCells[index];
                if (cell?.Row?.Index == _startRow)
                    firstBodyCells.Add(cell);
            }
            RenderCells(headerCells, firstBodyCells);
        }

        void RenderCell(Cell cell,
            HashSet<(int Boundary, int Start, int End, double LeftInset, double RightInset, double BorderWidth, double StrokeWidth, XColor Color, BorderStyle Style)> followingTopBorders,
            HashSet<(int Boundary, int Start, int End, double LeftInset, double RightInset, double BorderWidth, double StrokeWidth, XColor Color, BorderStyle Style)> precedingBottomBorders,
            HashSet<(int Boundary, int Start, int End, double BottomInset, double BorderWidth, double StrokeWidth, XColor Color, BorderStyle Style)> followingLeftBorders,
            bool renderBorders = true, Rectangle? displayRect = null,
            IReadOnlyList<XRect>? borderExclusions = null)
        {
            var innerRect = displayRect ?? GetInnerRect(CalcStartingHeight(), cell);
            RenderShading(cell, innerRect);
            RenderContent(cell, innerRect);
            if (renderBorders)
            {
                XGraphicsState? state = null;
                if (borderExclusions is { Count: > 0 })
                {
                    state = _gfx.Save();
                    ExcludeBorderRegions(borderExclusions);
                }
                RenderBorders(cell, innerRect, followingTopBorders, precedingBottomBorders, followingLeftBorders);
                if (state != null)
                    _gfx.Restore(state);
            }
        }

        void ExcludeBorderRegions(IReadOnlyList<XRect>? regions)
        {
            if (regions == null)
                return;
            foreach (var region in regions)
            {
                var outside = new XGraphicsPath { FillMode = XFillMode.Alternate };
                outside.AddRectangle(new XRect(-10000, -10000, 20000, 20000));
                outside.AddRectangle(region);
                _gfx.IntersectClip(outside);
            }
        }

        static void EqualizeRoundedCornerBorders(Cell cell)
        {
            // If any of a corner relevant border is set, we want to copy its values to the second corner relevant border, 
            // to ensure the innerWidth of the cell is the same, regardless of which border is used.
            // If set, we use the vertical borders as source for the values, otherwise we use the horizontal borders.
            var roundedCorner = cell.RoundedCorner;

            if (roundedCorner == RoundedCorner.None)
                return;

            BorderType primaryBorderType = BorderType.Top, secondaryBorderType = BorderType.Top;

            if (roundedCorner == RoundedCorner.TopLeft || roundedCorner == RoundedCorner.BottomLeft)
                primaryBorderType = BorderType.Left;
            if (roundedCorner == RoundedCorner.TopRight || roundedCorner == RoundedCorner.BottomRight)
                primaryBorderType = BorderType.Right;

            if (roundedCorner == RoundedCorner.TopLeft || roundedCorner == RoundedCorner.TopRight)
                secondaryBorderType = BorderType.Top;
            if (roundedCorner == RoundedCorner.BottomLeft || roundedCorner == RoundedCorner.BottomRight)
                secondaryBorderType = BorderType.Bottom;

            // If both borders don’t exist, there’s nothing to do, and we should not create one by accessing it.
            if (!cell.Borders.HasBorder(primaryBorderType) && !cell.Borders.HasBorder(secondaryBorderType))
                return;

            // Get the borders. By using GV.ReadWrite we create the border, if not existing.
            var primaryBorder = (Border)cell.Borders.GetValue(primaryBorderType.ToString(), GV.ReadWrite)!;  // Border is created.
            var secondaryBorder = (Border)cell.Borders.GetValue(secondaryBorderType.ToString(), GV.ReadWrite)!;  // Border is created.

            var source = primaryBorder.Visible
                ? primaryBorder
                : secondaryBorder.Visible ? secondaryBorder : null;
            var target = primaryBorder.Visible
                ? secondaryBorder
                : secondaryBorder.Visible ? primaryBorder : null;

            if (source == null || target == null)
                return;

            target.Visible = source.Visible;
            target.Width = source.Width;
            target.Style = source.Style;
            target.Color = source.Color;
        }

        void RenderShading(Cell cell, Rectangle innerRect)
        {
            var shadingRenderer = new ShadingRenderer(_gfx, cell.Shading);
            shadingRenderer.Render(innerRect.X, innerRect.Y, innerRect.Width, innerRect.Height, cell.RoundedCorner);
        }

        void RenderBorders(Cell cell, Rectangle innerRect,
            HashSet<(int Boundary, int Start, int End, double LeftInset, double RightInset, double BorderWidth, double StrokeWidth, XColor Color, BorderStyle Style)> followingTopBorders,
            HashSet<(int Boundary, int Start, int End, double LeftInset, double RightInset, double BorderWidth, double StrokeWidth, XColor Color, BorderStyle Style)> precedingBottomBorders,
            HashSet<(int Boundary, int Start, int End, double BottomInset, double BorderWidth, double StrokeWidth, XColor Color, BorderStyle Style)> followingLeftBorders)
        {
            XUnitPt leftPos = innerRect.X;
            XUnitPt rightPos = leftPos + innerRect.Width;
            XUnitPt topPos = innerRect.Y;
            XUnitPt bottomPos = innerRect.Y + innerRect.Height;
            var mergedBorders = _mergedCells.GetEffectiveBordersPdf(cell, _lastHeaderRow);

            var bordersRenderer = new BordersRenderer(mergedBorders, _gfx);
            XUnitPt bottomWidth = bordersRenderer.GetWidth(BorderType.Bottom);
            XUnitPt leftWidth = bordersRenderer.GetWidth(BorderType.Left);
            XUnitPt topWidth = bordersRenderer.GetWidth(BorderType.Top);
            XUnitPt rightWidth = bordersRenderer.GetWidth(BorderType.Right);
            XUnitPt? outerStrokeTop = default;
            if (bordersRenderer.GetStyle(BorderType.Top) == BorderStyle.Double)
                outerStrokeTop = topPos - topWidth + topWidth / 6;
            var leftOuterStrokeTop = cell.Column?.Index == 0 ? outerStrokeTop : null;
            var rightOuterStrokeTop = cell.Column?.Index + cell.MergeRight == _table.Columns.Count - 1
                ? outerStrokeTop : null;
            XUnitPt? innerStrokeBottom = default;
            if (bordersRenderer.GetStyle(BorderType.Bottom) == BorderStyle.Double)
                innerStrokeBottom = bottomPos;
            var leftInnerStrokeBottom = cell.Column?.Index == 0 ? innerStrokeBottom : null;
            var rightInnerStrokeBottom = cell.Column?.Index + cell.MergeRight == _table.Columns.Count - 1
                ? innerStrokeBottom : null;
            XUnitPt? innerStrokeLeft = default;
            if (bordersRenderer.GetStyle(BorderType.Left) == BorderStyle.Double)
                innerStrokeLeft = leftPos;
            XUnitPt? innerStrokeRight = default;
            if (bordersRenderer.GetStyle(BorderType.Right) == BorderStyle.Double)
                innerStrokeRight = rightPos;
            var internalTopEdge = cell.Row?.Index > 0;
            var internalBottomEdge = cell.Row?.Index + cell.MergeDown < _table.Rows.Count - 1;
            // Decorative horizontal borders occupy their own band.  A vertical
            // stroke that runs through that band destroys the intended break at
            // a four-way junction (most visibly with waves and hatches).  The
            // horizontal edge owns the band; the vertical edge ends at its near
            // boundary and resumes below it in the next cell.
            var bottomStyle = bordersRenderer.GetStyle(BorderType.Bottom);
            var horizontalOwnsBottomJunction = bottomStyle is BorderStyle.Double or
                BorderStyle.Triple or BorderStyle.Wave or BorderStyle.DoubleWave or
                BorderStyle.DashDotStroked ||
                CompoundProfile(bottomStyle) != null;
            var verticalBottom = horizontalOwnsBottomJunction ? bottomPos : bottomPos + bottomWidth;

            if (cell.RoundedCorner != RoundedCorner.None)
            {
                // Difficult case: rounded borders.
                if (cell.RoundedCorner == RoundedCorner.TopLeft)
                    bordersRenderer.RenderRounded(cell.RoundedCorner, innerRect.X, innerRect.Y, innerRect.Width + rightWidth, innerRect.Height + bottomWidth);
                else if (cell.RoundedCorner == RoundedCorner.TopRight)
                    bordersRenderer.RenderRounded(cell.RoundedCorner, innerRect.X - leftWidth, innerRect.Y, innerRect.Width + leftWidth, innerRect.Height + bottomWidth);
                else if (cell.RoundedCorner == RoundedCorner.BottomLeft)
                    bordersRenderer.RenderRounded(cell.RoundedCorner, innerRect.X, innerRect.Y - topWidth, innerRect.Width + rightWidth, innerRect.Height + topWidth);
                else if (cell.RoundedCorner == RoundedCorner.BottomRight)
                    bordersRenderer.RenderRounded(cell.RoundedCorner, innerRect.X - leftWidth, innerRect.Y - topWidth, innerRect.Width + leftWidth, innerRect.Height + topWidth);

                // Render horizontal and vertical borders only if touching no rounded corner.
                if (cell.RoundedCorner != RoundedCorner.TopRight && cell.RoundedCorner != RoundedCorner.BottomRight)
                    bordersRenderer.RenderVertically(BorderType.Right, rightPos, topPos,
                        bottomPos + bottomWidth - topPos, rightOuterStrokeTop, rightInnerStrokeBottom);

                if (cell.RoundedCorner != RoundedCorner.TopLeft && cell.RoundedCorner != RoundedCorner.BottomLeft)
                    bordersRenderer.RenderVertically(BorderType.Left, leftPos - leftWidth, topPos,
                        bottomPos + bottomWidth - topPos, leftOuterStrokeTop, leftInnerStrokeBottom);

                if (cell.RoundedCorner != RoundedCorner.BottomLeft && cell.RoundedCorner != RoundedCorner.BottomRight)
                    bordersRenderer.RenderHorizontally(BorderType.Bottom, leftPos - leftWidth, bottomPos,
                        rightPos + rightWidth + leftWidth - leftPos, innerStrokeLeft, innerStrokeRight,
                        internalBottomEdge);

                if (cell.RoundedCorner != RoundedCorner.TopLeft && cell.RoundedCorner != RoundedCorner.TopRight)
                    bordersRenderer.RenderHorizontally(BorderType.Top, leftPos - leftWidth, topPos - topWidth,
                        rightPos + rightWidth + leftWidth - leftPos, innerStrokeLeft, innerStrokeRight,
                        internalTopEdge);
            }
            else
            {
                // Simple case: no rounded borders.
                var rightPen = bordersRenderer.GetPen(BorderType.Right);
                var rightKey = rightPen == null ? default : VerticalBorderKey(cell, bordersRenderer, rightPen, false);
                var followingOwnsRight = rightPen != null && CompoundProfile(rightKey.Style) != null &&
                    followingLeftBorders.Any(edge => edge.Boundary == rightKey.Boundary &&
                        edge.Start == rightKey.Start && edge.End == rightKey.End);
                if (!followingOwnsRight && !AdjacentClosedCellOwnsEdge(cell, BorderType.Right, bordersRenderer) &&
                    (rightPen == null || !IsSharedEdgeStyle(bordersRenderer.GetStyle(BorderType.Right), rightPen) ||
                     !followingLeftBorders.Contains(rightKey)))
                    bordersRenderer.RenderVertically(BorderType.Right, rightPos, topPos,
                        verticalBottom - topPos, rightOuterStrokeTop, rightInnerStrokeBottom);
                if (cell.Borders.GetBorder(BorderType.Left)?.Values.Visible != false &&
                    !AdjacentClosedCellOwnsEdge(cell, BorderType.Left, bordersRenderer))
                    bordersRenderer.RenderVertically(BorderType.Left, leftPos - leftWidth, topPos,
                        verticalBottom - topPos, leftOuterStrokeTop, leftInnerStrokeBottom);
                var bottomLeft = leftPos - leftWidth;
                var bottomLength = rightPos + rightWidth + leftWidth - leftPos;
                var bottomPen = bordersRenderer.GetPen(BorderType.Bottom);
                var bottomKey = bottomPen == null ? default : HorizontalBorderKey(cell, bordersRenderer, bottomPen, false);
                var followingOwnsBottom = bottomPen != null && CompoundProfile(bottomKey.Style) != null &&
                    followingTopBorders.Any(edge => edge.Boundary == bottomKey.Boundary &&
                        edge.Start == bottomKey.Start && edge.End == bottomKey.End);
                if (!followingOwnsBottom && !AdjacentClosedCellOwnsEdge(cell, BorderType.Bottom, bordersRenderer) &&
                    (bottomPen == null || !IsSharedEdgeStyle(bordersRenderer.GetStyle(BorderType.Bottom), bottomPen) ||
                     !CoveredByNarrowerEdges(bottomKey, followingTopBorders, false)))
                    bordersRenderer.RenderHorizontally(BorderType.Bottom, bottomLeft, bottomPos, bottomLength,
                        innerStrokeLeft, innerStrokeRight, internalBottomEdge);
                var topPen = bordersRenderer.GetPen(BorderType.Top);
                if (cell.Borders.GetBorder(BorderType.Top)?.Values.Visible != false &&
                    !AdjacentClosedCellOwnsEdge(cell, BorderType.Top, bordersRenderer) &&
                    (topPen == null || bordersRenderer.GetStyle(BorderType.Top) is not
                        (BorderStyle.Dot or BorderStyle.DashSmallGap or BorderStyle.DashDot or BorderStyle.DashDotDot) ||
                     !CoveredByNarrowerEdges(HorizontalBorderKey(cell, bordersRenderer, topPen, true), precedingBottomBorders, true)))
                    bordersRenderer.RenderHorizontally(BorderType.Top, leftPos - leftWidth, topPos - topWidth,
                        rightPos + rightWidth + leftWidth - leftPos, innerStrokeLeft, innerStrokeRight,
                        internalTopEdge);
            }

            RenderDiagonalBorders(mergedBorders, innerRect);
        }

        bool AdjacentClosedCellOwnsEdge(Cell cell, BorderType side, BordersRenderer current)
        {
            if (cell.MergeRight != 0 || cell.MergeDown != 0 || cell.Row == null || cell.Column == null)
                return false;
            var row = cell.Row.Index + (side == BorderType.Bottom ? 1 : side == BorderType.Top ? -1 : 0);
            var column = cell.Column.Index + (side == BorderType.Right ? 1 : side == BorderType.Left ? -1 : 0);
            if (row < 0 || row >= _table.Rows.Count || column < 0 || column >= _table.Columns.Count)
                return false;
            // A cell on another page cannot paint this fragment's boundary.
            // Repeated heading rows are rendered on the current page as well.
            if (row > _endRow || row < _startRow && row > _lastHeaderRow)
                return false;
            var neighbor = _table[row, column];
            if (neighbor.MergeRight != 0 || neighbor.MergeDown != 0 ||
                !TryGetClosedCellRailPen(neighbor, out var pen, out var style))
                return false;
            var currentPen = current.GetPen(side);
            return current.GetStyle(side) == style && currentPen != null &&
                currentPen.Width == pen!.Width && currentPen.Color == pen.Color;
        }

        (int Boundary, int Start, int End, double LeftInset, double RightInset, double BorderWidth, double StrokeWidth, XColor Color, BorderStyle Style) HorizontalBorderKey(
            Cell cell, BordersRenderer borders, XPen pen, bool top)
        {
            Debug.Assert(cell.Row != null && cell.Column != null);
            var boundary = top ? cell.Row.Index : cell.Row.Index + cell.MergeDown + 1;
            if (!top && boundary == _lastHeaderRow + 1 && _startRow > boundary)
                boundary = _startRow;
            return (boundary, cell.Column.Index, cell.Column.Index + cell.MergeRight + 1,
                borders.GetWidth(BorderType.Left).Point, borders.GetWidth(BorderType.Right).Point,
                borders.GetWidth(top ? BorderType.Top : BorderType.Bottom).Point, pen.Width, pen.Color,
                borders.GetStyle(top ? BorderType.Top : BorderType.Bottom));
        }

        (int Boundary, int Start, int End, double BottomInset, double BorderWidth, double StrokeWidth, XColor Color, BorderStyle Style) VerticalBorderKey(
            Cell cell, BordersRenderer borders, XPen pen, bool left)
        {
            Debug.Assert(cell.Row != null && cell.Column != null);
            return (left ? cell.Column.Index : cell.Column.Index + cell.MergeRight + 1,
                cell.Row.Index, cell.Row.Index + cell.MergeDown + 1,
                borders.GetWidth(BorderType.Bottom).Point,
                borders.GetWidth(left ? BorderType.Left : BorderType.Right).Point, pen.Width, pen.Color,
                borders.GetStyle(left ? BorderType.Left : BorderType.Right));
        }

        static bool IsSharedEdgeStyle(BorderStyle style, XPen pen) =>
            pen.DashStyle == XDashStyle.Solid || style is BorderStyle.Dot or BorderStyle.DashSmallGap or
                BorderStyle.DashDot or BorderStyle.DashDotDot;

        static bool CoveredByNarrowerEdges(
            (int Boundary, int Start, int End, double LeftInset, double RightInset, double BorderWidth, double StrokeWidth, XColor Color, BorderStyle Style) target,
            HashSet<(int Boundary, int Start, int End, double LeftInset, double RightInset, double BorderWidth, double StrokeWidth, XColor Color, BorderStyle Style)> candidates,
            bool requireNarrower)
        {
            if (!requireNarrower && candidates.Contains(target))
                return true;
            if (target.Style is not (BorderStyle.Dot or BorderStyle.DashSmallGap or
                BorderStyle.DashDot or BorderStyle.DashDotDot))
                return false;

            // Prefer the finer cell segmentation so each dash pattern restarts at
            // the same cell boundaries as Word's table borders.
            var coveredUntil = target.Start;
            var hasNarrowerEdge = false;
            foreach (var edge in candidates.Where(edge => edge.Boundary == target.Boundary &&
                         edge.Start >= target.Start && edge.End <= target.End &&
                         edge.BorderWidth == target.BorderWidth && edge.StrokeWidth == target.StrokeWidth &&
                         edge.Color == target.Color && edge.Style == target.Style)
                         .OrderBy(edge => edge.Start).ThenByDescending(edge => edge.End))
            {
                if (edge.End <= coveredUntil || edge.Start > coveredUntil)
                    continue;
                if (coveredUntil == target.Start && edge.LeftInset != target.LeftInset)
                    continue;
                if (edge.End == target.End && edge.RightInset != target.RightInset)
                    continue;
                hasNarrowerEdge |= edge.Start > target.Start || edge.End < target.End;
                coveredUntil = Math.Max(coveredUntil, edge.End);
                if (coveredUntil >= target.End)
                    return !requireNarrower || hasNarrowerEdge;
            }
            return false;
        }

        void RenderDiagonalBorders(Borders mergedBorders, Rectangle innerRect)
        {
            var bordersRenderer = new BordersRenderer(mergedBorders, _gfx);
            bordersRenderer.RenderDiagonally(BorderType.DiagonalDown, innerRect.X, innerRect.Y, innerRect.Width, innerRect.Height);
            bordersRenderer.RenderDiagonally(BorderType.DiagonalUp, innerRect.X, innerRect.Y, innerRect.Width, innerRect.Height);
        }

        void RenderContent(Cell cell, Rectangle innerRect)
        {
            var formattedCell = _formattedCells[cell];
            var renderInfos = formattedCell.GetRenderInfos();

            if (renderInfos == null)
                return;

            VerticalAlignment verticalAlignment = cell.VerticalAlignment;
            XUnitPt contentHeight = formattedCell.ContentHeight;
            XUnitPt innerHeight = innerRect.Height;
            Debug.Assert(cell.Column != null, "cell.Column != null");
            XUnitPt targetX = (float_)(innerRect.X + cell.Column.LeftPadding.Point);

            XUnitPt targetY;
            Debug.Assert(cell.Row != null, "cell.Row != null");
            if (verticalAlignment == VerticalAlignment.Bottom)
            {
                targetY = innerRect.Y + innerRect.Height;
                targetY -= (float_)cell.Row.BottomPadding.Point;
                targetY -= contentHeight;
            }
            else if (verticalAlignment == VerticalAlignment.Center)
            {
                targetY = (float_)(innerRect.Y + cell.Row.TopPadding.Point);
                targetY += (float_)(innerRect.Y + innerRect.Height - cell.Row.BottomPadding.Point);
                targetY -= contentHeight;
                targetY /= 2;
            }
            else
                targetY = (float_)(innerRect.Y + cell.Row.TopPadding.Point);

            RenderByInfos(targetX, targetY, renderInfos);
        }

        Rectangle GetInnerRect(XUnitPt startingHeight, Cell cell)
        {
            var bordersRenderer = new BordersRenderer(_mergedCells.GetEffectiveBordersPdf(cell, _lastHeaderRow), _gfx);
            var formattedCell = _formattedCells[cell];
            XUnitPt width = formattedCell.InnerWidth;

            XUnitPt y = _startY;
            Debug.Assert(cell.Row != null, "cell.Row != null");
            int cellRowIndex = cell.Row.Index; // Cache property result.
            if (cellRowIndex > _lastHeaderRow)
                y += startingHeight;
            else
                y += CalcMaxTopBorderWidth(0);

#if true
            if (!_bottomBorderMap.TryGetValue(cellRowIndex, out var upperBorderPos))
            {
                //_ = typeof(int);
            }
#else
            XUnitPt upperBorderPos = _bottomBorderMap[cell.Row.Index];
#endif

            y += upperBorderPos;
            if (cell.Row.Index > _lastHeaderRow)
                y -= _bottomBorderMap[_startRow];

#if true
            if (!_bottomBorderMap.TryGetValue(cellRowIndex + cell.MergeDown + 1, out var lowerBorderPos))
            {
                //_ = typeof(int);
            }
#else
            XUnitPt lowerBorderPos = _bottomBorderMap[cellRowIndex + cell.MergeDown + 1];
#endif

            XUnitPt height = lowerBorderPos - upperBorderPos;
            height -= bordersRenderer.GetWidth(BorderType.Bottom);

            XUnitPt x = _startX;
            Debug.Assert(cell.Column != null, "cell.Column != null");
            int cellColIndex = cell.Column.Index; // Cache property result.
            for (int clmIdx = 0; clmIdx < cellColIndex; ++clmIdx)
            {
                x += (float_)(_table.Columns[clmIdx]?.Width.Point ?? NRT.ThrowOnNull<int>());
            }
            x += LeftBorderOffset;

            return new(x, y, width, height);
        }

        internal override void Render()
        {
            InitRendering();
            RenderHeaderRows();
            var renderedCells = new List<Cell>();
            if (_startRow < _table.Rows.Count)
            {
                Cell cell; // = _table[_startRow, 0];

                int cellIdx = _mergedCells.BinarySearch(_table[_startRow, 0], new CellComparer());
                while (cellIdx < _mergedCells.Count)
                {
                    cell = _mergedCells[cellIdx];
                    Debug.Assert(cell.Row != null, "cell.Row != null");
                    if (cell.Row.Index > _endRow)
                        break;

                    renderedCells.Add(cell);
                    ++cellIdx;
                }
            }

            RenderCells(renderedCells);
        }

        void RenderCells(IReadOnlyList<Cell> cells, IReadOnlyList<Cell>? followingCells = null)
        {
            // A uniform double grid has two closed outlines: one facing each cell
            // and one around the outside of the table. Draw those as paths so the
            // corners are joins rather than unrelated line caps.
            XPen? gridPen = null;
            var uniformDoubleGrid = followingCells == null && TryGetUniformDoubleGridPen(cells, out gridPen);
            XPen? triplePen = null;
            var uniformTripleGrid = followingCells == null && TryGetUniformTripleGridPen(cells, out triplePen);
            XPen? compoundPen = null;
            BorderStyle compoundStyle = BorderStyle.None;
            var uniformCompoundGrid = followingCells == null &&
                TryGetUniformCompoundGridPen(cells, out compoundPen, out compoundStyle);
            XPen? threeDimensionalPen = null;
            BorderStyle threeDimensionalStyle = BorderStyle.None;
            var uniformThreeDimensionalGrid = followingCells == null &&
                TryGetUniformThreeDimensionalGridPen(cells, out threeDimensionalPen, out threeDimensionalStyle);
            var centeredGridBand = uniformDoubleGrid ? gridPen!.Width * 3 :
                uniformTripleGrid ? triplePen!.Width * 5 :
                uniformCompoundGrid ? compoundPen!.Width * ClosedRailWidthMultiplier(compoundStyle) : 0;
            var followingTopBorders = new HashSet<(int Boundary, int Start, int End, double LeftInset, double RightInset, double BorderWidth, double StrokeWidth, XColor Color, BorderStyle Style)>();
            var precedingBottomBorders = new HashSet<(int Boundary, int Start, int End, double LeftInset, double RightInset, double BorderWidth, double StrokeWidth, XColor Color, BorderStyle Style)>();
            var followingLeftBorders = new HashSet<(int Boundary, int Start, int End, double BottomInset, double BorderWidth, double StrokeWidth, XColor Color, BorderStyle Style)>();
            foreach (var cell in cells.Concat(followingCells ?? Array.Empty<Cell>()))
            {
                if (cell.RoundedCorner != RoundedCorner.None)
                    continue;
                var innerRect = GetInnerRect(CalcStartingHeight(), cell);
                var borders = new BordersRenderer(_mergedCells.GetEffectiveBordersPdf(cell, _lastHeaderRow), _gfx);
                var topPen = borders.GetPen(BorderType.Top);
                var bottomPen = borders.GetPen(BorderType.Bottom);
                var leftPen = borders.GetPen(BorderType.Left);
                if (leftPen != null && IsSharedEdgeStyle(borders.GetStyle(BorderType.Left), leftPen) &&
                    cell.Borders.GetBorder(BorderType.Left)?.Values.Visible != false)
                {
                    followingLeftBorders.Add(VerticalBorderKey(cell, borders, leftPen, true));
                }
                if (topPen != null && IsSharedEdgeStyle(borders.GetStyle(BorderType.Top), topPen) &&
                    cell.Borders.GetBorder(BorderType.Top)?.Values.Visible != false)
                {
                    followingTopBorders.Add(HorizontalBorderKey(cell, borders, topPen, true));
                }
                if (bottomPen != null && IsSharedEdgeStyle(borders.GetStyle(BorderType.Bottom), bottomPen) &&
                    cell.Borders.GetBorder(BorderType.Bottom)?.Values.Visible != false)
                {
                    precedingBottomBorders.Add(HorizontalBorderKey(cell, borders, bottomPen, false));
                }
            }
            var closedCells = new List<(Cell Cell, BorderStyle Style, XPen Pen, double Band)>();
            if (!uniformDoubleGrid && !uniformTripleGrid && !uniformCompoundGrid && !uniformThreeDimensionalGrid)
            {
                foreach (var cell in cells)
                    if (TryGetClosedCellRailPen(cell, out var pen, out var style))
                        closedCells.Add((cell, style, pen!, pen!.Width * ClosedRailWidthMultiplier(style)));
            }
            var closedByCell = closedCells.ToDictionary(item => item.Cell);
            var orderedClosed = closedCells.OrderBy(item => item.Band).ToArray();
            var closedMasks = orderedClosed.Select(item => GetClosedRailMasks(item.Cell, item.Style,
                item.Pen, item.Band)).ToArray();
            var junctions = !uniformDoubleGrid && !uniformTripleGrid && !uniformCompoundGrid && followingCells == null
                ? GetFourWayRailJunctions(cells)
                : new List<(JunctionArm North, JunctionArm South, JunctionArm West, JunctionArm East)>();
            var junctionMasks = junctions.Select(junction => GetFourWayRailJunctionBounds(
                junction.North, junction.South, junction.West, junction.East)).ToArray();
            var allClosedMasks = closedMasks.SelectMany(mask => mask).Concat(junctionMasks).ToArray();
            foreach (var cell in cells)
            {
                var closed = closedByCell.TryGetValue(cell, out var closedBorder);
                Rectangle? displayRect = null;
                if (closed)
                    displayRect = GetClosedCellInnerRect(cell, closedBorder.Band);
                else if (centeredGridBand > 0)
                {
                    var inner = GetInnerRect(CalcStartingHeight(), cell);
                    displayRect = new Rectangle(inner.X - XUnitPt.FromPoint(centeredGridBand / 2),
                        inner.Y, inner.Width, inner.Height);
                }
                RenderCell(cell, followingTopBorders, precedingBottomBorders, followingLeftBorders,
                    !uniformDoubleGrid && !uniformTripleGrid && !uniformCompoundGrid && !uniformThreeDimensionalGrid && !closed,
                    displayRect, allClosedMasks);
            }
            for (var index = 0; index < orderedClosed.Length; index++)
            {
                var closed = orderedClosed[index];
                var exclusions = closedMasks.Skip(index + 1).SelectMany(mask => mask)
                    .Concat(junctionMasks).ToArray();
                XGraphicsState? state = null;
                if (exclusions.Length > 0)
                {
                    state = _gfx.Save();
                    ExcludeBorderRegions(exclusions);
                }
                RenderClosedMultiRailCell(closed.Cell, closed.Style, closed.Pen, closed.Band);
                if (state != null)
                    _gfx.Restore(state);
            }
            foreach (var junction in junctions)
                RenderFourWayRailJunction(junction.North, junction.South, junction.West, junction.East);
            if (centeredGridBand > 0)
            {
                var state = _gfx.Save();
                _gfx.TranslateTransform(-centeredGridBand / 2, 0);
                if (uniformDoubleGrid)
                    RenderUniformDoubleGrid(cells, gridPen!);
                if (uniformTripleGrid)
                    RenderUniformTripleGrid(cells, triplePen!);
                if (uniformCompoundGrid)
                    RenderUniformCompoundGrid(cells, compoundPen!, compoundStyle);
                _gfx.Restore(state);
            }
            if (uniformThreeDimensionalGrid)
                RenderUniformThreeDimensionalGrid(cells, threeDimensionalPen!);
        }

        bool TryGetClosedCellRailPen(Cell cell, out XPen? pen, out BorderStyle style)
        {
            pen = null;
            style = BorderStyle.None;
            if (cell.RoundedCorner != RoundedCorner.None || _mergedCells.GetCoveringCell(cell) != cell)
                return false;
            var renderer = new BordersRenderer(cell.Borders, _gfx);
            var effective = new BordersRenderer(_mergedCells.GetEffectiveBordersPdf(cell, _lastHeaderRow), _gfx);
            foreach (var side in new[] { BorderType.Top, BorderType.Right, BorderType.Bottom, BorderType.Left })
            {
                var edgeStyle = renderer.GetStyle(side);
                if (edgeStyle is not (BorderStyle.Double or BorderStyle.Triple) &&
                    CompoundProfile(edgeStyle) == null ||
                    style != BorderStyle.None && edgeStyle != style ||
                    cell.Borders.GetBorder(side)?.Values.Visible == false)
                    return false;
                style = edgeStyle;
                var edgePen = renderer.GetPen(side);
                if (edgePen == null || edgePen.DashStyle != XDashStyle.Solid ||
                    pen != null && (pen.Width != edgePen.Width || pen.Color != edgePen.Color))
                    return false;
                // The effective-border resolver compares nominal widths, while
                // a multi-rail edge occupies a larger band. Keep the closed
                // box when that band wins; yield to a genuinely wider edge.
                var winningStyle = effective.GetStyle(side);
                if (winningStyle != edgeStyle &&
                    winningStyle is not (BorderStyle.Double or BorderStyle.Triple) &&
                    CompoundProfile(winningStyle) == null &&
                    effective.GetWidth(side).Point >=
                    edgePen.Width * ClosedRailWidthMultiplier(edgeStyle))
                    return false;
                pen ??= edgePen;
            }
            return pen != null;
        }

        Rectangle GetClosedCellInnerRect(Cell cell, double band)
        {
            var inner = GetInnerRect(CalcStartingHeight(), cell);
            var effectiveBorders = _mergedCells.GetEffectiveBordersPdf(cell, _lastHeaderRow);
            var effective = new BordersRenderer(effectiveBorders, _gfx);
            var rightWidth = effective.GetWidth(BorderType.Right).Point;
            var bottomWidth = effective.GetWidth(BorderType.Bottom).Point;
            var topCenter = HorizontalBoundaryCenter(cell, BorderType.Top, inner, bottomWidth);
            var bottomCenter = HorizontalBoundaryCenter(cell, BorderType.Bottom, inner, bottomWidth);
            // Layout reserves the winning edge width, which may belong to a
            // neighbor. The closed box uses its own band around shared row and
            // column crossings.
            return new Rectangle(inner.X + XUnitPt.FromPoint(band / 2 - LeftBorderOffset.Point),
                XUnitPt.FromPoint(topCenter + band / 2),
                inner.Width + XUnitPt.FromPoint(rightWidth - band),
                XUnitPt.FromPoint(bottomCenter - topCenter - band));
        }

        XRect[] GetClosedRailMasks(Cell cell, BorderStyle style, XPen pen, double band)
        {
            var inner = GetClosedCellInnerRect(cell, band);
            var borders = new BordersRenderer(cell.Borders, _gfx);
            if (CompoundProfile(style) != null)
            {
                var left = inner.X.Point - borders.GetWidth(BorderType.Left).Point;
                var top = inner.Y.Point - borders.GetWidth(BorderType.Top).Point;
                var right = inner.X.Point + inner.Width.Point;
                var bottom = inner.Y.Point + inner.Height.Point;
                var outerBottom = bottom + borders.GetWidth(BorderType.Bottom).Point;
                return new[]
                {
                    new XRect(left, top, band, outerBottom - top),
                    new XRect(right, top, band, outerBottom - top),
                    new XRect(inner.X.Point, top, inner.Width.Point, band),
                    new XRect(inner.X.Point, bottom, inner.Width.Point, band)
                };
            }
            var half = XUnitPt.FromPoint(pen.Width / 2);
            var innerLeft = inner.X - half;
            var innerTop = inner.Y - half;
            var innerRight = inner.X + inner.Width + half;
            var innerBottom = inner.Y + inner.Height + half;
            var outerLeft = inner.X - borders.GetWidth(BorderType.Left) + half;
            var outerTop = inner.Y - borders.GetWidth(BorderType.Top) + half;
            var outerRight = inner.X + inner.Width + borders.GetWidth(BorderType.Right) - half;
            var outerBottomRail = inner.Y + inner.Height + borders.GetWidth(BorderType.Bottom) - half;
            return new[]
            {
                new XRect(outerLeft - half, outerTop - half,
                    innerLeft - outerLeft + pen.Width, outerBottomRail - outerTop + pen.Width),
                new XRect(innerRight - half, outerTop - half,
                    outerRight - innerRight + pen.Width, outerBottomRail - outerTop + pen.Width),
                new XRect(innerLeft + half, outerTop - half,
                    innerRight - innerLeft - pen.Width, innerTop - outerTop + pen.Width),
                new XRect(innerLeft + half, innerBottom - half,
                    innerRight - innerLeft - pen.Width, outerBottomRail - innerBottom + pen.Width)
            };
        }

        (double Widest, double Narrowest) HorizontalBoundaryBands(int row)
        {
            if (_horizontalBoundaryWidths.TryGetValue(row, out var cached))
                return cached;
            var widest = 0.0;
            var narrowest = double.MaxValue;
            var measuredAbove = new HashSet<Cell>();
            var measuredBelow = new HashSet<Cell>();
            for (var column = 0; column < _table.Columns.Count; column++)
            {
                if (row > 0)
                {
                    var cellAbove = _mergedCells.GetCoveringCell(_table[row - 1, column]);
                    if (cellAbove != null && cellAbove.Row!.Index + cellAbove.MergeDown + 1 == row &&
                        measuredAbove.Add(cellAbove))
                    {
                        var previous = _mergedCells.GetEffectiveBordersPdf(cellAbove, _lastHeaderRow);
                        var width = new BordersRenderer(previous, _gfx).GetWidth(BorderType.Bottom).Point;
                        widest = Math.Max(widest, width);
                        if (width > 0) narrowest = Math.Min(narrowest, width);
                    }
                }
                if (row < _table.Rows.Count)
                {
                    var cellBelow = _mergedCells.GetCoveringCell(_table[row, column]);
                    if (cellBelow != null && cellBelow.Row!.Index == row && measuredBelow.Add(cellBelow))
                    {
                        var next = _mergedCells.GetEffectiveBordersPdf(cellBelow, _lastHeaderRow);
                        var width = new BordersRenderer(next, _gfx).GetWidth(BorderType.Top).Point;
                        widest = Math.Max(widest, width);
                        if (width > 0) narrowest = Math.Min(narrowest, width);
                    }
                }
            }
            var bands = (widest, narrowest == double.MaxValue ? 0 : narrowest);
            _horizontalBoundaryWidths.Add(row, bands);
            return bands;
        }

        double HorizontalBoundaryCenter(Cell cell, BorderType side, Rectangle inner, double bottomWidth)
        {
            var row = cell.Row!.Index + (side == BorderType.Bottom ? 1 : 0);
            var bands = HorizontalBoundaryBands(row);
            // At the table perimeter the narrowest stroke defines the cell
            // boundary; wider bands extend equally on both sides of it.
            if (row == 0)
                return inner.Y.Point - bands.Narrowest / 2;
            var outerEnd = side == BorderType.Top
                ? inner.Y.Point
                : inner.Y.Point + inner.Height.Point + bottomWidth;
            if (row == _table.Rows.Count)
                return outerEnd - bands.Narrowest / 2;
            return outerEnd - bands.Widest / 2;
        }

        void RenderClosedMultiRailCell(Cell cell, BorderStyle style, XPen pen, double band)
        {
            if (CompoundProfile(style) is { } profile)
            {
                RenderClosedCompoundCell(cell, pen, profile, band);
                return;
            }
            var inner = GetClosedCellInnerRect(cell, band);
            var effectiveBorders = _mergedCells.GetEffectiveBordersPdf(cell, _lastHeaderRow);
            var borders = new BordersRenderer(cell.Borders, _gfx);
            var half = XUnitPt.FromPoint(pen.Width / 2);
            var left = inner.X - half;
            var top = inner.Y - half;
            var right = inner.X + inner.Width + half;
            var bottom = inner.Y + inner.Height + half;
            var outerLeft = inner.X - borders.GetWidth(BorderType.Left) + half;
            var outerTop = inner.Y - borders.GetWidth(BorderType.Top) + half;
            var outerRight = inner.X + inner.Width + borders.GetWidth(BorderType.Right) - half;
            var outerBottom = inner.Y + inner.Height + borders.GetWidth(BorderType.Bottom) - half;

            // Keep the gaps in a dominant cell's border clear.  Otherwise a
            // neighboring single or narrower multi-rail border remains visible
            // through them at a four-way junction.  These strips cover only the
            // border band, leaving the cell's shaded content area intact.

            _gfx.DrawRectangle(pen, new XRect(left, top, right - left, bottom - top));
            if (style == BorderStyle.Triple)
            {
                var offset = XUnitPt.FromPoint(2 * pen.Width);
                _gfx.DrawRectangle(pen, new XRect(left - offset, top - offset,
                    right - left + 2 * offset, bottom - top + 2 * offset));
            }
            _gfx.DrawRectangle(pen, new XRect(outerLeft, outerTop,
                outerRight - outerLeft, outerBottom - outerTop));
            RenderDiagonalBorders(effectiveBorders, inner);
        }

        static double ClosedRailWidthMultiplier(BorderStyle style)
        {
            if (style == BorderStyle.Double)
                return 3;
            if (style == BorderStyle.Triple)
                return 5;
            var profile = CompoundProfile(style)!.Value;
            return profile.First + profile.Middle + profile.Gap * (profile.ThreeRails ? 2 : 1) +
                (profile.ThreeRails ? profile.First : 0);
        }

        sealed class JunctionArm
        {
            public BorderStyle Style { get; init; }
            public XPen Pen { get; init; } = null!;
            public double[] Positions { get; init; } = null!;
            public double[] Widths { get; init; } = null!;
            public double BorderWidth { get; init; }
        }

        JunctionArm? GetJunctionArm(Cell cell, BorderType side)
        {
            if (cell.RoundedCorner != RoundedCorner.None || cell.MergeRight != 0 || cell.MergeDown != 0 ||
                cell.Borders.GetBorder(side)?.Values.Visible == false)
                return null;
            var renderer = new BordersRenderer(_mergedCells.GetEffectiveBordersPdf(cell, _lastHeaderRow), _gfx);
            var style = renderer.GetStyle(side);
            var pen = renderer.GetPen(side);
            if (pen == null)
                return null;
            double[] widths;
            double gap;
            if (style is BorderStyle.Double or BorderStyle.Triple)
            {
                widths = Enumerable.Repeat(pen.Width, style == BorderStyle.Triple ? 3 : 2).ToArray();
                gap = pen.Width;
            }
            else if (CompoundProfile(style) is { } profile)
            {
                widths = profile.ThreeRails
                    ? new[] { pen.Width * profile.First, pen.Width * profile.Middle,
                        pen.Width * profile.First }
                    : new[] { pen.Width * profile.First, pen.Width * profile.Middle };
                gap = pen.Width * profile.Gap;
            }
            else
            {
                // A single or decorative edge can still own this arm.  Keep it
                // in the four-neighbor comparison even though the multi-rail
                // junction painter cannot redraw its pattern.
                widths = new[] { pen.Width };
                gap = 0;
            }

            var inner = GetInnerRect(CalcStartingHeight(), cell);
            var borderWidth = renderer.GetWidth(side).Point;
            var start = side switch
            {
                BorderType.Left => inner.X.Point - borderWidth,
                BorderType.Right => inner.X.Point + inner.Width.Point,
                BorderType.Top => inner.Y.Point - borderWidth,
                BorderType.Bottom => inner.Y.Point + inner.Height.Point,
                _ => throw new ArgumentOutOfRangeException(nameof(side))
            };
            // Both owners of a shared edge must put the winning arm on the
            // same column crossing, even if only one has a closed rail box.
            if (side == BorderType.Left)
                start = inner.X.Point - LeftBorderOffset.Point - borderWidth / 2;
            else if (side == BorderType.Right)
                start = inner.X.Point + inner.Width.Point + borderWidth -
                    LeftBorderOffset.Point - borderWidth / 2;
            else if (side == BorderType.Top)
                start = HorizontalBoundaryCenter(cell, side, inner,
                    renderer.GetWidth(BorderType.Bottom).Point) - borderWidth / 2;
            else if (side == BorderType.Bottom)
                start = HorizontalBoundaryCenter(cell, side, inner, borderWidth) - borderWidth / 2;
            var positions = new double[widths.Length];
            var offset = 0.0;
            for (var index = 0; index < widths.Length; index++)
            {
                positions[index] = start + offset + widths[index] / 2;
                offset += widths[index] + gap;
            }
            return new JunctionArm { Style = style, Pen = pen, Positions = positions,
                Widths = widths, BorderWidth = borderWidth };
        }

        JunctionArm? SelectJunctionArm(Cell first, BorderType firstSide,
            Cell second, BorderType secondSide)
        {
            var a = GetJunctionArm(first, firstSide);
            var b = GetJunctionArm(second, secondSide);
            if (a == null) return b;
            if (b == null) return a;
            return b.BorderWidth >= a.BorderWidth ? b : a;
        }

        List<(JunctionArm North, JunctionArm South, JunctionArm West, JunctionArm East)>
            GetFourWayRailJunctions(IReadOnlyList<Cell> cells)
        {
            var junctions = new List<(JunctionArm, JunctionArm, JunctionArm, JunctionArm)>();
            var grid = cells.Where(cell => cell.MergeRight == 0 && cell.MergeDown == 0 &&
                    cell.RoundedCorner == RoundedCorner.None)
                .ToDictionary(cell => (cell.Row!.Index, cell.Column!.Index));
            for (var row = 1; row < _table.Rows.Count; row++)
            for (var column = 1; column < _table.Columns.Count; column++)
            {
                if (!grid.TryGetValue((row - 1, column - 1), out var nw) ||
                    !grid.TryGetValue((row - 1, column), out var ne) ||
                    !grid.TryGetValue((row, column - 1), out var sw) ||
                    !grid.TryGetValue((row, column), out var se))
                    continue;
                var north = SelectJunctionArm(nw, BorderType.Right, ne, BorderType.Left);
                var south = SelectJunctionArm(sw, BorderType.Right, se, BorderType.Left);
                var west = SelectJunctionArm(nw, BorderType.Bottom, sw, BorderType.Top);
                var east = SelectJunctionArm(ne, BorderType.Bottom, se, BorderType.Top);
                if (north == null || south == null || west == null || east == null ||
                    north.Style != south.Style || north.Style != west.Style || north.Style != east.Style ||
                    north.Pen.DashStyle != XDashStyle.Solid ||
                    north.Pen.Width != south.Pen.Width || north.Pen.Width != west.Pen.Width ||
                    north.Pen.Width != east.Pen.Width || north.Pen.Color != south.Pen.Color ||
                    north.Pen.Color != west.Pen.Color || north.Pen.Color != east.Pen.Color)
                    continue;
                var count = north.Positions.Length;
                if (count != south.Positions.Length || count != west.Positions.Length ||
                    count != east.Positions.Length || count is not (2 or 3))
                    continue;
                if (Enumerable.Range(0, count).Any(index =>
                        Math.Abs(north.Positions[index] - south.Positions[index]) > 1 ||
                        Math.Abs(west.Positions[index] - east.Positions[index]) > 1))
                    continue;
                junctions.Add((north, south, west, east));
            }
            return junctions;
        }

        static XRect GetFourWayRailJunctionBounds(JunctionArm north, JunctionArm south,
            JunctionArm west, JunctionArm east)
        {
            var last = north.Positions.Length - 1;
            var stroke = new[] { north, south, west, east }.SelectMany(arm => arm.Widths).Max();
            var x0 = Math.Min(north.Positions[0], south.Positions[0]) - stroke;
            var x1 = Math.Max(north.Positions[last], south.Positions[last]) + stroke;
            var y0 = Math.Min(west.Positions[0], east.Positions[0]) - stroke;
            var y1 = Math.Max(west.Positions[last], east.Positions[last]) + stroke;
            return new XRect(x0, y0, x1 - x0, y1 - y0);
        }

        void RenderFourWayRailJunction(JunctionArm north, JunctionArm south,
            JunctionArm west, JunctionArm east)
        {
            var last = north.Positions.Length - 1;
            var bounds = GetFourWayRailJunctionBounds(north, south, west, east);
            var stroke = new[] { north, south, west, east }.SelectMany(arm => arm.Widths).Max();
            var x0 = bounds.X;
            var x1 = bounds.Right;
            var y0 = bounds.Y;
            var y1 = bounds.Bottom;

            // The outer rails turn around the four cells.  An odd profile has
            // a middle rail, which continues through both directions.
            DrawJunctionCorner(north, 0, west, 0, x0, y0, true, true);
            DrawJunctionCorner(north, last, east, 0, x1, y0, false, true);
            DrawJunctionCorner(south, 0, west, last, x0, y1, true, false);
            DrawJunctionCorner(south, last, east, last, x1, y1, false, false);
            if (last == 2)
            {
                var vertical = new XPen(north.Pen.Color, north.Widths[1]);
                var horizontal = new XPen(west.Pen.Color, west.Widths[1]);
                DrawCompoundRail(vertical, (north.Positions[1] + south.Positions[1]) / 2,
                    y0 - stroke, (north.Positions[1] + south.Positions[1]) / 2, y1 + stroke);
                DrawCompoundRail(horizontal, x0 - stroke,
                    (west.Positions[1] + east.Positions[1]) / 2, x1 + stroke,
                    (west.Positions[1] + east.Positions[1]) / 2);
            }
        }

        void DrawJunctionCorner(JunctionArm vertical, int verticalIndex,
            JunctionArm horizontal, int horizontalIndex, double xBoundary, double yBoundary,
            bool left, bool top)
        {
            var x = vertical.Positions[verticalIndex];
            var y = horizontal.Positions[horizontalIndex];
            var verticalPen = new XPen(vertical.Pen.Color, vertical.Widths[verticalIndex]);
            var horizontalPen = new XPen(horizontal.Pen.Color, horizontal.Widths[horizontalIndex]);
            DrawCompoundRail(verticalPen, x, top ? yBoundary - verticalPen.Width : y - horizontalPen.Width / 2,
                x, top ? y + horizontalPen.Width / 2 : yBoundary + verticalPen.Width);
            DrawCompoundRail(horizontalPen,
                left ? xBoundary - horizontalPen.Width : x - verticalPen.Width / 2, y,
                left ? x + verticalPen.Width / 2 : xBoundary + horizontalPen.Width, y);
        }

        void RenderClosedCompoundCell(Cell cell, XPen pen,
            (double First, double Middle, double Gap, bool ThreeRails) profile,
            double band)
        {
            var inner = GetClosedCellInnerRect(cell, band);
            var effectiveBorders = _mergedCells.GetEffectiveBordersPdf(cell, _lastHeaderRow);
            var borders = new BordersRenderer(cell.Borders, _gfx);
            var widths = profile.ThreeRails
                ? new[] { pen.Width * profile.First, pen.Width * profile.Middle,
                    pen.Width * profile.First }
                : new[] { pen.Width * profile.First, pen.Width * profile.Middle };
            var total = widths.Sum() + pen.Width * profile.Gap * (widths.Length - 1);
            var centers = new double[widths.Length];
            var position = 0.0;
            for (var index = 0; index < widths.Length; index++)
            {
                centers[index] = position + widths[index] / 2;
                position += widths[index] + pen.Width * profile.Gap;
            }

            var left = inner.X.Point - borders.GetWidth(BorderType.Left).Point;
            var top = inner.Y.Point - borders.GetWidth(BorderType.Top).Point;
            var right = inner.X.Point + inner.Width.Point;
            var bottom = inner.Y.Point + inner.Height.Point;
            var outerRight = right + borders.GetWidth(BorderType.Right).Point;
            var outerBottom = bottom + borders.GetWidth(BorderType.Bottom).Point;
            // Clear the full compound band before drawing.  Its gaps remain
            // empty when a narrower neighboring border meets this cell.

            var last = widths.Length - 1;
            for (var rail = 0; rail <= last; rail++)
            {
                var opposite = last - rail;
                var railPen = new XPen(pen.Color, widths[rail]);
                var topY = top + centers[rail];
                var bottomY = bottom + centers[rail];
                var leftX = left + centers[rail];
                var rightX = right + centers[rail];
                // The profile is ordered outer-to-inner on top/left and
                // inner-to-outer on bottom/right.  Reverse its index at the
                // latter corners so each rail joins its matching perimeter.
                DrawCompoundRail(railPen,
                    left + centers[rail] - widths[rail] / 2, topY,
                    right + centers[opposite] + widths[opposite] / 2, topY);
                DrawCompoundRail(railPen,
                    left + centers[opposite] - widths[opposite] / 2, bottomY,
                    right + centers[rail] + widths[rail] / 2, bottomY);
                DrawCompoundRail(railPen,
                    leftX, top + centers[rail] - widths[rail] / 2,
                    leftX, bottom + centers[opposite] + widths[opposite] / 2);
                DrawCompoundRail(railPen,
                    rightX, top + centers[opposite] - widths[opposite] / 2,
                    rightX, bottom + centers[rail] + widths[rail] / 2);
            }
            RenderDiagonalBorders(effectiveBorders, inner);
        }

        void DrawCompoundRail(XPen pen, double x1, double y1, double x2, double y2)
        {
#if PSGFX
            _gfx.DrawLine(pen, new XPoint(x1, y1), new XPoint(x2, y2));
#else
            _gfx.DrawLine(pen, x1, y1, x2, y2);
#endif
        }

        static (double First, double Middle, double Gap, bool ThreeRails)? CompoundProfile(BorderStyle style) => style switch
        {
            BorderStyle.ThinThickSmallGap => (1.0, 0.375, 0.375, false),
            BorderStyle.ThickThinSmallGap => (0.375, 1.0, 0.375, false),
            BorderStyle.ThinThickThinSmallGap => (0.375, 1.0, 0.375, true),
            BorderStyle.ThinThickMediumGap => (1.0, 0.5, 0.5, false),
            BorderStyle.ThickThinMediumGap => (0.5, 1.0, 0.5, false),
            BorderStyle.ThinThickThinMediumGap => (0.5, 1.0, 0.5, true),
            BorderStyle.ThinThickLargeGap => (0.75, 0.375, 1.0, false),
            BorderStyle.ThickThinLargeGap => (0.375, 0.75, 1.0, false),
            BorderStyle.ThinThickThinLargeGap => (0.375, 0.75, 1.0, true),
            _ => null
        };

        bool TryGetUniformCompoundGridPen(IReadOnlyList<Cell> cells, out XPen? pen, out BorderStyle style)
        {
            pen = null;
            style = BorderStyle.None;
            if (cells.Count == 0 || _lastHeaderRow >= 0 || _startRow != 0 ||
                _endRow != _table.Rows.Count - 1 || cells.Count != _table.Rows.Count * _table.Columns.Count)
                return false;
            foreach (var cell in cells)
            {
                if (cell.RoundedCorner != RoundedCorner.None || cell.MergeRight != 0 || cell.MergeDown != 0)
                    return false;
                var renderer = new BordersRenderer(_mergedCells.GetEffectiveBordersPdf(cell, _lastHeaderRow), _gfx);
                foreach (var side in new[] { BorderType.Top, BorderType.Right, BorderType.Bottom, BorderType.Left })
                {
                    var edgeStyle = renderer.GetStyle(side);
                    if (CompoundProfile(edgeStyle) == null || style != BorderStyle.None && edgeStyle != style)
                        return false;
                    style = edgeStyle;
                    var edgePen = renderer.GetPen(side);
                    if (edgePen == null || edgePen.DashStyle != XDashStyle.Solid ||
                        pen != null && (edgePen.Width != pen.Width || edgePen.Color != pen.Color))
                        return false;
                    pen ??= edgePen;
                }
            }
            return pen != null;
        }

        bool TryGetUniformThreeDimensionalGridPen(IReadOnlyList<Cell> cells, out XPen? pen, out BorderStyle style)
        {
            pen = null;
            style = BorderStyle.None;
            if (cells.Count == 0 || _lastHeaderRow >= 0 || _startRow != 0 ||
                _endRow != _table.Rows.Count - 1 || cells.Count != _table.Rows.Count * _table.Columns.Count)
                return false;
            foreach (var cell in cells)
            {
                if (cell.RoundedCorner != RoundedCorner.None || cell.MergeRight != 0 || cell.MergeDown != 0)
                    return false;
                var renderer = new BordersRenderer(_mergedCells.GetEffectiveBordersPdf(cell, _lastHeaderRow), _gfx);
                foreach (var side in new[] { BorderType.Top, BorderType.Right, BorderType.Bottom, BorderType.Left })
                {
                    var edgeStyle = renderer.GetStyle(side);
                    if (edgeStyle is not (BorderStyle.Emboss3D or BorderStyle.Engrave3D) ||
                        style != BorderStyle.None && edgeStyle != style)
                        return false;
                    style = edgeStyle;
                    var edgePen = renderer.GetPen(side);
                    if (edgePen == null || pen != null &&
                        (edgePen.Width != pen.Width || edgePen.Color != pen.Color))
                        return false;
                    pen ??= edgePen;
                }
            }
            return pen != null;
        }

        void RenderUniformThreeDimensionalGrid(IReadOnlyList<Cell> cells, XPen pen)
        {
            var grid = cells.ToDictionary(cell => (cell.Row!.Index, cell.Column!.Index),
                cell => GetInnerRect(CalcStartingHeight(), cell));
            var first = grid[(0, 0)];
            var last = grid[(_table.Rows.Count - 1, _table.Columns.Count - 1)];
            var thickness = XUnitPt.FromPoint(pen.Width * 1.75);
            var left = first.X - thickness;
            var top = first.Y - thickness;
            var right = last.X + last.Width;
            var bottom = last.Y + last.Height;
            var renderer = new BordersRenderer(_mergedCells.GetEffectiveBordersPdf(cells[0], _lastHeaderRow), _gfx);
            for (var column = 0; column <= _table.Columns.Count; column++)
            {
                var x = column == _table.Columns.Count ? right : grid[(0, column)].X - thickness;
                renderer.RenderVertically(BorderType.Left, x, top, bottom - top + thickness);
            }
            for (var row = 0; row <= _table.Rows.Count; row++)
            {
                var y = row == _table.Rows.Count ? bottom : grid[(row, 0)].Y - thickness;
                renderer.RenderHorizontally(BorderType.Top, left, y, right - left + thickness);
            }
            foreach (var cell in cells)
                RenderDiagonalBorders(_mergedCells.GetEffectiveBordersPdf(cell, _lastHeaderRow),
                    GetInnerRect(CalcStartingHeight(), cell));
        }

        void RenderUniformCompoundGrid(IReadOnlyList<Cell> cells, XPen pen, BorderStyle style)
        {
            var profile = CompoundProfile(style)!.Value;
            var firstWidth = pen.Width * profile.First;
            var middleWidth = pen.Width * profile.Middle;
            var gap = pen.Width * profile.Gap;
            if (profile.ThreeRails)
            {
                var firstPen = new XPen(pen.Color, firstWidth);
                var middlePen = new XPen(pen.Color, middleWidth);
                var offset = XUnitPt.FromPoint((firstWidth + middleWidth) / 2 + gap);
                XUnitPt? outerLeft = null, outerTop = null, outerRight = null, outerBottom = null;
                foreach (var cell in cells)
                {
                    var inner = GetInnerRect(CalcStartingHeight(), cell);
                    var borders = new BordersRenderer(_mergedCells.GetEffectiveBordersPdf(cell, _lastHeaderRow), _gfx);
                    var half = XUnitPt.FromPoint(firstWidth / 2);
                    var left = inner.X - half;
                    var top = inner.Y - half;
                    var right = inner.X + inner.Width + half;
                    var bottom = inner.Y + inner.Height + half;
                    _gfx.DrawRectangle(firstPen, new XRect(left, top, right - left, bottom - top));
                    _gfx.DrawRectangle(middlePen, new XRect(left - offset, top - offset,
                        right - left + 2 * offset, bottom - top + 2 * offset));
                    var cellOuterLeft = inner.X - borders.GetWidth(BorderType.Left) + half;
                    var cellOuterTop = inner.Y - borders.GetWidth(BorderType.Top) + half;
                    var cellOuterRight = inner.X + inner.Width + borders.GetWidth(BorderType.Right) - half;
                    var cellOuterBottom = inner.Y + inner.Height + borders.GetWidth(BorderType.Bottom) - half;
                    outerLeft = outerLeft == null || cellOuterLeft < outerLeft ? cellOuterLeft : outerLeft;
                    outerTop = outerTop == null || cellOuterTop < outerTop ? cellOuterTop : outerTop;
                    outerRight = outerRight == null || cellOuterRight > outerRight ? cellOuterRight : outerRight;
                    outerBottom = outerBottom == null || cellOuterBottom > outerBottom ? cellOuterBottom : outerBottom;
                }
                _gfx.DrawRectangle(firstPen, new XRect(outerLeft!.Value, outerTop!.Value,
                    outerRight!.Value - outerLeft.Value, outerBottom!.Value - outerTop.Value));
            }
            else
            {
                RenderUniformTwoRailGrid(cells, pen, profile);
                return;
            }
            foreach (var cell in cells)
                RenderDiagonalBorders(_mergedCells.GetEffectiveBordersPdf(cell, _lastHeaderRow),
                    GetInnerRect(CalcStartingHeight(), cell));
        }

        void RenderUniformTwoRailGrid(IReadOnlyList<Cell> cells, XPen pen,
            (double First, double Middle, double Gap, bool ThreeRails) profile)
        {
            var first = pen.Width * profile.First;
            var second = pen.Width * profile.Middle;
            var gap = pen.Width * profile.Gap;
            var band = first + gap + second;
            var grid = cells.ToDictionary(cell => (cell.Row!.Index, cell.Column!.Index),
                cell => GetInnerRect(CalcStartingHeight(), cell));
            var x = new double[_table.Columns.Count + 1];
            var y = new double[_table.Rows.Count + 1];
            for (var column = 0; column < _table.Columns.Count; column++)
                x[column] = grid[(0, column)].X.Point - band;
            for (var row = 0; row < _table.Rows.Count; row++)
                y[row] = grid[(row, 0)].Y.Point - band;
            var last = grid[(_table.Rows.Count - 1, _table.Columns.Count - 1)];
            x[^1] = last.X.Point + last.Width.Point;
            y[^1] = last.Y.Point + last.Height.Point;
            var brush = new XSolidBrush(pen.Color);
            void Fill(double left, double top, double width, double height)
            {
                if (width > 0 && height > 0)
                    _gfx.DrawRectangle(brush, new XRect(left, top, width, height));
            }

            // Lines occupy only the spans between junctions. No white mask or
            // crossing stroke is needed. A small same-color overlap at tile
            // seams avoids antialiasing cracks without entering the white channel.
            const double tileOverlap = 0.2;
            for (var column = 0; column <= _table.Columns.Count; column++)
            for (var row = 0; row < _table.Rows.Count; row++)
            {
                var top = y[row] + band - tileOverlap;
                var height = y[row + 1] - top + tileOverlap;
                Fill(x[column], top, first, height);
                Fill(x[column] + first + gap, top, second, height);
            }
            for (var row = 0; row <= _table.Rows.Count; row++)
            for (var column = 0; column < _table.Columns.Count; column++)
            {
                var left = x[column] + band - tileOverlap;
                var width = x[column + 1] - left + tileOverlap;
                Fill(left, y[row], width, first);
                Fill(left, y[row] + first + gap, width, second);
            }

            // A junction owns the four rail corners. Their shared white cross
            // has exactly the same gap width as the adjoining line segments.
            for (var row = 0; row <= _table.Rows.Count; row++)
            for (var column = 0; column <= _table.Columns.Count; column++)
            {
                var left = x[column];
                var top = y[row];
                Fill(left, top, first, first);
                Fill(left + first + gap, top, second, first);
                Fill(left, top + first + gap, first, second);
                Fill(left + first + gap, top + first + gap, second, second);

                // Close channels where they meet the table perimeter. The
                // outside rail continues across each T junction and corner.
                if (row == 0)
                    Fill(left + first - tileOverlap, top, gap + 2 * tileOverlap, first);
                if (row == _table.Rows.Count)
                    Fill(left + first - tileOverlap, top + first + gap,
                        gap + 2 * tileOverlap, second);
                if (column == 0)
                    Fill(left, top + first - tileOverlap, first, gap + 2 * tileOverlap);
                if (column == _table.Columns.Count)
                    Fill(left + first + gap, top + first - tileOverlap,
                        second, gap + 2 * tileOverlap);
            }
            foreach (var cell in cells)
                RenderDiagonalBorders(_mergedCells.GetEffectiveBordersPdf(cell, _lastHeaderRow),
                    GetInnerRect(CalcStartingHeight(), cell));
        }

        bool TryGetUniformTripleGridPen(IReadOnlyList<Cell> cells, out XPen? pen)
        {
            pen = null;
            if (cells.Count == 0 || _lastHeaderRow >= 0 || _startRow != 0 ||
                _endRow != _table.Rows.Count - 1 || cells.Count != _mergedCells.Count)
                return false;
            foreach (var cell in cells)
            {
                if (cell.RoundedCorner != RoundedCorner.None)
                    return false;
                var renderer = new BordersRenderer(_mergedCells.GetEffectiveBordersPdf(cell, _lastHeaderRow), _gfx);
                foreach (var side in new[] { BorderType.Top, BorderType.Right, BorderType.Bottom, BorderType.Left })
                {
                    if (renderer.GetStyle(side) != BorderStyle.Triple)
                        return false;
                    var edgePen = renderer.GetPen(side);
                    if (edgePen == null || edgePen.DashStyle != XDashStyle.Solid ||
                        pen != null && (edgePen.Width != pen.Width || edgePen.Color != pen.Color))
                        return false;
                    pen ??= edgePen;
                }
            }
            return pen != null;
        }

        void RenderUniformTripleGrid(IReadOnlyList<Cell> cells, XPen pen)
        {
            var halfStroke = XUnitPt.FromPoint(pen.Width / 2);
            var middleOffset = XUnitPt.FromPoint(2 * pen.Width);
            XUnitPt? outerLeft = null, outerTop = null, outerRight = null, outerBottom = null;
            foreach (var cell in cells)
            {
                var inner = GetInnerRect(CalcStartingHeight(), cell);
                var effectiveBorders = _mergedCells.GetEffectiveBordersPdf(cell, _lastHeaderRow);
                var borders = new BordersRenderer(effectiveBorders, _gfx);
                var left = inner.X - halfStroke;
                var top = inner.Y - halfStroke;
                var right = inner.X + inner.Width + halfStroke;
                var bottom = inner.Y + inner.Height + halfStroke;
                _gfx.DrawRectangle(pen, new XRect(left, top, right - left, bottom - top));
                _gfx.DrawRectangle(pen, new XRect(left - middleOffset, top - middleOffset,
                    right - left + 2 * middleOffset, bottom - top + 2 * middleOffset));

                var cellOuterLeft = inner.X - borders.GetWidth(BorderType.Left) + halfStroke;
                var cellOuterTop = inner.Y - borders.GetWidth(BorderType.Top) + halfStroke;
                var cellOuterRight = inner.X + inner.Width + borders.GetWidth(BorderType.Right) - halfStroke;
                var cellOuterBottom = inner.Y + inner.Height + borders.GetWidth(BorderType.Bottom) - halfStroke;
                outerLeft = outerLeft == null || cellOuterLeft < outerLeft ? cellOuterLeft : outerLeft;
                outerTop = outerTop == null || cellOuterTop < outerTop ? cellOuterTop : outerTop;
                outerRight = outerRight == null || cellOuterRight > outerRight ? cellOuterRight : outerRight;
                outerBottom = outerBottom == null || cellOuterBottom > outerBottom ? cellOuterBottom : outerBottom;
                RenderDiagonalBorders(effectiveBorders, inner);
            }
            _gfx.DrawRectangle(pen, new XRect(outerLeft!.Value, outerTop!.Value,
                outerRight!.Value - outerLeft.Value, outerBottom!.Value - outerTop.Value));
        }

        bool TryGetUniformDoubleGridPen(IReadOnlyList<Cell> cells, out XPen? pen)
        {
            pen = null;
            if (cells.Count == 0 || _lastHeaderRow >= 0 || _startRow != 0 ||
                _endRow != _table.Rows.Count - 1 || cells.Count != _mergedCells.Count)
                return false;

            foreach (var cell in cells)
            {
                if (cell.RoundedCorner != RoundedCorner.None)
                    return false;
                var renderer = new BordersRenderer(_mergedCells.GetEffectiveBordersPdf(cell, _lastHeaderRow), _gfx);
                foreach (var side in new[] { BorderType.Top, BorderType.Right, BorderType.Bottom, BorderType.Left })
                {
                    if (renderer.GetStyle(side) != BorderStyle.Double)
                        return false;
                    var edgePen = renderer.GetPen(side);
                    if (edgePen == null || edgePen.DashStyle != XDashStyle.Solid ||
                        pen != null && (edgePen.Width != pen.Width || edgePen.Color != pen.Color))
                        return false;
                    pen ??= edgePen;
                }
            }
            return pen != null;
        }

        void RenderUniformDoubleGrid(IReadOnlyList<Cell> cells, XPen pen)
        {
            var halfStroke = XUnitPt.FromPoint(pen.Width / 2);
            XUnitPt? outerLeft = null, outerTop = null, outerRight = null, outerBottom = null;
            foreach (var cell in cells)
            {
                var inner = GetInnerRect(CalcStartingHeight(), cell);
                var effectiveBorders = _mergedCells.GetEffectiveBordersPdf(cell, _lastHeaderRow);
                var borders = new BordersRenderer(effectiveBorders, _gfx);
                var left = inner.X - halfStroke;
                var top = inner.Y - halfStroke;
                var right = inner.X + inner.Width + halfStroke;
                var bottom = inner.Y + inner.Height + halfStroke;
                _gfx.DrawRectangle(pen, new XRect(left, top, right - left, bottom - top));

                var cellOuterLeft = inner.X - borders.GetWidth(BorderType.Left) + halfStroke;
                var cellOuterTop = inner.Y - borders.GetWidth(BorderType.Top) + halfStroke;
                var cellOuterRight = inner.X + inner.Width + borders.GetWidth(BorderType.Right) - halfStroke;
                var cellOuterBottom = inner.Y + inner.Height + borders.GetWidth(BorderType.Bottom) - halfStroke;
                outerLeft = outerLeft == null || cellOuterLeft < outerLeft ? cellOuterLeft : outerLeft;
                outerTop = outerTop == null || cellOuterTop < outerTop ? cellOuterTop : outerTop;
                outerRight = outerRight == null || cellOuterRight > outerRight ? cellOuterRight : outerRight;
                outerBottom = outerBottom == null || cellOuterBottom > outerBottom ? cellOuterBottom : outerBottom;
                RenderDiagonalBorders(effectiveBorders, inner);
            }
            _gfx.DrawRectangle(pen, new XRect(outerLeft!.Value, outerTop!.Value,
                outerRight!.Value - outerLeft.Value, outerBottom!.Value - outerTop.Value));
        }

        void InitFormat(Area area, FormatInfo? previousFormatInfo)
        {
            _horizontalBoundaryWidths.Clear();
            var prevTableFormatInfo = (TableFormatInfo?)previousFormatInfo;
            var tblRenderInfo = new TableRenderInfo
            {
                DocumentObject = _table
            };

            _renderInfo = tblRenderInfo;

            if (prevTableFormatInfo != null)
            {
                _mergedCells = prevTableFormatInfo.MergedCells ?? NRT.ThrowOnNull<MergedCellList>();
                _formattedCells = prevTableFormatInfo.FormattedCells ?? NRT.ThrowOnNull<Dictionary<Cell, FormattedCell>>();
                _bottomBorderMap = prevTableFormatInfo.BottomBorderMap ?? NRT.ThrowOnNull<Dictionary<int, XUnitPt>>();
                _lastHeaderRow = prevTableFormatInfo.LastHeaderRow;
                //_connectedRowsMap = prevTableFormatInfo.ConnectedRowsMap ?? NRT.ThrowOnNull<Dictionary<int, int>>();
                _connectedRowsMap = prevTableFormatInfo.ConnectedRowsMap ?? NRT.ThrowOnNull<int[]>();
                _minMergedCellRowMap = prevTableFormatInfo.MinMergedCellRowMap ?? NRT.ThrowOnNull<int[]>();
                _startRow = prevTableFormatInfo.EndRow + 1;
            }
            else
            {
                // InitFormat is called for every page. Some tasks must be performed only once. Do them here.

                // Equalize the two borders, that are used to determine a rounded corner’s border.
                // This way the innerWidth of the cell, which is got by the saved _formattedCells, is the same regardless of which corner relevant border is set.
                foreach (var row in _table.Rows.Cast<Row>())
                {
                    foreach (var cell in row.Cells.Cast<Cell>())
                        EqualizeRoundedCornerBorders(cell);
                }

                _mergedCells = new MergedCellList(_table);
                FormatCells();
                CalcLastHeaderRow();
                CreateConnectedRows();
                CreateBottomBorderMap();
                if (_doHorizontalBreak)
                {
                    CalcLastHeaderColumn();
                    CreateConnectedColumns();
                }
                _startRow = _lastHeaderRow + 1;
            }
            ((TableFormatInfo)tblRenderInfo.FormatInfo).MergedCells = _mergedCells;
            ((TableFormatInfo)tblRenderInfo.FormatInfo).FormattedCells = _formattedCells;
            ((TableFormatInfo)tblRenderInfo.FormatInfo).BottomBorderMap = _bottomBorderMap;
            ((TableFormatInfo)tblRenderInfo.FormatInfo).ConnectedRowsMap = _connectedRowsMap;
            ((TableFormatInfo)tblRenderInfo.FormatInfo).MinMergedCellRowMap = _minMergedCellRowMap;
            ((TableFormatInfo)tblRenderInfo.FormatInfo).LastHeaderRow = _lastHeaderRow;
        }

        void FormatCells()
        {
            _formattedCells = new(); //new Sorted_List(new CellComparer());
            int count = _mergedCells.Count;
            for (var index = 0; index < count; index++)
            {
                var cell = _mergedCells[index];
                var formattedCell = new FormattedCell(cell, _documentRenderer,
                    _mergedCells.GetEffectiveBordersPdf(cell, _lastHeaderRow),
                    _fieldInfos, 0, 0);
                formattedCell.Format(_gfx);
                _formattedCells.Add(cell, formattedCell);
            }
        }

        /// <summary>
        /// Formats (measures) the table.
        /// </summary>
        /// <param name="area">The area on which to fit the table.</param>
        /// <param name="previousFormatInfo"> </param>
        internal override void Format(Area area, FormatInfo? previousFormatInfo)
        {
            if (DocumentRelations.GetParent(_table) is DocumentElements elements)
            {
                if (DocumentRelations.GetParent(elements) is Section section)
                    _doHorizontalBreak = section.PageSetup.HorizontalPageBreak;
            }

            _renderInfo = new TableRenderInfo();
            InitFormat(area, previousFormatInfo);

            // Don’t take any Rows higher then MaxElementHeight
            XUnitPt topHeight = CalcStartingHeight();
            XUnitPt offset;
            if (_startRow > _lastHeaderRow + 1 &&
                _startRow < _table.Rows.Count)
                offset = _bottomBorderMap[_startRow] - topHeight;
            else
                offset = -CalcMaxTopBorderWidth(0);

            int probeRow = _startRow;
            XUnitPt currentHeight = 0;
            XUnitPt startingHeight = 0;
            bool isEmpty = false;

            while (probeRow < _table.Rows.Count)
            {
                bool firstProbe = probeRow == _startRow;
                probeRow = _connectedRowsMap[probeRow];
                // Don’t take any Rows higher then MaxElementHeight
                XUnitPt probeHeight = _bottomBorderMap[probeRow + 1] - offset;
                // First test whether MaxElementHeight has been set.
                if (MaxElementHeight > 0 && firstProbe && probeHeight > MaxElementHeight - Tolerance)
                    probeHeight = MaxElementHeight - Tolerance;
                //if (firstProbe && probeHeight > MaxElementHeight - Tolerance)
                //    probeHeight = MaxElementHeight - Tolerance;

                //The height for the first new row(s) + headerrows:
                if (startingHeight == 0)
                {
                    if (probeHeight > area.Height)
                    {
                        isEmpty = true;
                        break;
                    }
                    startingHeight = probeHeight;
                }

                if (probeHeight > area.Height)
                    break;

                else
                {
                    _currRow = probeRow;
                    currentHeight = probeHeight;
                    ++probeRow;
                }
            }
            if (!isEmpty)
            {
                var formatInfo = (TableFormatInfo)_renderInfo.FormatInfo;
                formatInfo.StartRow = _startRow;
                formatInfo._isEnding = _currRow >= _table.Rows.Count - 1;
                formatInfo.EndRow = _currRow;

                UpdateThisPagesBookmarks(_startRow, _currRow);
            }
            FinishLayoutInfo(area, currentHeight, startingHeight);
        }

        /// <summary>
        /// Updates the bookmarks in the given rows.
        /// Otherwise, each BookmarkField will refer to the first page of the table, because initially they are set before the table gets split over the pages.
        /// </summary>
        void UpdateThisPagesBookmarks(int startRow, int endRow)
        {
            if (_table.Rows.Count == 0)
                return;

            for (var r = startRow; r <= endRow; r++)
            {
                var row = _table.Rows[r];

                foreach (var bookmark in row.GetElementsRecursively<BookmarkField>())
                {
                    // _fieldInfos can be null when RenderObject is used instead of RenderDocument.
                    if (_fieldInfos == null)
                        NRT.ThrowOnNull();
                    _fieldInfos.AddBookmark(bookmark.Name);
                }
            }
        }

        void FinishLayoutInfo(Area area, XUnitPt currentHeight, XUnitPt startingHeight)
        {
            var layoutInfo = _renderInfo.LayoutInfo;
            if (_currRow >= 0 && _currRow == _table.Rows.Count - 1 && _table.Columns.Count > 0 &&
                Enumerable.Range(0, _table.Columns.Count).Any(column =>
                    TryGetClosedCellRailPen(_table[_currRow, column], out _, out _)))
            {
                var bands = HorizontalBoundaryBands(_table.Rows.Count);
                layoutInfo.MarginBottom = XUnitPt.FromPoint((bands.Widest - bands.Narrowest) / 2);
            }
            layoutInfo.StartingHeight = startingHeight;
            //REM: Trailing height would have to be calculated in case tables had a keep with next property.
            layoutInfo.TrailingHeight = 0;
            if (_currRow >= 0)
            {
                layoutInfo.ContentArea = new Rectangle(area.X, area.Y, 0, currentHeight);
                XUnitPt width = LeftBorderOffset;
                //foreach (Column clm in _table.Columns)
                foreach (var clm in _table.Columns.Cast<Column>())
                {
                    width += (float_)(clm.Width.Point);
                }
                layoutInfo.ContentArea.Width = width;
            }
            layoutInfo.MinWidth = layoutInfo.ContentArea.Width;

            //if (_table.Rows.Values.LeftIndent is not null)
            if (!_table.Rows.Values.LeftIndent.IsValueNullOrEmpty())
                layoutInfo.Left = (float_)_table.Rows.LeftIndent.Point;

            else if (_table.Rows.Alignment == RowAlignment.Left)
            {
                XUnitPt leftOffset = LeftBorderOffset;
                leftOffset += (float_)_table.Columns[0].LeftPadding.Point;
                layoutInfo.Left = -leftOffset;
            }

            switch (_table.Rows.Alignment)
            {
                case RowAlignment.Left:
                    layoutInfo.HorizontalAlignment = ElementAlignment.Near;
                    break;

                case RowAlignment.Right:
                    layoutInfo.HorizontalAlignment = ElementAlignment.Far;
                    break;

                case RowAlignment.Center:
                    layoutInfo.HorizontalAlignment = ElementAlignment.Center;
                    break;
            }
        }

        XUnitPt LeftBorderOffset
        {
            get
            {
                if (_leftBorderOffset < 0)
                {
                    if (_table.Rows.Count > 0 && _table.Columns.Count > 0)
                    {
                        var borders = _mergedCells.GetEffectiveBordersPdf(_table[0, 0], _lastHeaderRow);
                        var bordersRenderer = new BordersRenderer(borders, _gfx);
                        _leftBorderOffset = bordersRenderer.GetWidth(BorderType.Left);
                    }
                    else
                        _leftBorderOffset = 0;
                }
                return _leftBorderOffset;
            }
        }

        XUnitPt _leftBorderOffset = -1;

        /// <summary>
        /// Calculates either the height of the header rows or the height of the uppermost top border.
        /// </summary>
        XUnitPt CalcStartingHeight()
        {
            XUnitPt height = 0;
            if (_lastHeaderRow >= 0)
            {
                height = _bottomBorderMap[_lastHeaderRow + 1];
                height += CalcMaxTopBorderWidth(0);
            }
            else
            {
                if (_table.Rows.Count > _startRow)
                    height = CalcMaxTopBorderWidth(_startRow);
            }
            if (_startRow > 0 && _lastHeaderRow < 0 &&
                _documentRenderer?.TableFragmentTopBorderReservation?.Invoke(_table, _startRow) is double requested &&
                !double.IsNaN(requested) && !double.IsInfinity(requested) && requested > height.Point)
                height = XUnitPt.FromPoint(requested);
            return height;
        }

        void CalcLastHeaderColumn()
        {
            _lastHeaderColumn = -1;
            foreach (var clm in _table.Columns.Cast<Column>())
            {
                if (clm.HeadingFormat)
                    _lastHeaderColumn = clm.Index;
                else break;
            }
            if (_lastHeaderColumn >= 0)
                _lastHeaderRow = CalcLastConnectedColumn(_lastHeaderColumn);

            // Ignore heading format if all the table is heading.
            if (_lastHeaderRow == _table.Rows.Count - 1)
                _lastHeaderRow = -1;
        }

        void CalcLastHeaderRow()
        {
            _lastHeaderRow = -1;
            foreach (var row in _table.Rows.Cast<Row>())
            {
                if (row.HeadingFormat)
                    _lastHeaderRow = row.Index;
                else break;
            }
            // Do not use _connectedRowsMap here, it was not yet initialized.
            if (_lastHeaderRow >= 0)
                _lastHeaderRow = CalcLastConnectedRowDirect(_lastHeaderRow);

            // Ignore heading format if all the table is heading.
            if (_lastHeaderRow == _table.Rows.Count - 1)
                _lastHeaderRow = -1;
        }

        /// <summary>
        /// Calculate the last covered row where every row, taking MergeDown into account.
        /// </summary>
        void CreateConnectedRows()
        {
            int rows = _table.Rows.Count;
            int cols = _table.Columns.Count;
            _connectedRowsMap = new int[rows];
            _minMergedCellRowMap = new int[rows];
            int lastRowIndex = -1;
            int count = _mergedCells.Count;
            for (var index = 0; index < count; index++)
            {
                var cell = _mergedCells[index];
                Debug.Assert(cell.Row != null, "cell.Row != null");
                // _mergedCells are sorted from top to bottom and left to right.
                int cellRowIndex = cell.Row.Index;
                // Skip cells from the same row we already handled.
                if (lastRowIndex != cellRowIndex /*&& !_connectedRowsMap.ContainsKey(cellRowIndex)*/)
                {
                    int lastConnectedRow = CalcLastConnectedRowDirect(cellRowIndex);
                    _connectedRowsMap[cellRowIndex] = lastConnectedRow;
                    lastRowIndex = cellRowIndex;
                }

                int cellBottom = cellRowIndex + cell.MergeDown;
                if (cellBottom > 0 && _minMergedCellRowMap[cellBottom] == 0) // 0 can be the correct value for some rows.
                    _minMergedCellRowMap[cellBottom] = cellRowIndex;
            }
        }

        void CreateConnectedColumns()
        {
            _connectedColumnsMap = new(); //new SortedList();
            int count = _mergedCells.Count;
            for (var index = 0; index < count; index++)
            {
                var cell = _mergedCells[index];
                Debug.Assert(cell.Column != null, "cell.Column != null");
                if (!_connectedColumnsMap.ContainsKey(cell.Column.Index))
                {
                    int lastConnectedColumn = CalcLastConnectedColumn(cell.Column.Index);
                    _connectedColumnsMap[cell.Column.Index] = lastConnectedColumn;
                }
            }
        }

        void CreateBottomBorderMap()
        {
#if DEBUG
            CreateBottomBorderMapOld();
#endif
            _bottomBorderMap = new(); //new SortedList();
            _bottomBorderMap.Add(0, XUnitPt.FromPoint(0));
            int skipIndex = 0;
            int lastBorderRow = 0;
            int count = _table.Rows.Count;
            while (!_bottomBorderMap.ContainsKey(count))
            {
                CreateNextBottomBorderPosition(ref skipIndex, ref lastBorderRow);
            }
        }

#if DEBUG
        void CreateBottomBorderMapOld()
        {
            _bottomBorderMapOld = new Dictionary<int, XUnitPt>(); //new SortedList();
            _bottomBorderMapOld.Add(0, XUnitPt.FromPoint(0));
            while (!_bottomBorderMapOld.ContainsKey(_table.Rows.Count))
            {
                CreateNextBottomBorderPositionOld();
            }
        }

        Dictionary<int, XUnitPt> _bottomBorderMapOld = null!;

        /// <summary>
        ///   Creates the next bottom border position.
        /// </summary>
        void CreateNextBottomBorderPositionOld()
        {
            //int lastIdx = _bottomBorderMap.Count - 1;
            // SortedList version:
            //int lastBorderRow = (int)bottomBorderMap.GetKey(lastIdx);
            //XUnitPt lastPos = (XUnitPt)bottomBorderMap.GetByIndex(lastIdx);
            int lastBorderRow = 0;
            foreach (int key in _bottomBorderMapOld.Keys)
            {
                if (key > lastBorderRow)
                    lastBorderRow = key;
            }
            XUnitPt lastPos = _bottomBorderMapOld[lastBorderRow];

            Cell minMergedCell = GetMinMergedCell(lastBorderRow);
            FormattedCell minMergedFormattedCell = _formattedCells[minMergedCell];
            XUnitPt maxBottomBorderPosition = lastPos + minMergedFormattedCell.InnerHeight;
            maxBottomBorderPosition += CalcBottomBorderWidth(minMergedCell);

            foreach (Cell cell in _mergedCells)
            {
                if (cell.Row!.Index > minMergedCell.Row!.Index + minMergedCell.MergeDown)
                    break;

                if (cell.Row.Index + cell.MergeDown == minMergedCell.Row.Index + minMergedCell.MergeDown)
                {
                    FormattedCell formattedCell = _formattedCells[cell];
                    {
#if true
                        if (!_bottomBorderMapOld.TryGetValue(cell.Row.Index, out var topBorderPos))
                        {
                            //_ = typeof(int);
                        }
#else
                    XUnitPt topBorderPos = _bottomBorderMapOld[cell.Row.Index];
#endif
                        XUnitPt bottomBorderPos = topBorderPos + formattedCell.InnerHeight;
                        bottomBorderPos += CalcBottomBorderWidth(cell);
                        if (bottomBorderPos > maxBottomBorderPosition)
                            maxBottomBorderPosition = bottomBorderPos;
                    }
                }
            }
            _bottomBorderMapOld.Add(minMergedCell.Row!.Index + minMergedCell.MergeDown + 1, maxBottomBorderPosition);
        }
#endif

        /// <summary>
        /// Calculates the top border width for the first row that is rendered or formatted.
        /// </summary>
        /// <param name="row">The row index.</param>
        XUnitPt CalcMaxTopBorderWidth(int row)
        {
            XUnitPt maxWidth = 0;
            if (_table.Rows.Count > row)
            {
                int cellIdx = _mergedCells.BinarySearch(_table[row, 0], new CellComparer());
                while (cellIdx < _mergedCells.Count)
                {
                    var rowCell = _mergedCells[cellIdx];
                    Debug.Assert(rowCell.Row != null, "rowCell.Row != null");
                    if (rowCell.Row.Index > row)
                        break;

                    if (rowCell.Values.Borders != null)
                    {
                        BordersRenderer bordersRenderer = new BordersRenderer(rowCell.Borders, _gfx);
                        XUnitPt width = bordersRenderer.GetWidth(BorderType.Top);
                        if (width > maxWidth)
                            maxWidth = width;
                    }
                    ++cellIdx;
                }
            }
            return maxWidth;
        }

        /// <summary>
        /// Creates the next bottom border position.
        /// </summary>
        void CreateNextBottomBorderPosition(ref int skipIndex, ref int lastBorderRow)
        {
            XUnitPt lastPos = _bottomBorderMap[lastBorderRow];

            var minMergedCell = GetMinMergedCell(lastBorderRow);
            var minMergedFormattedCell = _formattedCells[minMergedCell];
            XUnitPt maxBottomBorderPosition = lastPos + minMergedFormattedCell.InnerHeight;
            maxBottomBorderPosition += CalcBottomBorderWidth(minMergedCell);

            Debug.Assert(minMergedCell.Row != null, "minMergedCell.Row != null");
            int minRowIndex = minMergedCell.Row.Index; // Cache property result.
            int minMergeDown = minMergedCell.MergeDown; // Cache property result.
            int minRowIndexPlusMergeDown = minRowIndex + minMergeDown;

            bool first = true;
            int rowToSkip = -1;
            // Do not change skipIndex if we have MergeDown and still need it.
            int count = _bottomBorderMap.Count - 1;
            if (_connectedRowsMap[count] == count)
                rowToSkip = count;
            else
                first = false;

            int cells = _mergedCells.Count;
            for (int idx = skipIndex; idx < cells; idx++)
            {
                var cell = _mergedCells[idx];

                Debug.Assert(cell.Row != null, "cell.Row != null");
                int rowIndex = cell.Row.Index; // Cache property result.
                int cellMergeDown = cell.MergeDown; // Cache property result.

                if (first && rowIndex > rowToSkip)
                {
                    skipIndex = idx;
                    first = false;
                }

                if (rowIndex > minRowIndexPlusMergeDown)
                    break;

                if (rowIndex + cell.MergeDown == minRowIndexPlusMergeDown)
                {
                    FormattedCell formattedCell = _formattedCells[cell];
                    {
#if true
                        if (!_bottomBorderMap.TryGetValue(rowIndex, out var topBorderPos))
                        {
                            //_ = typeof(int);
                        }
#else
                    XUnitPt topBorderPos = _bottomBorderMap[rowIndex];
#endif
                        XUnitPt bottomBorderPos = topBorderPos + formattedCell.InnerHeight;
                        bottomBorderPos += CalcBottomBorderWidth(cell);
                        if (bottomBorderPos > maxBottomBorderPosition)
                            maxBottomBorderPosition = bottomBorderPos;
                    }
                }
            }

            if (_bottomBorderMap.TryGetValue(minRowIndexPlusMergeDown + 1, out var temp))
            {
                // We can only come here if all cells of a row are "hidden" by MergeDown in rows above.
                Debug.Assert(temp == maxBottomBorderPosition);
                lastBorderRow = 0;
                foreach (int key in _bottomBorderMap.Keys)
                {
                    if (key > lastBorderRow)
                        lastBorderRow = key;
                }
            }
            else
            {
#if DEBUG
                var f = _bottomBorderMapOld.TryGetValue(minRowIndexPlusMergeDown + 1, out var tempUnit);
                if (f)
                {
                    if (maxBottomBorderPosition != tempUnit)
                        _ = typeof(int);
                    Debug.Assert(maxBottomBorderPosition == tempUnit);
                }
                else
                {
                    _ = typeof(int);
                    Debug.Assert(false, "Unexpected mismatch.");
                }
#endif
                _bottomBorderMap.Add(minRowIndexPlusMergeDown + 1, maxBottomBorderPosition);
                lastBorderRow = minRowIndexPlusMergeDown + 1;
            }
        }

        /// <summary>
        /// Calculates bottom border width of a cell.
        /// </summary>
        /// <param name="cell">The cell the bottom border of the row that is probed.</param>
        /// <returns>The calculated border width.</returns>
        XUnitPt CalcBottomBorderWidth(Cell cell)
        {
            var borders = _mergedCells.GetEffectiveBordersPdf(cell, _lastHeaderRow);
            if (borders != null!)
            {
                BordersRenderer bordersRenderer = new BordersRenderer(borders, _gfx);
                return bordersRenderer.GetWidth(BorderType.Bottom);
            }
            return 0;
        }

        /// <summary>
        /// Gets the first cell that ends in the given row or as close as possible.
        /// </summary>
        /// <param name="row">The row to probe.</param>
        /// <returns>The first cell with minimal vertical merge.</returns>
        Cell GetMinMergedCell(int row)
        {
#if DEBUG
            // Comparing the result of the new implementation with the old version.
            // It does not matter which cell is actually returned. Just the bottom margin of the cell matters.
            var originalResult = GetMinMergedCellOriginal(row);
#endif

            //var resultRowIndex = _minMergedCellRowMap[row];
            var resultRowIndex = row;
            var resultRow = _table.Rows[resultRowIndex];
            int clsCount = _table.Columns.Count;
            Cell? closestCell = null;
            for (int idx = 0; idx < clsCount; idx++)
            {
                var cell = resultRow[idx];
                if (!_mergedCells.Contains(cell))
                    continue;
                if (closestCell == null || cell.MergeDown < closestCell.MergeDown)
                    closestCell = cell;
                if (resultRowIndex + cell.MergeDown == row)
                {
#if DEBUG
                    // Does it matter which cell we return??? 2023-03-08 Yes, we need a result that is in _mergedCells.
                    Debug.Assert(originalResult == cell);

                    // It does not matter which cell is actually returned. Just the bottom margin of the cell matters.
                    Debug.Assert(originalResult.Row!.Index + originalResult.MergeDown ==
                                 cell.Row!.Index + cell.MergeDown);
#endif
                    return cell;
                }
            }
            if (closestCell != null)
            {
#if DEBUG
                Debug.Assert(originalResult == closestCell);
#endif
                return closestCell;
            }
            throw new InvalidOperationException("GetMinMergedCell: Unexpected problem #1");
        }

#if DEBUG
        Cell GetMinMergedCellOriginal(int row)
        {
            int minMerge = _table.Rows.Count;
            Cell minCell = null!;
            foreach (Cell cell in _mergedCells)
            {
                if (cell.Row!.Index == row)
                {
                    if (cell.MergeDown == 0)
                    {
                        minCell = cell;
                        break;
                    }
                    else if (cell.MergeDown < minMerge)
                    {
                        minMerge = cell.MergeDown;
                        minCell = cell;
                    }
                }
                else if (cell.Row.Index > row)
                    break;
            }
            return minCell ?? NRT.ThrowOnNull<Cell>();
        }
#endif

        /// <summary>
        /// Calculates the last row that is connected with the given row. Uses cached values and can only be used after initialization.
        /// </summary>
        /// <param name="row">The row that is probed for downward connection.</param>
        /// <returns>The last row that is connected with the given row.</returns>
        int CalcLastConnectedRow(int row)
        {
            return _connectedRowsMap[row];
        }

        /// <summary>
        /// Calculates the last row that is connected with the given row. This version does not rely on cached values and can be called anytime.
        /// </summary>
        /// <param name="row">The row that is probed for downward connection.</param>
        /// <returns>The last row that is connected with the given row.</returns>
        int CalcLastConnectedRowDirect(int row)
        {
            int lastConnectedRow = row;
            var cellRow = _table.Rows[row];
            var cells = cellRow.Cells;
            var count = cells.Count;
            bool finished;
            do
            {
                finished = false;
                for (var index = 0; index < count; index++)
                {
                    var cell = cells[index];
                    Debug.Assert(cell != null, nameof(cell) + " != null");
                    Debug.Assert(cell.Row != null, "cell.Row != null");
                    int downConnection = Math.Max(cellRow.KeepWith, cell.MergeDown);
                    if (lastConnectedRow < row + downConnection)
                    {
                        lastConnectedRow = row + downConnection;
                    }
                }

                // Do we have to inspect further rows?
                if (row < lastConnectedRow)
                {
                    // Yes, check next row also.
                    ++row;
                    cellRow = _table.Rows[row];
                    cells = cellRow.Cells;
                    count = cells.Count;
                }
                else
                {
                    // No, finished.
                    finished = true;
                }
            } while (!finished);

            return lastConnectedRow;
        }

        /// <summary>
        /// Calculates the last column that is connected with the specified column.
        /// </summary>
        /// <param name="column">The column that is probed for downward connection.</param>
        /// <returns>The last column that is connected with the given column.</returns>
        int CalcLastConnectedColumn(int column)
        {
            int lastConnectedColumn = column;
            int count = _mergedCells.Count;
            for (var index = 0; index < count; index++)
            {
                var cell = _mergedCells[index];
                Debug.Assert(cell.Column != null, "cell.Column != null");
                if (cell.Column.Index <= lastConnectedColumn)
                {
                    int rightConnection = Math.Max(cell.Column.KeepWith, cell.MergeRight);
                    if (lastConnectedColumn < cell.Column.Index + rightConnection)
                        lastConnectedColumn = cell.Column.Index + rightConnection;
                }
            }

            return lastConnectedColumn;
        }

        readonly Table _table;
        MergedCellList _mergedCells = null!;  // Set in InitRendering.
        readonly Dictionary<int, (double Widest, double Narrowest)> _horizontalBoundaryWidths = new();
        Dictionary<Cell, FormattedCell> _formattedCells = null!;  // Set in InitRendering. //SortedList formattedCells;
        Dictionary<int, XUnitPt> _bottomBorderMap = null!;  // Set in InitRendering. //SortedList bottomBorderMap;
        //Dictionary<int, int> _connectedRowsMap = null!;  // Set in InitRendering. //SortedList connectedRowsMap;
        int[] _connectedRowsMap = null!;  // Set in InitRendering. // Array connectedRowsMap;
        int[] _minMergedCellRowMap = null!;  // Set in InitRendering. // Array connectedRowsMap;
        Dictionary<int, int> _connectedColumnsMap = null!;  // Set in InitRendering. //SortedList connectedColumnsMap;

        int _lastHeaderRow;
        int _lastHeaderColumn;
        int _startRow;
        int _currRow;
        int _endRow = -1;

        bool _doHorizontalBreak;
        XUnitPt _startX;
        XUnitPt _startY;
    }
}
