// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

using PdfSharp.Drawing;
using MigraDoc.DocumentObjectModel.Shapes;

namespace MigraDoc.Rendering
{
    /// <summary>
    /// Renders text frames.
    /// </summary>
    class TextFrameRenderer : ShapeRenderer
    {
        internal TextFrameRenderer(XGraphics gfx, TextFrame textFrame, FieldInfos? fieldInfos)
            : base(gfx, textFrame, fieldInfos)
        {
            _textFrame = textFrame;
            var renderInfo = new TextFrameRenderInfo
            {
                DocumentObject = _shape
            };
            _renderInfo = renderInfo;
        }

        internal TextFrameRenderer(XGraphics gfx, RenderInfo renderInfo, FieldInfos? fieldInfos)
            : base(gfx, renderInfo, fieldInfos)
        {
            _textFrame = (TextFrame)renderInfo.DocumentObject;
        }

        internal override void Format(Area area, FormatInfo? previousFormatInfo)
        {
            FormattedTextFrame formattedTextFrame = new FormattedTextFrame(_textFrame, _documentRenderer, _fieldInfos);
            formattedTextFrame.Format(_gfx);
            ((TextFrameFormatInfo)_renderInfo.FormatInfo).FormattedTextFrame = formattedTextFrame;
            base.Format(area, previousFormatInfo);
        }

        //internal override LayoutInfo InitialLayoutInfo => base.InitialLayoutInfo;  // redundant 

        internal override void Render()
        {
            var angle = _textFrame.Rotation;
            if (angle == 0)
            {
                RenderFilling();
                RenderContent();
                RenderLine();
                return;
            }

            var area = _renderInfo.LayoutInfo.ContentArea;
            var centerX = area.X + area.Width / 2;
            var centerY = area.Y + area.Height / 2;
            var state = _gfx.Save();
            try
            {
                _gfx.TranslateTransform(centerX, centerY);
                _gfx.RotateTransform(angle);
                _gfx.TranslateTransform(-centerX, -centerY);
                RenderFilling();
                RenderContent();
                RenderLine();
            }
            finally
            {
                _gfx.Restore(state);
            }
        }

        void RenderContent()
        {
            FormattedTextFrame formattedTextFrame = ((TextFrameFormatInfo)_renderInfo.FormatInfo).FormattedTextFrame;
            var renderInfos = formattedTextFrame.GetRenderInfos();
            if (renderInfos == null)
                return;

            if (_documentRenderer.ClipTextFrameContent?.Invoke(_textFrame) == true)
            {
                var clipState = _gfx.Save();
                try
                {
                    var area = _renderInfo.LayoutInfo.ContentArea;
                    _gfx.IntersectClip(new XRect(area.X.Point, area.Y.Point, area.Width.Point, area.Height.Point));
                    RenderFrameContent(renderInfos);
                }
                finally
                {
                    _gfx.Restore(clipState);
                }
                return;
            }

            RenderFrameContent(renderInfos);
        }

        void RenderFrameContent(RenderInfo[] renderInfos)
        {
            XGraphicsState state = Transform();
            RenderByInfos(renderInfos);
            ResetTransform(state);
        }

        XGraphicsState Transform()
        {
            Area frameContentArea = _renderInfo.LayoutInfo.ContentArea;
#if PSGFX
            var state = _gfx.Save();
#else
            XGraphicsState state = _gfx.Save();
#endif
            XUnitPt xPosition;
            XUnitPt yPosition;
            switch (_textFrame.Orientation)
            {
                case TextOrientation.Downward:
                case TextOrientation.Vertical:
                case TextOrientation.VerticalFarEast:
                    xPosition = frameContentArea.X + frameContentArea.Width;
                    yPosition = frameContentArea.Y;
                    _gfx.TranslateTransform(xPosition, yPosition);
                    _gfx.RotateTransform(90);
                    break;

                case TextOrientation.Upward:
                    state = _gfx.Save();
                    xPosition = frameContentArea.X;
                    yPosition = frameContentArea.Y + frameContentArea.Height;
                    _gfx.TranslateTransform(xPosition, yPosition);
                    _gfx.RotateTransform(-90);
                    break;

                default:
                    xPosition = frameContentArea.X;
                    yPosition = frameContentArea.Y;
                    _gfx.TranslateTransform(xPosition, yPosition);
                    break;
            }
            return state;
        }

        void ResetTransform(XGraphicsState? state)
        {
#if PSGFX
            if (state != null)
                _gfx.Restore(state.Value);
#else
            if (state != null)
                _gfx.Restore(state);
#endif
        }

        readonly TextFrame _textFrame;
    }
}
