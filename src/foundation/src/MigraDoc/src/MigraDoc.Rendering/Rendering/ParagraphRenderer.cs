// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Drawing;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Fields;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.Rendering.Extensions;

namespace MigraDoc.Rendering
{
    struct TabOffset
    {
        internal TabOffset(TabLeader leader, XUnitPt offset)
        {
            Leader = leader;
            Offset = offset;
        }
        internal TabLeader Leader;
        internal XUnitPt Offset;
    }

    /// <summary>
    /// Summary description for ParagraphRenderer.
    /// </summary>
    class ParagraphRenderer : Renderer
    {
        static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        bool IsImageOnlyParagraph
        {
            get
            {
                if (_isImageOnlyParagraph is bool cached)
                    return cached;

                var hasImage = false;
                bool IsImageOrWhitespace(DocumentObject element)
                {
                    switch (element)
                    {
                        case Image:
                            hasImage = true;
                            return true;
                        case Character { SymbolName: SymbolName.Tab or SymbolName.Blank or
                            SymbolName.En or SymbolName.Em or SymbolName.EmQuarter or SymbolName.NonBreakableBlank }:
                            return true;
                        case Text text:
                            if (!string.IsNullOrWhiteSpace(text.Content))
                                return false;
                            return true;
                        case FormattedText formatted:
                            return formatted.Elements.Cast<DocumentObject>().All(IsImageOrWhitespace);
                        default:
                            return false;
                    }
                }
                if (!_paragraph.Elements.Cast<DocumentObject>().All(IsImageOrWhitespace))
                    return (_isImageOnlyParagraph = false).Value;
                return (_isImageOnlyParagraph = hasImage).Value;
            }
        }

        bool? _isImageOnlyParagraph;

        /// <summary>
        /// Process phases of the renderer.
        /// </summary>
        enum Phase
        {
            Formatting,
            Rendering
        }

        /// <summary>
        /// Results that can occur when processing a paragraph element
        /// during formatting.
        /// </summary>
        enum FormatResult
        {
            /// <summary>
            /// Ignore the current element during formatting.
            /// </summary>
            Ignore,

            /// <summary>
            /// Continue with the next element within the same line.
            /// </summary>
            Continue,

            /// <summary>
            /// Start a new line from the current object on.
            /// </summary>
            NewLine,

            /// <summary>
            /// Break formatting and continue in a new area (e.g. a new page).
            /// </summary>
            NewArea
        }

        Phase _phase;

        /// <summary>
        /// Initializes a ParagraphRenderer object for formatting.
        /// </summary>
        /// <param name="gfx">The XGraphics object to do measurements on.</param>
        /// <param name="paragraph">The paragraph to format.</param>
        /// <param name="fieldInfos">The field infos.</param>
        internal ParagraphRenderer(XGraphics gfx, Paragraph paragraph, FieldInfos? fieldInfos)
            : base(gfx, paragraph, fieldInfos)
        {
            _paragraph = paragraph;

            ParagraphRenderInfo parRenderInfo = new()
            {
                DocumentObject = _paragraph
            };
            ((ParagraphFormatInfo)parRenderInfo.FormatInfo)._widowControl = _paragraph.Format.WidowControl;

            _renderInfo = parRenderInfo;
        }

        /// <summary>
        /// Initializes a ParagraphRenderer object for rendering.
        /// </summary>
        /// <param name="gfx">The XGraphics object to render on.</param>
        /// <param name="renderInfo">The render info object containing information necessary for rendering.</param>
        /// <param name="fieldInfos">The field infos.</param>
        internal ParagraphRenderer(XGraphics gfx, RenderInfo renderInfo, FieldInfos? fieldInfos)
            : base(gfx, renderInfo, fieldInfos)
        {
            _paragraph = (Paragraph)renderInfo.DocumentObject;
        }

        /// <summary>
        /// Renders the paragraph.
        /// </summary>
        internal override void Render()
        {
            // Widow correction can clear an opening fragment after its render
            // info has already been stored on the preceding page.
            if (_renderInfo.FormatInfo.IsEmpty)
                return;

            InitRendering();
            if ((int)_paragraph.Format.OutlineLevel >= 1 && _gfx.PdfPage != null) // Don’t call GetOutlineTitle() in vain
                DocumentRenderer.AddOutline((int)_paragraph.Format.OutlineLevel, GetOutlineTitle(), _gfx.PdfPage, GetDestinationPosition());

            RenderShading();
            RenderBorders();

            ParagraphFormatInfo parFormatInfo = (ParagraphFormatInfo)_renderInfo.FormatInfo;
            for (int idx = 0; idx < parFormatInfo.LineCount; idx++)
            {
                LineInfo lineInfo = parFormatInfo.GetLineInfo(idx);
                _isLastLine = idx == parFormatInfo.LineCount - 1;
                _collapsedSpacesOnLine = lineInfo.CollapsedSpaces ?? [];

                _lastTabPosition = 0;
                // Field remeasurement formats in the rendering phase. Clear the
                // previous line's paint state before it can affect measured geometry.
                _hasTextBoundaryPredecessor = false;
                _textBoundaryPaintOffset = 0;
                if (lineInfo.ReMeasureLine)
                    ReMeasureLine(ref lineInfo);

                RenderLine(lineInfo);
            }
        }

        static bool IsRenderedField(DocumentObject docObj)
        {
            if (docObj is NumericFieldBase or DocumentInfo or DateField)
                return true;

            return false;
        }

        string GetFieldValue(DocumentObject field)
        {
            if (field is NumericFieldBase numericFieldBase)
            {
                int number = -1;
                if (field is PageRefField refField)
                {
                    var pageRefField = refField;
                    number = _fieldInfos?.GetShownPageNumber(pageRefField.Name) ?? NRT.ThrowOnNull<int, FieldInfos>();
                    if (number <= 0)
                    {
                        if (_phase == Phase.Formatting)
                            return "XX";
                        return MdPdfMsgs.BookmarkNotDefined(pageRefField.Name).Message;
                    }
                }
                else if (field is SectionField)
                {
                    number = _fieldInfos?.Section ?? NRT.ThrowOnNull<int>();
                    if (number <= 0)
                        return "XX";
                }
                else if (field is PageField)
                {
                    number = _fieldInfos?.DisplayPageNr ?? NRT.ThrowOnNull<int>();
                    if (number <= 0)
                        return "XX";
                }
                else if (field is NumPagesField)
                {
                    number = _fieldInfos?.NumPages ?? NRT.ThrowOnNull<int>();
                    if (number <= 0)
                        return "XXX";
                }
                else if (field is SectionPagesField)
                {
                    number = _fieldInfos?.SectionPages ?? NRT.ThrowOnNull<int>();
                    if (number <= 0)
                        return "XX";
                }
                return NumberFormatter.Format(number, numericFieldBase.Format);
            }
            else
            {
                if (field is DateField dateField)
                {
                    var dt = _fieldInfos?.Date ?? NRT.ThrowOnNull<DateTimeOffset>();
                    if (dt == DateTimeOffset.MinValue)
                        dt = DateTimeOffset.Now;

                    return FormatDateTimeForField(dt, dateField);
                }

                if (field is InfoField infoField)
                    return GetDocumentInfo(infoField.Name);

                Debug.Assert(false, "Given parameter must be a rendered Field");
            }
            return "";
        }

        static String FormatDateTimeForField(DateTimeOffset dateTime, DateField dateField)
        {
            var culture = dateField.Document!.EffectiveCulture;
            var dtfInfo = culture.DateTimeFormat;

            var format = dateField.Format;
            if (String.IsNullOrEmpty(format))
                format = dtfInfo.ShortDatePattern + " " + dtfInfo.LongTimePattern;

            return dateTime.ToString(format, dtfInfo);
        }

        string GetOutlineTitle()
        {
            var iter = new ParagraphIterator(_paragraph.Elements);
            iter = iter.GetFirstLeaf();

            var ignoreBlank = true;
            var title = "";
            while (iter != null)
            {
                var current = iter.Current;
                if (!ignoreBlank && (IsBlank(current) || IsTab(current) || IsLineBreak(current)))
                {
                    title += " ";
                    ignoreBlank = true;
                }
                else if (current is Text text && !IsSoftHyphen(text))
                {
                    title += text.Content;
                    ignoreBlank = false;
                }
                else if (IsRenderedField(current))
                {
                    title += GetFieldValue(current);
                    ignoreBlank = false;
                }
                else if (IsSymbol(current))
                {
                    title += GetSymbol((Character)current);
                    ignoreBlank = false;
                }
                iter = iter.GetNextLeaf();
            }
            return title;
        }

        /// <summary>
        /// Gets a layout info with only margin and break information set.
        /// It can be taken before the paragraph is formatted.
        /// </summary>
        /// <remarks>
        /// The following layout information is set properly:<br />
        /// MarginTop, MarginLeft, MarginRight, MarginBottom, KeepTogether, KeepWithNext, PagebreakBefore.
        /// </remarks>
        internal override LayoutInfo InitialLayoutInfo
        {
            get
            {
                LayoutInfo layoutInfo = new()
                {
                    PageBreakBefore = _paragraph.Format.PageBreakBefore,
                    MarginTop = _paragraph.Format.SpaceBefore.Point,
                    MarginBottom = _paragraph.Format.SpaceAfter.Point,
                    // Don’t confuse margins with left or right indent.
                    // Indents are invisible for the layouter.
                    MarginRight = 0,
                    MarginLeft = 0,
                    KeepTogether = _paragraph.Format.KeepTogether,
                    KeepWithNext = _paragraph.Format.KeepWithNext
                };
                return layoutInfo;
            }
        }

        /// <summary>
        /// Adjusts the current x position to the given tab stop if possible.
        /// </summary>
        /// <returns>True if the text doesn’t fit the line anymore and the tab causes a line break.</returns>
        FormatResult FormatTab()
        {
            // For Tabs in Justified context
            if (_paragraph.Format.Alignment == ParagraphAlignment.Justify)
                _reMeasureLine = true;
            var nextTabStop = GetNextTabStop();
            _savedWordWidth = 0;
            if (nextTabStop == null)
                return FormatResult.NewLine;

            bool notFitting = false;
            XUnitPt xPositionBeforeTab = _currentXPosition;
            switch (nextTabStop.Alignment)
            {
                case TabAlignment.Left:
                    _currentXPosition = ProbeAfterLeftAlignedTab(nextTabStop.Position.Point, out notFitting);
                    break;

                case TabAlignment.Right:
                    _currentXPosition = ProbeAfterRightAlignedTab(nextTabStop.Position.Point, out notFitting);
                    break;

                case TabAlignment.Center:
                    _currentXPosition = ProbeAfterCenterAlignedTab(nextTabStop.Position.Point, out notFitting);
                    break;

                case TabAlignment.Decimal:
                    _currentXPosition = ProbeAfterDecimalAlignedTab(nextTabStop.Position.Point, out notFitting);
                    break;
            }
            if (!notFitting)
            {
                // For correct right paragraph alignment with tabs
                if (!IgnoreHorizontalGrowth &&
                    !(_phase == Phase.Rendering && _paragraph.Format.Alignment == ParagraphAlignment.Justify))
                    _currentLineWidth += _currentXPosition - xPositionBeforeTab;

                _currentContractionSpaceWidth = 0;
                _currentMinimumBlankWidth = double.PositiveInfinity;
                _savedBlankWidth = 0;
                _savedBlankContractionWidth = 0;
                _tabOffsets.Add(new TabOffset(nextTabStop.Leader, _currentXPosition - xPositionBeforeTab));
                if (_currentLeaf != null)
                    _lastTab = _currentLeaf.Current;
            }

            return notFitting ? FormatResult.NewLine : FormatResult.Continue;
        }

        static bool IsLineBreak(DocumentObject docObj)
        {
            if (docObj is Character { SymbolName: SymbolName.LineBreak })
                return true;
            return false;
        }

        static bool IsBlankOrSoftHyphen(DocumentObject docObj)
        {
            return docObj is Text { Content: " " or "\u00AD" };
        }

        static bool IsBlank(DocumentObject docObj)
        {
            if (docObj is Text { Content: " " })
                return true;
            return false;
        }

        // TODO_OLD Make combined predicates for better performance (e.g. IsTabOrLineBreak).
        static bool IsTab(DocumentObject docObj)
        {
            if (docObj is Character { SymbolName: SymbolName.Tab })
                return true;
            return false;
        }

        static bool IsSoftHyphen(DocumentObject docObj)
        {
            if (docObj is Text text)
                return text.Content == "\u00AD";

            return false;
        }

        /// <summary>
        /// Probes the paragraph elements after a left aligned tab stop and returns the vertical text position to start at.
        /// </summary>
        /// <param name="tabStopPosition">Position of the tab to probe.</param>
        /// <param name="notFitting">Out parameter determining whether the tab causes a line break.</param>
        /// <returns>The new x-position to restart behind the tab.</returns>
        XUnitPt ProbeAfterLeftAlignedTab(XUnitPt tabStopPosition, out bool notFitting)
        {
            //--- Save ---------------------------------
            SaveBeforeProbing(out var iter, out var blankCount, out var wordsWidth, out var xPosition, out var lineWidth, out var blankWidth,
                out var lineEndsWithLineBreak, out var naturalFontHeight, out var contractionSpaceWidth, out var clippedTrailingWidth);
            //------------------------------------------

            XUnitPt xPositionAfterTab = xPosition;
            _currentXPosition = _formattingArea.X + tabStopPosition.Point;

            notFitting = ProbeAfterTab();
            if (!notFitting)
                xPositionAfterTab = _formattingArea.X + tabStopPosition;

            //--- Restore ---------------------------------
            RestoreAfterProbing(iter, blankCount, wordsWidth, xPosition, lineWidth, blankWidth, lineEndsWithLineBreak, naturalFontHeight, contractionSpaceWidth, clippedTrailingWidth);
            //------------------------------------------
            return xPositionAfterTab;
        }

        /// <summary>
        /// Probes the paragraph elements after a right aligned tab stop and returns the vertical text position to start at.
        /// </summary>
        /// <param name="tabStopPosition">Position of the tab to probe.</param>
        /// <param name="notFitting">Out parameter determining whether the tab causes a line break.</param>
        /// <returns>The new x-position to restart behind the tab.</returns>
        XUnitPt ProbeAfterRightAlignedTab(XUnitPt tabStopPosition, out bool notFitting)
        {
            //--- Save ---------------------------------
            SaveBeforeProbing(out var iter, out var blankCount, out var wordsWidth, out var xPosition, out var lineWidth, out var blankWidth,
                out var lineEndsWithLineBreak, out var naturalFontHeight, out var contractionSpaceWidth, out var clippedTrailingWidth);
            //------------------------------------------

            XUnitPt xPositionAfterTab = xPosition;

            notFitting = ProbeAfterTab();
            if (!notFitting && xPosition + _currentLineWidth <= _formattingArea.X + tabStopPosition)
                xPositionAfterTab = _formattingArea.X + tabStopPosition - _currentLineWidth;

            //--- Restore ------------------------------
            RestoreAfterProbing(iter, blankCount, wordsWidth, xPosition, lineWidth, blankWidth, lineEndsWithLineBreak, naturalFontHeight, contractionSpaceWidth, clippedTrailingWidth);
            //------------------------------------------
            return xPositionAfterTab;
        }

        Hyperlink? GetHyperlink()
        {
            if (_currentLeaf is not null)
            {
                var elements = DocumentRelations.GetParent(_currentLeaf.Current);
                var parent = DocumentRelations.GetParent(elements);
                while (parent is not Paragraph)
                {
                    if (parent is Hyperlink hyperlink)
                        return hyperlink;
                    elements = DocumentRelations.GetParent(parent);
                    parent = DocumentRelations.GetParent(elements);
                }
                return null;
            }
            NRT.ThrowOnNull();
            return null;
        }

        /// <summary>
        /// Probes the paragraph elements after a right aligned tab stop and returns the vertical text position to start at.
        /// </summary>
        /// <param name="tabStopPosition">Position of the tab to probe.</param>
        /// <param name="notFitting">Out parameter determining whether the tab causes a line break.</param>
        /// <returns>The new x-position to restart behind the tab.</returns>
        XUnitPt ProbeAfterCenterAlignedTab(XUnitPt tabStopPosition, out bool notFitting)
        {
            //--- Save ---------------------------------
            SaveBeforeProbing(out var iter, out var blankCount, out var wordsWidth, out var xPosition, out var lineWidth, out var blankWidth,
                out var lineEndsWithLineBreak, out var naturalFontHeight, out var contractionSpaceWidth, out var clippedTrailingWidth);
            //------------------------------------------

            XUnitPt xPositionAfterTab = xPosition;
            notFitting = ProbeAfterTab();

            if (!notFitting)
            {
                if (xPosition + _currentLineWidth / 2.0 <= _formattingArea.X + tabStopPosition)
                {
                    var rect = _formattingArea.GetFittingRect(_currentYPosition, _currentVerticalInfo.Height);
                    if (rect is null)
                        NRT.ThrowOnNull();
                    if (_formattingArea.X + tabStopPosition + _currentLineWidth / 2.0 > rect.X + rect.Width - RightIndent)
                    {
                        // The text is too long on the right hand side of the tab stop => align to right indent.
                        xPositionAfterTab = rect.X +
                          rect.Width -
                          RightIndent -
                          _currentLineWidth;
                    }
                    else
                        xPositionAfterTab = _formattingArea.X + tabStopPosition - _currentLineWidth / 2;
                }
            }

            //--- Restore ------------------------------
            RestoreAfterProbing(iter, blankCount, wordsWidth, xPosition, lineWidth, blankWidth, lineEndsWithLineBreak, naturalFontHeight, contractionSpaceWidth, clippedTrailingWidth);
            //------------------------------------------
            return xPositionAfterTab;
        }

        /// <summary>
        /// Probes the paragraph elements after a right aligned tab stop and returns the vertical text position to start at.
        /// </summary>
        /// <param name="tabStopPosition">Position of the tab to probe.</param>
        /// <param name="notFitting">Out parameter determining whether the tab causes a line break.</param>
        /// <returns>The new x-position to restart behind the tab.</returns>
        XUnitPt ProbeAfterDecimalAlignedTab(XUnitPt tabStopPosition, out bool notFitting)
        {
            notFitting = false;

            //--- Save ---------------------------------
            SaveBeforeProbing(out var iter, out var blankCount, out var wordsWidth, out var xPosition, out var lineWidth, out var blankWidth,
                out var lineEndsWithLineBreak, out var naturalFontHeight, out var contractionSpaceWidth, out var clippedTrailingWidth);
            //------------------------------------------

            try
            {
                // Extra for auto tab after list symbol.
                if (IsTab(_currentLeaf?.Current ?? NRT.ThrowOnNull<DocumentObject>()))
                    _currentLeaf = _currentLeaf.GetNextLeaf();
                if (_currentLeaf == null)
                {
                    return _currentXPosition + tabStopPosition;
                }
                var newVerticalInfo = CalcCurrentVerticalInfo();
                var fittingRect = _formattingArea.GetFittingRect(_currentYPosition, newVerticalInfo.Height);
                if (fittingRect == null)
                {
                    notFitting = true;
                    return _currentXPosition;
                }

                if (IsPlainText(_currentLeaf.Current))
                {
                    var text = (Text)_currentLeaf.Current;
                    string word = text.Content;

                    var culture = text.Document!.EffectiveCulture;

                    var decimalSeparator = culture.NumberFormat.NumberDecimalSeparator;
                    var decimalSeparatorLength = decimalSeparator.Length;
                    var groupSeparator = culture.NumberFormat.NumberGroupSeparator;
                    var groupSeparatorLength = groupSeparator.Length;

                    // Get the index of the decimal position the word should be aligned at. 
                    var decimalPosIndex = -1;
                    var hasNumber = false;
                    for (var i = 0; i < word.Length;)
                    {
                        var c = word[i];

                        // Number is always accepted before decimal position.
                        if (char.IsNumber(c))
                        {
                            hasNumber = true;
                            i++;
                            continue;
                        }

                        var restLength = word.Length - i;

                        // Decimal Separator always determines decimal position.
                        if (decimalSeparatorLength <= restLength && word.Substring(i, decimalSeparatorLength) == decimalSeparator)
                        {
                            decimalPosIndex = i;
                            break;
                        }

                        // Group Separator is always accepted before decimal position.
                        if (groupSeparatorLength <= restLength && word.Substring(i, groupSeparatorLength) == groupSeparator)
                        {
                            i += groupSeparator.Length;
                            continue;
                        }

                        // Other characters determine decimal position, if word contains numbers by now.
                        if (hasNumber)
                        {
                            decimalPosIndex = i;
                            break;
                        }

                        // Otherwise other characters are accepted before decimal position.
                        i++;
                    }

                    var pairAdvances = GetTextPairAdvances(word, CurrentFont);
                    if (decimalPosIndex >= 0)
                        word = word[..decimalPosIndex];

                    XUnitPt wordLength = MeasureString(word, includeTrailingAdvance: false);
                    if (decimalPosIndex >= 0 && pairAdvances != null)
                        wordLength += pairAdvances.Take(decimalPosIndex).Sum();
                    notFitting = _currentXPosition + wordLength >= _formattingArea.X + _formattingArea.Width + Tolerance;
                    if (!notFitting)
                        return _formattingArea.X + tabStopPosition - wordLength;

                    return _currentXPosition;
                }
            }
            finally
            {
                //--- Restore ------------------------------
                RestoreAfterProbing(iter, blankCount, wordsWidth, xPosition, lineWidth, blankWidth, lineEndsWithLineBreak, naturalFontHeight, contractionSpaceWidth, clippedTrailingWidth);
                //------------------------------------------
            }

            return ProbeAfterRightAlignedTab(tabStopPosition, out notFitting);
        }

        void SaveBeforeProbing(out ParagraphIterator? paragraphIter, out int blankCount, out XUnitPt wordsWidth, out XUnitPt xPosition, out XUnitPt lineWidth, out (XUnitPt Width, XUnitPt ContractionWidth) blankWidth,
            out bool lineEndsWithLineBreak, out double naturalFontHeight, out (XUnitPt Width, double MinimumBlankWidth) contractionSpaceWidth, out XUnitPt clippedTrailingWidth)
        {
            paragraphIter = _currentLeaf;
            blankCount = _currentBlankCount;
            xPosition = _currentXPosition;
            lineWidth = _currentLineWidth;
            wordsWidth = _currentWordsWidth;
            blankWidth = (_savedBlankWidth, _savedBlankContractionWidth);
            lineEndsWithLineBreak = _currentLineEndsWithLineBreak;
            naturalFontHeight = _currentLineNaturalFontHeight;
            contractionSpaceWidth = (_currentContractionSpaceWidth, _currentMinimumBlankWidth);
            clippedTrailingWidth = _currentClippedTrailingWidth;
        }

        void RestoreAfterProbing(ParagraphIterator? paragraphIter, int blankCount, XUnitPt wordsWidth, XUnitPt xPosition, XUnitPt lineWidth, (XUnitPt Width, XUnitPt ContractionWidth) blankWidth,
            bool lineEndsWithLineBreak, double naturalFontHeight, (XUnitPt Width, double MinimumBlankWidth) contractionSpaceWidth, XUnitPt clippedTrailingWidth)
        {
            _currentLeaf = paragraphIter;
            _currentBlankCount = blankCount;
            _currentXPosition = xPosition;
            _currentLineWidth = lineWidth;
            _currentWordsWidth = wordsWidth;
            _savedBlankWidth = blankWidth.Width;
            _savedBlankContractionWidth = blankWidth.ContractionWidth;
            _currentLineEndsWithLineBreak = lineEndsWithLineBreak;
            _currentLineNaturalFontHeight = naturalFontHeight;
            _currentContractionSpaceWidth = contractionSpaceWidth.Width;
            _currentMinimumBlankWidth = contractionSpaceWidth.MinimumBlankWidth;
            _currentClippedTrailingWidth = clippedTrailingWidth;
            if (_formattingArea is SideExclusionArea sideExclusion)
                sideExclusion.NaturalFontHeight = naturalFontHeight;
        }

        /// <summary>
        /// Probes the paragraph after a tab.
        /// Caution: This Function resets the word count and line width before doing its work.
        /// </summary>
        /// <returns>True if the tab causes a linebreak.</returns>
        bool ProbeAfterTab()
        {
            _currentLineWidth = 0;
            _currentBlankCount = 0;
            _currentContractionSpaceWidth = 0;
            _currentMinimumBlankWidth = double.PositiveInfinity;
            // Extra for auto tab after list symbol.

            // Check if this conditional statement is still required.
            if (_currentLeaf != null && IsTab(_currentLeaf.Current))
                _currentLeaf = _currentLeaf.GetNextLeaf();

            bool wordAppeared = false;
            while (_currentLeaf != null && !IsLineBreak(_currentLeaf.Current) && !IsTab(_currentLeaf.Current))
            {
                FormatResult result = FormatElement(_currentLeaf.Current);
                if (result == FormatResult.Ignore)
                {
                    _currentLeaf = _currentLeaf.GetNextLeaf();
                    continue;
                }
                if (result != FormatResult.Continue)
                    break;

                wordAppeared = wordAppeared || IsWordLikeElement(_currentLeaf.Current);
                _currentLeaf = _currentLeaf.GetNextLeaf();
            }
            return _currentLeaf != null && !IsLineBreak(_currentLeaf.Current) &&
              !IsTab(_currentLeaf.Current) && !wordAppeared;
        }

        /// <summary>
        /// Gets the next tab stop following the current x position.
        /// </summary>
        /// <returns>The searched tab stop.</returns>
        TabStop? GetNextTabStop()
        {
            var format = _paragraph.Format;
            var tabStops = format.TabStops;
            XUnitPt lastPosition = 0;

            foreach (var tabStop in tabStops.Cast<TabStop>())
            {
                if (tabStop is null)
                    NRT.ThrowOnNull();

                if (tabStop.Position.Point > _formattingArea.Width - RightIndent + Tolerance)
                    break;

                // Compare with "Tolerance" to prevent rounding errors from taking us one tab stop too far.
                if (tabStop.Position.Point + _formattingArea.X > _currentXPosition + Tolerance)
                    return tabStop;

                lastPosition = tabStop.Position.Point;
            }
            // Automatic tab stop: FirstLineIndent < 0 => automatic tab stop at LeftIndent.

            if (format.FirstLineIndent < 0 ||
                (format.Values.ListInfo is not null && !format.Values.ListInfo.IsNull() && format.ListInfo.NumberPosition < format.LeftIndent))
            {
                XUnitPt leftIndent = format.LeftIndent.Point;
                if (_isFirstLine && _currentXPosition < leftIndent + _formattingArea.X)
                    return new TabStop(leftIndent.Point);
            }
            XUnitPt defaultTabStop = "1.25cm";
            //if (!_paragraph.Document._defaultTabStop.IsNull)
            //if (_paragraph.Document.Values.DefaultTabStop is not null)
            Debug.Assert(_paragraph.Document != null, "_paragraph.Document != null");
            if (!_paragraph.Document.Values.DefaultTabStop.IsValueNullOrEmpty())
            {
                var configuredInterval = _paragraph.Document.DefaultTabStop.Point;
                // An interval smaller than the position tolerance cannot advance to a usable stop.
                if (configuredInterval >= Tolerance.Point && !double.IsInfinity(configuredInterval))
                    defaultTabStop = configuredInterval;
            }

            var currTabPos = defaultTabStop;
            while (currTabPos <= _formattingArea.Width - RightIndent)
            {
                if (currTabPos > lastPosition && currTabPos + _formattingArea.X > _currentXPosition + Tolerance)
                    return new TabStop(currTabPos.Point);

                currTabPos += defaultTabStop;
            }
            return null;
        }

        /// <summary>
        /// Gets the horizontal position to start a new line.
        /// </summary>
        /// <returns>The position to start the line.</returns>
        XUnitPt StartXPosition
        {
            get
            {
#if true
                // New code with StartXPosition independent of _currentYPosition.

                XUnitPt xPos = 0;

                if (_phase == Phase.Formatting)
                {
                    xPos = _formattingArea is SideExclusionArea
                        ? _formattingArea.GetFittingRect(_currentYPosition,
                            _currentVerticalInfo.Height)?.X ?? _formattingArea.X
                        : _formattingArea.X;
                    xPos += LeftIndent;
                }
                else //if (phase == Phase.Rendering)
                {
                    Area contentArea = _renderInfo.LayoutInfo.ContentArea;
                    // Next lines for non-fitting lines that produce an empty fitting rect:
                    XUnitPt rectX = _currentLineRect?.X ?? contentArea.X;
                    XUnitPt rectWidth = _currentLineRect?.Width ?? contentArea.Width;

                    switch (_paragraph.Format.Alignment)
                    {
                        case ParagraphAlignment.Left:
                        case ParagraphAlignment.Justify:
                            xPos = rectX;
                            xPos += LeftIndent;
                            break;

                        case ParagraphAlignment.Right:
                            xPos = rectX + rectWidth - RightIndent;
                            xPos -= _currentLineWidth;
                            break;

                        case ParagraphAlignment.Center:
                            xPos = rectX + (rectWidth + LeftIndent - RightIndent - _currentLineWidth) / 2.0;
                            break;
                    }
                }
                return xPos;
#else
                // Old code with StartXPosition dependent on _currentYPosition.

                // Why should StartXPosition be dependent on GetFittingRect() and _currentYPosition? 
                // GetFittingRect() always returns the X and Width from the area it was called for.
                // Or it returns null. In that case the original area’s X and width was used in rendering phase
                // and an exception occurred in formatting phase. What was the purpose?

                XUnitPt xPos = 0;

                if (_phase == Phase.Formatting)
                {
                    xPos = _formattingArea.GetFittingRect(_currentYPosition, _currentVerticalInfo.Height)?.X ?? NRT.ThrowOnNull<XUnitPt>();
                    xPos += LeftIndent;
                }
                else //if (phase == Phase.Rendering)
                {
                    Area contentArea = _renderInfo.LayoutInfo.ContentArea;
                    // Next lines for non-fitting lines that produce an empty fitting rect:
                    XUnitPt rectX = contentArea.X;
                    XUnitPt rectWidth = contentArea.Width;

                    var fittingRect = contentArea.GetFittingRect(_currentYPosition, _currentVerticalInfo.Height);
                    if (fittingRect != null)
                    {
                        rectX = fittingRect.X;
                        rectWidth = fittingRect.Width;
                    }
                    switch (_paragraph.Format.Alignment)
                    {
                        case ParagraphAlignment.Left:
                        case ParagraphAlignment.Justify:
                            xPos = rectX;
                            xPos += LeftIndent;
                            break;

                        case ParagraphAlignment.Right:
                            xPos = rectX + rectWidth - RightIndent;
                            xPos -= _currentLineWidth;
                            break;

                        case ParagraphAlignment.Center:
                            xPos = rectX + (rectWidth + LeftIndent - RightIndent - _currentLineWidth) / 2.0;
                            break;
                    }
                }
                return xPos;
#endif
            }
        }

        /// <summary>
        /// Renders a single line.
        /// </summary>
        /// <param name="lineInfo"></param>
        void RenderLine(LineInfo lineInfo)
        {
            _currentYPosition += lineInfo.GapBefore;
            _currentLineRect = lineInfo.FittingRect;
            var transitions = lineInfo.SegmentTransitions;
            _currentLineWordsWidth = transitions is { Count: > 0 }
                ? transitions[0].WordsBefore : lineInfo.WordsWidth;
            _currentLineBlankCount = transitions is { Count: > 0 }
                ? transitions[0].BlanksBefore : lineInfo.BlankCount;
            _currentSegmentNeedsContraction = transitions is { Count: > 0 } &&
                transitions[0].PreviousSegmentNeedsContraction;
            _currentVerticalInfo = lineInfo.Vertical;
            _currentLeaf = lineInfo.StartIter;
            _startLeaf = lineInfo.StartIter;
            _endLeaf = lineInfo.EndIter;
            if (!_baselineOffsetInitialized)
            {
                _exactParagraphBaselineOffset = 0;
                if (_paragraph.Format.LineSpacingRule == LineSpacingRule.Exactly &&
                    _documentRenderer.ExactParagraphBaselineOffset is { } baselineOffset)
                {
                    var savedLeaf = _currentLeaf;
                    var fonts = new List<XFont>();
                    for (var leaf = lineInfo.StartIter; leaf != null; leaf = leaf.GetNextLeaf())
                    {
                        _currentLeaf = leaf;
                        if (leaf.Current is Text text && !IsBlank(text))
                            fonts.Add(CurrentFont);
                        if (leaf.Current == lineInfo.EndIter?.Current)
                            break;
                    }
                    _currentLeaf = savedLeaf;
                    if (baselineOffset(_paragraph, fonts) is double offset &&
                        !double.IsNaN(offset) && !double.IsInfinity(offset))
                        _exactParagraphBaselineOffset = offset;
                }
                if (_paragraph.Format.LineSpacingRule == LineSpacingRule.AtLeast &&
                    _paragraph.Format.AtLeastBaselineOffset is { } atLeastOffset &&
                    IsFinite(atLeastOffset.Point))
                    _exactParagraphBaselineOffset = atLeastOffset.Point;
                if (_documentRenderer.ParagraphPaintBaselineOffset is { } paintBaselineOffset)
                {
                    var savedLeaf = _currentLeaf;
                    var fonts = new List<XFont>();
                    for (var leaf = lineInfo.StartIter; leaf != null; leaf = leaf.GetNextLeaf())
                    {
                        _currentLeaf = leaf;
                        if (leaf.Current is Text text && !IsBlank(text))
                            fonts.Add(CurrentFont);
                        if (leaf.Current == lineInfo.EndIter?.Current)
                            break;
                    }
                    _currentLeaf = savedLeaf;
                    if (paintBaselineOffset(_paragraph, fonts) is double offset && IsFinite(offset))
                        _exactParagraphBaselineOffset += offset;
                }
                _baselineOffsetInitialized = true;
            }
            _currentBlankCount = lineInfo.BlankCount;
            _currentLineWidth = lineInfo.LineWidth;
            _currentClippedTrailingWidth = lineInfo.ClippedTrailingWidth;
            _currentLineEndsWithLineBreak = lineInfo.LineEndsWithLineBreak;
            _currentWordsWidth = lineInfo.WordsWidth;
            // Fitting may clip closing marks, but every interval paints their
            // full advance. Include only this interval's clipped width when
            // distributing justified spaces.
            if (_paragraph.Format.Alignment == ParagraphAlignment.Justify)
                _currentLineWordsWidth += transitions is { Count: > 0 }
                    ? transitions[0].ClippedWidthBefore : lineInfo.ClippedTrailingWidth;
            double[]? renderWidthAdjustments = null;
            if (_paragraph.Format.Alignment == ParagraphAlignment.Justify &&
                _documentRenderer.JustifiedRenderWordWidthAdjustment is { } renderWidthAdjustment)
            {
                renderWidthAdjustments = new double[(transitions?.Count ?? 0) + 1];
                var widthSegment = 0;
                var savedLeaf = _currentLeaf;
                for (var leaf = lineInfo.StartIter; leaf != null; leaf = leaf.GetNextLeaf())
                {
                    _currentLeaf = leaf;
                    if (transitions is { Count: > 0 } && widthSegment < transitions.Count &&
                        transitions[widthSegment].StartIter?.Current == leaf.Current)
                        widthSegment++;
                    if (leaf.Current is Text text && !IsBlank(text) &&
                        renderWidthAdjustment(_paragraph, text.Content, CurrentFont) is double adjustment &&
                        IsFinite(adjustment))
                    {
                        _currentWordsWidth += adjustment;
                        renderWidthAdjustments[widthSegment] += adjustment;
                    }
                    if (leaf.Current == lineInfo.EndIter?.Current)
                        break;
                }
                _currentLeaf = savedLeaf;
                _currentLineWordsWidth += renderWidthAdjustments[0];
            }
            _currentXPosition = StartXPosition;
            _tabOffsets = lineInfo.TabOffsets;
            _lastTabPassed = lineInfo.LastTab == null;
            _lastTab = lineInfo.LastTab;

            // Selected blank advances are fixed boundary spacing, not stretchable
            // justification space. Reserve them independently for each flow interval.
            double[]? blankAdvances = null;
            List<double>[]? blankSelections = null;
            _currentLineBlankAdvances = null;
            if (_paragraph.Format.Alignment == ParagraphAlignment.Justify &&
                _documentRenderer.TextTrailingAdvance != null)
            {
                blankAdvances = GetJustifiedBlankAdvances(lineInfo, out blankSelections);
                _currentLineWordsWidth += blankAdvances[0];
                _currentLineBlankAdvances = blankSelections[0];
            }

            _tabIdx = 0;
            var segmentIndex = 0;

            bool ready = _currentLeaf == null;
            if (_isFirstLine)
                RenderListSymbol();

            while (!ready)
            {
                if (_currentLeaf is null)
                    NRT.ThrowOnNull();

                if (_currentLeaf.Current == (lineInfo.EndIter?.Current ?? NRT.ThrowOnNull<DocumentObject>()))
                    ready = true;

                if (_currentLeaf.Current == lineInfo.LastTab)
                    _lastTabPassed = true;

                // If a DocumentObjectCollection is returned as a leaf, it is obviously empty.
                // There is no need to render an empty DocumentObjectCollection. Also, RenderElement() will throw an exception, because it doesn’t contain implementations for DocumentObjectCollections. So we better won¹t call it for them.
                if (transitions is { Count: > 0 } && segmentIndex < transitions.Count &&
                    transitions[segmentIndex].StartIter?.Current == _currentLeaf.Current)
                {
                    var transition = transitions[segmentIndex];
                    _hasTextBoundaryPredecessor = false;
                    _textBoundaryPaintOffset = 0;
                    _currentXPosition = transition.Rect.X;
                    _currentLineRect = transition.Rect;
                    segmentIndex++;
                    _currentSegmentNeedsContraction = segmentIndex < transitions.Count &&
                        transitions[segmentIndex].PreviousSegmentNeedsContraction;
                    _currentLineWordsWidth = (segmentIndex < transitions.Count
                        ? transitions[segmentIndex].WordsBefore : lineInfo.WordsWidth) -
                        transition.WordsBefore;
                    if (_paragraph.Format.Alignment == ParagraphAlignment.Justify)
                        _currentLineWordsWidth += (segmentIndex < transitions.Count
                            ? transitions[segmentIndex].ClippedWidthBefore : lineInfo.ClippedTrailingWidth) -
                            transition.ClippedWidthBefore;
                    if (renderWidthAdjustments != null)
                        _currentLineWordsWidth += renderWidthAdjustments[segmentIndex];
                    if (blankAdvances != null && blankSelections != null)
                    {
                        _currentLineWordsWidth += blankAdvances[segmentIndex];
                        _currentLineBlankAdvances = blankSelections[segmentIndex];
                    }
                    _currentLineBlankCount = (segmentIndex < transitions.Count
                        ? transitions[segmentIndex].BlanksBefore : lineInfo.BlankCount) -
                        transition.BlanksBefore;
                    if (_paragraph.Format.Alignment == ParagraphAlignment.Justify &&
                        _currentLineBlankCount == 0 &&
                        !(_isLastLine && _renderInfo.FormatInfo.IsEnding))
                    {
                        var freeWidth = Math.Max(0, transition.Rect.Width - RightIndent -
                            _currentLineWordsWidth);
                        _currentXPosition += segmentIndex == transitions.Count
                            ? freeWidth : freeWidth / 2;
                    }
                    if (_currentLeaf.Current is not Text splitBlank || !IsBlank(splitBlank))
                        RenderElement(_currentLeaf.Current);
                }
                else if (_currentLeaf.Current is not DocumentObjectCollection)
                    RenderElement(_currentLeaf.Current);

                _currentLeaf = _currentLeaf.GetNextLeaf();
            }
            if (_documentRenderer.EmitJustifiedLineGeometry &&
                _paragraph.Format.Alignment == ParagraphAlignment.Justify)
                _gfx.WriteComment($"MD-JustifiedLine last={(_isLastLine && _renderInfo.FormatInfo.IsEnding ? 1 : 0)}");
            _currentYPosition += lineInfo.Vertical.Height;
            _isFirstLine = false;
        }

        void ReMeasureLine(ref LineInfo lineInfo)
        {
            //--- Save ---------------------------------
            SaveBeforeProbing(out var iter, out var blankCount, out var wordsWidth, out var xPosition, out var lineWidth, out var blankWidth,
                out var lineEndsWithLineBreak, out var naturalFontHeight, out var contractionSpaceWidth, out var clippedTrailingWidth);
            bool origLastTabPassed = _lastTabPassed;
            //------------------------------------------
            _currentLeaf = lineInfo.StartIter;
            _startLeaf = lineInfo.StartIter;
            _endLeaf = lineInfo.EndIter;
            _formattingArea = _renderInfo.LayoutInfo.ContentArea;
            _tabOffsets = [];
            _currentLineWidth = 0;
            _currentClippedTrailingWidth = 0;
            _currentWordsWidth = 0;
            _currentBlankCount = 0;
            _currentContractionSpaceWidth = 0;
            _currentMinimumBlankWidth = double.PositiveInfinity;
            _savedBlankWidth = 0;
            _savedBlankContractionWidth = 0;
            _lastTabPassed = lineInfo.LastTab == null;
            _currentLineEndsWithLineBreak = false;

            var fittingRect = _formattingArea.GetFittingRect(_currentYPosition, _currentVerticalInfo.Height);
            if (fittingRect != null)
            {
                _currentXPosition = fittingRect.X + LeftIndent;
                FormatListSymbol();
                bool goOn = true;
                while (goOn && _currentLeaf != null)
                {
                    if (_currentLeaf.Current == lineInfo.LastTab)
                        _lastTabPassed = true;

                    FormatElement(_currentLeaf.Current);

                    if (_endLeaf is null)
                        NRT.ThrowOnNull();
                    goOn = _currentLeaf != null && _currentLeaf.Current != _endLeaf.Current;
                    if (goOn)
                        _currentLeaf = _currentLeaf!.GetNextLeaf();
                }
                lineInfo.LineWidth = _currentLineWidth;
                lineInfo.ClippedTrailingWidth = _currentClippedTrailingWidth;
                lineInfo.WordsWidth = _currentWordsWidth;
                lineInfo.BlankCount = _currentBlankCount;
                lineInfo.TabOffsets = _tabOffsets;
                lineInfo.LineEndsWithLineBreak = _currentLineEndsWithLineBreak;
                lineInfo.ReMeasureLine = false;
                _lastTabPassed = origLastTabPassed;
            }
            RestoreAfterProbing(iter, blankCount, wordsWidth, xPosition, lineWidth, blankWidth, lineEndsWithLineBreak, naturalFontHeight, contractionSpaceWidth, clippedTrailingWidth);
        }

        XUnitPt CurrentWordDistance
        {
            get
            {
                if (_phase == Phase.Rendering && !_isLastLine &&
                    _paragraph.Format.Alignment == ParagraphAlignment.Left &&
                    _lastTabPassed && _currentLineBlankCount > 0 &&
                    _currentClippedTrailingWidth > 0 &&
                    _documentRenderer.HangingNonterminalLineSpaceLimit is { } nonterminalLimit)
                {
                    var endLeaf = _endLeaf;
                    while (endLeaf?.Current is Text blank && IsBlank(blank))
                        endLeaf = endLeaf.GetPreviousLeaf();
                    if (endLeaf?.Current is Text { Content: { Length: > 0 } nonterminalText })
                    {
                        var savedLeaf = _currentLeaf;
                        _currentLeaf = endLeaf;
                        var font = CurrentFont;
                        var limit = nonterminalLimit(_paragraph, nonterminalText, font);
                        var markWidth = limit is >= 0 && IsFinite(limit.Value)
                            ? MeasureString(nonterminalText[^1..], includeTrailingAdvance: false) : 0;
                        _currentLeaf = savedLeaf;
                        if (limit is >= 0 && IsFinite(limit.Value))
                        {
                            var area = _renderInfo.LayoutInfo.ContentArea;
                            var width = (_currentLineRect?.Width ??
                                area.GetFittingRect(_currentYPosition,
                                    _currentVerticalInfo.Height)?.Width ?? NRT.ThrowOnNull<XUnitPt>()) -
                                LeftIndent - RightIndent;
                            var expansion = width - _currentLineWidth -
                                _currentClippedTrailingWidth + markWidth;
                            if (expansion > 0 && expansion / _currentLineBlankCount <= limit.Value)
                                return MeasureString(" ") + expansion / _currentLineBlankCount;
                        }
                    }
                }
                if (_phase == Phase.Rendering && _isLastLine && _renderInfo.FormatInfo.IsEnding &&
                    _paragraph.Format.Alignment == ParagraphAlignment.Left &&
                    _lastTabPassed && _currentLineBlankCount > 0 &&
                    _endLeaf?.Current is Text { Content: { Length: > 0 } endText } &&
                    _documentRenderer.HangingTerminalLineSpaceThreshold is { } hangingThreshold)
                {
                    var savedLeaf = _currentLeaf;
                    _currentLeaf = _endLeaf;
                    var font = CurrentFont;
                    var threshold = hangingThreshold(_paragraph, endText, font);
                    var nearEdgeThreshold = _documentRenderer.HangingTerminalLineNearEdgeThreshold?.Invoke(
                        _paragraph, endText, font);
                    var markWidth = threshold is >= 0 && IsFinite(threshold.Value)
                        ? MeasureString(endText[^1..], includeTrailingAdvance: false) : 0;
                    var renderedAdjustment = threshold is >= 0 && IsFinite(threshold.Value)
                        ? GetRenderOnlyTrailingGlyphKerning(endText, font) : 0;
                    var fitAdjustment = threshold is >= 0 && IsFinite(threshold.Value)
                        ? _documentRenderer.HangingTerminalLineFitKerning?.Invoke(
                            _paragraph, endText, font) : null;
                    _currentLeaf = savedLeaf;
                    if (threshold is >= 0 && IsFinite(threshold.Value))
                    {
                        var contentArea = _renderInfo.LayoutInfo.ContentArea;
                        var width = (_currentLineRect?.Width ??
                            contentArea.GetFittingRect(_currentYPosition,
                                _currentVerticalInfo.Height)?.Width ?? NRT.ThrowOnNull<XUnitPt>()) -
                            LeftIndent - RightIndent;
                        var renderedLineWidth = _currentLineWidth + renderedAdjustment;
                        var expansion = width - renderedLineWidth + markWidth;
                        var fitLineWidth = _currentLineWidth +
                            (fitAdjustment is double adjustment && IsFinite(adjustment)
                                ? adjustment : renderedAdjustment);
                        if ((fitLineWidth - width > threshold.Value ||
                             nearEdgeThreshold is >= 0 && IsFinite(nearEdgeThreshold.Value) &&
                             fitLineWidth - width > -nearEdgeThreshold.Value) && expansion > 0)
                            return MeasureString(" ") + expansion / _currentLineBlankCount;
                    }
                }
                if (_phase == Phase.Rendering &&
                  _paragraph.Format.Alignment == ParagraphAlignment.Justify && _lastTabPassed)
                {
                    var contentArea = _renderInfo.LayoutInfo.ContentArea;
                    XUnitPt width = _currentLineRect?.Width ??
                        contentArea.GetFittingRect(_currentYPosition,
                            _currentVerticalInfo.Height)?.Width ?? NRT.ThrowOnNull<XUnitPt>();
                    if (_lastTabPosition > 0)
                        width -= _lastTabPosition - contentArea.X;
                    else
                        width -= LeftIndent;
                    width -= RightIndent;
                    if (_currentLineBlankCount >= 1 &&
                        (!(_isLastLine && _renderInfo.FormatInfo.IsEnding) ||
                         _currentSegmentNeedsContraction ||
                         _documentRenderer.JustifiedSpaceShrinkFactorForParagraph?.Invoke(_paragraph) is > 0 and < 1 &&
                         _currentLineWidth > width))
                    {
                        if (_documentRenderer.TextTrailingAdvance != null && MeasureString(" ") <= 0)
                            return 0;
                        return GetJustifiedBlankDistance(
                            (width - _currentLineWordsWidth) / _currentLineBlankCount);
                    }
                }
                return MeasureString(" ");
            }
        }

        void RenderElement(DocumentObject docObj)
        {
            switch (docObj)
            {
                case Text obj:
                    if (!IsBlankOrSoftHyphen(obj))
                    {
                        RenderText(obj);
                        break;
                    }
                    if (IsSoftHyphen(obj))
                        RenderSoftHyphen();
                    else
                        RenderBlank();
                    //if (IsBlank(docObj))
                    //    RenderBlank();
                    //else if (IsSoftHyphen(docObj))
                    //    RenderSoftHyphen();
                    //else
                    //    RenderText((Text)docObj);
                    break;

                case Character obj:
                    RenderCharacter(obj);
                    break;

                case DateField obj:
                    RenderDateField(obj);
                    break;

                case InfoField obj:
                    RenderInfoField(obj);
                    break;

                case NumPagesField obj:
                    RenderNumPagesField(obj);
                    break;

                case PageField obj:
                    RenderPageField(obj);
                    break;

                case SectionField obj:
                    RenderSectionField(obj);
                    break;

                case SectionPagesField obj:
                    RenderSectionPagesField(obj);
                    break;

                case BookmarkField obj:
                    RenderBookmarkField(obj);
                    break;

                case PageRefField obj:
                    RenderPageRefField(obj);
                    break;

                case Image obj:
                    RenderImage(obj);
                    break;

                case Footnote:
                    // TODO_OLD Not yet implemented.
                    break;

                default:
                    throw new NotSupportedException(docObj.GetType().Name + " is not implemented.");
            }
        }

        (double Left, double Top, double Right, double Bottom) GetInlineImageEffectExtent(Image image)
        {
            var selected = _documentRenderer.InlineImageEffectExtent?.Invoke(image);
            if (selected is not { } extent ||
                extent.Left < 0 || extent.Top < 0 || extent.Right < 0 || extent.Bottom < 0 ||
                !IsFinite(extent.Left + extent.Top + extent.Right + extent.Bottom))
                return default;
            return extent;
        }

        XUnitPt GetInlineImageLayoutHeight(Image image, XUnitPt originalHeight)
        {
            var selected = _documentRenderer.InlineImageLayoutHeight?.Invoke(image);
            return selected is double height && height > 0 && IsFinite(height)
                ? XUnitPt.FromPoint(height) : originalHeight;
        }

        void RenderImage(Image image)
        {
            var renderInfo = CurrentImageRenderInfo;
            XUnitPt top = CurrentBaselinePosition;
            var contentArea = renderInfo?.LayoutInfo.ContentArea ?? NRT.ThrowOnNull<Area>();
            top -= contentArea.Height;
            var layoutHeight = GetInlineImageLayoutHeight(image, contentArea.Height);
            var effect = GetInlineImageEffectExtent(image);
            var occupiedHeight = layoutHeight + effect.Top + effect.Bottom;
            top -= effect.Bottom;
            var occupiesDescent = IsImageOnlyParagraph ||
                _documentRenderer.InlineImageOccupiesDescent?.Invoke(image) == true;
            var lineRule = _paragraph.Format.LineSpacingRule;
            var atLeastHasRoomBelowImage = lineRule == LineSpacingRule.AtLeast &&
                _paragraph.Format.LineSpacing.Point >= occupiedHeight.Point + FontHandler.GetDescent(CurrentFont).Point;
            if (occupiesDescent &&
                lineRule != LineSpacingRule.Exactly && !(occupiesDescent && atLeastHasRoomBelowImage))
            {
                if (occupiedHeight > CurrentFont.GetHeight())
                {
                    // Preserve compensation for the effective space already
                    // stored after paragraph spacing and minimum line metrics.
                    var originalSpace = occupiedHeight + CurrentFont.GetHeight() - FontHandler.GetAscent(CurrentFont);
                    top += Math.Max(0, originalSpace - _currentVerticalInfo.InherentLineSpace);
                }
                else if (occupiesDescent)
                    top += FontHandler.GetDescent(CurrentFont);
            }
            if (_documentRenderer.InlineImageVerticalOffset?.Invoke(image) is double offset &&
                !double.IsNaN(offset) && !double.IsInfinity(offset))
                top += offset;
            top += contentArea.Height - layoutHeight;
            RenderByInfos(CurrentPaintXPosition + effect.Left, top, [renderInfo]);
            if (_documentRenderer.InlineImageOverlay is { } overlay)
            {
                var state = _gfx.Save();
                try
                {
                    overlay(image, _gfx, (CurrentPaintXPosition + effect.Left).Point, top.Point,
                        contentArea.Width.Point, contentArea.Height.Point);
                }
                finally
                {
                    _gfx.Restore(state);
                }
            }


            var occupiedWidth = contentArea.Width + effect.Left + effect.Right;
            RenderUnderline(occupiedWidth, true);
            RealizeHyperlink(occupiedWidth);

            _currentXPosition += occupiedWidth;
        }

        void RenderDateField(DateField dateField)
        {
            if (_fieldInfos is null)
                NRT.ThrowOnNull();
            var value = FormatDateTimeForField(_fieldInfos.Date, dateField);
            RenderWord(value);
        }

        void RenderInfoField(InfoField infoField)
        {
            RenderWord(GetDocumentInfo(infoField.Name));
        }

        void RenderNumPagesField(NumPagesField numPagesField)
        {
            RenderWord(GetFieldValue(numPagesField));
        }

        void RenderPageField(PageField pageField)
        {
            RenderWord(GetFieldValue(pageField));
        }

        void RenderSectionField(SectionField sectionField)
        {
            RenderWord(GetFieldValue(sectionField));
        }

        void RenderSectionPagesField(SectionPagesField sectionPagesField)
        {
            RenderWord(GetFieldValue(sectionPagesField));
        }

        void RenderBookmarkField(BookmarkField bookmarkField)
        {
            // Add also a named destination, if a PdfDocument is rendered.
            //var pdfDocument = _gfx?.PdfPage?.Owner;
            var pdfDocument = _gfx.PdfPage?.Owner;
            if (pdfDocument != null)
            {
                var pageNr = pdfDocument.PageCount; // Magic: Pages are added while rendering, so the current page number equals pdfDocument.PageCount.
                Debug.Assert(pageNr >= 1);

                var destinationName = bookmarkField.Name;
                var position = GetDestinationPosition();
                pdfDocument.AddNamedDestination(destinationName, pageNr, PdfNamedDestinationParameters.CreatePosition(position.AsXPoint));
            }

            RenderUnderline(0, false);
        }

        /// <summary>
        /// Gets the current position for destinations in PDF world space units.
        /// The position is moved by a margin value to leave space between the window and the content that is located at the destination
        /// and it is transformed to PDF world space units.
        /// </summary>
        XPoint GetDestinationPosition()
        {
            //var margin = XUnitPt.FromCentimeter(0.5);
            var x = CurrentPaintXPosition > _margin ? CurrentPaintXPosition - _margin : 0;
            var y = _currentYPosition > _margin ? _currentYPosition - _margin : 0;
            var destinationPosition = new XPoint(x, y);

            var pdfPosition = _gfx.Transformer.WorldToDefaultPage(destinationPosition);
            return pdfPosition;
        }
        // ReSharper disable once InconsistentNaming
        static readonly XUnitPt _margin = XUnitPt.FromCentimeter(0.5f);

        void RenderPageRefField(PageRefField pageRefField)
        {
            RenderWord(GetFieldValue(pageRefField));
        }

        void RenderCharacter(Character character)
        {
            switch (character.SymbolName)
            {
                case SymbolName.Blank:
                case SymbolName.Em:
                case SymbolName.Em4:
                case SymbolName.En:
                    RenderSpace(character);
                    break;
                case SymbolName.LineBreak:
                    RenderLinebreak();
                    break;

                case SymbolName.Tab:
                    RenderTab();
                    break;

                default:
                    RenderSymbol(character);
                    break;
            }
        }

        void RenderSpace(Character character)
        {
            if (_collapsedSpacesOnLine.Contains(character))
            {
                RenderUnderline(0, false);
                RealizeHyperlink(0);
                return;
            }
            XUnitPt width = GetSpaceWidth(character);
            _hasTextBoundaryPredecessor |= width > 0;
            RenderUnderline(width, false);
            RealizeHyperlink(width);
            _currentXPosition += width;
        }

        void RenderLinebreak()
        {
            RenderUnderline(0, false);
            RealizeHyperlink(0);
        }

        void RenderSymbol(Character character)
        {
            string sym = GetSymbol(character);
            string completeWord = sym;
            for (int idx = 1; idx < character.Count; idx++)
                completeWord += sym;

            RenderWord(completeWord);
        }

        void RenderTab()
        {
            var tabOffset = NextTabOffset();
            RenderUnderline(tabOffset.Offset, false);
            RenderTabLeader(tabOffset);
            RealizeHyperlink(tabOffset.Offset);
            _currentXPosition += tabOffset.Offset;
            if (_currentLeaf is null)
                NRT.ThrowOnNull();
            if (_currentLeaf.Current == _lastTab)
                _lastTabPosition = _currentXPosition;
        }

        void RenderTabLeader(TabOffset tabOffset)
        {
            string leaderString;
            switch (tabOffset.Leader)
            {
                case TabLeader.Dashes:
                    leaderString = "-";
                    break;

                case TabLeader.Dots:
                    leaderString = ".";
                    break;

                case TabLeader.Heavy:
                case TabLeader.Lines:
                    leaderString = "_";
                    break;

                case TabLeader.MiddleDot:
                    leaderString = "·";
                    break;

                default:
                    return;
            }
            XUnitPt leaderWidth = MeasureString(leaderString, includeTrailingAdvance: false);
            XUnitPt xPosition = _currentXPosition;
            string drawString = "";

            while (xPosition + leaderWidth <= _currentXPosition + tabOffset.Offset)
            {
                drawString += leaderString;
                xPosition += leaderWidth;
            }
            Font font = CurrentDomFont;
            XFont xFont = CurrentFont;
            if (font.Subscript || font.Superscript)
                xFont = FontHandler.ToSubSuperFont(xFont);

            var gridPitch = _documentRenderer.TabLeaderGridPitch?.Invoke(_paragraph, tabOffset.Leader, xFont);
            if (gridPitch is double grid && !Double.IsNaN(grid) && !Double.IsInfinity(grid) && grid > 0)
            {
                // Use the actual script font and painted pen, independent of text fit scaling.
                var naturalWidth = _gfx.MeasureString(leaderString, xFont, StringFormat).Width;
                var start = CurrentPaintXPosition.Point;
                var end = start + tabOffset.Offset.Point;
                var first = Math.Ceiling(start / grid) * grid;
                var cells = first >= end ? 0 : Math.Floor((end - first - Math.Max(grid, naturalWidth)) / grid) + 1;
                if (naturalWidth > 0 && !Double.IsInfinity(naturalWidth) &&
                    !Double.IsNaN(cells) && !Double.IsInfinity(cells) &&
                    cells < Int32.MaxValue && first >= start && !Double.IsInfinity(first) &&
                    (cells <= 1 || first + grid > first))
                {
                    if (CurrentBrush != null)
                        for (var index = 0; index < Math.Max(0, cells); index++)
                            _gfx.DrawString(leaderString, xFont, CurrentBrush,
                                first + index * grid, CurrentBaselinePosition);
                    return;
                }
            }

            var layout = _documentRenderer.TabLeaderLayout?.Invoke(_paragraph, tabOffset.Leader, xFont);
            if (layout is { } adjustment &&
                leaderWidth.Point + adjustment.CharacterSpacing > 0 &&
                tabOffset.Offset.Point - adjustment.RightInset >= leaderWidth.Point)
            {
                var pitch = leaderWidth.Point + adjustment.CharacterSpacing;
                var available = tabOffset.Offset.Point - adjustment.RightInset;
                var count = (int)Math.Floor((available - leaderWidth.Point) / pitch) + 1;
                var first = _currentXPosition.Point + available - leaderWidth.Point - (count - 1) * pitch;
                while (count > 0 && first < _currentXPosition.Point + adjustment.LeftInset)
                {
                    first += pitch;
                    count--;
                }
                if (CurrentBrush != null)
                    for (var index = 0; index < count; index++)
                        _gfx.DrawString(leaderString, xFont, CurrentBrush,
                            first + index * pitch + _textBoundaryPaintOffset, CurrentBaselinePosition);
                return;
            }

            if (CurrentBrush != null)
                _gfx.DrawString(drawString, xFont, CurrentBrush, CurrentPaintXPosition, CurrentBaselinePosition);
        }

        TabOffset NextTabOffset()
        {
            TabOffset offset = _tabOffsets.Count > _tabIdx ? _tabOffsets[_tabIdx] :
              new TabOffset(0, 0);

            ++_tabIdx;
            return offset;
        }

        int _tabIdx;

        bool IgnoreBlank()
        {
            if (_currentLeaf == _startLeaf)
                return true;

            if (/*_endLeaf is null ||*/ _currentLeaf is null)
                NRT.ThrowOnNull();

            if (_endLeaf != null && _currentLeaf.Current == _endLeaf.Current)
                return true;

            var nextIter = _currentLeaf.GetNextLeaf();
            while (nextIter != null && (IsBlank(nextIter.Current) || nextIter.Current is BookmarkField))
            {
                nextIter = nextIter.GetNextLeaf();
            }
            if (nextIter == null)
                return true;

            if (IsTab(nextIter.Current))
                return true;

            var prevIter = _currentLeaf.GetPreviousLeaf();
            // Can be null if currentLeaf is the first leaf.
            var obj = prevIter?.Current;
            while (obj is BookmarkField)
            {
                prevIter = prevIter?.GetPreviousLeaf();
                obj = prevIter?.Current;
            }
            if (obj == null)
                return true;

            return IsBlank(obj) || IsTab(obj);
        }

        void RenderBlank()
        {
            if (!IgnoreBlank())
            {
                XUnitPt wordDistance = CurrentWordDistance;
                _hasTextBoundaryPredecessor |= wordDistance > 0;
                RenderUnderline(wordDistance, false);
                RealizeHyperlink(wordDistance);
                _currentXPosition += wordDistance;
                if (_documentRenderer.EmitJustifiedLineGeometry &&
                    _paragraph.Format.Alignment == ParagraphAlignment.Justify &&
                    !(_isLastLine && _renderInfo.FormatInfo.IsEnding))
                    _gfx.WriteComment("MD-JustifiedGap");
            }
            else
            {
                RenderUnderline(0, false);
                RealizeHyperlink(0);
            }
        }

        void RenderSoftHyphen()
        {
            if (_currentLeaf is null || _endLeaf is null)
                NRT.ThrowOnNull();

            if (_currentLeaf.Current == _endLeaf.Current)
                RenderWord("-");
        }

        void RenderText(Text text)
        {
            RenderWord(text.Content);
        }

        void RenderWord(string word)
        {
            var leading = GetLeadingKerningAfterBlank(word);
            if (leading != 0)
                _currentXPosition += leading;
            else
            {
                var boundary = GetRenderOnlyTextBoundaryKerning(word);
                _textBoundaryPaintOffset += boundary;
            }
            Font font = CurrentDomFont;
            XFont xFont = CurrentFont;
            if (font.Subscript || font.Superscript)
                xFont = FontHandler.ToSubSuperFont(xFont);

            if (CurrentBrush != null)
            {
                var thresholdSelector = _documentRenderer.TextKerningMinimumSizePoints;
                var taggedText = _currentLeaf?.Current as Text;
                var emitKerningMetadata = thresholdSelector != null && taggedText?.Content == word;
                if (emitKerningMetadata)
                {
                    var threshold = thresholdSelector!(taggedText!.Tag);
                    _gfx.WriteComment(threshold is double value && !Double.IsNaN(value) &&
                        !Double.IsInfinity(value) && value >= 0
                            ? "MD-Kern " + value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                            : "MD-Kern None");
                }
                var kerning = GetTrailingGlyphKerning(word, CurrentFont) +
                    GetRenderOnlyTrailingGlyphKerning(word, CurrentFont);
                var layoutScale = GetTextHorizontalScale();
                var paintScale = GetTextPaintHorizontalScale(layoutScale);
                var pairAdvances = GetTextPairAdvances(word, CurrentFont);
                if (pairAdvances != null)
                    DrawPairAdjustedString(word, xFont, pairAdvances, layoutScale, paintScale, kerning);
                else if (paintScale != layoutScale)
                {
                    _gfx.DrawStringWithPaintScale(word, xFont, CurrentBrush,
                        CurrentPaintXPosition, CurrentBaselinePosition, layoutScale, paintScale, kerning);
                }
                else if (kerning != 0)
                {
                    var prefix = word[..^1];
                    DrawScaledString(prefix, xFont, CurrentPaintXPosition, paintScale);
                    DrawScaledString(word[^1..], xFont,
                        CurrentPaintXPosition + _gfx.MeasureString(prefix, xFont, StringFormat).Width *
                            layoutScale + kerning, paintScale);
                }
                else
                    DrawScaledString(word, xFont, CurrentPaintXPosition, paintScale);
                if (emitKerningMetadata)
                    _gfx.WriteComment("MD-Kern End");
            }
            XUnitPt wordWidth = MeasureString(word);
            RenderUnderline(wordWidth, true);
            RealizeHyperlink(wordWidth);
            _currentXPosition += wordWidth;
            _hasTextBoundaryPredecessor |= word.Length > 0;
        }

        void DrawPairAdjustedString(string word, XFont font, double[] adjustments,
            double layoutScale, double paintScale, double trailingKerning)
        {
            var glyphs = new List<(string Text, int End)>();
            for (var index = 0; index < word.Length;)
            {
                var length = char.IsHighSurrogate(word[index]) ? 2 : 1;
                glyphs.Add((word.Substring(index, length), index + length - 1));
                index += length;
            }
            if (_gfx.PdfPage != null)
            {
                _gfx.DrawStringWithPairAdjustments(word, font, CurrentBrush!,
                    CurrentPaintXPosition.Point, CurrentBaselinePosition.Point, layoutScale, paintScale,
                    glyphs.Take(glyphs.Count - 1).Select(glyph => adjustments[glyph.End]).ToArray(), trailingKerning);
            }
            else
            {
                // Preview/printing graphics do not support positioned PDF glyphs.
                var advances = new double[glyphs.Count - 1];
                for (var index = 0; index < advances.Length; index++)
                    advances[index] = _gfx.MeasureString(glyphs[index].Text, font, StringFormat).Width * layoutScale +
                        adjustments[glyphs[index].End] + (index == advances.Length - 1 ? trailingKerning : 0);
                var x = CurrentPaintXPosition;
                for (var index = 0; index < glyphs.Count; index++)
                {
                    DrawScaledString(glyphs[index].Text, font, x, paintScale);
                    if (index < advances.Length) x += advances[index];
                }
            }
        }

        double[]? GetTextPairAdvances(string word, XFont font)
        {
            if (word.Length < 2 || _currentLeaf?.Current is not Text text || text.Content != word ||
                _documentRenderer.TextPairAdvances?.Invoke(text.Tag, word, font) is not { } selected ||
                selected.Length != word.Length - 1 || selected.Any(value => !IsFinite(value)))
                return null;
            var copy = (double[])selected.Clone();
            for (var index = 0; index < word.Length; index++)
            {
                if (char.IsHighSurrogate(word[index]))
                {
                    if (index + 1 >= word.Length || !char.IsLowSurrogate(word[index + 1])) return null;
                    copy[index++] = 0;
                }
                else if (char.IsLowSurrogate(word[index])) return null;
            }
            var sum = copy.Sum();
            var naturalWidth = _gfx.MeasureString(word, font, StringFormat).Width * GetTextHorizontalScale();
            var domFont = CurrentDomFont;
            if (domFont.Subscript || domFont.Superscript)
                naturalWidth *= FontHandler.GetSubSuperScaling(font);
            return IsFinite(sum) && naturalWidth + sum >= 0 && copy.Any(value => value != 0) ? copy : null;
        }

        double GetTextHorizontalScale()
        {
            var scale = _currentLeaf?.Current is Text text
                ? _documentRenderer.TextHorizontalScale?.Invoke(text.Tag) : null;
            return scale is > 0 and < 10 && IsFinite(scale.Value) ? scale.Value : 1;
        }

        double GetTextPaintHorizontalScale(double layoutScale)
        {
            var selected = _currentLeaf?.Current is Text text
                ? _documentRenderer.TextPaintHorizontalScale?.Invoke(text.Tag, CurrentFont) : null;
            // Preview and printing graphics do not have a PDF page. Retain their
            // existing layout-scale drawing path instead of invoking the PDF-only helper.
            return _gfx.PdfPage is not null && selected is > 0 and < 10 && IsFinite(selected.Value)
                ? selected.Value : layoutScale;
        }

        void DrawScaledString(string text, XFont font, XUnitPt x, double scale)
        {
            if (scale == 1)
            {
                _gfx.DrawString(text, font, CurrentBrush!, x, CurrentBaselinePosition);
                return;
            }
            var state = _gfx.Save();
            try
            {
                _gfx.TranslateTransform(x.Point, 0);
                _gfx.ScaleTransform(scale, 1);
                _gfx.DrawString(text, font, CurrentBrush!, 0, CurrentBaselinePosition);
            }
            finally
            {
                _gfx.Restore(state);
            }
        }

        void StartHyperlink(XUnitPt left, XUnitPt top)
        {
            _hyperlinkRect = new XRect(left, top, 0, 0);
        }

        void EndHyperlink(Hyperlink hyperlink, XUnitPt bottom)
        {
            _hyperlinkRect.Height = bottom - _hyperlinkRect.Y;
            var page = _gfx.PdfPage;
            if (page != null)
            {
                XRect rect = _gfx.Transformer.WorldToDefaultPage(_hyperlinkRect);

                switch (hyperlink.Type)
                {
                    case HyperlinkType.Local:

                        // Try to use named destination, if a document is rendered.
                        var pdfDocument = _gfx.PdfPage?.Owner;
                        if (pdfDocument != null)
                        {
                            page.AddDocumentLink(new PdfRectangle(rect.AsXRect), hyperlink.BookmarkName);
                        }
                        // Otherwise use page from bookmark’s fieldInfo.
                        else
                        {
                            var pageRef = _fieldInfos?.GetPhysicalPageNumber(hyperlink.BookmarkName) ?? NRT.ThrowOnNull<int>();
                            if (pageRef > 0)
                                page.AddDocumentLink(new PdfRectangle(rect.AsXRect), pageRef);
                        }
                        break;

                    case HyperlinkType.ExternalBookmark:
                        page.AddDocumentLink(new PdfRectangle(rect.AsXRect), hyperlink.Filename, hyperlink.BookmarkName, ConvertHyperlinkTargetWindow(hyperlink.NewWindow));
                        break;

                    case HyperlinkType.EmbeddedDocument:
                        page.AddEmbeddedDocumentLink(new PdfRectangle(rect.AsXRect), hyperlink.Filename, hyperlink.BookmarkName, ConvertHyperlinkTargetWindow(hyperlink.NewWindow));
                        break;

                    case HyperlinkType.Web:
                        page.AddWebLink(new PdfRectangle(rect.AsXRect), hyperlink.Filename);
                        break;

                    case HyperlinkType.File:
                        page.AddFileLink(new PdfRectangle(rect.AsXRect), hyperlink.Filename);
                        break;
                }
                _hyperlinkRect = new XRect();
            }
        }

        static bool? ConvertHyperlinkTargetWindow(HyperlinkTargetWindow hyperlinkTargetWindow)
        {
            return hyperlinkTargetWindow switch
            {
                HyperlinkTargetWindow.NewWindow => true,
                HyperlinkTargetWindow.SameWindow => false,
                HyperlinkTargetWindow.UserPreference => null,
                _ => null
            };
        }

        void RealizeHyperlink(XUnitPt width)
        {
            XUnitPt top = _currentYPosition;
            XUnitPt left = CurrentPaintXPosition;
            XUnitPt bottom = top + _currentVerticalInfo.Height;
            XUnitPt right = left + width;
            var hyperlink = GetHyperlink();

            bool hyperlinkChanged = _currentHyperlink != hyperlink;

            if (hyperlinkChanged)
            {
                if (_currentHyperlink != null)
                    EndHyperlink(_currentHyperlink, bottom);

                if (hyperlink != null)
                    StartHyperlink(left, top);

                _currentHyperlink = hyperlink;
            }

            if (_currentHyperlink != null)
            {
                // Accumulate only this hyperlink's painted spans. The next leaf's
                // offset must not resize this link, and signed shifts within the
                // link may place a later span before its original start.
                var spanLeft = Math.Min(_hyperlinkRect.X, left.Point);
                var spanRight = Math.Max(_hyperlinkRect.Right, right.Point);
                _hyperlinkRect.X = spanLeft;
                _hyperlinkRect.Width = spanRight - spanLeft;
            }

            if (_currentLeaf is null || _endLeaf is null)
                NRT.ThrowOnNull();

            if (_currentLeaf.Current == _endLeaf.Current)
            {
                if (_currentHyperlink != null)
                    EndHyperlink(_currentHyperlink, bottom);

                _currentHyperlink = null;
            }
        }
        Hyperlink? _currentHyperlink;
        XRect _hyperlinkRect;

        XUnitPt CurrentBaselinePosition
        {
            get
            {
                VerticalLineInfo verticalInfo = _currentVerticalInfo;
                XUnitPt position = _currentYPosition + _exactParagraphBaselineOffset;
                if (_paragraph.Format.LineSpacingRule == LineSpacingRule.Exactly &&
                    _documentRenderer.ExactFontBaselineOffset?.Invoke(_paragraph, CurrentFont) is double offset &&
                    !double.IsNaN(offset) && !double.IsInfinity(offset))
                    position += offset;

                Font font = CurrentDomFont;
                XFont xFont = CurrentFont;

                double setLineSpace = verticalInfo.InherentLineSpace;
                double standardFontLineSpace = xFont.GetHeight();

                // Set position to bottom of text.
                position += setLineSpace;
                if (font.Subscript)
                {
                    // Move sub-/superscaled descender up.
                    position -= FontHandler.GetSubSuperScaling(xFont) * FontHandler.GetDescent(xFont);
                }
                else if (font.Superscript)
                {
                    // Set position to top of text.
                    position -= standardFontLineSpace;
                    // Move sub-/superscaled LineSpace down and descender up.
                    position += FontHandler.GetSubSuperScaling(xFont) * (standardFontLineSpace - FontHandler.GetDescent(xFont));
                }
                else
                    // Move descender up.
                    position -= verticalInfo.Descent;

                return position;
            }
        }

        XBrush? CurrentBrush
        {
            get
            {
                if (_currentLeaf != null)
                    return FontHandler.FontColorToXBrush(CurrentDomFont);

                return null;
            }
        }

        void InitRendering()
        {
            _phase = Phase.Rendering;
            _baselineOffsetInitialized = false;

            var parFormatInfo = (ParagraphFormatInfo)_renderInfo.FormatInfo;
            if (parFormatInfo.LineCount == 0)
                return;
            _isFirstLine = parFormatInfo.IsStarting;

            LineInfo lineInfo = parFormatInfo.GetFirstLineInfo();
            Area contentArea = _renderInfo.LayoutInfo.ContentArea;
            _currentYPosition = contentArea.Y + TopBorderOffset(contentArea.Y);
            // StL: GetFittingRect liefert manchmal null
            var rect = contentArea.GetFittingRect(_currentYPosition, lineInfo.Vertical.Height);
            if (rect != null)
                _currentXPosition = rect.X;
            _currentLineWidth = 0;
            _currentClippedTrailingWidth = 0;
            _currentLineEndsWithLineBreak = false;
            _collapsedSpacesOnLine = [];
        }

        /// <summary>
        /// Initializes this instance for formatting.
        /// </summary>
        /// <param name="area">The area for formatting.</param>
        /// <param name="previousFormatInfo">A previous format info.</param>
        /// <returns>False if nothing of the paragraph will fit the area anymore.</returns>
        bool InitFormat(Area area, FormatInfo? previousFormatInfo)
        {
            _phase = Phase.Formatting;

            _tabOffsets = [];

            var prevParaFormatInfo = (ParagraphFormatInfo?)previousFormatInfo;
            if (prevParaFormatInfo == null || prevParaFormatInfo.LineCount == 0)
            {
                ((ParagraphFormatInfo)_renderInfo.FormatInfo)._isStarting = true;
                var parIt = new ParagraphIterator(_paragraph.Elements);
                _currentLeaf = parIt.GetFirstLeaf();
                _isFirstLine = true;
            }
            else
            {
                var lastLine = prevParaFormatInfo.GetLastLineInfo();
                _newLineStartedByBlankWrap = lastLine.NextLineStartedByBlankWrap;
                _currentLeaf = lastLine.HasContinuationOverride
                    ? lastLine.ContinuationIter
                    : lastLine.EndIter?.GetNextLeaf();
                _isFirstLine = false;
                ((ParagraphFormatInfo)_renderInfo.FormatInfo)._isStarting = false;
            }

            _startLeaf = _currentLeaf;
            _currentLineNaturalFontHeight = 0;
            _currentVerticalInfo = CalcCurrentVerticalInfo();
            _currentYPosition = area.Y + TopBorderOffset(area.Y);
            _formattingArea = area;
            if (area is SideExclusionArea sideExclusion)
                sideExclusion.NaturalFontHeight = _currentLineNaturalFontHeight;
            var rect = GetFittingRectForLine(_currentYPosition, _currentVerticalInfo);
            if (rect == null)
                return false;

            _currentXPosition = rect.X + LeftIndent;
            if (_isFirstLine)
                FormatListSymbol();

            return true;
        }

        /// <summary>
        /// Gets information necessary to render or measure the list symbol.
        /// </summary>
        /// <param name="symbol">The text or list symbol to render or measure.</param>
        /// <param name="font">The font to use for rendering or measuring.</param>
        /// <returns>True if a symbol needs to be rendered.</returns>
        bool GetListSymbol(out string symbol, out XFont font)
        {
            symbol = null!;
            font = null!;
            var formatInfo = (ParagraphFormatInfo)_renderInfo.FormatInfo;
            if (_phase == Phase.Formatting)
            {
                var format = _paragraph.Format;
                if (format.Values.ListInfo is not null && !format.Values.ListInfo.IsNull())
                {
                    var listInfo = format.ListInfo;
                    double size = format.Font.Size.Point;
                    var style = FontHandler.GetXStyle(format.Font);

                    switch (listInfo.ListType)
                    {
                        case ListType.BulletList1:
                            symbol = _documentRenderer.FontsAndChars.Bullets.Level1Character.ToString();
                            font = _documentRenderer.FontsAndChars.Bullets.GetLevel1Font(size, style);
                            break;

                        case ListType.BulletList2:
                            symbol = _documentRenderer.FontsAndChars.Bullets.Level2Character.ToString();
                            font = _documentRenderer.FontsAndChars.Bullets.GetLevel2Font(size, style);
                            break;

                        case ListType.BulletList3:
                            symbol = _documentRenderer.FontsAndChars.Bullets.Level3Character.ToString();
                            font = _documentRenderer.FontsAndChars.Bullets.GetLevel3Font(size, style);
                            break;

                        case ListType.NumberList1:
                            symbol = _documentRenderer.NextListNumber(listInfo) + ".";
                            font = FontHandler.FontToXFont(format.Font);
                            break;

                        case ListType.NumberList2:
                            symbol = NumberFormatter.Format(_documentRenderer.NextListNumber(listInfo), "alphabetic") + ".";
                            font = FontHandler.FontToXFont(format.Font);
                            break;

                        case ListType.NumberList3:
                            symbol = NumberFormatter.Format(_documentRenderer.NextListNumber(listInfo), "roman") + ".";
                            font = FontHandler.FontToXFont(format.Font);
                            break;
                        default:
                            throw new InvalidOperationException("Invalid list type.");
                    }
                    formatInfo.ListFont = font ?? NRT.ThrowOnNull<XFont>();
                    formatInfo.ListSymbol = symbol ?? NRT.ThrowOnNull<string>();
                    return true;
                }
            }
            else
            {
                if (formatInfo.ListFont != null! && formatInfo.ListSymbol != null!)
                {
                    font = formatInfo.ListFont;
                    symbol = formatInfo.ListSymbol;
                    return true;
                }
            }
            return false;
        }

        XUnitPt LeftIndent
        {
            get
            {
                ParagraphFormat format = _paragraph.Format;
                XUnitPt leftIndent = format.LeftIndent.Point;
                if (_isFirstLine)
                {
                    if (format.Values.ListInfo is not null && !format.Values.ListInfo.IsNull())
                    {
                        //if (format.ListInfo.Values.NumberPosition is not null)
                        if (!format.ListInfo.Values.NumberPosition.IsValueNullOrEmpty())
                            return format.ListInfo.NumberPosition.Point;
                        if (format.Values.FirstLineIndent.IsValueNullOrEmpty())
                            return 0;
                    }
                    return leftIndent + _paragraph.Format.FirstLineIndent.Point;
                }
                return leftIndent;
            }
        }

        XUnitPt RightIndent => _paragraph.Format.RightIndent.Point;

        /// <summary>
        /// Formats the paragraph by performing line breaks etc.
        /// </summary>
        /// <param name="area">The area in which to render.</param>
        /// <param name="previousFormatInfo">The format info that was obtained on formatting the same paragraph on a previous area.</param>
        internal override void Format(Area area, FormatInfo? previousFormatInfo)
        {
            _optionalSpaceDecisions.Clear();
            _newLineStartedByBlankWrap = false;
            var formatInfo = ((ParagraphFormatInfo)_renderInfo.FormatInfo);
            if (!InitFormat(area, previousFormatInfo))
            {
                formatInfo._isStarting = false;
                return;
            }
            formatInfo._isEnding = true;

            FormatResult lastResult = FormatResult.Continue;
            while (_currentLeaf != null)
            {
                FormatResult result = FormatElement(_currentLeaf.Current);
                switch (result)
                {
                    case FormatResult.Ignore:
                        _currentLeaf = _currentLeaf.GetNextLeaf();
                        break;

                    case FormatResult.Continue:
                        lastResult = result;
                        _currentLeaf = _currentLeaf.GetNextLeaf();
                        break;

                    case FormatResult.NewLine:
                        lastResult = result;
                        var wrappedAtBlank = !_currentLineEndsWithLineBreak &&
                            _currentLeaf?.Current is Text blank && IsBlank(blank);
                        var rejectedTab = false;
                        if (!_currentLineEndsWithLineBreak && _currentLineWidth != XUnitPt.Zero &&
                            _currentLeaf?.Current is Character { SymbolName: SymbolName.Tab })
                        {
                            var precedingLeaf = _currentLeaf.GetPreviousLeaf();
                            while (precedingLeaf != null &&
                                   (precedingLeaf.Current is BookmarkField || IsBlank(precedingLeaf.Current)))
                                precedingLeaf = precedingLeaf.GetPreviousLeaf();
                            rejectedTab = precedingLeaf?.Current is not Character { SymbolName: SymbolName.Tab };
                        }
                        StoreLineInformation(rejectedTab, wrappedAtBlank);
                        if (rejectedTab)
                            _currentLeaf = _currentLeaf?.GetNextLeaf();
                        // StartNewLine() no longer gives the signal to start a new page. We don’t know the line height yet and therefore can’t decide,
                        // if it will fit on the page. Page fitting is only checked by FormatElement() now, when the height of the new line is known.
                        StartNewLine();
                        _newLineStartedByBlankWrap = wrappedAtBlank;
                        break;
                }
                if (result == FormatResult.NewArea)
                {
                    lastResult = result;
                    formatInfo._isEnding = false;
                    break;
                }
            }
            if (formatInfo.IsEnding && lastResult != FormatResult.NewLine)
                StoreLineInformation();

            if (formatInfo.IsEnding)
                StoreBottomBorderInformation();

            formatInfo.ImageRenderInfos = _imageRenderInfos;
            FinishLayoutInfo();
        }

        /// <summary>
        /// Finishes the layout info by calculating starting and trailing heights.
        /// </summary>
        void FinishLayoutInfo()
        {
            LayoutInfo layoutInfo = _renderInfo.LayoutInfo;
            ParagraphFormat format = _paragraph.Format;
            ParagraphFormatInfo parInfo = (ParagraphFormatInfo)_renderInfo.FormatInfo;
            layoutInfo.MinWidth = _minWidth;
            layoutInfo.KeepTogether = format.KeepTogether;

            if (parInfo.IsComplete)
            {
                int limitOfLines = 1;
                if (parInfo._widowControl)
                    limitOfLines = 3;

                if (parInfo.LineCount <= limitOfLines)
                    layoutInfo.KeepTogether = true;
            }
            if (parInfo.IsStarting)
            {
                layoutInfo.MarginTop = format.SpaceBefore.Point;
                layoutInfo.PageBreakBefore = format.PageBreakBefore;
            }
            else
            {
                layoutInfo.MarginTop = 0;
                layoutInfo.PageBreakBefore = false;
            }

            if (parInfo.IsEnding)
            {
                layoutInfo.MarginBottom = _paragraph.Format.SpaceAfter.Point;
                layoutInfo.KeepWithNext = _paragraph.Format.KeepWithNext;
            }
            else
            {
                layoutInfo.MarginBottom = 0;
                layoutInfo.KeepWithNext = false;
            }
            if (parInfo.LineCount > 0)
            {
                XUnitPt startingHeight = parInfo.GetFirstLineInfo().Vertical.Height;
                if (parInfo._isStarting && _paragraph.Format.WidowControl && parInfo.LineCount >= 2)
                    startingHeight += parInfo.GetLineInfo(1).Vertical.Height;

                layoutInfo.StartingHeight = startingHeight;

                XUnitPt trailingHeight = parInfo.GetLastLineInfo().Vertical.Height;

                if (parInfo.IsEnding && _paragraph.Format.WidowControl && parInfo.LineCount >= 2)
                    trailingHeight += parInfo.GetLineInfo(parInfo.LineCount - 2).Vertical.Height;

                layoutInfo.TrailingHeight = trailingHeight;
            }
        }

        XUnitPt PopSavedBlankWidth()
        {
            XUnitPt width = _savedBlankWidth;
            _savedBlankWidth = 0;
            _savedBlankContractionWidth = 0;
            return width;
        }

        void SaveBlankWidth(XUnitPt blankWidth, XUnitPt contractionWidth)
        {
            _savedBlankWidth = blankWidth;
            _savedBlankContractionWidth = contractionWidth;
        }

        XUnitPt _savedBlankWidth = 0;
        XUnitPt _savedBlankContractionWidth;

        /// <summary>
        /// Processes the elements when formatting.
        /// </summary>
        /// <param name="docObj"></param>
        FormatResult FormatElement(DocumentObject docObj)
        {
            // Check for available space in the area must be made for each element and explicitly for the last paragraph’s element, because in formatting phase,
            // BottomBorderOffset contains the actual last line’s bottom border offset value only for the last paragraph’s object.
            var newVertInfo = CalcVerticalInfo(CurrentFont);
            var fittingRect = GetFittingRectForLine(_currentYPosition, newVertInfo, BottomBorderOffset);
            if (_currentSegmentIndex > 0 && _currentSecondaryRect != null)
                fittingRect = _currentSecondaryRect;
            if (fittingRect != null && fittingRect.Width <= 0 && _currentLineWidth == 0 &&
                _formattingArea is SideExclusionArea exclusions)
            {
                if (exclusions.GetNextFreeY(_currentYPosition, newVertInfo.Height + BottomBorderOffset)
                    is not { } nextY)
                    return FormatResult.NewArea;
                _currentLineGap += nextY - _currentYPosition;
                _currentYPosition = nextY;
                fittingRect = _formattingArea.GetFittingRect(_currentYPosition,
                    newVertInfo.Height + BottomBorderOffset);
                if (fittingRect != null)
                    _currentXPosition = fittingRect.X + LeftIndent;
            }
            if (fittingRect == null)
                return FormatResult.NewArea;

            FormatResult result;
            switch (docObj)
            {
                case Text obj:
                    if (!IsBlankOrSoftHyphen(obj))
                        result = FormatText(obj, fittingRect);
                    else if (IsBlank(obj))
                        result = FormatBlank(fittingRect);
                    else
                        //if (IsSoftHyphen(docObj))
                        result = FormatSoftHyphen();
                    break;

                case Character obj:
                    result = FormatCharacter(obj, fittingRect);
                    break;

                case DateField obj:
                    result = FormatDateField(obj, fittingRect);
                    break;

                case InfoField obj:
                    result = FormatInfoField(obj, fittingRect);
                    break;

                case NumPagesField obj:
                    result = FormatNumPagesField(obj, fittingRect);
                    break;

                case PageField obj:
                    result = FormatPageField(obj, fittingRect);
                    break;

                case SectionField obj:
                    result = FormatSectionField(obj, fittingRect);
                    break;

                case SectionPagesField obj:
                    result = FormatSectionPagesField(obj, fittingRect);
                    break;

                case BookmarkField obj:
                    result = FormatBookmarkField(obj);
                    break;

                case PageRefField obj:
                    result = FormatPageRefField(obj, fittingRect);
                    break;

                case Image obj:
                    result = FormatImage(obj, fittingRect);
                    break;

                default:
                    result = FormatResult.Continue;
                    break;
            }

            if (result == FormatResult.Continue)
                _currentVerticalInfo = newVertInfo;

            return result;
        }

        // ReSharper disable once UnusedParameter.Local
#pragma warning disable IDE0060
        FormatResult FormatImage(Image image, Rectangle fittingRect)
#pragma warning restore IDE0060
        {
            XUnitPt width = CurrentImageRenderInfo?.LayoutInfo.ContentArea.Width ?? NRT.ThrowOnNull<XUnitPt>();
            var effect = GetInlineImageEffectExtent(image);
            return FormatAsWord(width + effect.Left + effect.Right, fittingRect);
        }

        RenderInfo CalcImageRenderInfo(Image image)
        {
            var renderer = Create(_gfx, _documentRenderer, image, _fieldInfos);
            renderer!.Format(new Rectangle(0, 0, double.MaxValue, double.MaxValue), null);

            return renderer.RenderInfo;
        }

        static bool IsPlainText(DocumentObject docObj)
        {
            if (docObj is Text)
                return !IsSoftHyphen(docObj) && !IsBlank(docObj);

            return false;
        }

        static bool IsSymbol(DocumentObject docObj)
        {
            if (docObj is Character)
                return !IsSpaceCharacter(docObj) && !IsTab(docObj) && !IsLineBreak(docObj);

            return false;
        }

        static bool IsSpaceCharacter(DocumentObject docObj)
        {
            if (docObj is Character character)
            {
                switch (character.SymbolName)
                {
                    case SymbolName.Blank:
                    case SymbolName.Em:
                    case SymbolName.Em4:
                    case SymbolName.En:
                        return true;
                }
            }
            return false;
        }

        static bool IsWordLikeElement(DocumentObject docObj)
        {
            if (IsPlainText(docObj))
                return true;

            if (IsRenderedField(docObj))
                return true;

            if (IsSymbol(docObj))
                return true;

            return false;
        }

        FormatResult FormatBookmarkField(BookmarkField bookmarkField)
        {
            if (_fieldInfos is null)
                NRT.ThrowOnNull();

            _fieldInfos.AddBookmark(bookmarkField.Name);

            return FormatResult.Ignore;
        }

        FormatResult FormatPageRefField(PageRefField pageRefField, Rectangle fittingRect)
        {
            _reMeasureLine = true;
            string fieldValue = GetFieldValue(pageRefField);
            return FormatWord(fieldValue, fittingRect);
        }

        FormatResult FormatNumPagesField(NumPagesField numPagesField, Rectangle fittingRect)
        {
            _reMeasureLine = true;
            string fieldValue = GetFieldValue(numPagesField);
            return FormatWord(fieldValue, fittingRect);
        }

        FormatResult FormatPageField(PageField pageField, Rectangle fittingRect)
        {
            _reMeasureLine = true;
            string fieldValue = GetFieldValue(pageField);
            return FormatWord(fieldValue, fittingRect);
        }

        FormatResult FormatSectionField(SectionField sectionField, Rectangle fittingRect)
        {
            _reMeasureLine = true;
            string fieldValue = GetFieldValue(sectionField);
            return FormatWord(fieldValue, fittingRect);
        }

        FormatResult FormatSectionPagesField(SectionPagesField sectionPagesField, Rectangle fittingRect)
        {
            _reMeasureLine = true;
            string fieldValue = GetFieldValue(sectionPagesField);
            return FormatWord(fieldValue, fittingRect);
        }

        /// <summary>
        /// Helper function for formatting word-like elements like text and fields.
        /// </summary>
        FormatResult FormatWord(string word, Rectangle fittingRect)
        {
            XUnitPt wordWidth = MeasureString(word);
            XUnitPt width = GetFittingWordWidth(word, wordWidth);
            if (_phase == Phase.Formatting && word.Contains('\u00a0') &&
                _paragraph.Format.MoveOversizedNoBreakGroupToNextLine &&
                _currentXPosition > StartXPosition + Tolerance &&
                _currentLeaf?.GetPreviousLeaf() is { } previousLeaf && IsBlank(previousLeaf.Current))
            {
                var original = _currentLeaf;
                var next = original.GetNextLeaf();
                var groupWidth = width;
                try
                {
                    while (next?.Current is Text text &&
                           !IsBlankOrSoftHyphen(text) && !text.Content.Contains(' '))
                    {
                        _currentLeaf = next;
                        groupWidth += GetFittingWordWidth(text.Content, MeasureString(text.Content));
                        next = next.GetNextLeaf();
                    }
                }
                finally
                {
                    _currentLeaf = original;
                }
                if (_currentXPosition + groupWidth >
                    fittingRect.X + fittingRect.Width - RightIndent + Tolerance)
                    return FormatResult.NewLine;
            }
            if (_phase == Phase.Formatting && _currentLeaf?.Current is Text &&
                _currentXPosition > StartXPosition + Tolerance)
            {
                var previous = _currentLeaf.GetPreviousLeaf();
                if (previous == null || IsBlank(previous.Current) || IsTab(previous.Current))
                {
                    var original = _currentLeaf;
                    var next = original.GetNextLeaf();
                    var sequenceWidth = width;
                    var hasNonbreakingSpace = word.Contains('\u00a0');
                    try
                    {
                        while (next?.Current is Text text &&
                               !IsBlankOrSoftHyphen(text) && !text.Content.Contains(' '))
                        {
                            _currentLeaf = next;
                            sequenceWidth += GetFittingWordWidth(text.Content, MeasureString(text.Content));
                            if (hasNonbreakingSpace && !text.Content.EndsWith("\u00a0", StringComparison.Ordinal))
                                break;
                            hasNonbreakingSpace = text.Content.Contains('\u00a0');
                            next = next.GetNextLeaf();
                        }
                    }
                    finally
                    {
                        _currentLeaf = original;
                    }
                    var right = fittingRect.X + fittingRect.Width - RightIndent + Tolerance;
                    if (hasNonbreakingSpace && sequenceWidth <= right - StartXPosition &&
                        _currentXPosition + sequenceWidth > right)
                        return FormatResult.NewLine;
                }
            }
            if (_phase == Phase.Formatting && _currentLeaf?.Current is Text &&
                word.Length > 0 && _currentXPosition > StartXPosition + Tolerance)
            {
                var previous = _currentLeaf.GetPreviousLeaf();
                if (previous == null || IsBlank(previous.Current) || IsTab(previous.Current))
                {
                    var original = _currentLeaf;
                    var next = original.GetNextLeaf();
                    var joinedWidth = width;
                    var lastText = word;
                    var joined = false;
                    double overhangAllowance = 0;
                    try
                    {
                        while (next?.Current is Text text && text.Content.Length > 0 &&
                               !IsBlankOrSoftHyphen(text) && !text.Content.Contains(' ') &&
                               !text.HasExplicitBreakBefore &&
                               IsJoinedWordCharacter(lastText, true) && IsJoinedWordCharacter(text.Content, false))
                        {
                            joined = true;
                            _currentLeaf = next;
                            joinedWidth += GetFittingWordWidth(text.Content, MeasureString(text.Content));
                            lastText = text.Content;
                            next = next.GetNextLeaf();
                        }
                        if (joined && HasLineEndOverhang && GetLineEndOverhang(
                                ((Text)_currentLeaf.Current).Content, CurrentFont) is double allowance &&
                            allowance > 0 && IsFinite(allowance))
                            overhangAllowance = allowance;
                    }
                    finally
                    {
                        _currentLeaf = original;
                    }
                    if (joined)
                    {
                        var right = fittingRect.X + fittingRect.Width - RightIndent + Tolerance;
                        var followingLeaf = SkipContractionPolicyLeaves(next);
                        var shrinkFactor = followingLeaf == null
                            ? _documentRenderer.JustifiedSpaceShrinkFactorForParagraph?.Invoke(_paragraph)
                            : _documentRenderer.JustifiedNonterminalSpaceShrinkFactorForParagraph?.Invoke(_paragraph);
                        var spaceWidth = _currentContractionSpaceWidth + _savedBlankContractionWidth;
                        var shrinkAllowance = shrinkFactor is > 0 and < 1 &&
                            _paragraph.Format.Alignment == ParagraphAlignment.Justify && spaceWidth > 0
                            ? (1 - shrinkFactor.Value) * spaceWidth : 0;
                        shrinkAllowance = LimitBlankContraction(shrinkAllowance);
                        shrinkAllowance = CapWordContraction(shrinkAllowance, width);
                        var fitAllowance = UseAdditiveLineEndOverhang(next, followingLeaf == null)
                            ? (double)shrinkAllowance + overhangAllowance
                            : Math.Max((double)shrinkAllowance, overhangAllowance);
                        if (joinedWidth <= right - StartXPosition + overhangAllowance &&
                            _currentXPosition + joinedWidth > right + fitAllowance)
                        {
                            if (!TryMoveToSecondary(joinedWidth))
                                return FormatResult.NewLine;
                            fittingRect = _currentSecondaryRect!;
                        }
                    }
                }
            }
            if (_phase == Phase.Formatting && HasLineEndOverhang &&
                _currentXPosition > StartXPosition + Tolerance &&
                _currentLeaf?.GetNextLeaf() is { Current: Text { Content: { Length: 1 } nextText } } nextLeaf &&
                !IsBlank(nextLeaf.Current) && !IsNonpaintingLeaf(nextLeaf.Current))
            {
                var original = _currentLeaf;
                XUnitPt nextWidth;
                double? allowance = null;
                double contractionAllowance = 0;
                var right = fittingRect.X + fittingRect.Width - RightIndent + Tolerance;
                try
                {
                    _currentLeaf = nextLeaf;
                    nextWidth = GetFittingWordWidth(nextText, MeasureString(nextText));
                    if (_currentXPosition + width + nextWidth > right)
                    {
                        allowance = GetLineEndOverhang(nextText, CurrentFont);
                        if (allowance is > 0 && IsFinite(allowance.Value) &&
                            UseAdditiveLineEndOverhang(nextLeaf.GetNextLeaf()))
                        {
                            var following = SkipContractionPolicyLeaves(nextLeaf.GetNextLeaf());
                            var shrinkFactor = following == null
                                ? _documentRenderer.JustifiedSpaceShrinkFactorForParagraph?.Invoke(_paragraph)
                                : _documentRenderer.JustifiedNonterminalSpaceShrinkFactorForParagraph?.Invoke(_paragraph);
                            var spaceWidth = _currentContractionSpaceWidth + _savedBlankContractionWidth;
                            var shrinkAllowance = shrinkFactor is > 0 and < 1 &&
                                _paragraph.Format.Alignment == ParagraphAlignment.Justify && spaceWidth > 0
                                ? (1 - shrinkFactor.Value) * spaceWidth : 0;
                            shrinkAllowance = LimitBlankContraction(shrinkAllowance);
                            contractionAllowance = CapWordContraction(shrinkAllowance, width + nextWidth);
                        }
                    }
                }
                finally
                {
                    _currentLeaf = original;
                }
                var fitAllowance = allowance.GetValueOrDefault() + contractionAllowance;
                if (allowance is > 0 && !double.IsInfinity(allowance.Value) &&
                    _currentXPosition + width + nextWidth - fitAllowance > right &&
                    width + nextWidth - fitAllowance <= right - StartXPosition)
                    return FormatResult.NewLine;
            }
            if (_phase == Phase.Formatting && HasLineEndOverhang &&
                _currentXPosition > StartXPosition + Tolerance)
            {
                var right = fittingRect.X + fittingRect.Width - RightIndent + Tolerance;
                if (UseAdditiveLineEndOverhang(_currentLeaf?.GetNextLeaf()) &&
                    _currentXPosition + width > right &&
                    GetLineEndOverhang(word, CurrentFont) is double additiveAllowance &&
                    additiveAllowance > 0 && IsFinite(additiveAllowance))
                {
                    var additiveResult = FormatAsWord(width, fittingRect, additiveAllowance);
                    if (additiveResult == FormatResult.NewLine)
                        _savedWordWidth = wordWidth;
                    return additiveResult;
                }
                if (_currentXPosition + width > right &&
                    GetLineEndOverhang(word, CurrentFont) is double allowance &&
                    allowance > 0 && !double.IsInfinity(allowance) &&
                    _currentXPosition + width - allowance <= right)
                {
                    _currentClippedTrailingWidth += width - (right - _currentXPosition);
                    width = right - _currentXPosition;
                }
            }
            var result = FormatAsWord(width, fittingRect);
            if (result == FormatResult.NewLine)
                _savedWordWidth = wordWidth;
            return result;
        }

        XUnitPt _savedWordWidth = 0;

        static bool IsJoinedWordCharacter(string text, bool atEnd)
        {
            var index = atEnd ? text.Length - 1 : 0;
            if (atEnd && index > 0 && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1]))
                index--;
            var codePoint = char.IsHighSurrogate(text[index]) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1])
                ? char.ConvertToUtf32(text[index], text[index + 1]) : text[index];
            // CJK ideographs in the BMP and supplementary ideographic planes allow character breaks.
            if (codePoint is >= 0x2E80 and <= 0x9FFF or >= 0xAC00 and <= 0xD7FF or
                >= 0xF900 and <= 0xFAFF or >= 0x20000 and <= 0x3FFFF)
                return false;
            // Preserve a surrogate pair split across runs; classify complete pairs as code points.
            if (codePoint is >= 0xD800 and <= 0xDFFF)
                return true;
            var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(text, index);
            return category is System.Globalization.UnicodeCategory.UppercaseLetter or
                System.Globalization.UnicodeCategory.LowercaseLetter or
                System.Globalization.UnicodeCategory.TitlecaseLetter or
                System.Globalization.UnicodeCategory.ModifierLetter or
                System.Globalization.UnicodeCategory.OtherLetter or
                System.Globalization.UnicodeCategory.DecimalDigitNumber or
                System.Globalization.UnicodeCategory.NonSpacingMark or
                System.Globalization.UnicodeCategory.SpacingCombiningMark;
        }

        /// <summary>
        /// When rendering a justified paragraph, only the part after the last tab stop needs remeasuring.
        /// </summary>
        bool IgnoreHorizontalGrowth =>
            _phase == Phase.Rendering && _paragraph.Format.Alignment == ParagraphAlignment.Justify &&
            !_lastTabPassed;

        ParagraphIterator? SkipContractionPolicyLeaves(ParagraphIterator? leaf)
        {
            // Bookmarks and other nonpainting leaves do not make the preceding word nonfinal.
            while (leaf != null && (IsBlank(leaf.Current) || IsNonpaintingLeaf(leaf.Current)))
                leaf = leaf.GetNextLeaf();
            if (leaf != null && (IsLineBreak(leaf.Current) || IsTab(leaf.Current)))
            {
                // A terminal break or tab leaves this as the final line, including
                // a tab rejected at the right edge and discarded by Format().
                // Trailing bookmarks/ignored blanks preserve that behavior.
                var afterBreak = leaf.GetNextLeaf();
                while (afterBreak != null &&
                       (afterBreak.Current is BookmarkField || IsBlank(afterBreak.Current)))
                    afterBreak = afterBreak.GetNextLeaf();
                if (afterBreak == null)
                    return null;
            }
            return leaf;
        }

        bool UseAdditiveLineEndOverhang(ParagraphIterator? next, bool? terminal = null)
        {
            while (next != null && IsNonpaintingLeaf(next.Current))
                next = next.GetNextLeaf();
            var following = next;
            while (following != null && (IsBlank(following.Current) || IsNonpaintingLeaf(following.Current)))
                following = following.GetNextLeaf();
            // Preserve the joined-word guard's existing terminal break/tab classification.
            return _documentRenderer.AdditiveTerminalLineEndOverhang && (terminal ?? following == null) ||
                _documentRenderer.AdditiveNonterminalLineEndOverhang &&
                next != null && IsBlank(next.Current) && SkipContractionPolicyLeaves(following) != null;
        }

        bool HasLineEndOverhang => _documentRenderer.LineEndOverhang != null ||
            _documentRenderer.TerminalLineEndOverhangWithSpaceCount != null;

        double? GetLineEndOverhang(string word, XFont font)
        {
            var following = _currentLeaf?.GetNextLeaf();
            while (following != null && (IsBlank(following.Current) || IsNonpaintingLeaf(following.Current)))
                following = following.GetNextLeaf();
            var established = _documentRenderer.LineEndOverhang?.Invoke(_paragraph, word, font);
            if (following != null ||
                _documentRenderer.TerminalLineEndOverhangWithSpaceCount is not { } withSpaceCount)
                return established;
            var eligibleSpaces = _currentBlankCount + (_savedBlankWidth > 0 ? 1 : 0);
            var lineAllowance = withSpaceCount(_paragraph, word, font, eligibleSpaces);
            return lineAllowance is { } allowance && allowance > 0 && IsFinite(allowance)
                ? Math.Max(established is > 0 && IsFinite(established.Value) ? established.Value : 0, allowance)
                : established;
        }

        FormatResult FormatAsWord(XUnitPt width, Rectangle fittingRect, double overhangAllowance = 0)
        {
            _savedWordWidth = width;

            var right = fittingRect.X + fittingRect.Width - RightIndent + Tolerance;
            var overrun = _currentXPosition + width - right;
            var nextLeaf = SkipContractionPolicyLeaves(_currentLeaf?.GetNextLeaf());
            var shrinkFactor = nextLeaf == null
                ? _documentRenderer.JustifiedSpaceShrinkFactorForParagraph?.Invoke(_paragraph)
                : _documentRenderer.JustifiedNonterminalSpaceShrinkFactorForParagraph?.Invoke(_paragraph);
            var spaceWidth = _currentContractionSpaceWidth + _savedBlankContractionWidth;
            var shrinkAllowance = shrinkFactor is > 0 and < 1 &&
                _paragraph.Format.Alignment == ParagraphAlignment.Justify &&
                spaceWidth > 0
                ? (1 - shrinkFactor.Value) * spaceWidth
                : 0;
            shrinkAllowance = LimitBlankContraction(shrinkAllowance);
            shrinkAllowance = CapWordContraction(shrinkAllowance, width);
            if (overrun > 0)
            {
                if (!TryMoveToSecondary(width) && overrun > shrinkAllowance + overhangAllowance)
                    return FormatResult.NewLine;
            }

            _currentXPosition += width;
            // For Tabs in justified context.
            if (!IgnoreHorizontalGrowth)
                _currentWordsWidth += width;
            if (_savedBlankWidth > 0)
            {
                // For Tabs in justified context.
                if (!IgnoreHorizontalGrowth)
                {
                    ++_currentBlankCount;
                    _currentContractionSpaceWidth += _savedBlankContractionWidth;
                    _currentMinimumBlankWidth = Math.Min(_currentMinimumBlankWidth, _savedBlankWidth.Point);
                }
            }
            // For Tabs in justified context.
            if (!IgnoreHorizontalGrowth)
                _currentLineWidth += width + PopSavedBlankWidth();
            _minWidth = Math.Max(_minWidth, width);
            return FormatResult.Continue;
        }

        XUnitPt LimitBlankContraction(XUnitPt allowance)
        {
            if (_documentRenderer.TextTrailingAdvance == null || allowance <= 0)
                return allowance;
            var count = _currentBlankCount + (_savedBlankWidth > 0 ? 1 : 0);
            var minimum = _savedBlankWidth > 0
                ? Math.Min(_currentMinimumBlankWidth, _savedBlankWidth.Point)
                : _currentMinimumBlankWidth;
            // Justification distributes contraction equally, so no individual
            // effective blank may lose more than its own remaining width.
            return Math.Min(allowance.Point, minimum * count);
        }

        XUnitPt CapWordContraction(XUnitPt allowance, XUnitPt fragmentWidth)
        {
            if (allowance <= 0 ||
                (_documentRenderer.JustifiedTerminalWordOverrunLimitForParagraph == null &&
                 _documentRenderer.JustifiedNonterminalWordOverrunLimitForParagraph == null))
                return allowance;

            var original = _currentLeaf;
            var first = original;
            var wordWidth = fragmentWidth;
            var following = original?.GetNextLeaf();
            var spaceWidth = MeasureString(" ", includeTrailingAdvance: false);
            try
            {
                // Use the same boundaries as the joined-word admission guard.
                while (first?.GetPreviousLeaf() is { } previous && Joins(first, previous))
                    first = previous;
                if (first?.Current is Text)
                {
                    wordWidth = 0;
                    for (var leaf = first; leaf != null;)
                    {
                        _currentLeaf = leaf;
                        wordWidth += MeasureString(((Text)leaf.Current).Content);
                        following = leaf.GetNextLeaf();
                        if (following == null || !Joins(following, leaf))
                        {
                            spaceWidth = MeasureString(" ", includeTrailingAdvance: false);
                            break;
                        }
                        leaf = following;
                    }
                }
            }
            finally
            {
                _currentLeaf = original;
            }
            var limit = SkipContractionPolicyLeaves(following) == null
                ? _documentRenderer.JustifiedTerminalWordOverrunLimitForParagraph?.Invoke(_paragraph)
                : _documentRenderer.JustifiedNonterminalWordOverrunLimitForParagraph?.Invoke(_paragraph);
            if (limit is not { } cap || !IsFinite(cap.WordWidthFraction) ||
                !IsFinite(cap.SpaceWidthFloor) || cap.WordWidthFraction < 0 || cap.SpaceWidthFloor < 0)
                return allowance;
            return Math.Min(allowance, Math.Max(wordWidth * cap.WordWidthFraction,
                spaceWidth * cap.SpaceWidthFloor));

            static bool Joins(ParagraphIterator current, ParagraphIterator previous) =>
                current.Current is Text { Content.Length: > 0 } text &&
                previous.Current is Text { Content.Length: > 0 } prior &&
                !text.HasExplicitBreakBefore && !IsBlankOrSoftHyphen(text) &&
                !IsBlankOrSoftHyphen(prior) && !text.Content.Contains(' ') && !prior.Content.Contains(' ') &&
                IsJoinedWordCharacter(prior.Content, true) && IsJoinedWordCharacter(text.Content, false);
        }

        bool TryMoveToSecondary(XUnitPt width)
        {
            if (_formattingArea is not SideExclusionArea exclusion)
                return false;

            var intervals = exclusion.GetIntervals(_currentYPosition, _currentVerticalInfo.Height);
            var nextIndex = _currentSegmentIndex + 1;
            while (nextIndex < intervals.Count &&
                width > intervals[nextIndex].Width - RightIndent + Tolerance)
                nextIndex++;
            if (nextIndex >= intervals.Count)
                return false;
            var secondary = intervals[nextIndex];

            _currentSecondaryRect = secondary;
            var current = intervals[_currentSegmentIndex];
            var needsContraction = _currentContractionSpaceWidth > 0 &&
                _documentRenderer.JustifiedNonterminalSpaceShrinkFactorForParagraph?.Invoke(_paragraph) is > 0 and < 1 &&
                _currentXPosition > current.X + current.Width - RightIndent + Tolerance;
            _currentSegmentTransitions.Add(new LineSegmentInfo(secondary, _currentLeaf,
                _currentWordsWidth, _currentBlankCount, needsContraction, _currentClippedTrailingWidth));
            _savedBlankWidth = 0;
            _savedBlankContractionWidth = 0;
            _currentContractionSpaceWidth = 0;
            _currentMinimumBlankWidth = double.PositiveInfinity;
            _currentXPosition = secondary.X;
            _currentSegmentIndex = nextIndex;
            return true;
        }

        FormatResult FormatDateField(DateField dateField, Rectangle fittingRect)
        {
            _reMeasureLine = true;
            // Use a fixed date to measure the dimensions required.
            var estimatedFieldValue = FormatDateTimeForField(new DateTimeOffset(2999, 08, 28, 22, 28, 28, TimeSpan.Zero), dateField);
            //var estimatedFieldValue = FormatDateTimeForField(DateTimeOffset.Now, dateField);
            return FormatWord(estimatedFieldValue, fittingRect);
        }

        FormatResult FormatInfoField(InfoField infoField, Rectangle fittingRect)
        {
            string fieldValue = GetDocumentInfo(infoField.Name);
            if (fieldValue == "")
                return FormatResult.Continue;

            return FormatWord(fieldValue, fittingRect);

        }

        string GetDocumentInfo(string name)
        {
            Debug.Assert(_paragraph.Document != null, "_paragraph.Document != null");
            return name.ToLower() switch
            {
                "title" => _paragraph.Document.Info.Title,
                "author" => _paragraph.Document.Info.Author,
                "keywords" => _paragraph.Document.Info.Keywords,
                "subject" => _paragraph.Document.Info.Subject,
                _ => ""
            };
        }

        Area GetShadingArea()
        {
            var contentArea = _renderInfo.LayoutInfo.ContentArea;
            var format = _paragraph.Format;
            XUnitPt left = contentArea.X;
            left += format.LeftIndent.Point;
            if (format.FirstLineIndent < 0)
                left += format.FirstLineIndent.Point;

            XUnitPt top = contentArea.Y;
            XUnitPt bottom = contentArea.Y + contentArea.Height;
            XUnitPt right = contentArea.X + contentArea.Width;
            right -= format.RightIndent.Point;

            if (_paragraph.Format.Values.Borders is not null && !_paragraph.Format.Values.Borders.IsNull())
            {
                Borders borders = format.Borders;
                var bordersRenderer = new BordersRenderer(borders, _gfx);

                if (_renderInfo.FormatInfo.IsStarting)
                    top += bordersRenderer.GetWidth(BorderType.Top);
                if (_renderInfo.FormatInfo.IsEnding)
                    bottom -= bordersRenderer.GetWidth(BorderType.Bottom);

                left -= borders.DistanceFromLeft.Point;
                right += borders.DistanceFromRight.Point;
            }
            return new Rectangle(left, top, right - left, bottom - top);
        }

        void RenderShading()
        {
            if (_paragraph.Format.Values.Shading is null || _paragraph.Format.Values.Shading.IsNull())
                return;

            var shadingRenderer = new ShadingRenderer(_gfx, _paragraph.Format.Shading);
            var area = GetShadingArea();

            shadingRenderer.Render(area.X, area.Y, area.Width, area.Height);
        }

        void RenderBorders()
        {
            if (_paragraph.Format.Values.Borders is null || _paragraph.Format.Values.Borders.IsNull())
                return;

            var shadingArea = GetShadingArea();
            XUnitPt left = shadingArea.X;
            XUnitPt top = shadingArea.Y;
            XUnitPt bottom = shadingArea.Y + shadingArea.Height;
            XUnitPt right = shadingArea.X + shadingArea.Width;

            var borders = _paragraph.Format.Borders;
            var bordersRenderer = new BordersRenderer(borders, _gfx);
            XUnitPt borderWidth = bordersRenderer.GetWidth(BorderType.Left);
            if (borderWidth > 0)
            {
                left -= borderWidth;
                bordersRenderer.RenderVertically(BorderType.Left, left, top, bottom - top);
            }

            borderWidth = bordersRenderer.GetWidth(BorderType.Right);
            if (borderWidth > 0)
            {
                bordersRenderer.RenderVertically(BorderType.Right, right, top, bottom - top);
                right += borderWidth;
            }

            borderWidth = bordersRenderer.GetWidth(BorderType.Top);
            if (_renderInfo.FormatInfo.IsStarting && borderWidth > 0)
            {
                top -= borderWidth;
                bordersRenderer.RenderHorizontally(BorderType.Top, left, top, right - left);
            }

            borderWidth = bordersRenderer.GetWidth(BorderType.Bottom);
            if (_renderInfo.FormatInfo.IsEnding && borderWidth > 0)
            {
                bordersRenderer.RenderHorizontally(BorderType.Bottom, left, bottom, right - left);
            }
        }

        XUnitPt MeasureString(string word, bool includeTrailingAdvance = true)
        {
            var xFont = CurrentFont;
            XUnitPt width = _gfx.MeasureString(word, xFont, StringFormat).Width * GetTextHorizontalScale();
            var font = CurrentDomFont;

            if (font.Subscript || font.Superscript)
                width *= FontHandler.GetSubSuperScaling(xFont);

            if (GetTextPairAdvances(word, xFont) is { } pairAdvances)
                width += pairAdvances.Sum();
            width += GetTrailingGlyphKerning(word, xFont);
            if (_phase == Phase.Rendering)
                width += GetRenderOnlyTrailingGlyphKerning(word, xFont);

            // Prefix and synthetic measurements do not own a leaf's outgoing advance.
            if (includeTrailingAdvance && _currentLeaf?.Current is Text text && text.Content == word)
                width = AddTextTrailingAdvance(width);

            if (_phase == Phase.Formatting)
            {
                var adjustment = _documentRenderer.TextWidthAdjustment?.Invoke(_paragraph, word, xFont);
                if (adjustment is { } value && !double.IsNaN(value) && !double.IsInfinity(value) &&
                    width.Point + value > 0 && !double.IsInfinity(width.Point + value))
                    width += value;
            }

            return width;
        }

        double[] GetJustifiedBlankAdvances(LineInfo lineInfo, out List<double>[] selections)
        {
            var transitions = lineInfo.SegmentTransitions;
            var advances = new double[(transitions?.Count ?? 0) + 1];
            selections = new List<double>[advances.Length];
            for (var index = 0; index < selections.Length; index++)
                selections[index] = [];
            var segment = 0;
            var afterLastTab = lineInfo.LastTab == null;
            var savedLeaf = _currentLeaf;
            try
            {
                for (var leaf = lineInfo.StartIter; leaf != null; leaf = leaf.GetNextLeaf())
                {
                    _currentLeaf = leaf;
                    var segmentStart = transitions is { Count: > 0 } && segment < transitions.Count &&
                        transitions[segment].StartIter?.Current == leaf.Current;
                    if (segmentStart)
                        segment++;
                    if (leaf.Current == lineInfo.LastTab)
                        afterLastTab = true;
                    if (afterLastTab && !segmentStart && IsBlank(leaf.Current) && !IgnoreBlank())
                    {
                        var natural = MeasureString(" ", includeTrailingAdvance: false);
                        var selected = AddTextTrailingAdvance(natural);
                        // A collapsed blank was not included in the fitted blank count.
                        if (selected > 0)
                        {
                            var adjustment = (selected - natural).Point;
                            advances[segment] += adjustment;
                            selections[segment].Add(adjustment);
                        }
                    }
                    if (leaf.Current == lineInfo.EndIter?.Current)
                        break;
                }
            }
            finally
            {
                _currentLeaf = savedLeaf;
            }
            foreach (var selection in selections)
                selection.Sort();
            return advances;
        }

        IReadOnlyList<double>? _currentLineBlankAdvances;

        double GetJustifiedBlankDistance(double baseWidth)
        {
            var adjustment = GetBlankTrailingAdvance();
            if (_currentLineBlankAdvances is not { Count: > 0 } advances)
                return baseWidth + adjustment;
            var count = advances.Count;
            var remainingAdvance = advances.Sum();
            var available = Math.Max(0, baseWidth * count + remainingAdvance);
            // Equal spacing may be smaller than a mixed-font blank's natural
            // width. Freeze any reversed gap at zero and redistribute its deficit
            // across the remaining gaps, preserving the interval's total width.
            foreach (var advance in advances)
            {
                if (baseWidth + advance >= 0)
                    break;
                remainingAdvance -= advance;
                if (--count == 0)
                    return 0;
                baseWidth = (available - remainingAdvance) / count;
            }
            return Math.Max(0, baseWidth + adjustment);
        }

        double GetBlankTrailingAdvance()
        {
            var natural = MeasureString(" ", includeTrailingAdvance: false);
            return (AddTextTrailingAdvance(natural) - natural).Point;
        }

        XUnitPt AddTextTrailingAdvance(XUnitPt width)
            => width + Math.Max(GetTextTrailingAdvance(), -width.Point);

        double GetTextTrailingAdvance()
        {
            if (_currentLeaf?.Current is not Text text ||
                _documentRenderer.TextTrailingAdvance?.Invoke(text.Tag) is not double advance ||
                advance == 0 || !IsFinite(advance))
                return 0;
            var next = _currentLeaf.GetNextLeaf();
            while (next?.Current is ParagraphElements { Count: 0 })
                next = next.GetNextLeaf();
            if (next != null && next.Current is not Text and not Image and not
                    Character { SymbolName: SymbolName.LineBreak })
                return 0;
            return advance;
        }

        double GetTrailingGlyphKerning(string word, XFont xFont)
        {
            if (word.Length < 2 || char.IsLowSurrogate(word[^1]))
                return 0;
            var taggedAdjustment = _currentLeaf?.Current is Text text
                ? _documentRenderer.TrailingGlyphKerningWithTextTag?.Invoke(
                    _paragraph, text.Tag, word, xFont) : null;
            if ((taggedAdjustment ?? _documentRenderer.TrailingGlyphKerning?.Invoke(
                    _paragraph, word, xFont)) is not double adjustment || !IsFinite(adjustment))
                return 0;
            var font = CurrentDomFont;
            return font.Subscript || font.Superscript
                ? adjustment * FontHandler.GetSubSuperScaling(xFont) : adjustment;
        }

        double GetRenderOnlyTrailingGlyphKerning(string word, XFont xFont)
        {
            if (_phase != Phase.Rendering || word.Length < 2 || char.IsLowSurrogate(word[^1]) ||
                _documentRenderer.RenderOnlyTrailingGlyphKerning?.Invoke(_paragraph, word, xFont)
                    is not double adjustment || !IsFinite(adjustment))
                return 0;
            var font = CurrentDomFont;
            return font.Subscript || font.Superscript
                ? adjustment * FontHandler.GetSubSuperScaling(xFont) : adjustment;
        }

        double GetLeadingKerningAfterBlank(string word)
        {
            if (word.Length == 0 || _currentXPosition <= StartXPosition + Tolerance ||
                _currentLeaf?.GetPreviousLeaf() is not { } previous || !IsBlank(previous.Current))
                return 0;
            var taggedAdjustment = _currentLeaf.Current is Text text
                ? _documentRenderer.LeadingKerningAfterBlankWithTextTag?.Invoke(
                    _paragraph, text.Tag, word, CurrentFont) : null;
            var adjustment = taggedAdjustment ??
                (_phase == Phase.Formatting
                    ? _documentRenderer.FitOnlyLeadingKerningAfterBlank?.Invoke(_paragraph, word, CurrentFont)
                    : null) ??
                _documentRenderer.LeadingKerningAfterBlank?.Invoke(_paragraph, word, CurrentFont);
            return adjustment is double value && IsFinite(value) ? value : 0;
        }

        XUnitPt GetFittingWordWidth(string word, XUnitPt measuredWidth)
        {
            var boundary = _phase == Phase.Formatting && _currentXPosition > StartXPosition + Tolerance
                ? GetTextBoundaryKerning(word, _documentRenderer.FitOnlyTextBoundaryKerning) : 0;
            return Math.Max(0, measuredWidth.Point + boundary) + GetLeadingKerningAfterBlank(word);
        }

        double GetRenderOnlyTextBoundaryKerning(string word)
            => _phase == Phase.Rendering && _hasTextBoundaryPredecessor
                ? GetTextBoundaryKerning(word, _documentRenderer.RenderOnlyTextBoundaryKerning) : 0;

        double GetTextBoundaryKerning(string word,
            Func<Paragraph, object?, object?, char, char, XFont, double?>? callback)
        {
            if (word.Length == 0 || callback is null ||
                _currentLeaf?.GetPreviousLeaf() is not { } previous ||
                _currentLeaf.Current is not Text { Tag: { } tag })
                return 0;
            var prior = previous.Current;
            var priorChar = prior switch
            {
                Text { Content: { Length: > 0 } text } => text[^1],
                Character { SymbolName: SymbolName.Blank, Count: > 0 } => ' ',
                _ => '\0',
            };
            if (priorChar == '\0' || char.IsSurrogate(priorChar) || char.IsSurrogate(word[0]))
                return 0;
            var font = CurrentFont;
            var domFont = CurrentDomFont;
            var saved = _currentLeaf;
            XFont priorFont;
            Font priorDomFont;
            try
            {
                _currentLeaf = previous;
                priorFont = CurrentFont;
                priorDomFont = CurrentDomFont;
            }
            finally { _currentLeaf = saved; }
            if (priorFont.FontFamily.Name != font.FontFamily.Name ||
                priorFont.Size != font.Size || priorFont.Style != font.Style ||
                priorDomFont.Subscript != domFont.Subscript || priorDomFont.Superscript != domFont.Superscript)
                return 0;
            var adjustment = callback(_paragraph, prior.Tag, tag, priorChar, word[0], font);
            return adjustment is { } value && IsFinite(value) ? value : 0;
        }

        XUnitPt GetSpaceWidth(Character character)
        {
            XUnitPt width = character.SymbolName switch
            {
                SymbolName.Blank => MeasureString(" "),
                SymbolName.Em => MeasureString("m"),
                SymbolName.Em4 => 0.25 * MeasureString("m"),
                SymbolName.En => MeasureString("n"),
                _ => 0
            };
            width *= character.Count;
            var measured = _documentRenderer.SpaceWidthFromTextMetrics?.Invoke(
                character.Tag, width.Point, character.Count);
            return measured is { } value && IsFinite(value)
                ? XUnitPt.FromPoint(Math.Max(0, value)) : width;
        }

        void RenderListSymbol()
        {
            if (GetListSymbol(out var symbol, out var font))
            {
                var brush = FontHandler.FontColorToXBrush(_paragraph.Format.Font);
                _gfx.DrawString(symbol, font, brush, CurrentPaintXPosition, CurrentBaselinePosition);
                _currentXPosition += _gfx.MeasureString(symbol, font, StringFormat).Width;
                var tabOffset = NextTabOffset();
                _currentXPosition += tabOffset.Offset;
                _lastTabPosition = _currentXPosition;
            }
        }

        void FormatListSymbol()
        {
            if (GetListSymbol(out var symbol, out var font))
            {
                _currentVerticalInfo = CalcVerticalInfo(font, true);
                _currentXPosition += _gfx.MeasureString(symbol, font, StringFormat).Width;
                FormatTab();
            }
        }

        FormatResult FormatSpace(Character character, Rectangle fittingRect)
        {
            XUnitPt width = GetSpaceWidth(character);
            if (_phase == Phase.Rendering)
                return _collapsedSpacesOnLine.Contains(character)
                    ? FormatResult.Ignore : FormatAsWord(width, fittingRect);
            if (_optionalSpaceDecisions.TryGetValue(character, out var alreadyCollapsed))
                return alreadyCollapsed ? FormatResult.Ignore : FormatAsWord(width, fittingRect);
            if (_documentRenderer.CollapsibleSpaceBeforeWrap?.Invoke(_paragraph, character) == true)
            {
                var spaces = new List<(ParagraphIterator Leaf, Character? Space,
                    bool Selected, XUnitPt Width)>
                {
                    (_currentLeaf!, character, true, width)
                };
                var following = _currentLeaf?.GetNextLeaf();
                while (following != null)
                {
                    if (IsNonpaintingLeaf(following.Current))
                    {
                        following = following.GetNextLeaf();
                        continue;
                    }
                    if (IsBlank(following.Current))
                    {
                        spaces.Add((following, null, false, 0));
                        following = following.GetNextLeaf();
                        continue;
                    }
                    if (following.Current is not Character nextSpace ||
                        !IsSpaceCharacter(nextSpace))
                        break;
                    var selected = _documentRenderer.CollapsibleSpaceBeforeWrap(_paragraph, nextSpace);
                    var savedLeaf = _currentLeaf;
                    try
                    {
                        _currentLeaf = following;
                        spaces.Add((following, nextSpace, selected, GetSpaceWidth(nextSpace)));
                    }
                    finally
                    {
                        _currentLeaf = savedLeaf;
                    }
                    following = following.GetNextLeaf();
                }
                var collapse = following != null &&
                    TryGetFollowingWord(following.Current, out var word) &&
                    (SelectedSpaceStartedWrappedLine() ||
                     FollowingWordWrapsAfterSpaces(spaces, following, word));
                foreach (var (_, space, selected, _) in spaces)
                {
                    if (space == null)
                        continue;
                    _optionalSpaceDecisions[space] = selected && collapse;
                    if (selected && collapse)
                        _collapsedSpacesOnLine.Add(space);
                    else
                        _collapsedSpacesOnLine.Remove(space);
                }
                if (collapse)
                    return FormatResult.Ignore;
            }
            return FormatAsWord(width, fittingRect);
        }

        bool IsNonpaintingLeaf(DocumentObject element) =>
            element is BookmarkField or Text { Content.Length: 0 } or
                ParagraphElements { Count: 0 } ||
            IsSoftHyphen(element) ||
            element is InfoField info && GetDocumentInfo(info.Name).Length == 0;

        bool SelectedSpaceStartedWrappedLine()
        {
            return _newLineStartedByBlankWrap && _currentLineWidth == 0;
        }

        bool TryGetFollowingWord(DocumentObject element, out string word)
        {
            word = element switch
            {
                Text text => text.Content,
                DateField date => FormatDateTimeForField(
                    new DateTimeOffset(2999, 08, 28, 22, 28, 28, TimeSpan.Zero), date),
                InfoField info => GetDocumentInfo(info.Name),
                NumericFieldBase field => GetFieldValue(field),
                Character symbol when IsSymbol(symbol) => GetSymbol(symbol),
                _ => ""
            };
            return word.Length > 0;
        }

        bool FollowingWordWrapsAfterSpaces(
            List<(ParagraphIterator Leaf, Character? Space, bool Selected, XUnitPt Width)> spaces,
            ParagraphIterator following, string word)
        {
            var savedLeaf = _currentLeaf;
            var savedX = _currentXPosition;
            var savedLineWidth = _currentLineWidth;
            var savedWordsWidth = _currentWordsWidth;
            var savedBlankCount = _currentBlankCount;
            var savedContractionSpaceWidth = _currentContractionSpaceWidth;
            var savedMinimumBlankWidth = _currentMinimumBlankWidth;
            var savedBlankWidth = _savedBlankWidth;
            var savedBlankContractionWidth = _savedBlankContractionWidth;
            var savedClippedWidth = _currentClippedTrailingWidth;
            var savedWordWidth = _savedWordWidth;
            var savedMinWidth = _minWidth;
            var savedSecondaryRect = _currentSecondaryRect;
            var savedSegmentIndex = _currentSegmentIndex;
            var savedTransitionCount = _currentSegmentTransitions.Count;
            var savedVerticalInfo = _currentVerticalInfo;
            var savedNaturalFontHeight = _currentLineNaturalFontHeight;
            var exclusion = _formattingArea as SideExclusionArea;
            var savedExclusionFontHeight = exclusion?.NaturalFontHeight;
            var savedBlankWidthCache = _blankWidth;
            var savedLastFont = _lastFont;
            try
            {
                // Use the same line formatter as the real word, including overhang,
                // justified shrink and the available float segments.
                foreach (var (leaf, explicitSpace, _, spaceWidth) in spaces)
                {
                    _currentLeaf = leaf;
                    var spaceVerticalInfo = CalcVerticalInfo(CurrentFont);
                    var spaceRect = _currentSegmentIndex > 0 && _currentSecondaryRect != null
                        ? _currentSecondaryRect
                        : _formattingArea.GetFittingRect(_currentYPosition,
                            spaceVerticalInfo.Height + BottomBorderOffset);
                    if (spaceRect == null)
                        return false;
                    var result = explicitSpace == null
                        ? FormatBlank(spaceRect)
                        : FormatAsWord(spaceWidth, spaceRect);
                    if (result == FormatResult.NewLine)
                        return true;
                    if (result == FormatResult.Continue)
                        _currentVerticalInfo = spaceVerticalInfo;
                }
                _currentLeaf = following;
                var nextVerticalInfo = CalcVerticalInfo(CurrentFont);
                var nextRect = _currentSegmentIndex > 0 && _currentSecondaryRect != null
                    ? _currentSecondaryRect
                    : _formattingArea.GetFittingRect(_currentYPosition,
                        nextVerticalInfo.Height + BottomBorderOffset);
                return nextRect != null && FormatWord(word, nextRect) == FormatResult.NewLine;
            }
            finally
            {
                _currentLeaf = savedLeaf;
                _currentXPosition = savedX;
                _currentLineWidth = savedLineWidth;
                _currentWordsWidth = savedWordsWidth;
                _currentBlankCount = savedBlankCount;
                _currentContractionSpaceWidth = savedContractionSpaceWidth;
                _currentMinimumBlankWidth = savedMinimumBlankWidth;
                _savedBlankWidth = savedBlankWidth;
                _savedBlankContractionWidth = savedBlankContractionWidth;
                _currentClippedTrailingWidth = savedClippedWidth;
                _savedWordWidth = savedWordWidth;
                _minWidth = savedMinWidth;
                _currentSecondaryRect = savedSecondaryRect;
                _currentSegmentIndex = savedSegmentIndex;
                _currentVerticalInfo = savedVerticalInfo;
                _currentLineNaturalFontHeight = savedNaturalFontHeight;
                _blankWidth = savedBlankWidthCache;
                _lastFont = savedLastFont;
                if (exclusion != null && savedExclusionFontHeight.HasValue)
                    exclusion.NaturalFontHeight = savedExclusionFontHeight.Value;
                if (_currentSegmentTransitions.Count > savedTransitionCount)
                    _currentSegmentTransitions.RemoveRange(savedTransitionCount,
                        _currentSegmentTransitions.Count - savedTransitionCount);
            }
        }

        static string GetSymbol(Character character)
        {
            char ch;
            switch (character.SymbolName)
            {
                case SymbolName.Euro:
                    ch = '€';
                    break;

                case SymbolName.Copyright:
                    ch = '©';
                    break;

                case SymbolName.Trademark:
                    ch = '™';
                    break;

                case SymbolName.RegisteredTrademark:
                    ch = '®';
                    break;

                case SymbolName.Bullet:
                    ch = '•';
                    break;

                case SymbolName.Not:
                    ch = '¬';
                    break;
                // REM: Non-breakable blanks are still ignored.
                //        case SymbolName.SymbolNonBreakableBlank:
                //          return "\xA0";
                //          break;

                case SymbolName.EmDash:
                    ch = '—';
                    break;

                case SymbolName.EnDash:
                    ch = '–';
                    break;

                default:
                    char c = character.Char;
                    char[] chars = Encoding.UTF8.GetChars([(byte)c]);
                    ch = chars[0];
                    break;
            }

            int count = character.Count;
            if (!(count > 0))
                return ch.ToString(); // Return a single character.

            // Possibly return more characters.
            var returnString = new StringBuilder(ch.ToString());
            while (--count > 0)
                returnString.Append(ch);
            return returnString.ToString();
        }

        FormatResult FormatSymbol(Character character, Rectangle fittingRect)
        {
            return FormatWord(GetSymbol(character), fittingRect);
        }

        /// <summary>
        /// Processes (measures) a special character within text.
        /// </summary>
        /// <param name="character">The character to process.</param>
        /// <param name="fittingRect">The rect defining the space that is still available on the area to render the character.</param>
        /// <returns>True if the character should start at a new line.</returns>
        FormatResult FormatCharacter(Character character, Rectangle fittingRect)
        {
            return character.SymbolName switch
            {
                SymbolName.Blank => FormatSpace(character, fittingRect),
                SymbolName.Em => FormatSpace(character, fittingRect),
                SymbolName.Em4 => FormatSpace(character, fittingRect),
                SymbolName.En => FormatSpace(character, fittingRect),
                SymbolName.LineBreak => FormatLineBreak(),
                SymbolName.Tab => FormatTab(),
                _ => FormatSymbol(character, fittingRect)
            };
        }

        /// <summary>
        /// Processes (measures) a blank.
        /// </summary>
        /// <returns>True if the blank causes a line break.</returns>
        FormatResult FormatBlank(Rectangle fittingRect)
        {

            if (IgnoreBlank())
                return FormatResult.Ignore;

            _savedWordWidth = 0;
            XUnitPt width;
            var currentFont = CurrentFont;
            if (_lastFont == currentFont && _documentRenderer.TextHorizontalScale == null)
            {
                width = _blankWidth;
            }
            else
            {
                _blankWidth = width = MeasureString(" ", includeTrailingAdvance: false);
                _lastFont = currentFont;
            }

            // Cache only the width without the current leaf's tag-dependent advance.
            var naturalBlankWidth = width;
            width = AddTextTrailingAdvance(width);

            if (_currentXPosition + width > fittingRect.X + fittingRect.Width - RightIndent + Tolerance)
            {
                if (TryMoveToSecondary(0))
                    return FormatResult.Continue;
                return FormatResult.NewLine;
            }

            _currentXPosition += width;
            // Fixed positive advances cannot contract. Negative advances reduce
            // the remaining width available before a blank would move backwards.
            SaveBlankWidth(width, Math.Min(naturalBlankWidth.Point, width.Point));
            return FormatResult.Continue;
        }

        XUnitPt _blankWidth;
        XFont? _lastFont;

        FormatResult FormatLineBreak()
        {
            if (_phase != Phase.Rendering)
                _currentLeaf = _currentLeaf?.GetNextLeaf();

            _savedWordWidth = 0;
            _currentLineEndsWithLineBreak = true;
            return FormatResult.NewLine;
        }

        /// <summary>
        /// Processes a text element during formatting.
        /// </summary>
        /// <param name="text">The text element to measure.</param>
        /// <param name="fittingRect">The available space for the text on the area.</param>
        FormatResult FormatText(Text text, Rectangle fittingRect)
        {
            return FormatWord(text.Content, fittingRect);
        }

        FormatResult FormatSoftHyphen()
        {
            if (_currentLeaf is null || _startLeaf is null)
                NRT.ThrowOnNull();

            if (_currentLeaf.Current == _startLeaf.Current)
                return FormatResult.Continue;

            var nextIter = _currentLeaf.GetNextLeaf();
            var prevIter = _currentLeaf.GetPreviousLeaf() ?? NRT.ThrowOnNull<ParagraphIterator>();
            // nextIter can be null if the soft hyphen is at the end of a paragraph. To prevent a crash, we jump out if nextIter is null.
            if (!IsWordLikeElement(prevIter.Current) || nextIter == null || !IsWordLikeElement(nextIter.Current))
                return FormatResult.Continue;

            //--- Save ---------------------------------
            SaveBeforeProbing(out var iter, out var blankCount, out var wordsWidth, out var xPosition, out var lineWidth, out var blankWidth,
                out var lineEndsWithLineBreak, out var naturalFontHeight, out var contractionSpaceWidth, out var clippedTrailingWidth);
            //------------------------------------------
            _currentLeaf = nextIter;
            var result = FormatElement(nextIter.Current);

            //--- Restore ------------------------------
            RestoreAfterProbing(iter, blankCount, wordsWidth, xPosition, lineWidth, blankWidth, lineEndsWithLineBreak, naturalFontHeight, contractionSpaceWidth, clippedTrailingWidth);
            //------------------------------------------
            if (result == FormatResult.Continue)
                return FormatResult.Continue;

            RestoreAfterProbing(iter, blankCount, wordsWidth, xPosition, lineWidth, blankWidth, lineEndsWithLineBreak, naturalFontHeight, contractionSpaceWidth, clippedTrailingWidth);
            var fittingRect = _formattingArea.GetFittingRect(_currentYPosition, _currentVerticalInfo.Height);
            if (fittingRect is null)
                NRT.ThrowOnNull();

            XUnitPt hyphenWidth = MeasureString("-");
            if (xPosition + hyphenWidth <= fittingRect.X + fittingRect.Width + Tolerance
                // If one word fits, but not the hyphen, the formatting must continue with the next leaf.
                || prevIter.Current == _startLeaf.Current)
            {
                // For Tabs in justified context
                if (!IgnoreHorizontalGrowth)
                {
                    _currentWordsWidth += hyphenWidth;
                    _currentLineWidth += hyphenWidth;
                }
                _currentLeaf = nextIter;
                return FormatResult.NewLine;
            }
            else
            {
                _currentWordsWidth -= _savedWordWidth;
                _currentLineWidth -= _savedWordWidth;
                _currentLineWidth -= GetPreviousBlankWidth(prevIter);
                _currentLeaf = prevIter;
                return FormatResult.NewLine;
            }
        }

        XUnitPt GetPreviousBlankWidth(ParagraphIterator beforeIter)
        {
            XUnitPt width = 0;
            var savedIter = _currentLeaf;
            _currentLeaf = beforeIter.GetPreviousLeaf();
            while (_currentLeaf != null)
            {
                if (_currentLeaf.Current is BookmarkField)
                    _currentLeaf = _currentLeaf.GetPreviousLeaf();
                else if (IsBlank(_currentLeaf.Current))
                {
                    if (!IgnoreBlank())
                        width = CurrentWordDistance;

                    break;
                }
                else
                    break;
            }
            _currentLeaf = savedIter;
            return width;
        }

        void HandleNonFittingLine()
        {
            if (_currentLeaf != null)
            {
                if (_savedWordWidth > 0)
                {
                    _currentWordsWidth = _savedWordWidth;
                    _currentLineWidth = _savedWordWidth;
                }
                _currentLeaf = _currentLeaf.GetNextLeaf();
                _currentYPosition += _currentVerticalInfo.Height;
                _currentVerticalInfo = new VerticalLineInfo();
            }
        }

        /// <summary>
        /// Starts a new line by resetting measuring values.
        /// Do not call before the first line is formatted!
        /// </summary>
        void StartNewLine()
        {
            _tabOffsets = [];
            _lastTab = null;
            _lastTabPosition = 0;
            _currentYPosition += _currentVerticalInfo.Height;

            // Height of the new line may be not equal to the height of the current line, so taking it for determining if it will fit on the page
            // could lead to a wrongly added page break. Page fitting must only be checked in FormatElement(), where the height of the new line is known.
            //var rect = _formattingArea.GetFittingRect(_currentYPosition, _currentVerticalInfo.Height + BottomBorderOffset);
            //if (rect == null)
            //    return false;

            _isFirstLine = false;
            _currentSegmentIndex = 0;
            _currentSegmentTransitions.Clear();
            _currentLineGap = 0;
            _currentLineNaturalFontHeight = 0;
            _currentSecondaryRect = null;
            _currentXPosition = StartXPosition;
            _currentVerticalInfo = new VerticalLineInfo();
            _currentVerticalInfo = CalcCurrentVerticalInfo();
            _startLeaf = _currentLeaf;
            _currentBlankCount = 0;
            _currentContractionSpaceWidth = 0;
            _currentMinimumBlankWidth = double.PositiveInfinity;
            _currentWordsWidth = 0;
            _currentLineWidth = 0;
            _currentClippedTrailingWidth = 0;
            _currentLineEndsWithLineBreak = false;
            _collapsedSpacesOnLine = [];
        }

        /// <summary>
        /// Stores all line information.
        /// </summary>
        void StoreLineInformation(bool rejectedTab = false, bool nextLineStartedByBlankWrap = false)
        {
            PopSavedBlankWidth();

            if (_currentLeaf == null && _paragraph.Format.LineSpacingRule == LineSpacingRule.Exactly &&
                _renderInfo.FormatInfo is ParagraphFormatInfo paragraphInfo &&
                (paragraphInfo.LineCount >= 2 || !paragraphInfo.IsStarting) &&
                _documentRenderer.ExactFinalLineHeight is { } finalLineHeight)
            {
                var fonts = new List<XFont>();
                try
                {
                    for (var leaf = _startLeaf; leaf != null; leaf = leaf.GetNextLeaf())
                    {
                        _currentLeaf = leaf;
                        if (leaf.Current is Text text && !IsBlank(text))
                            fonts.Add(CurrentFont);
                    }
                }
                finally
                {
                    _currentLeaf = null;
                }
                if (fonts.Count > 0 && finalLineHeight(_paragraph, fonts) is double height &&
                    IsFinite(height) && height > 0 && height < _currentVerticalInfo.Height.Point)
                    _currentVerticalInfo.Height = XUnitPt.FromPoint(height);
            }

            XUnitPt topBorderOffset = TopBorderOffset(_formattingArea.Y);
            var paragraphFormatInfo = (ParagraphFormatInfo)_renderInfo.FormatInfo;
            if (paragraphFormatInfo.LineCount == 0)
                paragraphFormatInfo.TopBorderOffset = topBorderOffset;
            var contentArea = _renderInfo.LayoutInfo.ContentArea;
            if (topBorderOffset > 0) // May only occur for the first line.
                contentArea = _formattingArea.GetFittingRect(_formattingArea.Y, topBorderOffset);

            if (contentArea == null)
                contentArea = GetFittingRectForLine(_currentYPosition, _currentVerticalInfo);
            else
                contentArea = contentArea.Unite(GetFittingRectForLine(_currentYPosition, _currentVerticalInfo) ?? NRT.ThrowOnNull<Rectangle>());

            var lineInfo = new LineInfo
            {
                Vertical = _currentVerticalInfo,
                FittingRect = _formattingArea is SideExclusionArea
                    ? GetFittingRectForLine(_currentYPosition, _currentVerticalInfo)
                    : null,
                SegmentTransitions = [.. _currentSegmentTransitions],
                GapBefore = _currentLineGap
            };

#if true
            // For lines containing only one not fitting element, HandleNonFittingLine must be called,
            // to add the element to the line and prepare for the next one.
            // The current element must have caused the line break (StoreLineInformation is called on a line break)
            // so if _currentLineWidth is zero by now (the length of the current element is not yet added),
            // there seems to be only this one not fitting element.
            // But also a manual line break may have caused StoreLineInformation. For this case, do not call 
            // HandleNonFittingLine. Blanks before _currentLeaf don’t have any influence.
            if (_currentLineWidth == XUnitPt.Zero && !_currentLineEndsWithLineBreak)
                HandleNonFittingLine();
#else
            // This old code does not call HandleNonFittingLine for one not fitting element, if the line starts with a blank.
            // The blank is ignored on formatting, but causes _currentLeaf not to be _startLeaf, so HandleNonFittingLine is not executed.
            
            if (_startLeaf != null && _startLeaf == _currentLeaf)
                HandleNonFittingLine();
#endif

            lineInfo.LastTab = _lastTab;
            _renderInfo.LayoutInfo.ContentArea = contentArea ?? NRT.ThrowOnNull<Area>();

            lineInfo.StartIter = _startLeaf;

            if (_currentLeaf == null)
                lineInfo.EndIter = new ParagraphIterator(_paragraph.Elements).GetLastLeaf();
            else
                lineInfo.EndIter = _currentLeaf.GetPreviousLeaf() ?? NRT.ThrowOnNull<ParagraphIterator>();
            if (rejectedTab)
            {
                lineInfo.HasContinuationOverride = true;
                lineInfo.ContinuationIter = _currentLeaf?.GetNextLeaf();
            }

            lineInfo.BlankCount = _currentBlankCount;

            lineInfo.WordsWidth = _currentWordsWidth;

            lineInfo.LineWidth = _currentLineWidth;
            lineInfo.ClippedTrailingWidth = _currentClippedTrailingWidth;
            lineInfo.TabOffsets = _tabOffsets;
            lineInfo.ReMeasureLine = _reMeasureLine;
            lineInfo.LineEndsWithLineBreak = _currentLineEndsWithLineBreak;
            lineInfo.NextLineStartedByBlankWrap = nextLineStartedByBlankWrap;
            lineInfo.CollapsedSpaces = _collapsedSpacesOnLine;

            _savedWordWidth = 0;
            _reMeasureLine = false;
            ((ParagraphFormatInfo)_renderInfo.FormatInfo).AddLineInfo(lineInfo);
        }

        /// <summary>
        /// Adds the BottomBorderOffset to the ContentArea. This should only be called for the last line of a paragraph.
        /// </summary>
        void StoreBottomBorderInformation()
        {
            var contentArea = _renderInfo.LayoutInfo.ContentArea;

            var bottomBorderOffset = BottomBorderOffset;
            if (bottomBorderOffset > 0)
                contentArea = contentArea.Unite(_formattingArea.GetFittingRect(_currentYPosition + _currentVerticalInfo.Height, bottomBorderOffset) ?? NRT.ThrowOnNull<Rectangle>());

            _renderInfo.LayoutInfo.ContentArea = contentArea;
        }

        /// <summary>
        /// Gets the top border offset for the first line, else 0.
        /// </summary>
        XUnitPt TopBorderOffset(XUnitPt areaY)
        {
            XUnitPt offset = 0;
            if (_isFirstLine && _paragraph.Format.Values.Borders is not null &&
                !_paragraph.Format.Values.Borders.IsNull())
            {
                var distance = _paragraph.Format.Borders.DistanceFromTop.Point;
                var replacement = _documentRenderer.TopBorderDistanceAtAreaStart?.Invoke(
                    _paragraph, _fieldInfos?.PhysicalPageNr ?? 0, areaY.Point);
                if (replacement is double candidate && IsFinite(candidate) && candidate >= 0)
                    distance = candidate;
                offset += distance;
                var bordersRenderer = new BordersRenderer(_paragraph.Format.Borders, _gfx);
                offset += bordersRenderer.GetWidth(BorderType.Top);
            }
            return offset;
        }

        /// <summary>
        /// Gets the bottom border offset for the last line, else 0.
        /// While formatting, the actual border offset for the last line is only returned for the last paragraph’s element.
        /// </summary>
        XUnitPt BottomBorderOffset
        {
            get
            {
                bool considerAsLastLine;
                // While formatting, it is impossible to determine whether we are in the last line until the last leaf is reached.
                if (_phase == Phase.Formatting)
                    considerAsLastLine = _currentLeaf == null || _currentLeaf.IsLastLeaf;
                else
                    considerAsLastLine = _isLastLine;

                if (!considerAsLastLine)
                    return 0;

                if (_paragraph.Format.Values.Borders is null || _paragraph.Format.Values.Borders.IsNull())
                    return 0;

                XUnitPt offset = _paragraph.Format.Borders.DistanceFromBottom.ToXUnitPt();
                var bordersRenderer = new BordersRenderer(_paragraph.Format.Borders, _gfx);
                offset += bordersRenderer.GetWidth(BorderType.Bottom);
                return offset;
            }
        }

        VerticalLineInfo CalcCurrentVerticalInfo()
        {
            return CalcVerticalInfo(CurrentFont);
        }

        Rectangle? GetFittingRectForLine(XUnitPt y, VerticalLineInfo line, XUnitPt bottomBorder = default)
        {
            // The additional leading of a multiple-spaced line determines the next
            // line's position. Word does not require that trailing leading to fit
            // inside the bottom margin when this is the last line on a page.
            XUnitPt fitHeight = _paragraph.Format.LineSpacingRule == LineSpacingRule.Multiple
                ? XUnitPt.FromPoint(Math.Min(line.Height.Point, line.InherentLineSpace.Point))
                : line.Height;
            var rect = _formattingArea.GetFittingRect(y, fitHeight + bottomBorder);
            if (rect != null)
                rect.Height = line.Height + bottomBorder;
            return rect;
        }

        VerticalLineInfo CalcVerticalInfo(XFont font, bool isListSymbol = false)
        {
            var paragraphFormat = _paragraph.Format;
            var lineSpacingRule = paragraphFormat.LineSpacingRule;

            XUnitPt singleLineSpace = font.GetHeight();
            if (_phase == Phase.Formatting)
            {
                _currentLineNaturalFontHeight = Math.Max(_currentLineNaturalFontHeight,
                    singleLineSpace.Point);
                if (_formattingArea is SideExclusionArea sideExclusion)
                    sideExclusion.NaturalFontHeight = _currentLineNaturalFontHeight;
            }
            var imageRenderInfo = CurrentImageRenderInfo;
            XUnitPt imageHeight = XUnitPt.Zero;
            if (imageRenderInfo != null)
            {
                imageHeight = _currentLeaf?.Current is Image image
                    ? GetInlineImageLayoutHeight(image, imageRenderInfo.LayoutInfo.ContentArea.Height)
                    : imageRenderInfo.LayoutInfo.ContentArea.Height;
                if (_currentLeaf?.Current is Image inlineImage)
                {
                    var effect = GetInlineImageEffectExtent(inlineImage);
                    imageHeight += effect.Top + effect.Bottom;
                }
                var occupiesDescent = IsImageOnlyParagraph ||
                    _currentLeaf?.Current is Image descentImage &&
                    _documentRenderer.InlineImageOccupiesDescent?.Invoke(descentImage) == true;
                singleLineSpace = occupiesDescent
                    ? Math.Max(singleLineSpace, imageHeight)
                    : singleLineSpace - FontHandler.GetAscent(font) + imageHeight;
            }

            XUnitPt inherentLineSpace, lineHeight;
            if (lineSpacingRule == LineSpacingRule.Exactly)
            {
                // singleLineSpace shall be exactly _paragraph.Format.LineSpacing.
                singleLineSpace = _paragraph.Format.LineSpacing.ToXUnitPt();

                // Set inherentLineSpace and lineHeight to the same exact value.
                inherentLineSpace = singleLineSpace;
                lineHeight = singleLineSpace;

            }
            else if (lineSpacingRule == LineSpacingRule.AtLeast)
            {
                // singleLineSpace shall be at least _paragraph.Format.LineSpacing.Point.
                singleLineSpace = Math.Max(singleLineSpace, _paragraph.Format.LineSpacing.Point);

                // Set inherentLineSpace and lineHeight to the same maximum value. This way the space resulting from the difference
                // between original singleLineSpace and _paragraph.Format.LineSpacing.Point will be located at the top of the text line.
                inherentLineSpace = singleLineSpace;
                inherentLineSpace = Math.Max(inherentLineSpace, _currentVerticalInfo.InherentLineSpace);
                lineHeight = inherentLineSpace;
                var bottomSpace = _paragraph.Format.AtLeastBottomLineSpace.Point;
                if (!double.IsNaN(bottomSpace) && !double.IsInfinity(bottomSpace) && bottomSpace > 0)
                    lineHeight += bottomSpace;
            }
            else
            {
                var factor = lineSpacingRule switch
                {
                    LineSpacingRule.OnePtFive => 1.5,
                    LineSpacingRule.Double => 2.0,
                    LineSpacingRule.Multiple => _paragraph.Format.LineSpacing.Point,
                    _ => 1
                };

                // Set inherentLineSpace to the maximum original singleLineSpace value and...
                inherentLineSpace = singleLineSpace;
                inherentLineSpace = Math.Max(inherentLineSpace, _currentVerticalInfo.InherentLineSpace);

                // ...set lineHeight to the maximum multiplied singleLineSpace value. This way the space resulting from the difference
                // between original and multiplied singleLineSpace value will be located at the bottom of the text line.
                lineHeight = singleLineSpace * factor;
                if (imageRenderInfo != null)
                    lineHeight -= Math.Max(0, imageHeight - FontHandler.GetAscent(font)) *
                                  (factor - 1);
                lineHeight = Math.Max(lineHeight, _currentVerticalInfo.Height);
            }

            XUnitPt descent;
            if (lineSpacingRule == LineSpacingRule.Exactly)
            {
                // Set descent to the scaled paragraph’s font descent once for text positioning.
                // Like in word, we have a fixed baseline position this way, even if the line contains FormattedTests with a bigger descents.
                if (_currentVerticalInfo.Descent == XUnitPt.Zero)
                {
                    var paragraphFont = FontHandler.FontToXFont(_paragraph.Format.Font);
                    descent = FontHandler.GetDescent(paragraphFont);

                    // In word, the baseline position seems to be scaled to the set line spacing. Same here with the descent for text positioning. 
                    var factor = singleLineSpace.Point / paragraphFont.Size;
                    descent *= factor;
                }
                else
                    descent = _currentVerticalInfo.Descent;
            }
            else
            {
                // Set descent to the maximum font descent for text positioning.
                descent = isListSymbol ? 0 : FontHandler.GetDescent(font); // Ignore descent for bullet glyphs.
                descent = Math.Max(descent, _currentVerticalInfo.Descent);
            }

            if (_documentRenderer.MinimumLineMetricsForFont?.Invoke(_paragraph, font) is { } minimumMetrics &&
                IsFinite(minimumMetrics.LineHeight) && minimumMetrics.LineHeight > 0 &&
                IsFinite(minimumMetrics.InherentLineSpace) && minimumMetrics.InherentLineSpace > 0)
            {
                lineHeight = Math.Max(lineHeight, XUnitPt.FromPoint(minimumMetrics.LineHeight));
                inherentLineSpace = Math.Max(inherentLineSpace,
                    XUnitPt.FromPoint(minimumMetrics.InherentLineSpace));
            }

            if (MaxElementHeight > 0)
                lineHeight = Math.Min(lineHeight, MaxElementHeight - Tolerance);

            return new(lineHeight, descent, inherentLineSpace);
        }

        /// <summary>
        /// The font used for the current paragraph element.
        /// </summary>
        XFont CurrentFont => FontHandler.FontToXFont(CurrentDomFont);

        Font CurrentDomFont
        {
            get
            {
                if (_currentDomFont != null && _currentObject == _currentLeaf?.Current)
                {
#if DEBUG
                    var font = CurrentDomFontOld;
                    if (font.Name != _currentDomFont.Name ||
                        font.Bold != _currentDomFont.Bold ||
                        font.Italic != _currentDomFont.Italic ||
                        font.Size != _currentDomFont.Size ||
                        font.Superscript != _currentDomFont.Superscript ||
                        font.Subscript != _currentDomFont.Subscript ||
                        font.Color != _currentDomFont.Color ||
                        font.Underline != _currentDomFont.Underline)
                        throw new InvalidOperationException("Check CurrentDomFont.");
#endif
                    return _currentDomFont;
                }

                if (_currentLeaf != null)
                {
                    if (_currentLeaf.Current is Image inlineImage &&
                        _documentRenderer.InlineImageRunFont?.Invoke(inlineImage) is { } imageFont)
                    {
                        _currentObject = inlineImage;
                        return _currentDomFont = imageFont;
                    }
                    var parent = DocumentRelations.GetParent(_currentLeaf.Current);
                    parent = DocumentRelations.GetParent(parent);

                    if (parent is FormattedText formattedText)
                    {
                        _currentObject = _currentLeaf.Current;
                        return _currentDomFont = formattedText.Font;
                    }

                    if (parent is Hyperlink hyperlink)
                    {
                        _currentObject = _currentLeaf.Current;
                        return _currentDomFont = hyperlink.Font;
                    }
                }
                _currentObject = _currentLeaf?.Current;
                return _currentDomFont = _paragraph.Format.Font;
            }
        }
        Font? _currentDomFont;
        DocumentObject? _currentObject;

#if DEBUG
        Font CurrentDomFontOld
        {
            get
            {
                if (_currentLeaf != null)
                {
                    if (_currentLeaf.Current is Image inlineImage &&
                        _documentRenderer.InlineImageRunFont?.Invoke(inlineImage) is { } imageFont)
                        return imageFont;
                    var parent = DocumentRelations.GetParent(_currentLeaf.Current);
                    parent = DocumentRelations.GetParent(parent);

                    if (parent is FormattedText formattedText)
                    {
                        return formattedText.Font;
                    }

                    if (parent is Hyperlink hyperlink)
                    {
                        return hyperlink.Font;
                    }
                }
                return _paragraph.Format.Font;
            }
        }
#endif

        /// <summary>
        /// Help function to receive a line height on empty paragraphs.
        /// </summary>
        /// <param name="format">The format.</param>
        /// <param name="gfx">The GFX.</param>
        /// <param name="renderer">The renderer.</param>
#pragma warning disable IDE0060
        internal static XUnitPt GetLineHeight(ParagraphFormat format, XGraphics gfx, DocumentRenderer renderer)
#pragma warning restore IDE0060
        {
            var font = FontHandler.FontToXFont(format.Font);
            XUnitPt singleLineSpace = font.GetHeight();
            return format.LineSpacingRule switch
            {
                LineSpacingRule.Exactly => format.LineSpacing.Point,
                LineSpacingRule.AtLeast => Math.Max(format.LineSpacing.Point, font.GetHeight()),
                LineSpacingRule.Multiple => format.LineSpacing.Point * format.Font.Size.Point,
                LineSpacingRule.OnePtFive => 1.5 * singleLineSpace,
                LineSpacingRule.Double => 2.0 * singleLineSpace,
                LineSpacingRule.Single => singleLineSpace,
                _ => singleLineSpace
            };
        }

        void RenderUnderline(XUnitPt width, bool isWord)
        {
            var font = CurrentDomFont;
            var geometry = _documentRenderer.UnderlineGeometry?.Invoke(font);
            double? offsetPoints = null;
            if (geometry == null && _documentRenderer.UseFontUnderlineMetrics)
            {
                var metrics = CurrentFont.Metrics;
                if (metrics.UnitsPerEm > 0 && metrics.UnderlineThickness > 0)
                {
                    geometry = ((double)metrics.UnderlineThickness / metrics.UnitsPerEm, 0);
                    offsetPoints = font.Size.Point *
                        (-metrics.UnderlinePosition + metrics.UnderlineThickness / 2.0) /
                        metrics.UnitsPerEm;
                }
            }
            var pen = GetUnderlinePen(isWord, font, geometry?.StrokeWidthFactor);
            var offsetFactor = geometry?.VerticalOffsetFactor ??
                _documentRenderer.UnderlineVerticalOffsetFactor ?? 0.33;

            bool penChanged = UnderlinePenChanged(pen, offsetFactor, offsetPoints, font.Underline == Underline.Double, font.Underline == Underline.Wavy);
            if (penChanged)
            {
                if (_currentUnderlinePen != null)
                    EndUnderline(_currentUnderlinePen, _underlineEndPos);

                if (pen != null)
                {
                    StartUnderline(CurrentPaintXPosition);
                    _currentUnderlineOffsetFactor = offsetFactor;
                    _currentUnderlineOffsetPoints = offsetPoints;
                    _currentUnderlineIsDouble = font.Underline == Underline.Double;
                    _currentUnderlineIsWavy = font.Underline == Underline.Wavy;
                }

                _currentUnderlinePen = pen;
            }
            if (_currentUnderlinePen != null)
            {
                // Own this run's painted extent rather than closing it at the
                // following leaf's potentially shifted origin. Keep both bounds
                // when a signed adjustment moves later content before the start.
                _underlineStartPos = Math.Min(_underlineStartPos.Point, CurrentPaintXPosition.Point);
                _underlineEndPos = Math.Max(_underlineEndPos.Point, (CurrentPaintXPosition + width).Point);
            }
            // A style can change without changing the realized pen width (for example,
            // thick 12pt followed by single 24pt). Remember every leaf's style.
            _currentUnderlineIsThick = font.Underline == Underline.Thick;
            if (_currentLeaf is null || _endLeaf is null)
                NRT.ThrowOnNull();
            if (_currentLeaf.Current == _endLeaf.Current)
            {
                if (_currentUnderlinePen != null)
                    EndUnderline(_currentUnderlinePen, _underlineEndPos);

                _currentUnderlinePen = null;
            }
        }

        void StartUnderline(XUnitPt xPosition)
        {
            _underlineStartPos = xPosition;
            _underlineEndPos = xPosition;
        }

        void EndUnderline(XPen pen, XUnitPt xPosition)
        {
            XUnitPt yPosition = CurrentBaselinePosition;
            yPosition += _currentUnderlineOffsetPoints ??
                _currentUnderlineOffsetFactor * _currentVerticalInfo.Descent.Point;
            if (_currentUnderlineIsWavy)
            {
                DrawWaveUnderline(pen, xPosition, yPosition);
                return;
            }
#if PSGFX
            _gfx.DrawLine(pen, new(_underlineStartPos, yPosition), new(xPosition, yPosition));
#else
            _gfx.DrawLine(pen, _underlineStartPos, yPosition, xPosition, yPosition);
#endif
            if (_currentUnderlineIsDouble)
            {
                // Leave one stroke width of clear space between the two strokes.
                yPosition += 2 * pen.Width;
#if PSGFX
                _gfx.DrawLine(pen, new(_underlineStartPos, yPosition), new(xPosition, yPosition));
#else
                _gfx.DrawLine(pen, _underlineStartPos, yPosition, xPosition, yPosition);
#endif
            }
        }

        void DrawWaveUnderline(XPen pen, XUnitPt endPosition, XUnitPt center)
        {
            var start = _underlineStartPos.Point;
            var end = endPosition.Point;
            if (end <= start)
                return;

            // A zero-width PDF pen is a hairline; keep its wave finite and visible.
            var scale = Math.Max(0.1, pen.Width);
            var halfPeriod = 3 * scale;
            var amplitude = scale;
            var path = new XGraphicsPath();
            var x = start;
            var y = center.Point;
            var targetY = center.Point - amplitude;
            var first = true;
            while (x < end)
            {
                var segment = Math.Min(first ? halfPeriod / 2 : halfPeriod, end - x);
                var next = x + segment;
                // Keep all control points inside the run, including a partial final wave.
                path.AddBezier(x, y, x + segment / 2, y,
                    x + segment / 2, targetY, next, targetY);
                x = next;
                y = targetY;
                targetY = center.Point + (targetY < center.Point ? amplitude : -amplitude);
                first = false;
            }
            var state = _gfx.Save();
            try
            {
                _gfx.IntersectClip(new XRect(start, center.Point - 2 * scale,
                    end - start, 4 * scale));
                _gfx.DrawPath(pen, path);
            }
            finally
            {
                _gfx.Restore(state);
            }
        }

        XPen? _currentUnderlinePen;
        XUnitPt _underlineStartPos;
        XUnitPt _underlineEndPos;
        double _currentUnderlineOffsetFactor;

        double? _currentUnderlineOffsetPoints;
        bool _currentUnderlineIsDouble;
        bool _currentUnderlineIsWavy;
        bool _currentUnderlineIsThick;

        bool UnderlinePenChanged(XPen? pen, double offsetFactor, double? offsetPoints, bool isDouble, bool isWavy)
        {
            if (pen == null && _currentUnderlinePen == null)
                return false;

            if (pen == null && _currentUnderlinePen != null)
                return true;

            if (pen != null && _currentUnderlinePen == null)
                return true;

            if (_currentUnderlinePen is null)
                NRT.ThrowOnNull();

            if (pen != null && (pen.Color != _currentUnderlinePen!.Color ||
                                pen.DashStyle != _currentUnderlinePen.DashStyle))
                return true;

            if (pen is null)
                NRT.ThrowOnNull();

            if (offsetFactor != _currentUnderlineOffsetFactor || offsetPoints != _currentUnderlineOffsetPoints ||
                isDouble != _currentUnderlineIsDouble || isWavy != _currentUnderlineIsWavy)
                return true;

            // ReSharper disable once CompareOfFloatsByEqualityOperator
            return pen.Width != _currentUnderlinePen.Width;
        }

        RenderInfo? CurrentImageRenderInfo
        {
            get
            {
                if (_currentLeaf is { Current: Image image })
                {
                    if (_imageRenderInfos != null! && _imageRenderInfos.TryGetValue(image, out var info))
                        return info;

                    _imageRenderInfos ??= [];

                    var renderInfo = CalcImageRenderInfo(image);
                    _imageRenderInfos.Add(image, renderInfo);
                    return renderInfo;
                }
                return null;
            }
        }

        XPen? GetUnderlinePen(bool isWord, Font font, double? strokeWidthFactor)
        {
            var underlineType = font.Underline;
            if (underlineType == Underline.None)
                return null;

            if (underlineType == Underline.Words && !isWord)
                return null;

            var underlineColor = font.UnderlineColor.IsEmpty ? font.Color : font.UnderlineColor;
            var underlineWidth = font.Size.Point * (strokeWidthFactor ??
                _documentRenderer.UnderlineStrokeWidthFactor ?? 1.0 / 16);
            if (underlineType == Underline.Thick)
                underlineWidth *= 2;
#if noCMYK
            XPen pen = new XPen(XColor.FromArgb(underlineColor.Argb), underlineWidth);
#else
            Debug.Assert(_paragraph.Document != null, "_paragraph.Document != null");
#if PSGFX
            var xxx = ColorHelper.ToXColor(underlineColor, _paragraph.Document.UseCmykColor);
            var pen = new XPen(new XSolidBrush(xxx), (float_)underlineWidth);
#else
            var pen = new XPen(ColorHelper.ToXColor(underlineColor, _paragraph.Document.UseCmykColor), underlineWidth);
#endif
#endif
            if (!isWord && underlineType == Underline.Single && strokeWidthFactor == null &&
                _documentRenderer.UnderlineStrokeWidthFactor == null && _currentUnderlinePen != null &&
                !_currentUnderlineIsThick &&
                pen.Color == _currentUnderlinePen.Color)
                pen.Width = _currentUnderlinePen.Width;
            pen.DashStyle = font.Underline switch
            {
#if PSGFX
                Underline.DotDash => XDashStyles.DashDot,
                Underline.DotDotDash => XDashStyles.DashDotDot,
                Underline.Dash => XDashStyles.Dash,
                Underline.Dotted => XDashStyles.Dot,
                Underline.Single => XDashStyles.Solid,
                _ => XDashStyles.Solid
#else
                Underline.DotDash => XDashStyle.DashDot,
                Underline.DotDotDash => XDashStyle.DashDotDot,
                Underline.Dash => XDashStyle.Dash,
                Underline.Dotted => XDashStyle.Dot,
                Underline.Single => XDashStyle.Solid,
                _ => XDashStyle.Solid
#endif
            };
            return pen;
        }

        //static XStringFormat StringFormat => _stringFormat ??= XStringFormats.Default;
        //static XStringFormat _stringFormat;
        static XStringFormat StringFormat { get; } = XStringFormats.Default;

        /// <summary>
        /// The paragraph to format or render.
        /// </summary>
        readonly Paragraph _paragraph;

        readonly Dictionary<Character, bool> _optionalSpaceDecisions = [];
        HashSet<Character> _collapsedSpacesOnLine = [];
        bool _newLineStartedByBlankWrap;

        XUnitPt _currentWordsWidth;
        int _currentBlankCount;
        XUnitPt _currentContractionSpaceWidth;
        double _currentMinimumBlankWidth = double.PositiveInfinity;
        bool _currentSegmentNeedsContraction;
        XUnitPt _currentLineWidth;
        XUnitPt _currentClippedTrailingWidth;
        bool _currentLineEndsWithLineBreak;
        bool _isFirstLine;
        bool _isLastLine;
        VerticalLineInfo _currentVerticalInfo;
        double _exactParagraphBaselineOffset;
        bool _baselineOffsetInitialized;
        Area _formattingArea = null!;
        Rectangle? _currentLineRect;
        XUnitPt _currentLineWordsWidth;
        int _currentLineBlankCount;
        Rectangle? _currentSecondaryRect;
        int _currentSegmentIndex;
        readonly List<LineSegmentInfo> _currentSegmentTransitions = [];
        XUnitPt _currentLineGap;
        double _currentLineNaturalFontHeight;
        XUnitPt _currentYPosition;
        XUnitPt _currentXPosition;
        ParagraphIterator? _currentLeaf;
        // Rendering history, unaffected by signed paint-only shifts of the pen.
        // Reset for each line and side-exclusion flow segment; ignored blanks do not set it.
        bool _hasTextBoundaryPredecessor;
        // _currentXPosition always holds the unshifted cursor, including while rendering.
        // Only drawing origins and hit/decorative bounds use the accumulated paint offset.
        XUnitPt CurrentPaintXPosition => _currentXPosition + _textBoundaryPaintOffset;
        XUnitPt _textBoundaryPaintOffset;

        ParagraphIterator? _startLeaf;
        ParagraphIterator? _endLeaf;
        bool _reMeasureLine;
        XUnitPt _minWidth = 0;
        Dictionary<Image, RenderInfo> _imageRenderInfos = default!;
        List<TabOffset> _tabOffsets = default!;
        DocumentObject? _lastTab;
        bool _lastTabPassed;
        XUnitPt _lastTabPosition;
    }
}
