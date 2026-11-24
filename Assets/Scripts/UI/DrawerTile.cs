using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Side-drawer menu tiles (#486): each drawer button holds a <see cref="GlyphIcon"/> and a small caption label
    /// (GameHUD.uxml, <c>drawer-tile</c>). Code that used to set a drawer button's text sets the caption instead.
    /// </summary>
    public static class DrawerTile
    {
        public const string CaptionClass = "drawer-tile__caption";

        /// <summary>The tile's caption label, or null for a plain button.</summary>
        public static Label Caption(VisualElement button) => button?.Q<Label>(className: CaptionClass);

        /// <summary>Sets the caption (or the button's own text when it isn't a tile).</summary>
        public static void SetCaption(Button button, string text)
        {
            if (button == null) return;
            var caption = Caption(button);
            if (caption != null) caption.text = text;
            else button.text = text;
        }
    }
}
