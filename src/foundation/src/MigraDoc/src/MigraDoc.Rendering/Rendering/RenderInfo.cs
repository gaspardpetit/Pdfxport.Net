// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Shapes;
using PdfSharp.Drawing;

namespace MigraDoc.Rendering
{
    /// <summary>
    /// Abstract base class for all classes that store rendering information.
    /// </summary>
    public abstract class RenderInfo
    {
        /// <summary>
        /// Gets the format information in a specific derived type. For a table, for example, this will be a TableFormatInfo with information about the first and last row showing on a page.
        /// </summary>
        public abstract FormatInfo FormatInfo { get; internal set; }

        /// <summary>
        /// Gets the layout information.
        /// </summary>
        public LayoutInfo LayoutInfo { get; } = new LayoutInfo();

        /// <summary>
        /// Gets the document object to which the layout information applies. Use the Tag property of DocumentObject to identify an object.
        /// </summary>
        public abstract DocumentObject DocumentObject { get; internal set; }

        internal virtual void RemoveEnding(bool allowRestart = true)
        {
            System.Diagnostics.Debug.Assert(false, "Unexpected call of RemoveEnding");
        }

        internal static XUnitPt GetTotalHeight(RenderInfo[]? renderInfos)
        {
            if (renderInfos == null || renderInfos.Length == 0)
                return 0;

            int lastIdx = renderInfos.Length - 1;
            var firstRenderInfo = renderInfos[0];
            var lastRenderInfo = renderInfos[lastIdx];
            var firstLayoutInfo = firstRenderInfo.LayoutInfo;
            var lastLayoutInfo = lastRenderInfo.LayoutInfo;
            XUnitPt top = firstLayoutInfo.ContentArea.Y - firstLayoutInfo.MarginTop;
            XUnitPt bottom = lastLayoutInfo.ContentArea.Y + lastLayoutInfo.ContentArea.Height;
            bottom += lastLayoutInfo.MarginBottom;

            // A backward-positioned nonflowing anchor is not the end of body
            // content. Preserve its paragraph's bottom and every shape extent
            // in a consecutive anchor chain, even when the final shape is shorter.
            XUnitPt? paragraphBottom = null;
            foreach (var renderInfo in renderInfos)
            {
                var layout = renderInfo.LayoutInfo;
                if (renderInfo.DocumentObject is Paragraph)
                    paragraphBottom = layout.ContentArea.Y + layout.ContentArea.Height + layout.MarginBottom;
                else if (renderInfo.DocumentObject is Shape { RelativeVertical: RelativeVertical.Paragraph,
                             WrapFormat.PositionFromPreviousParagraphTop: true } && layout.Floating == Floating.None)
                {
                    if (paragraphBottom is { } bodyBottom)
                    {
                        bottom = Math.Max(bottom, bodyBottom);
                        bottom = Math.Max(bottom, layout.ContentArea.Y + layout.ContentArea.Height + layout.MarginBottom);
                    }
                }
                else
                    paragraphBottom = null;
            }
            return bottom - top;
        }
    }
}
