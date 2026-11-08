// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using System.Diagnostics.CodeAnalysis;
using PdfSharp.Events;
using PdfSharp.Pdf;
using PdfSharp.Internal;
#if PSXGRA
using PdfSharp.Drawing;
#endif
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Visitors;
using MigraDoc.DocumentObjectModel.Shapes;
using MigraDoc.DocumentObjectModel.Tables;
using PdfSharp.Drawing;

namespace MigraDoc.Rendering
{
    /// <summary>
    /// Provides methods to render the document or single parts of it to a XGraphics object.
    /// </summary>
    /// <remarks>
    /// One prepared instance of this class can serve to render several output formats.
    /// </remarks>
    /// <remarks>
    /// Initializes a new instance of the DocumentRenderer class.
    /// </remarks>
    /// <param name="document">The MigraDoc document to render.</param>
    public class DocumentRenderer(Document document)
    {
        /// <summary>
        /// Optionally clips image painting in drawing coordinates, after selecting
        /// ImagePaintRectangle. Does not change layout, wrapping or the image border.
        /// Null, empty, nonfinite or nonpositive rectangles leave painting unclipped.
        /// Also clips failure placeholders. Image links cover only the intersection
        /// with painted bounds; link selectors are skipped when it has no area.
        /// </summary>
        public Func<Image, XRect, XRect?>? ImagePaintClipRectangle { get; set; }

        /// <summary>
        /// Optionally replaces the painted image rectangle, in drawing coordinates.
        /// The original frame still controls layout, wrapping and the image border.
        /// Image link callbacks receive the replacement rectangle. Also applies to
        /// failure placeholders. Empty, nonfinite or nonpositive rectangles are ignored.
        /// Called during painting; changing this callback does not require preparation.
        /// </summary>
        public Func<Image, XRect, XRect>? ImagePaintRectangle { get; set; }

        /// <summary>
        /// Selects a web link for a painted image. Receives the image and its
        /// destination rectangle in drawing coordinates. Null or empty omits the
        /// link; a nonempty web target takes precedence over ImageDocumentLink.
        /// Invoked during painting, including inline images and failure placeholders.
        /// </summary>
        public Func<Image, XRect, string?>? ImageWebLink { get; set; }

        /// <summary>
        /// Selects a named bookmark destination for a painted image when ImageWebLink
        /// supplies no target. The bookmark must exist in the rendered document.
        /// Null or empty omits the link. The hit area follows the image's transformed
        /// destination rectangle; neither callback changes image layout.
        /// </summary>
        public Func<Image, XRect, string?>? ImageDocumentLink { get; set; }

        /// <summary>
        /// Reserves additional body space in points above the usable page bottom,
        /// after footer clearance. The callback receives the one-based physical page
        /// number, regardless of displayed page numbering. Null or zero reserves nothing.
        /// Results must be finite, nonnegative, and leave positive body height.
        /// The callback can be evaluated repeatedly while formatting and must return
        /// a stable value for each page during a preparation pass. Prepare again after
        /// changing reservations. Footer placement is unchanged.
        /// </summary>
        public Func<int, double>? BodyBottomReservation { get; set; }

        /// <summary>Optional underline stroke width as a fraction of font size. The default is 1/16.</summary>
        public double? UnderlineStrokeWidthFactor { get; set; }

        /// <summary>
        /// Adjusts a justified word's painted width without changing line fitting or pagination.
        /// Return null to keep its measured width. Non-finite values are ignored.
        /// </summary>
        public Func<Paragraph, string, XFont, double?>? JustifiedRenderWordWidthAdjustment { get; set; }

        /// <summary>
        /// Returns the permitted line-end overhang in points for a word in a paragraph,
        /// or null to use ordinary wrapping.
        /// </summary>
        public Func<Paragraph, string, XFont, double?>? LineEndOverhang { get; set; }

        /// <summary>
        /// Adds the permitted overhang to the fit allowance for a final word,
        /// preserving its full measured width for rendering.
        /// </summary>
        public bool AdditiveTerminalLineEndOverhang { get; set; }

        /// <summary>
        /// Adjusts the spacing before a word's final glyph during painting only.
        /// The adjustment does not affect line fitting. Null or non-finite values are ignored.
        /// </summary>
        public Func<Paragraph, string, XFont, double?>? RenderOnlyTrailingGlyphKerning { get; set; }

        /// <summary>
        /// Sets the maximum protrusion, in points, at which a final hanging mark
        /// can expand the spaces on a left-aligned line. Null leaves spacing unchanged.
        /// </summary>
        public Func<Paragraph, string, XFont, double?>? HangingTerminalLineSpaceThreshold { get; set; }

        /// <summary>
        /// Allows the final hanging mark to expand spaces when its rendered line
        /// falls just short of the right edge. Returns the near-edge tolerance in points.
        /// </summary>
        public Func<Paragraph, string, XFont, double?>? HangingTerminalLineNearEdgeThreshold { get; set; }

        /// <summary>
        /// Sets the maximum added width per blank, in points, when a non-final
        /// left-aligned line ends in a clipped hanging mark. Null leaves spacing unchanged.
        /// </summary>
        public Func<Paragraph, string, XFont, double?>? HangingNonterminalLineSpaceLimit { get; set; }

        /// <summary>
        /// Selects a paint-only baseline offset in points for an exact-spaced paragraph.
        /// Receives the fonts on the first rendered line. Null or a non-finite value leaves the baseline unchanged.
        /// </summary>
        public Func<Paragraph, IReadOnlyList<XFont>, double?>? ExactParagraphBaselineOffset { get; set; }

        /// <summary>
        /// Selects a shorter layout height, in points, for the final line of an exact-spaced
        /// multi-line paragraph fragment. Receives the fonts used by that line. Null or an
        /// invalid height preserves the formatted line height.
        /// </summary>
        public Func<Paragraph, IReadOnlyList<XFont>, double?>? ExactFinalLineHeight { get; set; }

        /// <summary>
        /// Supplies minimum line height and inherent line space, in points, for a resolved font
        /// in a paragraph. Both values must be finite and positive; null or invalid values
        /// preserve the normal font metrics.
        /// </summary>
        public Func<Paragraph, XFont, (double LineHeight, double InherentLineSpace)?>?
            MinimumLineMetricsForFont { get; set; }

        /// <summary>
        /// Replaces the distance above a paragraph's top border at the start of a formatting area,
        /// in points. Receives the physical page number and area Y coordinate. Null, negative, or
        /// non-finite values preserve the paragraph's configured distance and border stroke width.
        /// </summary>
        public Func<Paragraph, int, double, double?>? TopBorderDistanceAtAreaStart { get; set; }

        /// <summary>
        /// Adds a paint-only baseline offset, in points, for a paragraph. Receives the resolved
        /// fonts on its first rendered line. Null or a non-finite result preserves the baseline.
        /// This offset is added after the exact-spacing baseline adjustment when present.
        /// </summary>
        public Func<Paragraph, IReadOnlyList<XFont>, double?>? ParagraphPaintBaselineOffset { get; set; }

        /// <summary>
        /// Optionally adjusts the spacing and insets of tab leader characters in points.
        /// Returning null retains the default rendering.
        /// </summary>
        public Func<Paragraph, TabLeader, XFont, (double CharacterSpacing, double LeftInset, double RightInset)?>? TabLeaderLayout { get; set; }

        /// <summary>
        /// Adjusts the next word's leading position after a breakable blank, in points.
        /// The adjustment affects line fitting and painting but never applies at a line start.
        /// Null or a non-finite value leaves the default spacing unchanged.
        /// </summary>
        public Func<Paragraph, string, XFont, double?>? LeadingKerningAfterBlank { get; set; }

        /// <summary>
        /// Adjusts the spacing before a word's final glyph in points during both layout and painting.
        /// Null or a non-finite value leaves the word unchanged.
        /// </summary>
        public Func<Paragraph, string, XFont, double?>? TrailingGlyphKerning { get; set; }

        /// <summary>
        /// Uses the font's OpenType underline thickness and position when no
        /// UnderlineGeometry callback result is supplied. Defaults to false.
        /// Font metrics take precedence over the global stroke-width and offset factors.
        /// Missing or nonpositive thickness retains the existing geometry settings.
        /// </summary>
        public bool UseFontUnderlineMetrics { get; set; }

        /// <summary>Optional underline offset as a fraction of font descent. The default is 0.33.</summary>
        public double? UnderlineVerticalOffsetFactor { get; set; }

        /// <summary>
        /// Selects a paint-only baseline offset in points for a resolved font in an exact-spaced paragraph.
        /// Null or a non-finite value leaves its baseline unchanged.
        /// </summary>
        public Func<Paragraph, XFont, double?>? ExactFontBaselineOffset { get; set; }

        /// <summary>Optional font-specific underline factors; null uses the renderer-wide factors.</summary>
        public Func<Font, (double StrokeWidthFactor, double VerticalOffsetFactor)?>? UnderlineGeometry { get; set; }

        /// <summary>
        /// Adjusts a word's measured width in points for paragraph layout. The callback
        /// runs during formatting only; it does not change painted glyph advances.
        /// Return null to use the measured width. Non-finite adjustments and resulting
        /// widths of zero or less are ignored.
        /// </summary>
        public Func<Paragraph, string, XFont, double?>? TextWidthAdjustment { get; set; }
        /// <summary>
        /// Optional vertical paint offset in points for an inline image; positive values move it down.
        /// </summary>
        public Func<Image, double?>? InlineImageVerticalOffset { get; set; }

        /// <summary>
        /// Supplies a minimum top-border space, in points, for a table fragment starting at the given row.
        /// The rendered border width is unchanged.
        /// </summary>
        public Func<Table, int, double?>? TableFragmentTopBorderReservation { get; set; }

        /// <summary>
        /// Optionally supplies a cell's paint and content rectangles after table
        /// layout. Null preserves the renderer's normal placement.
        /// </summary>
        public Func<Cell, TableCellPlacementContext, TableCellPlacement?>? TableCellPlacement { get; set; }

        /// <summary>
        /// Selects text frames whose content is clipped to the frame rectangle when
        /// <see cref="TextFrame.ClipContent"/> has not been set.
        /// </summary>
        public Func<TextFrame, bool>? ClipTextFrameContent { get; set; }

        /// <summary>
        /// Selects text frames to render before other page content when
        /// <see cref="TextFrame.Layer"/> has not been set.
        /// </summary>
        public Func<TextFrame, bool>? TextFrameBehindContent { get; set; }

        /// <summary>
        /// Selects a shape's polygonal wrap contour in normalized shape coordinates.
        /// </summary>
        public Func<Shape, IReadOnlyList<XPoint>?>? WrapContour { get; set; }

        /// <summary>
        /// Selects the effective contour band height for a shape and line.
        /// </summary>
        public Func<Shape, double, double?>? WrapContourBandHeight { get; set; }

        /// <summary>
        /// Selects the effective contour band height using the natural line font height.
        /// </summary>
        public Func<Shape, double, double, double?>? WrapContourLineFontBandHeight { get; set; }

        /// <summary>
        /// Selects the height used when testing whether a line fits beside a shape.
        /// </summary>
        public Func<Shape, double, double, double?>? WrapLineFitHeight { get; set; }

        /// <summary>
        /// Gets the minimum fraction of normal space width allowed when fitting
        /// the final word of a justified paragraph. Null or values outside 0 to 1
        /// disable contraction for that paragraph.
        /// </summary>
        public Func<Paragraph, double?>? JustifiedSpaceShrinkFactorForParagraph { get; set; }

        /// <summary>
        /// Gets the minimum fraction of normal space width allowed when fitting
        /// a nonfinal word on a justified line. Null or values outside the exclusive
        /// interval (0, 1) disable contraction. The final word uses
        /// <see cref="JustifiedSpaceShrinkFactorForParagraph"/> instead.
        /// </summary>
        public Func<Paragraph, double?>? JustifiedNonterminalSpaceShrinkFactorForParagraph { get; set; }

        /// <summary>
        /// Optionally caps the space-contraction allowance for the final word of a
        /// justified paragraph. The cap is the greater of the complete word width
        /// times WordWidthFraction and a space in the final fragment's font times
        /// SpaceWidthFloor, in points. It never increases the space-based allowance.
        /// Null or a tuple containing a negative or nonfinite value leaves that
        /// allowance unchanged. A (0, 0) cap disables contraction for that word.
        /// </summary>
        public Func<Paragraph, (double WordWidthFraction, double SpaceWidthFloor)?>?
            JustifiedTerminalWordOverrunLimitForParagraph { get; set; }

        /// <summary>
        /// Optionally caps space contraction for nonfinal words of justified
        /// paragraphs. Uses the same units and validation as
        /// <see cref="JustifiedTerminalWordOverrunLimitForParagraph"/>.
        /// Neither callback enables contraction by itself; configure the corresponding
        /// space shrink factor. Words split across formatting runs use their total width.
        /// </summary>
        public Func<Paragraph, (double WordWidthFraction, double SpaceWidthFloor)?>?
            JustifiedNonterminalWordOverrunLimitForParagraph { get; set; }


        /// <summary>
        /// Marks explicit space characters whose width should be dropped when the
        /// following word would not fit on the current line. Other explicit spaces
        /// retain their normal MigraDoc behavior.
        /// </summary>
        public Func<Paragraph, Character, bool>? CollapsibleSpaceBeforeWrap { get; set; }

        /// <summary>
        /// Prepares this instance for rendering.
        /// Commit renderEvents to allow RenderTextEvent calls.
        /// </summary>
        public void PrepareDocument(RenderEvents? renderEvents = null)
        {
            var visitor = new PdfFlattenVisitor();
            visitor.Visit(_document);
            _previousListNumbers[ListType.NumberList1] = 0;
            _previousListNumbers[ListType.NumberList2] = 0;
            _previousListNumbers[ListType.NumberList3] = 0;
            _formattedDocument = new FormattedDocument(_document, this);
#if PSGFX
            var gfx = (XGraphics)null!;  //XGraphics.CreateMeasureContext(new XSize(2000, 2000), XGraphicsUnit.Point, XPageDirection.Downwards, renderEvents);
#else
            var gfx = XGraphics.CreateMeasureContext(new XSize(2000, 2000), XGraphicsUnit.Point, XPageDirection.Downwards, renderEvents);
#endif
            _previousListInfo = null;
            _formattedDocument.Format(gfx);
        }

        /// <summary>
        /// Occurs while the document is being prepared (can be used to show a progress bar).
        /// </summary>
        public event PrepareDocumentProgressEventHandler? PrepareDocumentProgress;

        /// <summary>
        /// Allows applications to display a progress indicator while PrepareDocument() is being executed.
        /// </summary>
        /// <param name="value"></param>
        /// <param name="maximum"></param>
        internal virtual void OnPrepareDocumentProgress(int value, int maximum)
        {
            if (PrepareDocumentProgress != null)
            {
                // Invokes the delegates.
                var e = new PrepareDocumentProgressEventArgs(value, maximum);
                PrepareDocumentProgress(this, e);
            }
        }

        /// <summary>
        /// Gets a value indicating whether this instance supports PrepareDocumentProgress.
        /// </summary>
        public bool HasPrepareDocumentProgress => PrepareDocumentProgress != null;

        /// <summary>
        /// Gets the formatted document of this instance. Will be null if document was not yet prepared.
        /// </summary>
        public FormattedDocument FormattedDocument
        {
            [return: MaybeNull]
            get { return _formattedDocument!; }
        }
        FormattedDocument? _formattedDocument;

        /// <summary>
        /// Selects top-level text frames to omit from automatic body, header, and footer
        /// painting. Layout and pagination are unchanged. Null or false paints normally.
        /// Evaluated during RenderPage with CurrentRenderPage set; not evaluated during
        /// preparation or explicit RenderPreparedObject calls. Callers can paint the
        /// retained render infos explicitly in their own order. Nested frames are not selected.
        /// </summary>
        public Func<TextFrame, bool>? DeferTextFramePaint { get; set; }

        bool ShouldDeferFrame(RenderInfo info)
            => info.DocumentObject is TextFrame frame && DeferTextFramePaint?.Invoke(frame) == true;

        /// <summary>
        /// Paints caller-owned content after page headers and footers and before body
        /// content, including behind-content text frames. Receives the graphics object
        /// and one-based physical page number. Invoked only when RenderContent is
        /// selected, and never for automatically inserted blank pages.
        /// Graphics state is restored after the callback, including on exceptions.
        /// Exceptions propagate to the caller. This callback does not affect layout.
        /// </summary>
        public Action<XGraphics, int>? PaintBehindContent { get; set; }

        /// <summary>
        /// Renders a MigraDoc document to the specified graphics object.
        /// </summary>
        public void RenderPage(XGraphics gfx, int page)
            => RenderPage(gfx, page, PageRenderOptions.All);

        /// <summary>
        /// Renders a MigraDoc document to the specified graphics object.
        /// </summary>
        public void RenderPage(XGraphics gfx, int page, PageRenderOptions options)
        {
            if (_formattedDocument?.IsEmptyPage(page) ?? NRT.ThrowOnNull<bool>("FormattedDocument is null because document was not prepared yet"))
                return;

            var previousPage = CurrentRenderPage;
            CurrentRenderPage = page;
            try
            {
                var fieldInfos = _formattedDocument.GetFieldInfos(page);

                fieldInfos.Date = PrintDate ?? DateTimeOffset.Now;

                if ((options & PageRenderOptions.RenderHeader) == PageRenderOptions.RenderHeader)
                    RenderHeader(gfx, page);
                if ((options & PageRenderOptions.RenderFooter) == PageRenderOptions.RenderFooter)
                    RenderFooter(gfx, page);

                if ((options & PageRenderOptions.RenderContent) == PageRenderOptions.RenderContent)
                {
                    if (PaintBehindContent is { } paintBehindContent)
                    {
                        var state = gfx.Save();
                        try
                        {
                            paintBehindContent(gfx, page);
                        }
                        finally
                        {
                            gfx.Restore(state);
                        }
                    }
                    var renderInfos = _formattedDocument.GetRenderInfos(page);
                    if (renderInfos != null)
                    {
                        int count = renderInfos.Length;
                        if (renderInfos.Any(info => info.DocumentObject is TextFrame frame &&
                                frame.Values.Layer is not null) || TextFrameBehindContent is not null)
                        {
                            var layers = new TextFrameLayer[count];
                            for (int idx = 0; idx < count; idx++)
                            {
                                if (renderInfos[idx].DocumentObject is TextFrame frame)
                                    layers[idx] = frame.Values.Layer ??
                                        (TextFrameBehindContent?.Invoke(frame) == true
                                            ? TextFrameLayer.BehindContent
                                            : TextFrameLayer.Normal);
                            }
                            for (int idx = 0; idx < count; idx++)
                                if (layers[idx] == TextFrameLayer.BehindContent && !ShouldDeferFrame(renderInfos[idx]))
                                    Renderer.Create(gfx, this, renderInfos[idx], fieldInfos).Render();
                            for (int idx = 0; idx < count; idx++)
                                if (layers[idx] == TextFrameLayer.Normal && !ShouldDeferFrame(renderInfos[idx]))
                                    Renderer.Create(gfx, this, renderInfos[idx], fieldInfos).Render();
                            for (int idx = 0; idx < count; idx++)
                                if (layers[idx] == TextFrameLayer.AboveContent && !ShouldDeferFrame(renderInfos[idx]))
                                    Renderer.Create(gfx, this, renderInfos[idx], fieldInfos).Render();
                        }
                        else
                        {
                            for (int idx = 0; idx < count; idx++)
                            {
                                var renderInfo = renderInfos[idx];
                                if (ShouldDeferFrame(renderInfo))
                                    continue;
                                var renderer = Renderer.Create(gfx, this, renderInfo, fieldInfos);
                                renderer.Render();
                            }
                        }
                    }
                }
            }
            finally
            {
                CurrentRenderPage = previousPage;
            }
        }

        /// <summary>
        /// Gets the one-based physical page currently being painted by RenderPage
        /// or RenderPreparedObject,
        /// or zero outside page rendering. Displayed page-number restarts do not
        /// affect this value. Nested calls restore the enclosing page, including
        /// when rendering throws. Automatically inserted blank pages do not establish
        /// a rendering context. RenderObject does not change this value.
        /// </summary>
        public int CurrentRenderPage { get; private set; }

        /// <summary>
        /// Gets the document objects that get rendered on the specified page.
        /// </summary>
        public DocumentObject[] GetDocumentObjectsFromPage(int page)
        {
            var renderInfos = FormattedDocument.GetRenderInfos(page);
            if (renderInfos == null)
                return [];

            int count = renderInfos.Length;
            var documentObjects = new DocumentObject[count];
            for (int idx = 0; idx < count; idx++)
                documentObjects[idx] = renderInfos[idx].DocumentObject;
            return documentObjects;
        }

        /// <summary>
        /// Gets the render information for document objects that get rendered on the specified page.
        /// </summary>
        public RenderInfo[]? GetRenderInfoFromPage(int page)
            => FormattedDocument.GetRenderInfos(page);

        /// <summary>
        /// Gets render information for the header selected for the physical page.
        /// Returns an empty array when the page has no header.
        /// </summary>
        public RenderInfo[] GetHeaderRenderInfos(int page)
            => FormattedDocument.GetFormattedHeader(page)?.GetRenderInfos() ?? [];

        /// <summary>
        /// Gets render information for the footer selected for the physical page.
        /// Returns an empty array when the page has no footer.
        /// </summary>
        public RenderInfo[] GetFooterRenderInfos(int page)
            => FormattedDocument.GetFormattedFooter(page)?.GetRenderInfos() ?? [];

        /// <summary>
        /// Gets nested render information for a continuation fragment of a paginated text frame.
        /// Returns null for a non-frame object or the first fragment.
        /// </summary>
        public RenderInfo[]? GetTextFrameContinuationRenderInfos(RenderInfo renderInfo)
        {
            if (renderInfo.FormatInfo is not TextFrameFormatInfo { FragmentIndex: > 0 } frameInfo)
                return null;
            return frameInfo.FormattedTextFrame?.GetRenderInfos(frameInfo.FragmentIndex);
        }

        /// <summary>
        /// Renders a single object to the specified graphics object at the given point.
        /// </summary>
        /// <param name="graphics">The graphics object to render on.</param>
        /// <param name="xPosition">The left position of the rendered object.</param>
        /// <param name="yPosition">The top position of the rendered object.</param>
        /// <param name="width">The width.</param>
        /// <param name="documentObject">The document object to render. Can be paragraph, table, or shape.</param>
        /// <remarks>
        /// During RenderPage or RenderPreparedObject, uses the active physical page's
        /// isolated field context. Outside painting, renders without page context as before.
        /// Use the explicit-page overload to select another page. This function is still experimental.
        /// </remarks>
        public void RenderObject(XGraphics graphics, XUnitPt xPosition, XUnitPt yPosition, XUnitPt width, DocumentObject documentObject)
        {
            if (CurrentRenderPage > 0)
                RenderObject(graphics, xPosition, yPosition, width, documentObject, CurrentRenderPage);
            else
                RenderObjectCore(graphics, xPosition, yPosition, width, documentObject, null);
        }

        /// <summary>
        /// Renders an object using the field context of a prepared physical page.
        /// </summary>
        /// <param name="graphics">The graphics object to render on.</param>
        /// <param name="xPosition">The left position of the rendered object.</param>
        /// <param name="yPosition">The top position of the rendered object.</param>
        /// <param name="width">The available width.</param>
        /// <param name="documentObject">The paragraph, table, or shape to render.</param>
        /// <param name="page">The one-based physical page supplying page, section, bookmark, and date fields.</param>
        /// <remarks>
        /// Call PrepareDocument first. Automatically inserted blank pages have no field context.
        /// The object is formatted anew at the supplied position and width rather than reusing its prepared layout.
        /// </remarks>
        public void RenderObject(XGraphics graphics, XUnitPt xPosition, XUnitPt yPosition, XUnitPt width,
            DocumentObject documentObject, int page)
        {
            var formattedDocument = _formattedDocument ??
                throw new InvalidOperationException("Prepare the document before rendering an object with a page context.");
            if (page < 1 || page > formattedDocument.PageCount)
                throw new ArgumentOutOfRangeException(nameof(page));
            if (formattedDocument.IsEmptyPage(page))
                throw new ArgumentException("An automatically inserted blank page has no field context.", nameof(page));
            var fieldInfos = formattedDocument.GetFieldInfos(page).CreateSnapshot();
            fieldInfos.Date = PrintDate ?? DateTimeOffset.Now;
            RenderObjectCore(graphics, xPosition, yPosition, width, documentObject, fieldInfos);
        }

        void RenderObjectCore(XGraphics graphics, XUnitPt xPosition, XUnitPt yPosition, XUnitPt width,
            DocumentObject documentObject, FieldInfos? fieldInfos)
        {
            if (graphics == null)
                throw new ArgumentNullException(nameof(graphics));

            if (documentObject == null)
                throw new ArgumentNullException(nameof(documentObject));

            if (documentObject is not Shape &&
                documentObject is not Table &&
                documentObject is not Paragraph)
                throw new ArgumentException(MdPdfMsgs.ObjectNotRenderable(documentObject.GetType().Name).Message);

            var renderer = Renderer.Create(graphics, this, documentObject, fieldInfos);
            renderer!.Format(new Rectangle(xPosition, yPosition, width, Single.MaxValue), null);

            RenderInfo renderInfo = renderer.RenderInfo;
            renderInfo.LayoutInfo.ContentArea.X = xPosition;
            renderInfo.LayoutInfo.ContentArea.Y = yPosition;

            renderer = Renderer.Create(graphics, this, renderer.RenderInfo, fieldInfos);
            renderer.Render();
        }

        /// <summary>
        /// Paints a top-level body, header, or footer fragment from the current prepared
        /// layout without reformatting it. The fragment must belong to the specified
        /// one-based physical page of this renderer's current preparation.
        /// </summary>
        /// <param name="graphics">The graphics object to paint on.</param>
        /// <param name="renderInfo">A fragment returned by this renderer's page/header/footer render-info accessors.</param>
        /// <param name="page">The physical page supplying field context.</param>
        /// <param name="verticalOffset">A finite offset in points, positive downward, applied to the entire fragment.</param>
        /// <remarks>
        /// Coordinates are the stored layout coordinates, before footer bottom alignment.
        /// Graphics state and CurrentRenderPage are restored even when painting throws.
        /// </remarks>
        public void RenderPreparedObject(XGraphics graphics, RenderInfo renderInfo, int page,
            XUnitPt verticalOffset)
        {
            if (graphics == null)
                throw new ArgumentNullException(nameof(graphics));
            if (renderInfo == null)
                throw new ArgumentNullException(nameof(renderInfo));
            if (double.IsNaN(verticalOffset.Point) || double.IsInfinity(verticalOffset.Point))
                throw new ArgumentOutOfRangeException(nameof(verticalOffset));
            var formattedDocument = _formattedDocument ??
                throw new InvalidOperationException("Prepare the document before painting a prepared object.");
            if (page < 1 || page > formattedDocument.PageCount)
                throw new ArgumentOutOfRangeException(nameof(page));
            if (formattedDocument.IsEmptyPage(page))
                throw new ArgumentException("An automatically inserted blank page has no prepared objects.", nameof(page));
            if (!(GetRenderInfoFromPage(page)?.Contains(renderInfo) ?? false) &&
                !GetHeaderRenderInfos(page).Contains(renderInfo) && !GetFooterRenderInfos(page).Contains(renderInfo))
                throw new ArgumentException("The fragment does not belong to this renderer's prepared page.", nameof(renderInfo));

            var fields = formattedDocument.GetFieldInfos(page).CreateSnapshot();
            fields.Date = PrintDate ?? DateTimeOffset.Now;
            var state = graphics.Save();
            var previousPage = CurrentRenderPage;
            CurrentRenderPage = page;
            try
            {
                if (verticalOffset != 0)
                    graphics.TranslateTransform(0, verticalOffset.Point);
                Renderer.Create(graphics, this, renderInfo, fields).Render();
            }
            finally
            {
                CurrentRenderPage = previousPage;
                graphics.Restore(state);
            }
        }

        /// <summary>
        /// Gets or sets the working directory for rendering.
        /// </summary>
        public string WorkingDirectory { get; set; } = null!;

        void RenderHeader(XGraphics graphics, int page)
        {
            var formattedHeader = FormattedDocument.GetFormattedHeader(page);
            if (formattedHeader == null)
                return;

            /* var headerArea = */
            FormattedDocument.GetHeaderArea(page);  // BUG_OLD Does it return something?
            RenderInfo[] renderInfos = formattedHeader.GetRenderInfos();
            var fieldInfos = FormattedDocument.GetFieldInfos(page);
            foreach (RenderInfo renderInfo in renderInfos)
            {
                if (ShouldDeferFrame(renderInfo))
                    continue;
                var renderer = Renderer.Create(graphics, this, renderInfo, fieldInfos);
                renderer.Render();
            }
        }

        void RenderFooter(XGraphics graphics, int page)
        {
            var formattedFooter = FormattedDocument.GetFormattedFooter(page);
            if (formattedFooter == null)
                return;

            var footerArea = FormattedDocument.GetFooterArea(page);
            var renderInfos = formattedFooter.GetRenderInfos();

            // The footer is bottom-aligned and grows with its contents. topY specifies the Y position where the footer begins.
            XUnitPt topY = footerArea.Y + footerArea.Height - RenderInfo.GetTotalHeight(renderInfos);
            // offsetY specifies the offset (amount of movement) for all footer items. It’s the difference between topY and the position calculated for the first item.
            XUnitPt offsetY = renderInfos.Length > 0
                ? renderInfos[0].LayoutInfo.ContentArea.Y - topY : 0;

            var fieldInfos = FormattedDocument.GetFieldInfos(page);
            foreach (RenderInfo renderInfo in renderInfos)
            {
                if (ShouldDeferFrame(renderInfo))
                    continue;
                var renderer = Renderer.Create(graphics, this, renderInfo, fieldInfos);
                var savedY = renderer.RenderInfo.LayoutInfo.ContentArea.Y;
                // Apply offsetY only to items that do not have an absolute position.
                if (renderer.RenderInfo.LayoutInfo.Floating != Floating.None)
                    renderer.RenderInfo.LayoutInfo.ContentArea.Y -= offsetY;
                renderer.Render();
                renderer.RenderInfo.LayoutInfo.ContentArea.Y = savedY;
            }
        }

        internal static void AddOutline(int level, string title, PdfPage? destinationPage, XPoint position)
        {
            if (level < 1 || destinationPage == null)
                return;

            var document = destinationPage.Owner;
            Debug.Assert(document != null);
            //if (document == null)
            //    return;

            var outlines = document.Outlines;
            while (--level > 0)
            {
                int count = outlines.Count;
                if (count == 0)
                {
                    // You cannot add empty bookmarks to PDF. So we use blank here.
                    var outline = AddOutline(outlines, " ", destinationPage, position);
                    outlines = outline.Outlines;
                }
                else
                    outlines = outlines[count - 1].Outlines;
            }
            AddOutline(outlines, title, destinationPage, position);
        }

        static PdfOutline AddOutline(PdfOutlineCollection outlines, string title, PdfPage destinationPage, XPoint position)
        {
            var outline = outlines.Add(title, destinationPage, true);
            outline.Left = position.X;
            outline.Top = position.Y;
            return outline;
        }

        /// <summary>
        /// Measures speculative content without advancing the document's list sequence.
        /// </summary>
        internal void PreserveListNumbering(Action format)
        {
            var previousListInfo = _previousListInfo;
            var previousListNumbers = new Dictionary<ListType, int>(_previousListNumbers);
            try
            {
                format();
            }
            finally
            {
                _previousListInfo = previousListInfo;
                _previousListNumbers.Clear();
                foreach (var entry in previousListNumbers)
                    _previousListNumbers.Add(entry.Key, entry.Value);
            }
        }

        internal int NextListNumber(ListInfo listInfo)
        {
            var listType = listInfo.ListType;
            bool isNumberList = listType == ListType.NumberList1 ||
              listType == ListType.NumberList2 ||
              listType == ListType.NumberList3;

            int listNumber = Int32.MinValue;
            if (listInfo == _previousListInfo)
            {
                if (isNumberList)
                    return _previousListNumbers[listType];
                return listNumber;
            }

            //bool listTypeChanged = _previousListInfo == null || _previousListInfo.ListType != listType;

            if (isNumberList)
            {
                listNumber = 1;
                if (/*!listTypeChanged &&*/ (listInfo.Values.ContinuePreviousList is null || listInfo.ContinuePreviousList))
                    listNumber = _previousListNumbers[listType] + 1;

                _previousListNumbers[listType] = listNumber;
            }

            _previousListInfo = listInfo;
            return listNumber;
        }

        ListInfo? _previousListInfo;
        readonly Dictionary<ListType, int> _previousListNumbers = new(3);
        readonly Document _document = document;

        /// <summary>
        /// Gets or sets the print date, i.e. the rendering date.
        /// </summary>
        public DateTimeOffset? PrintDate { get; set; } = null;  // TODO: Breaking change, is now 'DateTimeOffset?'.

        internal PredefinedFontsAndChars FontsAndChars => _fontsAndChars ??= new ();
        PredefinedFontsAndChars? _fontsAndChars;

        internal class PredefinedFontsAndChars
        {
            /// <summary>
            /// Creates and caches the XFont used for error messages as they are of a fixed size and style.
            /// </summary>
            internal XFont ErrorFont => _errorFont ??= 
                CreateFont(MigraDoc.PredefinedFontsAndChars.ErrorFontName, 8, XFontStyleEx.Regular, "predefined error font");
            XFont? _errorFont;

            internal PredefinedBullets Bullets => _bullets ??= new();
            PredefinedBullets? _bullets;

            static XFont CreateFont(string familyName, double emSize, XFontStyleEx style, string propertyDescription)
            {
                try
                {
#if PSGFX
                    //return new(familyName, emSize, style);
                    return null!;
#else
                    return new(familyName, emSize, style);
#endif
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"The font '{familyName}' cannot be resolved for {propertyDescription}. Use another font name or fix your font resolver. See {UrlLiterals.LinkToMigraDocFontResolving} and {UrlLiterals.LinkToFontResolving} for further information.", ex);
                }
            }

            /// <summary>
            /// Creates all fixed predefined fonts for early failing.
            /// Bullet fonts are not created for early failing as their style and therefore the fontface depends on the format of the lists in the document.
            /// </summary>
            internal void CreateAllFixedFonts()
            {
                _ = ErrorFont;
            }
            
            internal class PredefinedBullets
            {
                internal XFont GetLevel1Font(double emSize, XFontStyleEx style) => 
                    CreateFont(MigraDoc.PredefinedFontsAndChars.Bullets.Level1FontName, emSize, style, "predefined bullet level 1 font");

                internal char Level1Character => MigraDoc.PredefinedFontsAndChars.Bullets.Level1Character;

                internal XFont GetLevel2Font(double emSize, XFontStyleEx style) => 
                    CreateFont(MigraDoc.PredefinedFontsAndChars.Bullets.Level2FontName, emSize, style, "predefined bullet level 2 font");

                internal char Level2Character => MigraDoc.PredefinedFontsAndChars.Bullets.Level2Character;
                
                internal XFont GetLevel3Font(double emSize, XFontStyleEx style) => 
                    CreateFont(MigraDoc.PredefinedFontsAndChars.Bullets.Level3FontName, emSize, style, "predefined bullet level 3 font");

                internal char Level3Character => MigraDoc.PredefinedFontsAndChars.Bullets.Level3Character;
            }
        }

        /// <summary>
        /// Arguments for the PrepareDocumentProgressEvent which is called while a document is being prepared (you can use this to display a progress bar).
        /// </summary>
        /// <remarks>
        /// Initializes a new instance of the <see cref="PrepareDocumentProgressEventArgs"/> class.
        /// </remarks>
        /// <param name="value">The current step in document preparation.</param>
        /// <param name="maximum">The latest step in document preparation.</param>
        public class PrepareDocumentProgressEventArgs(int value, int maximum) : EventArgs
        {
            /// <summary>
            /// Indicates the current step reached in document preparation.
            /// </summary>
            public int Value = value;

            /// <summary>
            /// Indicates the final step in document preparation. The quotient of Value and Maximum can be used to calculate a percentage (e.g. for use in a progress bar).
            /// </summary>
            public int Maximum = maximum;
        }

        /// <summary>
        /// The event handler that is being called for the PrepareDocumentProgressEvent event.
        /// </summary>
        public delegate void PrepareDocumentProgressEventHandler(object sender, PrepareDocumentProgressEventArgs e);

        internal int ProgressMaximum;
        internal int ProgressCompleted;
    }
}
