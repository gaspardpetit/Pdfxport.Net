// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
#if !PSXGRA
using PdfSharp.Drawing;
#endif

namespace MigraDoc.Rendering
{
    /// <summary>
    /// Renders a single Border.
    /// </summary>
    class BordersRenderer
    {
        internal BordersRenderer(Borders borders, XGraphics gfx)
        {
            Debug.Assert(borders.Document != null);
            _gfx = gfx;
            _borders = borders;
        }

        Border? GetBorder(BorderType type)
            => _borders.GetBorder(type);

        XColor GetColor(BorderType type)
        {
            var clr = Colors.Black;

            var border = GetBorder(type);
            if (border != null && !border.Color.IsEmpty)
                clr = border.Color;
            else if (!_borders.Color.IsEmpty)
                clr = _borders.Color;

#if noCMYK
            return XColor.FromArgb((int)clr.Argb);
#else
            //      bool cmyk = false; // BUG_OLD CMYK
            //      if (_borders.Document != null)
            //        cmyk = _borders.Document.UseCmykColor;
            //#if DEBUG_
            //      else
            //        GetT ype();
            //#endif
            Debug.Assert(_borders.Document != null, "_borders.Document != null");
            return ColorHelper.ToXColor(clr, _borders.Document.UseCmykColor);
#endif
        }

        internal BorderStyle GetStyle(BorderType type)
        {
            var style = BorderStyle.Single;

            var border = GetBorder(type);
            //if (border != null && !border._style.IsNull)
            if (border?.Values.Style != null)
                style = border.Style;
            //else if (!_borders._style.IsNull)
            else if (_borders.Values.Style is not null)
                style = _borders.Style;
            return style;
        }

        internal XUnitPt GetWidth(BorderType type)
        {
            var width = GetStrokeWidth(type);
            return GetStyle(type) switch
            {
                BorderStyle.Double => width * 3,
                BorderStyle.Triple => width * 5,
                BorderStyle.ThinThickSmallGap or BorderStyle.ThickThinSmallGap => width * 1.75,
                BorderStyle.ThinThickThinSmallGap => width * 2.5,
                BorderStyle.ThinThickMediumGap or BorderStyle.ThickThinMediumGap => width * 2,
                BorderStyle.ThinThickThinMediumGap => width * 3,
                BorderStyle.ThinThickLargeGap or BorderStyle.ThickThinLargeGap => width * 2.125,
                BorderStyle.ThinThickThinLargeGap => width * 3.5,
                BorderStyle.DoubleWave => width * 2.5,
                BorderStyle.Emboss3D or BorderStyle.Engrave3D => width * 1.75,
                _ => width
            };
        }

        static (double[] Widths, double Gap)? CompoundRails(BorderStyle style) => style switch
        {
            BorderStyle.Triple => (new[] { 1.0, 1.0, 1.0 }, 1.0),
            BorderStyle.ThinThickSmallGap => (new[] { 1.0, 0.375 }, 0.375),
            BorderStyle.ThickThinSmallGap => (new[] { 0.375, 1.0 }, 0.375),
            BorderStyle.ThinThickThinSmallGap => (new[] { 0.375, 1.0, 0.375 }, 0.375),
            BorderStyle.ThinThickMediumGap => (new[] { 1.0, 0.5 }, 0.5),
            BorderStyle.ThickThinMediumGap => (new[] { 0.5, 1.0 }, 0.5),
            BorderStyle.ThinThickThinMediumGap => (new[] { 0.5, 1.0, 0.5 }, 0.5),
            BorderStyle.ThinThickLargeGap => (new[] { 0.75, 0.375 }, 1.0),
            BorderStyle.ThickThinLargeGap => (new[] { 0.375, 0.75 }, 1.0),
            BorderStyle.ThinThickThinLargeGap => (new[] { 0.375, 0.75, 0.375 }, 1.0),
            _ => null
        };

        XUnitPt GetStrokeWidth(BorderType type)
        {
            if (_borders == null!)
                return 0;

            var border = GetBorder(type);

            if (border != null)
            {
                var values = border.Values;

                //if (!border._visible.IsNull && !border.Visible)
                if (values.Visible is not null && !values.Visible.Value)
                    return 0;

                //if (!border._width.IsNull)
                //if (border.Values.Width is not null)
                if (!values.Width.IsValueNullOrEmpty())
                    return (float_)values.Width!.Value.Point;

                //if (!border._color.IsNull || !border._style.IsNull || border.Visible)
                //if (border.Values.Color is not null || border.Values.Style is not null || border.Visible)
                if (!values.Color.IsValueNullOrEmpty() || values.Style is not null || border.Visible)
                {
                    //if (!_borders._width.IsNull)
                    //if (_borders.Values.Width is not null)
                    if (!_borders.Values.Width.IsValueNullOrEmpty())
                        return (float_)(_borders.Values.Width!.Value.Point);

                    return 0.5f;
                }
            }
            else if (!(type == BorderType.DiagonalDown || type == BorderType.DiagonalUp))
            {
                var values = _borders.Values;
                //if (!_borders._visible.IsNull && !_borders.Visible)
                if (values.Visible is not null && !values.Visible.Value)
                    return 0;

                //if (!_borders._width.IsNull)
                //if (_borders.Values.Width is not null)
                if (!values.Width.IsValueNullOrEmpty())
                    return (float_)(values.Width!.Value.Point);

                //if (!_borders._color.IsNull || !_borders._style.IsNull || _borders.Visible)
                //if (_borders.Values.Color is not null || _borders.Values.Style is not null || _borders.Visible)
                if (!values.Color.IsValueNullOrEmpty() || values.Style is not null || _borders.Visible)
                    return 0.5f;
            }
            return 0;
        }

        /// <summary>
        /// Renders the border top down.
        /// </summary>
        /// <param name="type">The type of the border.</param>
        /// <param name="left">The left position of the border.</param>
        /// <param name="top">The top position of the border.</param>
        /// <param name="height">The height on which to render the border.</param>
        internal void RenderVertically(BorderType type, XUnitPt left, XUnitPt top, XUnitPt height,
            XUnitPt? outerStrokeTop = null, XUnitPt? innerStrokeBottom = null)
        {
            XUnitPt borderWidth = GetWidth(type);
            if (borderWidth == 0)
                return;

            var pen = GetPen(type);
            if (pen != null)
            {
                if (GetStyle(type) is BorderStyle.Wave or BorderStyle.DoubleWave)
                {
                    RenderWave(left, top, height, true, GetStyle(type) == BorderStyle.DoubleWave, pen);
                    return;
                }
                if (GetStyle(type) == BorderStyle.DashDotStroked)
                {
                    RenderHatch(left, top, height, true, pen);
                    return;
                }
                if (GetStyle(type) is BorderStyle.Emboss3D or BorderStyle.Engrave3D)
                {
                    RenderThreeDimensional(left, top, height, true,
                        GetStyle(type) == BorderStyle.Emboss3D, pen);
                    return;
                }
                if (GetStyle(type) == BorderStyle.Double)
                {
                    var strokeWidth = borderWidth / 3;
                    var leftStrokeTop = type == BorderType.Left ? outerStrokeTop ?? top : top;
                    var rightStrokeTop = type == BorderType.Right ? outerStrokeTop ?? top : top;
                    var leftStrokeBottom = type == BorderType.Right ? innerStrokeBottom ?? top + height : top + height;
                    var rightStrokeBottom = type == BorderType.Left ? innerStrokeBottom ?? top + height : top + height;
#if PSGFX
                    _gfx.DrawLine(pen, new(left + strokeWidth / 2, leftStrokeBottom),
                        new(left + strokeWidth / 2, leftStrokeTop));
                    _gfx.DrawLine(pen, new(left + borderWidth - strokeWidth / 2, rightStrokeBottom),
                        new(left + borderWidth - strokeWidth / 2, rightStrokeTop));
#else
                    _gfx.DrawLine(pen, left + strokeWidth / 2, leftStrokeBottom, left + strokeWidth / 2, leftStrokeTop);
                    _gfx.DrawLine(pen, left + borderWidth - strokeWidth / 2, rightStrokeBottom,
                        left + borderWidth - strokeWidth / 2, rightStrokeTop);
#endif
                    return;
                }
                if (CompoundRails(GetStyle(type)) is { } rails)
                {
                    var position = left;
                    for (var rail = 0; rail < rails.Widths.Length; rail++)
                    {
                        var railWidth = pen.Width * rails.Widths[rail];
                        position += XUnitPt.FromPoint(railWidth / 2);
                        var railPen = new XPen(pen.Color, railWidth);
#if PSGFX
                        _gfx.DrawLine(railPen, new(position, top + height), new(position, top));
#else
                        _gfx.DrawLine(railPen, position, top + height, position, top);
#endif
                        position += XUnitPt.FromPoint(railWidth / 2 + pen.Width * rails.Gap);
                    }
                    return;
                }
                left += borderWidth / 2;
#if PSGFX
                _gfx.DrawLine(pen, new(left, top + height), new(left, top));
#else
                _gfx.DrawLine(pen, left, top + height, left, top);
#endif
                if (GetStyle(type) is BorderStyle.Dot or BorderStyle.DashLargeGap or BorderStyle.DashSmallGap or
                    BorderStyle.DashDot or BorderStyle.DashDotDot && height.Point > 0)
                {
                    var endpointLength = XUnitPt.FromPoint(Math.Min(pen.Width, height.Point / 2));
                    var endpointPen = new XPen(pen.Color, pen.Width);
#if PSGFX
                    _gfx.DrawLine(endpointPen, new(left, top), new(left, top + endpointLength));
                    _gfx.DrawLine(endpointPen, new(left, top + height - endpointLength), new(left, top + height));
#else
                    _gfx.DrawLine(endpointPen, left, top, left, top + endpointLength);
                    _gfx.DrawLine(endpointPen, left, top + height - endpointLength, left, top + height);
#endif
                }
            }
        }

        /// <summary>
        /// Renders the border top down.
        /// </summary>
        /// <param name="type">The type of the border.</param>
        /// <param name="left">The left position of the border.</param>
        /// <param name="top">The top position of the border.</param>
        /// <param name="width">The width on which to render the border.</param>
        internal void RenderHorizontally(BorderType type, XUnitPt left, XUnitPt top, XUnitPt width,
            XUnitPt? innerStrokeLeft = null, XUnitPt? innerStrokeRight = null,
            bool insetBothStrokes = false)
        {
            XUnitPt borderWidth = GetWidth(type);
            if (borderWidth == 0)
                return;

            var pen = GetPen(type);
            if (pen != null)
            {
                if (GetStyle(type) is BorderStyle.Wave or BorderStyle.DoubleWave)
                {
                    RenderWave(left, top, width, false, GetStyle(type) == BorderStyle.DoubleWave, pen);
                    return;
                }
                if (GetStyle(type) == BorderStyle.DashDotStroked)
                {
                    RenderHatch(left, top, width, false, pen);
                    return;
                }
                if (GetStyle(type) is BorderStyle.Emboss3D or BorderStyle.Engrave3D)
                {
                    RenderThreeDimensional(left, top, width, false,
                        GetStyle(type) == BorderStyle.Emboss3D, pen);
                    return;
                }
                if (GetStyle(type) == BorderStyle.Double)
                {
                    var strokeWidth = borderWidth / 3;
                    var outerLeft = left;
                    var outerRight = left + width;
                    var upperLeft = type == BorderType.Bottom || insetBothStrokes
                        ? innerStrokeLeft ?? outerLeft : outerLeft;
                    var upperRight = type == BorderType.Bottom || insetBothStrokes
                        ? innerStrokeRight ?? outerRight : outerRight;
                    var lowerLeft = type == BorderType.Top || insetBothStrokes
                        ? innerStrokeLeft ?? outerLeft : outerLeft;
                    var lowerRight = type == BorderType.Top || insetBothStrokes
                        ? innerStrokeRight ?? outerRight : outerRight;
#if PSGFX
                    _gfx.DrawLine(pen, new(upperRight, top + strokeWidth / 2),
                        new(upperLeft, top + strokeWidth / 2));
                    _gfx.DrawLine(pen, new(lowerRight, top + borderWidth - strokeWidth / 2),
                        new(lowerLeft, top + borderWidth - strokeWidth / 2));
#else
                    _gfx.DrawLine(pen, upperRight, top + strokeWidth / 2, upperLeft, top + strokeWidth / 2);
                    _gfx.DrawLine(pen, lowerRight, top + borderWidth - strokeWidth / 2,
                        lowerLeft, top + borderWidth - strokeWidth / 2);
#endif
                    return;
                }
                if (CompoundRails(GetStyle(type)) is { } rails)
                {
                    var position = top;
                    for (var rail = 0; rail < rails.Widths.Length; rail++)
                    {
                        var railWidth = pen.Width * rails.Widths[rail];
                        position += XUnitPt.FromPoint(railWidth / 2);
                        var railPen = new XPen(pen.Color, railWidth);
#if PSGFX
                        _gfx.DrawLine(railPen, new(left + width, position), new(left, position));
#else
                        _gfx.DrawLine(railPen, left + width, position, left, position);
#endif
                        position += XUnitPt.FromPoint(railWidth / 2 + pen.Width * rails.Gap);
                    }
                    return;
                }
                top += borderWidth / 2;
#if PSGFX
                _gfx.DrawLine(pen, new(left + width, top), new(left, top));
#else
                _gfx.DrawLine(pen, left + width, top, left, top);
#endif
                if (GetStyle(type) is BorderStyle.Dot or BorderStyle.DashLargeGap or BorderStyle.DashSmallGap or
                    BorderStyle.DashDot or BorderStyle.DashDotDot && width.Point > 0)
                {
                    var endpointLength = XUnitPt.FromPoint(Math.Min(pen.Width, width.Point / 2));
                    var endpointPen = new XPen(pen.Color, pen.Width);
#if PSGFX
                    _gfx.DrawLine(endpointPen, new(left, top), new(left + endpointLength, top));
                    _gfx.DrawLine(endpointPen, new(left + width - endpointLength, top), new(left + width, top));
#else
                    _gfx.DrawLine(endpointPen, left, top, left + endpointLength, top);
                    _gfx.DrawLine(endpointPen, left + width - endpointLength, top, left + width, top);
#endif
                }
            }
        }

        void RenderWave(XUnitPt left, XUnitPt top, XUnitPt length, bool vertical, bool doubleWave, XPen source)
        {
            var baseWidth = source.Width;
            var railPen = new XPen(source.Color, Math.Max(0.25, baseWidth * 0.18));
            var count = doubleWave ? 2 : 1;
            for (var rail = 0; rail < count; rail++)
            {
                var cross = doubleWave ? baseWidth * (0.7 + rail * 1.1) : baseWidth * 0.5;
                var amplitude = baseWidth * 0.28;
                var pitch = Math.Max(1.5, baseWidth * 1.45);
                var chevron = pitch * 0.68;
                for (var start = 0.0; start < length.Point; start += pitch)
                {
                    var end = Math.Min(length.Point, start + chevron);
                    var midpoint = (start + end) / 2;
                    if (vertical)
                    {
                        var x = left.Point + cross;
#if PSGFX
                        _gfx.DrawLine(railPen, new(x + amplitude, top.Point + start),
                            new(x - amplitude, top.Point + midpoint));
                        _gfx.DrawLine(railPen, new(x - amplitude, top.Point + midpoint),
                            new(x + amplitude, top.Point + end));
#else
                        _gfx.DrawLine(railPen, x + amplitude, top.Point + start,
                            x - amplitude, top.Point + midpoint);
                        _gfx.DrawLine(railPen, x - amplitude, top.Point + midpoint,
                            x + amplitude, top.Point + end);
#endif
                    }
                    else
                    {
                        var y = top.Point + cross;
#if PSGFX
                        _gfx.DrawLine(railPen, new(left.Point + start, y + amplitude),
                            new(left.Point + midpoint, y - amplitude));
                        _gfx.DrawLine(railPen, new(left.Point + midpoint, y - amplitude),
                            new(left.Point + end, y + amplitude));
#else
                        _gfx.DrawLine(railPen, left.Point + start, y + amplitude,
                            left.Point + midpoint, y - amplitude);
                        _gfx.DrawLine(railPen, left.Point + midpoint, y - amplitude,
                            left.Point + end, y + amplitude);
#endif
                    }
                }
            }
        }

        void RenderHatch(XUnitPt left, XUnitPt top, XUnitPt length, bool vertical, XPen source)
        {
            var tint = XColor.FromArgb(255,
                (int)Math.Round(((double)source.Color.R + 255) / 2),
                (int)Math.Round(((double)source.Color.G + 255) / 2),
                (int)Math.Round(((double)source.Color.B + 255) / 2));
            var railPen = new XPen(tint, Math.Max(0.35, source.Width * 0.35));
            var cross = source.Width;
            var pitch = Math.Max(0.8, source.Width * 0.8);
            for (var start = 0.0; start < length.Point; start += pitch)
            {
                if (vertical)
                {
                    var x = left.Point;
                    var y = top.Point + start;
#if PSGFX
                    _gfx.DrawLine(railPen, new(x, y + cross * 0.65), new(x + cross, y));
#else
                    _gfx.DrawLine(railPen, x, y + cross * 0.65, x + cross, y);
#endif
                }
                else
                {
                    var x = left.Point + start;
                    var y = top.Point;
#if PSGFX
                    _gfx.DrawLine(railPen, new(x, y + cross), new(x + cross * 0.65, y));
#else
                    _gfx.DrawLine(railPen, x, y + cross, x + cross * 0.65, y);
#endif
                }
            }
        }

        void RenderThreeDimensional(XUnitPt left, XUnitPt top, XUnitPt length,
            bool vertical, bool emboss, XPen source)
        {
            static XColor Highlight(XColor color, double amount) => XColor.FromArgb(
                (int)Math.Round(color.A * 255),
                (int)Math.Round(color.R + (255 - color.R) * amount),
                (int)Math.Round(color.G + (255 - color.G) * amount),
                (int)Math.Round(color.B + (255 - color.B) * amount));
            var shade = Highlight(source.Color, .28);
            var middle = Highlight(source.Color, .1);
            var colors = emboss
                ? new[] { shade, middle, source.Color }
                : new[] { source.Color, middle, shade };
            var widths = new[] { source.Width * .375, source.Width, source.Width * .375 };
            var position = vertical ? left : top;
            for (var rail = 0; rail < widths.Length; rail++)
            {
                position += XUnitPt.FromPoint(widths[rail] / 2);
                var railPen = new XPen(colors[rail], widths[rail]);
                if (vertical)
                {
#if PSGFX
                    _gfx.DrawLine(railPen, new(position, top + length), new(position, top));
#else
                    _gfx.DrawLine(railPen, position, top + length, position, top);
#endif
                }
                else
                {
#if PSGFX
                    _gfx.DrawLine(railPen, new(left + length, position), new(left, position));
#else
                    _gfx.DrawLine(railPen, left + length, position, left, position);
#endif
                }
                position += XUnitPt.FromPoint(widths[rail] / 2);
            }
        }

        internal void RenderDiagonally(BorderType type, XUnitPt left, XUnitPt top, XUnitPt width, XUnitPt height)
        {
            XUnitPt borderWidth = GetWidth(type);
            if (borderWidth == 0)
                return;

#if PSGFX
            //XGraphicsState state = _gfx.Save();
            //_gfx.IntersectClip(new XRect(left, top, width, height));

            //if (type == BorderType.DiagonalDown)
            //{
            //    var pen = GetPen(type);
            //    if (pen != null)
            //        _gfx.DrawLine(pen, left, top, left + width, top + height);
            //}
            //else if (type == BorderType.DiagonalUp)
            //{
            //    var pen = GetPen(type);
            //    if (pen != null)
            //        _gfx.DrawLine(pen, left, top + height, left + width, top);
            //}

            //_gfx.Restore(state);
#else
            XGraphicsState state = _gfx.Save();
            _gfx.IntersectClip(new XRect(left, top, width, height));

            var pen = GetPen(type);
            if (pen != null && GetStyle(type) == BorderStyle.Double)
            {
                var length = Math.Sqrt(width.Point * width.Point + height.Point * height.Point);
                if (length > 0)
                {
                    var shiftX = XUnitPt.FromPoint(height.Point / length * GetStrokeWidth(type).Point);
                    var shiftY = XUnitPt.FromPoint(width.Point / length * GetStrokeWidth(type).Point);
                    if (type == BorderType.DiagonalDown)
                    {
                        _gfx.DrawLine(pen, left - shiftX / 2, top + shiftY / 2,
                            left + width - shiftX / 2, top + height + shiftY / 2);
                        _gfx.DrawLine(pen, left + shiftX / 2, top - shiftY / 2,
                            left + width + shiftX / 2, top + height - shiftY / 2);
                    }
                    else if (type == BorderType.DiagonalUp)
                    {
                        _gfx.DrawLine(pen, left - shiftX / 2, top + height - shiftY / 2,
                            left + width - shiftX / 2, top - shiftY / 2);
                        _gfx.DrawLine(pen, left + shiftX / 2, top + height + shiftY / 2,
                            left + width + shiftX / 2, top + shiftY / 2);
                    }
                }
                _gfx.Restore(state);
                return;
            }

            if (type == BorderType.DiagonalDown)
            {
                if (pen != null)
                    _gfx.DrawLine(pen, left, top, left + width, top + height);
            }
            else if (type == BorderType.DiagonalUp)
            {
                if (pen != null)
                    _gfx.DrawLine(pen, left, top + height, left + width, top);
            }

            _gfx.Restore(state);
#endif
        }

        internal void RenderRounded(RoundedCorner roundedCorner, XUnitPt x, XUnitPt y, XUnitPt width, XUnitPt height)
        {
            if (roundedCorner == RoundedCorner.None)
                return;

            // As source we use the vertical borders.
            // If not set originally, they have been set to the horizontal border values in TableRenderer.EqualizeRoundedCornerBorders().
            BorderType borderType = BorderType.Top;
            if (roundedCorner == RoundedCorner.TopLeft || roundedCorner == RoundedCorner.BottomLeft)
                borderType = BorderType.Left;
            if (roundedCorner == RoundedCorner.TopRight || roundedCorner == RoundedCorner.BottomRight)
                borderType = BorderType.Right;

            var borderWidth = GetWidth(borderType);
            var borderPen = GetPen(borderType);

            if (borderWidth == 0 || borderPen == null)
                return;

            x -= borderWidth / 2;
            y -= borderWidth / 2;
            XUnitPt ellipseWidth = width * 2 + borderWidth;
            XUnitPt ellipseHeight = height * 2 + borderWidth;

            void DrawRoundedArc(XRect rectangle)
            {
                switch (roundedCorner)
                {
#if PSGFX
                    case RoundedCorner.TopLeft:
                    case RoundedCorner.TopRight:
                    case RoundedCorner.BottomRight:
                    case RoundedCorner.BottomLeft:
                        break;
#else
                    case RoundedCorner.TopLeft:
                        _gfx.DrawArc(borderPen, rectangle, 180, 90);
                        break;
                    case RoundedCorner.TopRight:
                        _gfx.DrawArc(borderPen, new XRect(rectangle.X - width, rectangle.Y,
                            rectangle.Width, rectangle.Height), 270, 90);
                        break;
                    case RoundedCorner.BottomRight:
                        _gfx.DrawArc(borderPen, new XRect(rectangle.X - width, rectangle.Y - height,
                            rectangle.Width, rectangle.Height), 0, 90);
                        break;
                    case RoundedCorner.BottomLeft:
                        _gfx.DrawArc(borderPen, new XRect(rectangle.X, rectangle.Y - height,
                            rectangle.Width, rectangle.Height), 90, 90);
                        break;
#endif
                }
            }

            var ellipse = new XRect(x, y, ellipseWidth, ellipseHeight);
            if (GetStyle(borderType) == BorderStyle.Double)
            {
                var strokeWidth = GetStrokeWidth(borderType);
                DrawRoundedArc(new XRect(x - strokeWidth, y - strokeWidth,
                    ellipseWidth + 2 * strokeWidth, ellipseHeight + 2 * strokeWidth));
                DrawRoundedArc(new XRect(x + strokeWidth, y + strokeWidth,
                    ellipseWidth - 2 * strokeWidth, ellipseHeight - 2 * strokeWidth));
            }
            else
                DrawRoundedArc(ellipse);
        }

        internal XPen? GetPen(BorderType type)
        {
            var borderWidth = GetStrokeWidth(type);
            if (borderWidth == 0)
                return null;

            var pen = new XPen(GetColor(type), borderWidth);
            var style = GetStyle(type);
            switch (style)
            {
#if PSGFX
                case BorderStyle.DashDot:
                    //pen.DashStyle = XDashStyle.DashDot;
                    break;

                case BorderStyle.DashDotDot:
                    //pen.DashStyle = XDashStyle.DashDotDot;
                    break;

                case BorderStyle.DashLargeGap:
                    //pen.DashPattern = [3, 3];
                    break;

                case BorderStyle.DashSmallGap:
                    //pen.DashPattern = [3, 1];
                    break;

                case BorderStyle.Dot:
                    //pen.DashStyle = XDashStyle.Dot;
                    break;

                case BorderStyle.Single:
                default:
                    //pen.DashStyle = XDashStyle.Solid;
                    break;
#else
                case BorderStyle.DashDot:
                    pen.DashStyle = XDashStyle.DashDot;
                    break;

                case BorderStyle.DashDotDot:
                    pen.DashStyle = XDashStyle.DashDotDot;
                    break;

                case BorderStyle.DashLargeGap:
                    pen.DashPattern = [3, 3];
                    break;

                case BorderStyle.DashSmallGap:
                    pen.DashPattern = [3, 1];
                    break;

                case BorderStyle.Dot:
                    pen.DashStyle = XDashStyle.Dot;
                    break;

                case BorderStyle.Single:
                default:
                    pen.DashStyle = XDashStyle.Solid;
                    break;
#endif
            }
            return pen;
        }

        internal bool IsRendered(BorderType borderType)
        {
            if (_borders == null!)
                return false;

            switch (borderType)
            {
                case BorderType.Left:
                    //if (_borders._left == null || _borders._left.IsNull())
                    if (_borders.Values.Left == null /*|| _borders._left.IsNull()*/)
                        return false;
                    return GetWidth(borderType) > 0;

                case BorderType.Right:
                    //if (_borders._right == null || _borders._right.IsNull())
                    if (_borders.Values.Right == null /*|| _borders._right.IsNull()*/)
                        return false;
                    return GetWidth(borderType) > 0;

                case BorderType.Top:
                    //if (_borders._top == null || _borders._top.IsNull())
                    if (_borders.Values.Top == null /*|| _borders._top.IsNull()*/)
                        return false;
                    return GetWidth(borderType) > 0;

                case BorderType.Bottom:
                    //if (_borders._bottom == null || _borders._bottom.IsNull())
                    if (_borders.Values.Bottom == null /*|| _borders._bottom.IsNull()*/)
                        return false;

                    return GetWidth(borderType) > 0;

                case BorderType.DiagonalDown:
                    //if (_borders._diagonalDown == null || _borders._diagonalDown.IsNull())
                    if (_borders.Values.DiagonalDown == null /*|| _borders._diagonalDown.IsNull()*/)
                        return false;
                    return GetWidth(borderType) > 0;

                case BorderType.DiagonalUp:
                    //if (_borders._diagonalUp == null || _borders._diagonalUp.IsNull())
                    if (_borders.Values.DiagonalUp == null /*|| _borders._diagonalUp.IsNull()*/)
                        return false;

                    return GetWidth(borderType) > 0;
            }
            return false;
        }

        readonly XGraphics _gfx;
        readonly Borders _borders;
    }
}
