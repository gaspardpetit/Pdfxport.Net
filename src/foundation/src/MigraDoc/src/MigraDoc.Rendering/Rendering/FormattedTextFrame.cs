// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using MigraDoc.DocumentObjectModel.Shapes;
using PdfSharp.Drawing;

namespace MigraDoc.Rendering
{
    /// <summary>
    /// Represents a formatted text frame.
    /// </summary>
    class FormattedTextFrame : IAreaProvider
    {
        internal FormattedTextFrame(TextFrame textframe, DocumentRenderer documentRenderer, FieldInfos? fieldInfos,
            XUnitPt? firstAreaHeight = null, XUnitPt? continuationAreaHeight = null)
        {
            _textFrame = textframe;
            _fieldInfos = fieldInfos;
            _documentRenderer = documentRenderer;
            _firstAreaHeight = firstAreaHeight;
            _continuationAreaHeight = continuationAreaHeight;
        }

        internal void Format(XGraphics gfx)
        {
            _gfx = gfx;
            _isFirstArea = true;
            _areaRenderInfos.Clear();
            _formatter = new TopDownFormatter(this, _documentRenderer, _textFrame.Elements);
            _formatter.FormatOnAreas(gfx, false);
            _contentHeight = RenderInfo.GetTotalHeight(_firstAreaHeight is null
                ? GetRenderInfos() : GetRenderInfos(0));
        }

        Area? IAreaProvider.GetNextArea()
        {
            if (_isFirstArea)
            {
                if (_firstAreaHeight is not null)
                    _isFirstArea = false;
                return BoundedContentRect(_firstAreaHeight);
            }
            return _continuationAreaHeight is { } height ? BoundedContentRect(height) : null;
        }

        Area? IAreaProvider.ProbeNextArea()
            => _continuationAreaHeight is { } height ? BoundedContentRect(height) : null;

        FieldInfos IAreaProvider.AreaFieldInfos => _fieldInfos ?? NRT.ThrowOnNull<FieldInfos>();

        void IAreaProvider.StoreRenderInfos(List<RenderInfo> renderInfos)
        {
            _renderInfos = renderInfos;
            _areaRenderInfos.Add(renderInfos.ToArray());
        }

        bool IAreaProvider.IsAreaBreakBefore(LayoutInfo layoutInfo) => false;

        internal RenderInfo[]? GetRenderInfos()
        {
            if (_renderInfos != null)
                return _renderInfos.ToArray();
            return null;
        }

        internal RenderInfo[]? GetRenderInfos(int areaIndex)
            => areaIndex < _areaRenderInfos.Count ? _areaRenderInfos[areaIndex] : null;

        internal int AreaCount => _areaRenderInfos.Count;

        internal XUnitPt GetAreaContentHeight(int areaIndex)
            => RenderInfo.GetTotalHeight(GetRenderInfos(areaIndex));

        Rectangle BoundedContentRect(XUnitPt? height)
        {
            var rectangle = CalcContentRect();
            return height is { } bounded
                ? new Rectangle(rectangle.X, rectangle.Y, rectangle.Width, bounded)
                : rectangle;
        }

        Rectangle CalcContentRect()
        {
            LineFormatRenderer lfr = new LineFormatRenderer(_textFrame.LineFormat, _gfx);
            XUnitPt lineWidth = lfr.GetWidth();
            XUnitPt width;
            XUnitPt xOffset = lineWidth / 2;
            XUnitPt yOffset = lineWidth / 2;

            if (_textFrame.Orientation == TextOrientation.Horizontal ||
              _textFrame.Orientation == TextOrientation.HorizontalRotatedFarEast)
            {
                width = _textFrame.Width.Point;
                xOffset += _textFrame.MarginLeft.Point;
                yOffset += _textFrame.MarginTop.Point;
                width -= xOffset;
                width -= _textFrame.MarginRight.Point + lineWidth / 2;
            }
            else
            {
                width = _textFrame.Height.Point;
                if (_textFrame.Orientation == TextOrientation.Upward)
                {
                    xOffset += _textFrame.MarginBottom.Point;
                    yOffset += _textFrame.MarginLeft.Point;
                    width -= xOffset;
                    width -= _textFrame.MarginTop.Point + lineWidth / 2;
                }
                else
                {
                    xOffset += _textFrame.MarginTop.Point;
                    yOffset += _textFrame.MarginRight.Point;
                    width -= xOffset;
                    width -= _textFrame.MarginBottom.Point + lineWidth / 2;
                }
            }
            XUnitPt height = double.MaxValue;
            return new Rectangle(xOffset, yOffset, width, height);
        }

        internal XUnitPt ContentHeight => _contentHeight;

        bool IAreaProvider.PositionVertically(LayoutInfo layoutInfo) => false;

        bool IAreaProvider.PositionHorizontally(LayoutInfo layoutInfo)
        {
            Rectangle rect = CalcContentRect();
            switch (layoutInfo.HorizontalAlignment)
            {
                case ElementAlignment.Near:
                    if (layoutInfo.Left != 0)
                    {
                        layoutInfo.ContentArea.X += layoutInfo.Left;
                        return true;
                    }
                    return false;

                case ElementAlignment.Far:
                    XUnitPt xPos = rect.X + rect.Width;
                    xPos -= layoutInfo.ContentArea.Width;
                    xPos -= layoutInfo.MarginRight;
                    layoutInfo.ContentArea.X = xPos;
                    return true;

                case ElementAlignment.Center:
                    xPos = rect.Width;
                    xPos -= layoutInfo.ContentArea.Width;
                    xPos = rect.X + xPos / 2;
                    layoutInfo.ContentArea.X = xPos;
                    return true;
            }
            return false;
        }

        readonly TextFrame _textFrame;
        readonly FieldInfos? _fieldInfos;
        TopDownFormatter? _formatter;
        List<RenderInfo>? _renderInfos;
        readonly List<RenderInfo[]> _areaRenderInfos = new();
        readonly XUnitPt? _firstAreaHeight;
        readonly XUnitPt? _continuationAreaHeight;
        XGraphics _gfx = null!;
        bool _isFirstArea;
        XUnitPt _contentHeight;
        readonly DocumentRenderer _documentRenderer;
    }
}
