using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Draws a <see cref="CyclotronRun"/>: the two D-shaped electrodes ("dees"), the gap between them (it glows
    /// while the particle is in the kick window), the particle's current orbit and the particle itself. The
    /// orbit widens with energy, so a good run visibly spirals outward. Vector-drawn with Painter2D like
    /// <see cref="GlyphIcon"/>; colours go through <c>.linear</c> because the project renders in linear space.
    /// </summary>
    [UxmlElement]
    public partial class CyclotronView : VisualElement
    {
        static readonly Color DeeFill    = new Color(0.10f, 0.32f, 0.42f, 0.85f);
        static readonly Color DeeEdge    = new Color(0.20f, 0.85f, 1.00f, 0.90f);
        static readonly Color GapIdle    = new Color(0.20f, 0.85f, 1.00f, 0.15f);
        static readonly Color GapHot     = new Color(1.00f, 0.85f, 0.30f, 0.85f);
        static readonly Color OrbitColor = new Color(1.00f, 1.00f, 1.00f, 0.18f);
        static readonly Color Particle   = new Color(1.00f, 0.95f, 0.70f, 1.00f);
        static readonly Color FlashGood  = new Color(0.40f, 1.00f, 0.60f, 1.00f);
        static readonly Color FlashMiss  = new Color(1.00f, 0.35f, 0.35f, 1.00f);

        public CyclotronRun Run { get; set; }

        // A short ring pulse around the particle after a tap (seconds left, and whether it was a miss).
        float _flash;
        bool  _flashMiss;

        public CyclotronView()
        {
            generateVisualContent += Draw;
        }

        public void Flash(bool miss)
        {
            _flash = 0.25f;
            _flashMiss = miss;
        }

        /// <summary>Advances the flash animation; call once a frame while a run is shown.</summary>
        public void Animate(float dt)
        {
            if (_flash > 0f) _flash = Mathf.Max(0f, _flash - dt);
            MarkDirtyRepaint();
        }

        void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            float size = Mathf.Min(r.width, r.height);
            if (size <= 0f) return;

            Vector2 c   = r.center;
            float   R   = size * 0.47f;
            float   gap = R * 0.14f;
            var     p   = ctx.painter2D;
            bool    hot = Run != null && Run.InGap;

            // Gap strip between the dees: where the particle is kicked.
            p.fillColor = (hot ? GapHot : GapIdle).linear;
            p.BeginPath();
            p.MoveTo(c + new Vector2(-gap * 0.5f, -R));
            p.LineTo(c + new Vector2( gap * 0.5f, -R));
            p.LineTo(c + new Vector2( gap * 0.5f,  R));
            p.LineTo(c + new Vector2(-gap * 0.5f,  R));
            p.ClosePath();
            p.Fill();

            // The two dees: half discs either side of the gap.
            DrawDee(p, c + new Vector2(-gap * 0.5f, 0f), R - gap * 0.5f, left: true);
            DrawDee(p, c + new Vector2( gap * 0.5f, 0f), R - gap * 0.5f, left: false);

            if (Run == null) return;

            // Current orbit and the particle on it (angle 0 = right, 90 = down: screen space).
            float orbit = (R - gap) * Run.OrbitFraction;
            p.strokeColor = OrbitColor.linear;
            p.lineWidth   = Mathf.Max(1f, size * 0.004f);
            p.BeginPath();
            p.Arc(c, orbit, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            p.Stroke();

            float a = Run.Angle * Mathf.Deg2Rad;
            Vector2 pos = c + orbit * new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            float dot = Mathf.Max(4f, size * 0.03f);
            p.fillColor = Particle.linear;
            p.BeginPath();
            p.Arc(pos, dot, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            p.Fill();

            if (_flash > 0f)
            {
                float t = 1f - _flash / 0.25f;
                var col = _flashMiss ? FlashMiss : FlashGood;
                col.a = 1f - t;
                p.strokeColor = col.linear;
                p.lineWidth   = Mathf.Max(2f, size * 0.01f);
                p.BeginPath();
                p.Arc(pos, dot * (1.5f + 2.5f * t), new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
                p.Stroke();
            }
        }

        static void DrawDee(Painter2D p, Vector2 flatCentre, float radius, bool left)
        {
            // A half disc whose flat side faces the gap: left dee spans 90..270 degrees, right dee -90..90.
            float from = left ? 90f : -90f;
            float to   = left ? 270f : 90f;
            p.fillColor   = DeeFill.linear;
            p.strokeColor = DeeEdge.linear;
            p.lineWidth   = Mathf.Max(1.5f, radius * 0.02f);
            p.BeginPath();
            p.MoveTo(flatCentre + new Vector2(0f, -radius));
            p.Arc(flatCentre, radius, new Angle(from, AngleUnit.Degree), new Angle(to, AngleUnit.Degree));
            p.ClosePath();
            p.Fill();
            p.Stroke();
        }
    }
}
