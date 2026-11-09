// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using MigraDoc.DocumentObjectModel;

namespace MigraDoc.Rendering
{
    /// <summary>
    /// Represents rendering information for a paragraph.
    /// </summary>
    public sealed class ParagraphRenderInfo : RenderInfo
    {
        internal ParagraphRenderInfo()
        { }

        /// <summary>
        /// Gets the format information in a specific derived type. For a table, for example, this will be a TableFormatInfo with information about the first and last row showing on a page.
        /// </summary>
        public override FormatInfo FormatInfo
        {
            get => _formatInfo;
            internal set => _formatInfo = (ParagraphFormatInfo)value;
        }
        ParagraphFormatInfo _formatInfo = new();

        /// <summary>
        /// Gets the number of formatted lines in this paragraph fragment.
        /// </summary>
        public int LineCount => _formatInfo.LineCount;

        /// <summary>
        /// Gets the bottom of a prepared line box, in points in the same coordinate
        /// system as this fragment's content area. Indices are zero-based within
        /// this fragment. Includes top-border space and gaps preceding the line;
        /// does not include the paragraph's trailing border or outside margins.
        /// </summary>
        public double GetLineBoxBottom(int lineIndex)
        {
            if (lineIndex < 0 || lineIndex >= LineCount)
                throw new ArgumentOutOfRangeException(nameof(lineIndex));
            var bottom = LayoutInfo.ContentArea.Y.Point + _formatInfo.TopBorderOffset.Point;
            for (var line = 0; line <= lineIndex; line++)
            {
                var info = _formatInfo.GetLineInfo(line);
                bottom += info.GapBefore.Point + info.Vertical.Height.Point;
            }
            return bottom;
        }

        /// <summary>
        /// Gets the document object to which the layout information applies. Use the Tag property of DocumentObject to identify an object.
        /// </summary>
        public override DocumentObject DocumentObject
        {
            get => _paragraph;
            internal set => _paragraph = (Paragraph)value;
        }
        Paragraph _paragraph = null!;

        internal override void RemoveEnding(bool allowRestart = true)
        {
            ParagraphFormatInfo pfInfo = (ParagraphFormatInfo)FormatInfo;
            pfInfo.RemoveEnding(allowRestart);
            Area contentArea = LayoutInfo.ContentArea;
            if (pfInfo.IsEmpty)
            {
                contentArea.Height = 0;
                LayoutInfo.StartingHeight = 0;
                LayoutInfo.TrailingHeight = 0;
            }
            else
                contentArea.Height -= LayoutInfo.TrailingHeight;
        }
    }
}
