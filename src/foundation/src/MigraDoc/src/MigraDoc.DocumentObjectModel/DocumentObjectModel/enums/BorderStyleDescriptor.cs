// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

namespace MigraDoc.DocumentObjectModel
{
    /// <summary>Shared nominal band and rail geometry for existing border styles.</summary>
    internal readonly record struct BorderStyleDescriptor(BorderRailProfile Rails,
        double PatternBandMultiplier = 1)
    {
        internal double BandMultiplier => Rails.Count > 0 ? Rails.BandMultiplier : PatternBandMultiplier;

        internal static BorderStyleDescriptor For(BorderStyle style) => style switch
        {
            BorderStyle.Double => Rails2(1, 1, 1),
            BorderStyle.Triple => Rails3(1, 1, 1),
            BorderStyle.ThinThickSmallGap => Rails2(1, .375, .375),
            BorderStyle.ThickThinSmallGap => Rails2(.375, 1, .375),
            BorderStyle.ThinThickThinSmallGap => Rails3(.375, 1, .375),
            BorderStyle.ThinThickMediumGap => Rails2(1, .5, .5),
            BorderStyle.ThickThinMediumGap => Rails2(.5, 1, .5),
            BorderStyle.ThinThickThinMediumGap => Rails3(.5, 1, .5),
            BorderStyle.ThinThickLargeGap => Rails2(.75, .375, 1),
            BorderStyle.ThickThinLargeGap => Rails2(.375, .75, 1),
            BorderStyle.ThinThickThinLargeGap => Rails3(.375, .75, 1),
            BorderStyle.DoubleWave => new(default, 2.5),
            BorderStyle.Emboss3D or BorderStyle.Engrave3D => new(default, 1.75),
            _ => new(default)
        };

        static BorderStyleDescriptor Rails2(double first, double second, double gap) =>
            new(new BorderRailProfile(2, first, second, gap));
        static BorderStyleDescriptor Rails3(double first, double middle, double gap) =>
            new(new BorderRailProfile(3, first, middle, gap));
    }

    /// <summary>Two or three rails; three-rail styles have symmetric outer rails.</summary>
    internal readonly record struct BorderRailProfile(int Count, double First, double Middle, double Gap)
    {
        internal double BandMultiplier => First + Middle + Gap +
            (Count == 3 ? First + Gap : 0);
        internal double[] Widths => Count == 3 ? [First, Middle, First] : [First, Middle];
    }
}
