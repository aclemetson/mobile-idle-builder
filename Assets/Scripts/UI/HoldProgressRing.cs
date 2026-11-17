using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The ring that fills while the player holds on a building (see <see cref="LongPress"/>): a faint full
    /// circle, and an arc that sweeps clockwise from the top as the hold nears edit mode, so the player can
    /// see how much longer to hold. Drawn with Painter2D like <see cref="GlyphIcon"/>, created from code by
    /// <see cref="PlayerInputRouter"/> and added to the HUD root. It ignores picking, so it never blocks a tap.
    /// </summary>
    public class HoldProgressRing : VisualElement
    {
        public const float Size = 80f;

        // Lifts the ring above the touch point so the finger doesn't cover it.
        private const float LiftAboveTouch = 64f;

        private static readonly Color TrackColor = new Color(1f, 1f, 1f, 0.22f);
        private static readonly Color FillColor  = new Color(0.35f, 1f, 0.75f, 0.95f); // placement-ghost green

        private float _progress;

        public HoldProgressRing()
        {
            name        = "hold-progress-ring";
            pickingMode = PickingMode.Ignore;
            style.position = Position.Absolute;
            style.width    = Size;
            style.height   = Size;
            style.display  = DisplayStyle.None;
            generateVisualContent += Draw;
        }

        /// <summary>Shows the ring over a screen point (pixels, origin bottom-left) at <paramref name="progress"/> (0-1).</summary>
        public void Show(Vector2 screenPos, float progress)
        {
            if (panel == null) return;
            var p = RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screenPos.x, Screen.height - screenPos.y));
            style.left    = p.x - Size * 0.5f;
            style.top     = Mathf.Max(0f, p.y - Size * 0.5f - LiftAboveTouch);
            style.display = DisplayStyle.Flex;
            BringToFront();

            if (!Mathf.Approximately(progress, _progress))
            {
                _progress = progress;
                MarkDirtyRepaint();
            }
        }

        public void Hide()
        {
            style.display = DisplayStyle.None;
            _progress     = 0f;
        }

        private void Draw(MeshGenerationContext ctx)
        {
            Vector2 c      = new Vector2(Size * 0.5f, Size * 0.5f);
            float   width  = Size * 0.12f;
            float   radius = Size * 0.5f - width;

            var p = ctx.painter2D;
            p.lineWidth = width;
            p.lineCap   = LineCap.Round;

            // Painter2D colours go through .linear: the project renders in linear colour space.
            p.strokeColor = TrackColor.linear;
            p.BeginPath();
            p.Arc(c, radius, new Angle(0f, AngleUnit.Degree), new Angle(360f, AngleUnit.Degree));
            p.Stroke();

            if (_progress <= 0f) return;

            // UI Toolkit is y-down, so -90 degrees is the top and increasing angles run clockwise.
            p.strokeColor = FillColor.linear;
            p.BeginPath();
            p.Arc(c, radius, new Angle(-90f, AngleUnit.Degree), new Angle(-90f + 360f * _progress, AngleUnit.Degree));
            p.Stroke();
        }
    }
}
