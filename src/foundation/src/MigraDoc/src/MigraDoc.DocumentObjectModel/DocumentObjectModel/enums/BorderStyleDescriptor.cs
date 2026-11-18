// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

namespace MigraDoc.DocumentObjectModel
{
    /// <summary>Describes the nominal PDF drawing family and geometry of a border style.</summary>
    /// <remarks>
    /// All widths are multipliers of the nominal stroke width. These measurements
    /// describe style geometry; border visibility, inheritance and suppression are
    /// handled separately. Obtain descriptors with <see cref="For"/>. A default-initialized
    /// descriptor has a zero-width band.
    /// </remarks>
    public readonly record struct BorderStyleDescriptor
    {
        internal BorderStyleDescriptor(BorderDrawingKind kind, BorderRailProfile rails,
            double patternBandMultiplier = 1)
        {
            Kind = kind;
            Rails = rails;
            _patternBandMultiplier = patternBandMultiplier;
        }

        /// <summary>Gets the PDF drawing family used for this style.</summary>
        public BorderDrawingKind Kind { get; }

        /// <summary>Gets the compound straight-rail profile, or an empty profile for other drawing families.</summary>
        public BorderRailProfile Rails { get; }

        /// <summary>Gets the full reserved band width as a multiple of the nominal stroke width.</summary>
        public double BandMultiplier => Rails.Count > 0 ? Rails.BandMultiplier : _patternBandMultiplier;

        readonly double _patternBandMultiplier;
        internal bool OwnsHorizontalJunction => Kind != BorderDrawingKind.Stroke;

        /// <summary>Gets the nominal PDF geometry of a border style.</summary>
        /// <remarks>
        /// <see cref="BorderStyle.None"/> and unrecognized values use ordinary-stroke
        /// metadata. This lookup does not determine whether a border is visible.
        /// </remarks>
        public static BorderStyleDescriptor For(BorderStyle style) => style switch
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
            BorderStyle.Wave => new(BorderDrawingKind.Wave, default),
            BorderStyle.DoubleWave => new(BorderDrawingKind.DoubleWave, default, 2.5),
            BorderStyle.DashDotStroked => new(BorderDrawingKind.DiagonalHatch, default),
            BorderStyle.Emboss3D or BorderStyle.Engrave3D => new(BorderDrawingKind.ThreeDimensional, default, 1.75),
            _ => new(BorderDrawingKind.Stroke, default)
        };

        static BorderStyleDescriptor Rails2(double first, double second, double gap) =>
            new(BorderDrawingKind.Rails, new BorderRailProfile(2, first, second, gap));
        static BorderStyleDescriptor Rails3(double first, double middle, double gap) =>
            new(BorderDrawingKind.Rails, new BorderRailProfile(3, first, middle, gap));
    }

    /// <summary>Identifies the PDF drawing family of a border style.</summary>
    public enum BorderDrawingKind
    {
        /// <summary>A single solid, dotted or dashed stroke.</summary>
        Stroke,
        /// <summary>Two or three parallel straight rails separated by gaps.</summary>
        Rails,
        /// <summary>A single wave.</summary>
        Wave,
        /// <summary>Two parallel waves.</summary>
        DoubleWave,
        /// <summary>Grouped diagonal hatch strokes.</summary>
        DiagonalHatch,
        /// <summary>Shaded embossed or engraved bands.</summary>
        ThreeDimensional
    }

    /// <summary>Describes the nominal widths and gaps of a compound straight-rail border.</summary>
    /// <remarks>
    /// Widths are multipliers of the nominal border stroke width. Rail zero is
    /// painted first from left to right for vertical bands and top to bottom for
    /// horizontal bands. Non-rail drawing families have an empty profile.
    /// </remarks>
    public readonly record struct BorderRailProfile
    {
        internal BorderRailProfile(int count, double first, double middle, double gap)
        {
            Count = count;
            First = first;
            Middle = middle;
            Gap = gap;
        }

        /// <summary>Gets the number of rails, or zero for an empty profile.</summary>
        public int Count { get; }

        /// <summary>Gets the gap between adjacent rails as a multiple of the nominal stroke width.</summary>
        public double Gap { get; }

        /// <summary>Gets the sum of all rail widths and gaps, or zero for an empty profile.</summary>
        public double BandMultiplier => First + Middle + Gap +
            (Count == 3 ? First + Gap : 0);

        /// <summary>Gets one rail's width as a multiple of the nominal stroke width.</summary>
        /// <param name="index">The zero-based rail index, less than <see cref="Count"/>.</param>
        /// <exception cref="ArgumentOutOfRangeException">The index is outside the profile.</exception>
        public double WidthAt(int index)
        {
            if (index < 0 || index >= Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            return index == 1 ? Middle : First;
        }

        internal double First { get; }
        internal double Middle { get; }
        internal double[] Widths => Count == 3 ? [First, Middle, First] : [First, Middle];
    }
}
