using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Vector-drawn UI icon (close / check / rotate / flip / info / empty / trash / undo) used in place of
    /// the ✕ ✓ ↻ ⇄ ⓘ ⤓ 🗑 ↶ text glyphs.
    /// The Android default font has none of those glyphs, so text buttons rendered blank on device
    /// (issue #165, Option B: vector icons instead of a symbol font). Drawing with Painter2D removes the font
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
        // Append only: UXML names the icon, and code may store the value.
        public enum Kind
        {
            Close, Check, Rotate, Flip, Info, Empty, Trash, Undo,
            // Side-drawer menu icons (#486)
            Build, Recipes, Codex, Stats, Research, Cyclotron, Upgrades, Managers, Megastructure,
            Rewards, Achievements, Worlds, Domains, Tournament, Shop,
        }

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
                case Kind.Info:   DrawInfo(p, c, h);   break;
                case Kind.Empty:  DrawEmpty(p, c, h);  break;
                case Kind.Trash:  DrawTrash(p, c, h);  break;
                case Kind.Undo:   DrawUndo(p, c, h);   break;
                case Kind.Build:         DrawBuild(p, c, h);         break;
                case Kind.Recipes:       DrawRecipes(p, c, h);       break;
                case Kind.Codex:         DrawCodex(p, c, h);         break;
                case Kind.Stats:         DrawStats(p, c, h);         break;
                case Kind.Research:      DrawResearch(p, c, h);      break;
                case Kind.Cyclotron:     DrawCyclotron(p, c, h);     break;
                case Kind.Upgrades:      DrawUpgrades(p, c, h);      break;
                case Kind.Managers:      DrawManagers(p, c, h);      break;
                case Kind.Megastructure: DrawMegastructure(p, c, h); break;
                case Kind.Rewards:       DrawRewards(p, c, h);       break;
                case Kind.Achievements:  DrawAchievements(p, c, h);  break;
                case Kind.Worlds:        DrawWorlds(p, c, h);        break;
                case Kind.Domains:       DrawDomains(p, c, h);       break;
                case Kind.Tournament:    DrawTournament(p, c, h);    break;
                case Kind.Shop:          DrawShop(p, c, h);          break;
            }
        }

        // UI Toolkit is y-down: +y is toward the bottom of the element.

        // ── Menu icons (#486). Same stroke weight and colour rules as the rest, drawn in a [-h, h] box. ──

        static Vector2 P(Vector2 c, float h, float x, float y) => c + new Vector2(x * h, y * h);

        static void Poly(Painter2D p, Vector2 c, float h, bool close, params float[] xy)
        {
            p.BeginPath();
            p.MoveTo(P(c, h, xy[0], xy[1]));
            for (int i = 2; i + 1 < xy.Length; i += 2) p.LineTo(P(c, h, xy[i], xy[i + 1]));
            if (close) p.ClosePath();
            p.Stroke();
        }

        static void Circle(Painter2D p, Vector2 centre, float radius, bool fill = false)
        {
            p.BeginPath();
            p.Arc(centre, radius, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            if (fill) p.Fill(); else p.Stroke();
        }

        // An ellipse as a closed polyline, rotated by deg (Painter2D has no ellipse primitive).
        static void Ellipse(Painter2D p, Vector2 centre, float rx, float ry, float deg)
        {
            float a = deg * Mathf.Deg2Rad, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
            const int n = 28;
            p.BeginPath();
            for (int i = 0; i <= n; i++)
            {
                float t = i * Mathf.PI * 2f / n;
                float x = rx * Mathf.Cos(t), y = ry * Mathf.Sin(t);
                var pt = centre + new Vector2(x * ca - y * sa, x * sa + y * ca);
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath();
            p.Stroke();
        }

        // Hammer: a handle up to the right and a heavy head across its top.
        static void DrawBuild(Painter2D p, Vector2 c, float h)
        {
            Poly(p, c, h, false, -0.7f, 0.75f, 0.2f, -0.15f);
            float w = p.lineWidth;
            p.lineWidth = w * 2.2f;
            Poly(p, c, h, false, -0.05f, -0.75f, 0.65f, -0.05f);
            p.lineWidth = w;
        }

        // Erlenmeyer flask with a liquid line.
        static void DrawRecipes(Painter2D p, Vector2 c, float h)
        {
            Poly(p, c, h, false, -0.2f, -0.8f, -0.2f, -0.3f, -0.72f, 0.75f, 0.72f, 0.75f, 0.2f, -0.3f, 0.2f, -0.8f);
            Poly(p, c, h, false, -0.38f, -0.8f, 0.38f, -0.8f);
            Poly(p, c, h, false, -0.48f, 0.3f, 0.48f, 0.3f);
        }

        // Open book: two pages meeting at the spine.
        static void DrawCodex(Painter2D p, Vector2 c, float h)
        {
            Poly(p, c, h, true, 0f, -0.5f, -0.85f, -0.72f, -0.85f, 0.55f, 0f, 0.75f);
            Poly(p, c, h, true, 0f, -0.5f, 0.85f, -0.72f, 0.85f, 0.55f, 0f, 0.75f);
        }

        // Bar chart on a baseline.
        static void DrawStats(Painter2D p, Vector2 c, float h)
        {
            Poly(p, c, h, false, -0.85f, 0.8f, 0.85f, 0.8f);
            float w = p.lineWidth;
            p.lineWidth = w * 1.6f;
            p.lineCap = LineCap.Butt;
            Poly(p, c, h, false, -0.5f, 0.65f, -0.5f, 0.1f);
            Poly(p, c, h, false, 0f, 0.65f, 0f, -0.75f);
            Poly(p, c, h, false, 0.5f, 0.65f, 0.5f, -0.3f);
            p.lineCap = LineCap.Round;
            p.lineWidth = w;
        }

        // Atom: three orbits and a nucleus.
        static void DrawResearch(Painter2D p, Vector2 c, float h)
        {
            float w = p.lineWidth;
            p.lineWidth = w * 0.7f;
            Ellipse(p, c, 0.9f * h, 0.34f * h, 0f);
            Ellipse(p, c, 0.9f * h, 0.34f * h, 60f);
            Ellipse(p, c, 0.9f * h, 0.34f * h, 120f);
            p.lineWidth = w;
            Circle(p, c, 0.16f * h, fill: true);
        }

        // Cyclotron: an open orbit with the particle at the gap.
        static void DrawCyclotron(Painter2D p, Vector2 c, float h)
        {
            p.BeginPath();
            p.Arc(c, 0.75f * h, new Angle(25f, AngleUnit.Degree), new Angle(335f, AngleUnit.Degree));
            p.Stroke();
            Circle(p, c, 0.28f * h);
            Circle(p, P(c, h, 0.75f, 0f), 0.17f * h, fill: true);
        }

        // ✦ four-pointed star (the prestige currency's sign).
        static void DrawUpgrades(Painter2D p, Vector2 c, float h)
        {
            p.BeginPath();
            for (int i = 0; i < 8; i++)
            {
                float r = (i % 2 == 0 ? 0.9f : 0.24f) * h;
                float a = (i * 45f - 90f) * Mathf.Deg2Rad;
                var pt = c + r * new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                if (i == 0) p.MoveTo(pt); else p.LineTo(pt);
            }
            p.ClosePath();
            p.Fill();
        }

        // Person: head and shoulders.
        static void DrawManagers(Painter2D p, Vector2 c, float h)
        {
            Circle(p, P(c, h, 0f, -0.38f), 0.32f * h);
            p.BeginPath();
            p.Arc(P(c, h, 0f, 0.85f), 0.68f * h, new Angle(180f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            p.Stroke();
        }

        // Dyson sphere: a star with a ring around it.
        static void DrawMegastructure(Painter2D p, Vector2 c, float h)
        {
            Circle(p, c, 0.5f * h);
            float w = p.lineWidth;
            p.lineWidth = w * 0.8f;
            Ellipse(p, c, 0.92f * h, 0.3f * h, -20f);
            p.lineWidth = w;
        }

        // ▶ play (rewarded videos).
        static void DrawRewards(Painter2D p, Vector2 c, float h)
        {
            p.BeginPath();
            p.MoveTo(P(c, h, -0.45f, -0.72f));
            p.LineTo(P(c, h, 0.78f, 0f));
            p.LineTo(P(c, h, -0.45f, 0.72f));
            p.ClosePath();
            p.Fill();
        }

        // Medal: a disc hanging from a V ribbon.
        static void DrawAchievements(Painter2D p, Vector2 c, float h)
        {
            Poly(p, c, h, false, -0.45f, -0.85f, 0f, -0.1f, 0.45f, -0.85f);
            Circle(p, P(c, h, 0f, 0.35f), 0.42f * h);
            Circle(p, P(c, h, 0f, 0.35f), 0.13f * h, fill: true);
        }

        // Globe: outline, a meridian and the equator.
        static void DrawWorlds(Painter2D p, Vector2 c, float h)
        {
            Circle(p, c, 0.82f * h);
            float w = p.lineWidth;
            p.lineWidth = w * 0.75f;
            Ellipse(p, c, 0.36f * h, 0.82f * h, 0f);
            Poly(p, c, h, false, -0.82f, 0f, 0.82f, 0f);
            p.lineWidth = w;
        }

        // Domains: a 2x2 grid of sites.
        static void DrawDomains(Painter2D p, Vector2 c, float h)
        {
            float w = p.lineWidth;
            p.lineWidth = w * 0.85f;
            Poly(p, c, h, true, -0.8f, -0.8f, -0.1f, -0.8f, -0.1f, -0.1f, -0.8f, -0.1f);
            Poly(p, c, h, true, 0.1f, -0.8f, 0.8f, -0.8f, 0.8f, -0.1f, 0.1f, -0.1f);
            Poly(p, c, h, true, -0.8f, 0.1f, -0.1f, 0.1f, -0.1f, 0.8f, -0.8f, 0.8f);
            Poly(p, c, h, true, 0.1f, 0.1f, 0.8f, 0.1f, 0.8f, 0.8f, 0.1f, 0.8f);
            p.lineWidth = w;
        }

        // Trophy: cup, handles, stem and base.
        static void DrawTournament(Painter2D p, Vector2 c, float h)
        {
            Poly(p, c, h, true, -0.5f, -0.8f, 0.5f, -0.8f, 0.42f, -0.2f, 0f, 0.15f, -0.42f, -0.2f);
            Poly(p, c, h, false, -0.5f, -0.65f, -0.85f, -0.6f, -0.62f, -0.25f);
            Poly(p, c, h, false, 0.5f, -0.65f, 0.85f, -0.6f, 0.62f, -0.25f);
            Poly(p, c, h, false, 0f, 0.15f, 0f, 0.55f);
            Poly(p, c, h, false, -0.42f, 0.78f, 0.42f, 0.78f);
        }

        // ♦ crystal (the premium currency): a cut gem.
        static void DrawShop(Painter2D p, Vector2 c, float h)
        {
            Poly(p, c, h, true, -0.82f, -0.25f, -0.42f, -0.75f, 0.42f, -0.75f, 0.82f, -0.25f, 0f, 0.82f);
            Poly(p, c, h, false, -0.82f, -0.25f, 0.82f, -0.25f);
            Poly(p, c, h, false, -0.2f, -0.75f, 0f, -0.25f, 0.2f, -0.75f);
        }

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

        // ⓘ: a circle with a dot over a short stem.
        static void DrawInfo(Painter2D p, Vector2 c, float h)
        {
            float radius = h * 0.82f;
            p.BeginPath();
            p.Arc(c, radius, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            p.Stroke();

            p.BeginPath();
            p.MoveTo(c + new Vector2(0f, -0.05f * h));
            p.LineTo(c + new Vector2(0f,  0.45f * h));
            p.Stroke();

            p.BeginPath();
            p.Arc(c + new Vector2(0f, -0.38f * h), p.lineWidth * 0.6f,
                  new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            p.Fill();
        }

        // ⤓ into a tray: "empty this into the inventory". A down arrow dropping into an open box.
        static void DrawEmpty(Painter2D p, Vector2 c, float h)
        {
            float w = h * 0.8f;
            p.BeginPath();                                    // tray: open-topped U
            p.MoveTo(c + new Vector2(-w, 0.15f * h));
            p.LineTo(c + new Vector2(-w, 0.8f  * h));
            p.LineTo(c + new Vector2( w, 0.8f  * h));
            p.LineTo(c + new Vector2( w, 0.15f * h));
            p.Stroke();

            p.BeginPath();                                    // arrow shaft
            p.MoveTo(c + new Vector2(0f, -0.85f * h));
            p.LineTo(c + new Vector2(0f,  0.2f  * h));
            p.Stroke();
            FillArrowhead(p, c + new Vector2(0f, 0.25f * h), new Vector2(0f, 1f), h * 0.6f);
        }

        // ↶: a hooked arrow. A line runs left, curls up and back over to the right, and the arrowhead at
        // its left end points back the way it came ("take the last step back").
        static void DrawUndo(Painter2D p, Vector2 c, float h)
        {
            float r = h * 0.42f;
            Vector2 arcCentre = c + new Vector2(0.25f * h, 0.05f * h);

            p.BeginPath();
            p.MoveTo(c + new Vector2(-0.55f * h, -0.37f * h));           // top line, from the arrowhead
            p.LineTo(arcCentre + new Vector2(0f, -r));
            p.Arc(arcCentre, r, new Angle(-90f, AngleUnit.Degree), new Angle(90f, AngleUnit.Degree),
                  ArcDirection.Clockwise);                                 // round the right side
            p.LineTo(c + new Vector2(-0.35f * h, arcCentre.y - c.y + r));   // bottom tail
            p.Stroke();

            FillArrowhead(p, c + new Vector2(-0.6f * h, -0.37f * h), new Vector2(-1f, 0f), h * 0.55f);
        }

        // A bin: a lid with a handle over a can that narrows to the bottom, with two ribs.
        static void DrawTrash(Painter2D p, Vector2 c, float h)
        {
            float lidY = -0.5f * h;
            p.BeginPath();                                    // lid
            p.MoveTo(c + new Vector2(-0.85f * h, lidY));
            p.LineTo(c + new Vector2( 0.85f * h, lidY));
            p.Stroke();

            p.BeginPath();                                    // handle
            p.MoveTo(c + new Vector2(-0.3f * h, lidY));
            p.LineTo(c + new Vector2(-0.3f * h, -0.82f * h));
            p.LineTo(c + new Vector2( 0.3f * h, -0.82f * h));
            p.LineTo(c + new Vector2( 0.3f * h, lidY));
            p.Stroke();

            p.BeginPath();                                    // can
            p.MoveTo(c + new Vector2(-0.62f * h, -0.3f * h));
            p.LineTo(c + new Vector2(-0.48f * h,  0.85f * h));
            p.LineTo(c + new Vector2( 0.48f * h,  0.85f * h));
            p.LineTo(c + new Vector2( 0.62f * h, -0.3f * h));
            p.Stroke();

            p.BeginPath();                                    // ribs
            p.MoveTo(c + new Vector2(-0.17f * h, -0.05f * h));
            p.LineTo(c + new Vector2(-0.14f * h,  0.55f * h));
            p.MoveTo(c + new Vector2( 0.17f * h, -0.05f * h));
            p.LineTo(c + new Vector2( 0.14f * h,  0.55f * h));
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
