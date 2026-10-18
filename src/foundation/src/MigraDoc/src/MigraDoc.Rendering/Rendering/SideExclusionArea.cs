using MigraDoc.DocumentObjectModel.Shapes;
using PdfSharp.Drawing;

namespace MigraDoc.Rendering
{
    internal sealed class SideExclusionArea : Area
    {
        readonly Area _area;
        readonly List<Exclusion> _exclusions;

        readonly record struct Exclusion(XUnitPt Left, XUnitPt Top, XUnitPt Right,
            XUnitPt Bottom, WrapStyle Style, IReadOnlyList<XPoint>? Contour,
            XUnitPt DistanceLeft, XUnitPt DistanceRight,
            Func<double, double?>? ContourBandHeight,
            Func<double, double, double?>? ContourLineFontBandHeight,
            Func<double, double, double?>? LineFitHeight);

        internal double NaturalFontHeight { get; set; }

        internal SideExclusionArea(Area area, XUnitPt left, XUnitPt top, XUnitPt right,
            XUnitPt bottom, WrapStyle style, IReadOnlyList<XPoint>? contour = null,
            XUnitPt distanceLeft = default, XUnitPt distanceRight = default,
            Func<double, double?>? contourBandHeight = null,
            Func<double, double, double?>? contourLineFontBandHeight = null,
            Func<double, double, double?>? lineFitHeight = null)
        {
            _area = area;
            _exclusions = [new Exclusion(left, top, right, bottom, style, contour,
                distanceLeft, distanceRight, contourBandHeight, contourLineFontBandHeight,
                lineFitHeight)];
        }

        SideExclusionArea(Area area, List<Exclusion> exclusions)
        {
            _area = area;
            _exclusions = exclusions;
        }

        internal SideExclusionArea Add(XUnitPt left, XUnitPt top, XUnitPt right,
            XUnitPt bottom, WrapStyle style, IReadOnlyList<XPoint>? contour = null,
            XUnitPt distanceLeft = default, XUnitPt distanceRight = default,
            Func<double, double?>? contourBandHeight = null,
            Func<double, double, double?>? contourLineFontBandHeight = null,
            Func<double, double, double?>? lineFitHeight = null)
            => new(_area, [.. _exclusions, new Exclusion(left, top, right, bottom,
                style, contour, distanceLeft, distanceRight, contourBandHeight,
                contourLineFontBandHeight, lineFitHeight)]);

        public override XUnitPt X { get => _area.X; internal set => _area.X = value; }
        public override XUnitPt Y { get => _area.Y; internal set => _area.Y = value; }
        public override XUnitPt Width { get => _area.Width; internal set => _area.Width = value; }
        public override XUnitPt Height { get => _area.Height; internal set => _area.Height = value; }

        internal override Rectangle? GetFittingRect(XUnitPt yPosition, XUnitPt height)
            => GetIntervals(yPosition, height).FirstOrDefault();

        internal IReadOnlyList<Rectangle> GetIntervals(XUnitPt yPosition, XUnitPt height)
        {
            var fitHeight = height;
            foreach (var exclusion in _exclusions)
                if (exclusion.LineFitHeight?.Invoke(height.Point, NaturalFontHeight) is { } candidate &&
                    !double.IsNaN(candidate) && !double.IsInfinity(candidate) &&
                    candidate > 0 && candidate < fitHeight.Point)
                    fitHeight = XUnitPt.FromPoint(candidate);
            var rectangle = _area.GetFittingRect(yPosition, fitHeight);
            if (rectangle == null)
                return [];
            rectangle.Height = height;

            List<Rectangle> intervals = [rectangle];
            foreach (var exclusion in _exclusions)
            {
                if (yPosition >= exclusion.Bottom || yPosition + height <= exclusion.Top)
                    continue;
                var (exclusionLeft, exclusionRight) = GetBandBounds(exclusion, yPosition, height,
                    NaturalFontHeight);
                if (exclusionLeft >= exclusionRight)
                    continue;
                var available = new List<Rectangle>();
                foreach (var interval in intervals)
                {
                    var leftWidth = Math.Max(0, Math.Min(interval.Width, exclusionLeft - interval.X));
                    var rightStart = Math.Max(interval.X, exclusionRight);
                    var rightWidth = Math.Max(0, interval.X + interval.Width - rightStart);
                    var left = new Rectangle(interval.X, interval.Y, leftWidth, interval.Height);
                    var right = new Rectangle(rightStart, interval.Y, rightWidth, interval.Height);
                    if (leftWidth > 0 && (exclusion.Style is WrapStyle.Left or WrapStyle.BothSides ||
                        exclusion.Style == WrapStyle.Largest && leftWidth >= rightWidth))
                        available.Add(left);
                    if (rightWidth > 0 && (exclusion.Style is WrapStyle.Right or WrapStyle.BothSides ||
                        exclusion.Style == WrapStyle.Largest && rightWidth > leftWidth))
                        available.Add(right);
                }
                intervals = available;
            }
            return intervals.Count > 0
                ? intervals.OrderBy(interval => interval.X).ToArray()
                : [new Rectangle(rectangle.X, rectangle.Y, 0, rectangle.Height)];
        }

        static (XUnitPt Left, XUnitPt Right) GetBandBounds(Exclusion exclusion,
            XUnitPt yPosition, XUnitPt height, double naturalFontHeight)
        {
            if (exclusion.Contour is not { Count: >= 3 } contour)
                return (exclusion.Left, exclusion.Right);

            var upper = Math.Max(yPosition.Point, exclusion.Top.Point);
            var candidateHeight = exclusion.ContourLineFontBandHeight?.Invoke(height.Point,
                naturalFontHeight) ?? exclusion.ContourBandHeight?.Invoke(height.Point);
            var effectiveHeight = candidateHeight is > 0 &&
                !double.IsNaN(candidateHeight.Value) && !double.IsInfinity(candidateHeight.Value)
                ? Math.Min(height.Point, candidateHeight.Value) : height.Point;
            var lower = Math.Min(yPosition.Point + effectiveHeight, exclusion.Bottom.Point);
            var positions = new List<double>();
            for (var index = 0; index < contour.Count; index++)
            {
                var start = contour[index];
                var end = contour[(index + 1) % contour.Count];
                var segmentUpper = Math.Max(upper, Math.Min(start.Y, end.Y));
                var segmentLower = Math.Min(lower, Math.Max(start.Y, end.Y));
                if (segmentUpper > segmentLower)
                    continue;
                if (start.Y == end.Y)
                {
                    positions.Add(start.X);
                    positions.Add(end.X);
                    continue;
                }
                positions.Add(start.X + (end.X - start.X) *
                    (segmentUpper - start.Y) / (end.Y - start.Y));
                positions.Add(start.X + (end.X - start.X) *
                    (segmentLower - start.Y) / (end.Y - start.Y));
            }
            return positions.Count == 0 ? (0, 0) :
                (XUnitPt.FromPoint(positions.Min() - exclusion.DistanceLeft.Point),
                    XUnitPt.FromPoint(positions.Max() + exclusion.DistanceRight.Point));
        }

        internal XUnitPt? GetNextFreeY(XUnitPt yPosition, XUnitPt height)
        {
            foreach (var bottom in _exclusions
                .Where(exclusion => yPosition < exclusion.Bottom &&
                    yPosition + height > exclusion.Top)
                .Select(exclusion => exclusion.Bottom).Distinct().OrderBy(value => value))
                if (GetIntervals(bottom, height).Any(interval => interval.Width > 0))
                    return bottom;
            return null;
        }

        internal override Area Unite(Area area)
            => new SideExclusionArea(_area.Unite(area), _exclusions);

        internal override Area Lower(XUnitPt verticalOffset)
            => new SideExclusionArea(_area.Lower(verticalOffset), _exclusions);
    }
}
