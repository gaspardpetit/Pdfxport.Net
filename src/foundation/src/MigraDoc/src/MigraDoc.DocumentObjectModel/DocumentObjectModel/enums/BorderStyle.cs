// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

namespace MigraDoc.DocumentObjectModel
{
    /// <summary>
    /// Specifies the style of the line of the Border object.
    /// </summary>
    public enum BorderStyle
    {
        /// <summary>
        /// No border.
        /// </summary>
        None,

        /// <summary>
        /// A single solid line.
        /// </summary>
        Single,

        /// <summary>
        /// A dotted line.
        /// </summary>
        Dot,

        /// <summary>
        /// A dashed line (small gaps).
        /// </summary>
        DashSmallGap,

        /// <summary>
        /// A dashed line (large gaps).
        /// </summary>
        DashLargeGap,

        /// <summary>
        /// Alternating dashes and dots.
        /// </summary>
        DashDot,

        /// <summary>
        /// A dash followed by two dots.
        /// </summary>
        DashDotDot,

        Double,

        Triple,

        ThinThickSmallGap,
        ThickThinSmallGap,
        ThinThickThinSmallGap,
        ThinThickMediumGap,
        ThickThinMediumGap,
        ThinThickThinMediumGap,
        ThinThickLargeGap,
        ThickThinLargeGap,
        ThinThickThinLargeGap,

        Wave,
        DoubleWave,
        DashDotStroked,

        Emboss3D,
        Engrave3D,

    }
}
