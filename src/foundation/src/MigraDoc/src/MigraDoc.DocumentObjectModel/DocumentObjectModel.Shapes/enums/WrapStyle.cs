// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

namespace MigraDoc.DocumentObjectModel.Shapes
{
    /// <summary>
    /// Specifies how the shape object should be placed between the other elements.
    /// </summary>
    public enum WrapStyle
    {
        /// <summary>
        /// The object will be placed between its predecessor and its successor.
        /// </summary>
        TopBottom,

        /// <summary>
        /// The object will be ignored when the other elements are placed.
        /// </summary>
        None,

        /// <summary>
        /// The object will be ignored when the other elements are placed.
        /// </summary>
        Through,

        /// <summary>
        /// Place surrounding text on the left side of the shape.
        /// </summary>
        Left,

        /// <summary>
        /// Place surrounding text on the right side of the shape.
        /// </summary>
        Right,

        /// <summary>
        /// Place surrounding text on the side with more available width.
        /// </summary>
        Largest,

        /// <summary>
        /// Place surrounding text on both available sides of the shape.
        /// </summary>
        BothSides,
    }
}
