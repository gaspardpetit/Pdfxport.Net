using MigraDoc.DocumentObjectModel.Shapes;
using PdfSharp.Drawing;

namespace MigraDoc.Rendering
{
    internal sealed class SideExclusionArea : Area
    {
        readonly Area _area;
        readonly List<Exclusion> _exclusions;

        readonly record struct Exclusion(XUnitPt Left, XUnitPt Top, XUnitPt Right,
            XUnitPt Bottom, WrapStyle Style);

        internal SideExclusionArea(Area area, XUnitPt left, XUnitPt top, XUnitPt right,
            XUnitPt bottom, WrapStyle style)
        {
            _area = area;
            _exclusions = [new Exclusion(left, top, right, bottom, style)];
        }

        SideExclusionArea(Area area, List<Exclusion> exclusions)
        {
            _area = area;
            _exclusions = exclusions;
        }

        internal SideExclusionArea Add(XUnitPt left, XUnitPt top, XUnitPt right,
            XUnitPt bottom, WrapStyle style)
            => new(_area, [.. _exclusions, new Exclusion(left, top, right, bottom, style)]);

        public override XUnitPt X { get => _area.X; internal set => _area.X = value; }
        public override XUnitPt Y { get => _area.Y; internal set => _area.Y = value; }
        public override XUnitPt Width { get => _area.Width; internal set => _area.Width = value; }
        public override XUnitPt Height { get => _area.Height; internal set => _area.Height = value; }

        internal override Rectangle? GetFittingRect(XUnitPt yPosition, XUnitPt height)
            => GetIntervals(yPosition, height).FirstOrDefault();

        internal IReadOnlyList<Rectangle> GetIntervals(XUnitPt yPosition, XUnitPt height)
        {
            var rectangle = _area.GetFittingRect(yPosition, height);
            if (rectangle == null)
                return [];

            List<Rectangle> intervals = [rectangle];
            foreach (var exclusion in _exclusions)
            {
                if (yPosition >= exclusion.Bottom || yPosition + height <= exclusion.Top)
                    continue;
                var available = new List<Rectangle>();
                foreach (var interval in intervals)
                {
                    var leftWidth = Math.Max(0, Math.Min(interval.Width, exclusion.Left - interval.X));
                    var rightStart = Math.Max(interval.X, exclusion.Right);
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
