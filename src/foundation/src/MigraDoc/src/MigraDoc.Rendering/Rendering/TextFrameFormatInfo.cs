// MigraDoc - Creating Documents on the Fly
// See the LICENSE file in the solution root for more information.

namespace MigraDoc.Rendering
{
    /// <summary>
    /// Formatting information for text frames.
    /// </summary>
    sealed class TextFrameFormatInfo : ShapeFormatInfo
    {
        internal FormattedTextFrame FormattedTextFrame = default!;
        internal int FragmentIndex;
        internal int FragmentCount;

        internal override bool IsStarting => FragmentCount == 0 ? base.IsStarting : FragmentIndex == 0;
        internal override bool IsEnding => FragmentCount == 0 ? base.IsEnding : FragmentIndex == FragmentCount - 1;
        internal override bool IsComplete => FragmentCount == 0 ? base.IsComplete : IsEnding;
        internal override bool StartingIsComplete => FragmentCount == 0 ? base.StartingIsComplete : true;
        internal override bool EndingIsComplete => FragmentCount == 0 ? base.EndingIsComplete : IsEnding;
        internal override bool IsEmpty => FragmentCount == 0 && base.IsEmpty;
    }
}
