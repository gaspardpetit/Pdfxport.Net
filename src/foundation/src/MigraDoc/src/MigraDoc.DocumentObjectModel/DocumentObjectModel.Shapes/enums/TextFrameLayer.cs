// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

namespace MigraDoc.DocumentObjectModel.Shapes
{
    /// <summary>
    /// Specifies when a text frame is painted relative to other page content.
    /// </summary>
    public enum TextFrameLayer
    {
        /// <summary>
        /// Paint the frame in its normal document order.
        /// </summary>
        Normal,

        /// <summary>
        /// Paint the frame before other page content.
        /// </summary>
        BehindContent
    }
}
