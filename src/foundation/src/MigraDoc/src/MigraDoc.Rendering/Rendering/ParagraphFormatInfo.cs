// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using MigraDoc.DocumentObjectModel.Shapes;
using PdfSharp.Drawing;
using MigraDoc.DocumentObjectModel;

namespace MigraDoc.Rendering
{
    /// <summary>
    /// Vertical measurements of a paragraph line.
    /// </summary>
    struct VerticalLineInfo
    {
        internal VerticalLineInfo(XUnitPt height, XUnitPt descent, XUnitPt inherentlineSpace)
        {
            Height = height;
            Descent = descent;
            InherentLineSpace = inherentlineSpace;
        }

        public XUnitPt Height;

        public XUnitPt Descent;

        public XUnitPt InherentLineSpace;
    }

    /// <summary>
    /// Line info object used by the paragraph format info.
    /// </summary>
    struct LineInfo
    {
        public ParagraphIterator? StartIter;
        public ParagraphIterator? EndIter;
        public ParagraphIterator? ContinuationIter;
        public bool HasContinuationOverride;
        public XUnitPt WordsWidth;
        public XUnitPt LineWidth;
        public XUnitPt ClippedTrailingWidth;
        public bool LineEndsWithLineBreak;
        public bool NextLineStartedByBlankWrap;
        public int BlankCount;
        public VerticalLineInfo Vertical;
        public List<TabOffset> TabOffsets;
        public bool ReMeasureLine;
        public DocumentObject? LastTab;
        public Rectangle? FittingRect;
        public List<LineSegmentInfo>? SegmentTransitions;
        public XUnitPt GapBefore;
        public HashSet<Character>? CollapsedSpaces;
    }

    readonly record struct LineSegmentInfo(Rectangle Rect, ParagraphIterator? StartIter,
        XUnitPt WordsBefore, int BlanksBefore, bool PreviousSegmentNeedsContraction, XUnitPt ClippedWidthBefore);

    /// <summary>
    /// Formatting information for a paragraph.
    /// </summary>
    sealed class ParagraphFormatInfo : FormatInfo
    {
        internal ParagraphFormatInfo()
        { }

        internal LineInfo GetLineInfo(int lineIdx)
            => _lineInfos[lineIdx];

        internal LineInfo GetLastLineInfo()
            => _lineInfos[LineCount - 1];

        internal LineInfo GetFirstLineInfo()
            => _lineInfos[0];

        internal void AddLineInfo(LineInfo lineInfo)
            => _lineInfos.Add(lineInfo);

        // Space between the fragment's content origin and the first line,
        // resolved while formatting (including the border-distance policy).
        internal XUnitPt TopBorderOffset { get; set; }

        internal int LineCount
            => _lineInfos.Count;

        // Fake docs is not allowed.
        /////// <summary>
        /////// 
        /////// </summary>
        /////// <param name="mergeInfo"></param>
        internal void Append(FormatInfo mergeInfo)
        {
            ParagraphFormatInfo formatInfo = (ParagraphFormatInfo)mergeInfo;
            _lineInfos.AddRange(formatInfo._lineInfos);
        }

        /// <summary>
        /// Indicates whether the paragraph is ending.
        /// </summary>
        /// <returns>True if the paragraph is ending.</returns>
        internal override bool IsEnding => _isEnding;

        internal bool _isEnding;  // TODO_OLD auto prop

        /// <summary>
        /// Indicates whether the paragraph is starting.
        /// </summary>
        /// <returns>True if the paragraph is starting.</returns>
        internal override bool IsStarting => _isStarting;

        internal bool _isStarting;  // TODO_OLD auto prop

        internal override bool IsComplete => _isStarting && _isEnding;

        internal override bool IsEmpty => _lineInfos.Count == 0;

        internal override bool StartingIsComplete
        {
            get
            {
                if (_widowControl)
                    return (IsComplete || (_isStarting && _lineInfos.Count >= 2));
                return _isStarting;
            }
        }
        internal bool _widowControl;  // TODO_OLD auto prop

        internal override bool EndingIsComplete
        {
            get
            {
                if (_widowControl)
                    return (IsComplete || (_isEnding && _lineInfos.Count >= 2));
                return _isEnding;
            }
        }

        internal void RemoveEnding(bool allowRestart)
        {
            if (!IsEmpty)
            {
                if (_widowControl && _isEnding && LineCount >= 2)
                    _lineInfos.RemoveAt(LineCount - 2);
                if (LineCount > 0)
                    _lineInfos.RemoveAt(LineCount - 1);

                // Moving a line to satisfy the ending widow constraint may
                // leave just one line on the first page. In that case restart
                // the paragraph on the next area so its opening also has at
                // least two lines, provided the paragraph fits there. Otherwise
                // retain the remaining line to guarantee pagination progress.
                if (allowRestart && _widowControl && _isStarting && LineCount < 2)
                    _lineInfos.Clear();

                _isEnding = false;
            }
        }

        internal string ListSymbol = null!;
        internal XFont ListFont = null!;
        internal Dictionary<Image, RenderInfo> ImageRenderInfos = null!;
        readonly List<LineInfo> _lineInfos = new();
    }
}
