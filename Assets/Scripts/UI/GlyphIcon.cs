using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Vector-drawn UI icon (close / check / rotate / flip) used in place of the ✕ ✓ ↻ ⇄ text glyphs.
    /// The Android default font has none of those glyphs, so text buttons rendered blank on device
    /// (issue #165, docs/mobile-icon-font-fix.md, Option B). Drawing with Painter2D removes the font
    /// dependency entirely and stays crisp at any DPI.
    ///
    /// The stroke uses the inherited text <c>color</c>, so existing button color rules (and
    /// <c>:hover</c>) style the icon too. Size comes from USS (<c>.glyph-icon</c> rules in components.uss).
    ///
    /// UXML: <c>&lt;MobileIdleBuilder.GlyphIcon icon="Close" /&gt;</c> inside a button with empty text
    /// and the <c>glyph-host</c> class. From code: <see cref="SetOn"/> / <see cref="ClearFrom"/>.
    /// </summary>
    [UxmlElement]
    public partial class GlyphIcon : VisualElement
    {
        public enum Kind { Close, Check, Rotate, Flip }

        public const string UssClass     = "glyph-icon";
        public const string HostUssClass = "glyph-host";

        Kind _icon;

        [UxmlAttribute("icon")]
        public Kind Icon
        {
            get => _icon;
            set { _icon = value; MarkDirtyRepaint(); }
        }

        public GlyphIcon()
        {
            AddToClassList(UssClass);
            pickingMode = PickingMode.Ignore;   // taps go to the host button
            generateVisualContent += Draw;
            // Repaint when the inherited color changes (e.g. the host's :hover rule).
            RegisterCallback<CustomStyleResolvedEvent>(_ => MarkDirtyRepaint());
        }

        public GlyphIcon(Kind icon) : this() => _icon = icon;

        /// <summary>
        /// Shows <paramref name="icon"/> on <paramref name="host"/> (a Button or Label): clears its text
        /// and adds a GlyphIcon child, or retargets the one already there.
        /// </summary>
        public static GlyphIcon SetOn(VisualElement host, Kind icon)
        {
            if (host is TextElement text) text.text = string.Empty;
            host.AddToClassList(HostUssClass);

            var existing = host.Q<GlyphIcon>();
            if (existing != null)
            {
                existing.Icon = icon;
                return existing;
            }

            var glyph = new GlyphIcon(icon);
            host.Add(glyph);
            return glyph;
        }

        /// <summary>
        /// Wraps <paramref name="title"/> in a row led by <paramref name="icon"/> (e.g. a ✓ before a
        /// completed item's name). Add the returned row where the title label would have gone.
        /// </summary>
        public static VisualElement TitleRow(Kind icon, Label title)
        {
            var row = new VisualElement();
            row.AddToClassList("glyph-title-row");
            row.Add(new GlyphIcon(icon));
            row.Add(title);
            return row;
        }

        /// <summary>Removes a GlyphIcon added by <see cref="SetOn"/> so the host can show text again.</summary>
        public static void ClearFrom(VisualElement host)
        {
            host.Q<GlyphIcon>()?.RemoveFromHierarchy();
            host.RemoveFromClassList(HostUssClass);
        }

        void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            float s = Mathf.Min(r.width, r.height);
            if (s <= 0f) return;

            Vector2 c = r.center;
            float   h = s * 0.5f;

            var p = ctx.painter2D;
            p.strokeColor = resolvedStyle.color;
            p.fillColor   = resolvedStyle.color;
            p.lineWidth   = Mathf.Max(1.5f, s * 0.14f);
            p.lineCap     = LineCap.Round;
            p.lineJoin    = LineJoin.Round;

            switch (_icon)
            {
                case Kind.Close:  DrawClose(p, c, h);  break;
                case Kind.Check:  DrawCheck(p, c, h);  break;
                case Kind.Rotate: DrawRotate(p, c, h); break;
                case Kind.Flip:   DrawFlip(p, c, h);   break;
            }
        }

        // UI Toolkit is y-down: +y is toward the bottom of the element.

        static void DrawClose(Painter2D p, Vector2 c, float h)
        {
            float k = h * 0.7f;
            p.BeginPath();
            p.MoveTo(c + new Vector2(-k, -k));
            p.LineTo(c + new Vector2( k,  k));
            p.MoveTo(c + new Vector2( k, -k));
            p.LineTo(c + new Vector2(-k,  k));
            p.Stroke();
        }

        static void DrawCheck(Painter2D p, Vector2 c, float h)
        {
            p.BeginPath();
            p.MoveTo(c + new Vector2(-0.75f * h,  0.05f * h));
            p.LineTo(c + new Vector2(-0.2f  * h,  0.6f  * h));
            p.LineTo(c + new Vector2( 0.8f  * h, -0.6f  * h));
            p.Stroke();
        }

        // ↻: an open circle running clockwise from the upper right, round to the upper left, with an
        // arrowhead at the end pointing along the direction of travel.
        static void DrawRotate(Painter2D p, Vector2 c, float h)
        {
            const float startDeg = -60f;
            const float endDeg   = 240f;
            float radius = h * 0.62f;

            p.BeginPath();
            p.Arc(c, radius, new Angle(startDeg, AngleUnit.Degree), new Angle(endDeg, AngleUnit.Degree),
                  ArcDirection.Clockwise);
            p.Stroke();

            float   a   = endDeg * Mathf.Deg2Rad;
            Vector2 end = c + radius * new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Vector2 dir = new Vector2(-Mathf.Sin(a), Mathf.Cos(a));   // clockwise tangent
            FillArrowhead(p, end, dir, h * 0.55f);
        }

        // ⇄: a right-pointing arrow above a left-pointing one.
        static void DrawFlip(Painter2D p, Vector2 c, float h)
        {
            float x = h * 0.8f;
            float y = h * 0.38f;
            float head = h * 0.35f;

            p.BeginPath();
            p.MoveTo(c + new Vector2(-x, -y));
            p.LineTo(c + new Vector2( x, -y));
            p.MoveTo(c + new Vector2( x - head, -y - head));
            p.LineTo(c + new Vector2( x, -y));
            p.LineTo(c + new Vector2( x - head, -y + head));

            p.MoveTo(c + new Vector2( x,  y));
            p.LineTo(c + new Vector2(-x,  y));
            p.MoveTo(c + new Vector2(-x + head,  y - head));
            p.LineTo(c + new Vector2(-x,  y));
            p.LineTo(c + new Vector2(-x + head,  y + head));
            p.Stroke();
        }

        static void FillArrowhead(Painter2D p, Vector2 at, Vector2 dir, float size)
        {
            Vector2 perp = new Vector2(-dir.y, dir.x);
            Vector2 tip  = at + dir * (size * 0.6f);
            Vector2 back = at - dir * (size * 0.4f);

            p.BeginPath();
            p.MoveTo(tip);
            p.LineTo(back + perp * (size * 0.55f));
            p.LineTo(back - perp * (size * 0.55f));
            p.ClosePath();
            p.Fill();
        }
    }
}
