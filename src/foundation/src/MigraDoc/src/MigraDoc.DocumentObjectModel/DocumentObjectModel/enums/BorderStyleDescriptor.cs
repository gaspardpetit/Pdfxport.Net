namespace MigraDoc.DocumentObjectModel
{
    /// <summary>
    /// The geometry and drawing family of a table border style. Multipliers are
    /// relative to the border's nominal stroke width.
    /// </summary>
    public readonly record struct BorderStyleDescriptor(
        BorderDrawingKind Kind,
        BorderRailProfile Rails,
        double PatternBandMultiplier = 1,
        double PatternPitchMultiplier = 0,
        bool UseBandAgainstSingle = false)
    {
        public double BandMultiplier => Rails.Count > 0 ? Rails.BandMultiplier : PatternBandMultiplier;
        public bool OwnsHorizontalJunction => Kind is not BorderDrawingKind.Stroke;
        public bool InsetsSpacedContentByFullBand => Kind is BorderDrawingKind.Wave or
            BorderDrawingKind.DoubleWave or BorderDrawingKind.DiagonalHatch;
        public double ContentXOffset(double strokeWidth) => Kind switch
        {
            BorderDrawingKind.Rails or BorderDrawingKind.Wave or BorderDrawingKind.DoubleWave =>
                BandMultiplier * strokeWidth / 2,
            BorderDrawingKind.DiagonalHatch => (BandMultiplier - 1) * strokeWidth / 2,
            _ => 0
        };
        public double ContentYOffset(double strokeWidth) => Kind switch
        {
            BorderDrawingKind.Wave or BorderDrawingKind.DoubleWave => strokeWidth,
            BorderDrawingKind.DiagonalHatch => strokeWidth / 2,
            _ => 0
        };

        public static BorderStyleDescriptor For(BorderStyle style, double strokeWidth = 1.5) => style switch
        {
            BorderStyle.Double => new(BorderDrawingKind.Rails,
                new BorderRailProfile(2, 1, 1, 0, 1), UseBandAgainstSingle: true),
            BorderStyle.Triple => new(BorderDrawingKind.Rails,
                new BorderRailProfile(3, 1, 1, 1, 1), UseBandAgainstSingle: true),
            BorderStyle.ThinThickSmallGap => Rails2(1, .375, .375),
            BorderStyle.ThickThinSmallGap => Rails2(.375, 1, .375),
            BorderStyle.ThinThickThinSmallGap => Rails3(ThinRail(strokeWidth), 1,
                ThinRail(strokeWidth), ThinRail(strokeWidth)),
            BorderStyle.ThinThickMediumGap => Rails2(1, .5, .5),
            BorderStyle.ThickThinMediumGap => Rails2(.5, 1, .5),
            BorderStyle.ThinThickThinMediumGap => Rails3(.5, 1, .5, .5),
            BorderStyle.ThinThickLargeGap => Rails2(.75, .375, 1),
            BorderStyle.ThickThinLargeGap => Rails2(.375, .75, 1),
            BorderStyle.ThinThickThinLargeGap => Rails3(ThinRail(strokeWidth),
                LargeMiddleRail(strokeWidth), ThinRail(strokeWidth), 1),
            BorderStyle.Wave => new(BorderDrawingKind.Wave, default, 2.5, 5),
            BorderStyle.DoubleWave => new(BorderDrawingKind.DoubleWave, default, 3.5, 3.5,
                UseBandAgainstSingle: true),
            BorderStyle.DashDotStroked => new(BorderDrawingKind.DiagonalHatch, default, 2, 3.84),
            BorderStyle.Emboss3D or BorderStyle.Engrave3D =>
                new(BorderDrawingKind.ThreeDimensional,
                    new BorderRailProfile(3, ThinRail(strokeWidth), 1,
                        ThinRail(strokeWidth), 0),
                    UseBandAgainstSingle: true),
            _ => new(BorderDrawingKind.Stroke, default)
        };

        static BorderStyleDescriptor Rails2(double first, double second, double gap) =>
            new(BorderDrawingKind.Rails, new BorderRailProfile(2, first, second, 0, gap),
                UseBandAgainstSingle: true);

        static BorderStyleDescriptor Rails3(double first, double middle, double last, double gap) =>
            new(BorderDrawingKind.Rails, new BorderRailProfile(3, first, middle, last, gap),
                UseBandAgainstSingle: true);

        // Word keeps narrow rails near 0.75 pt and the large-gap middle rail
        // near 1.5 pt when the nominal border is between 1.5 and 2 pt.
        static double ThinRail(double strokeWidth) => strokeWidth > 0
            ? System.Math.Max(.375, .75 / strokeWidth) : .5;

        static double LargeMiddleRail(double strokeWidth) => strokeWidth > 0
            ? System.Math.Max(.75, 1.5 / strokeWidth) : 1;
    }

    public enum BorderDrawingKind
    {
        Stroke,
        Rails,
        Wave,
        DoubleWave,
        DiagonalHatch,
        ThreeDimensional
    }

    public readonly record struct BorderRailProfile(
        int Count, double First, double Middle, double Last, double Gap)
    {
        public double BandMultiplier => Count switch
        {
            2 => First + Middle + Gap,
            3 => First + Middle + Last + 2 * Gap,
            _ => 0
        };

        public double WidthAt(int index) => index switch
        {
            0 => First,
            1 => Middle,
            2 when Count == 3 => Last,
            _ => throw new System.ArgumentOutOfRangeException(nameof(index))
        };
    }
}
