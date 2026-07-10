using System;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Per-field tap cooldown plus its radial "wheel" indicator.
    ///
    /// Tapping a field collects one item and starts a cooldown; the field cannot be tapped again
    /// until the wheel empties. Cooldown state is a UTC end timestamp (mirrors
    /// <see cref="ResearchService"/>'s timer) so it keeps ticking while the app is closed and cannot
    /// be reset by quitting and re-opening.
    ///
    /// The wheel is built programmatically from two <see cref="LineRenderer"/> rings (no uGUI / scene
    /// wiring): a faint full background ring and a bright foreground arc that shrinks clockwise from
    /// the top as the remaining fraction drops. The holder billboards toward the camera each frame.
    /// </summary>
    public class FieldCooldownIndicator : MonoBehaviour
    {
        private const int   Segments    = 48;
        private const float Radius      = 0.35f;
        private const float YOffset     = 1.1f;   // float the wheel above the field particles
        private const float LineWidth   = 0.05f;

        private DateTime _endUtc;
        private float    _durationSec;

        private Transform    _wheelRoot;
        private LineRenderer _background;
        private LineRenderer _foreground;
        private bool         _built;
        private bool         _visible;

        /// <summary>UTC instant the cooldown completes. Only meaningful while <see cref="IsOnCooldown"/>.</summary>
        public DateTime EndUtc => _endUtc;
        /// <summary>The full cooldown length used when the wheel started (for resuming the fill fraction).</summary>
        public float DurationSec => _durationSec;

        public bool IsReady => _durationSec <= 0f || DateTime.UtcNow >= _endUtc;
        public bool IsOnCooldown => !IsReady;

        /// <summary>Begins a fresh cooldown of <paramref name="durationSeconds"/> seconds from now.</summary>
        public void StartCooldown(float durationSeconds)
        {
            _durationSec = Mathf.Max(0f, durationSeconds);
            _endUtc      = DateTime.UtcNow.AddSeconds(_durationSec);
        }

        /// <summary>Restores a cooldown loaded from save. Caller should only pass a still-future end time.</summary>
        public void RestoreCooldown(DateTime endUtc, float durationSec)
        {
            _endUtc      = endUtc;
            _durationSec = Mathf.Max(0f, durationSec);
        }

        private float RemainingFraction()
        {
            if (_durationSec <= 0f) return 0f;
            double remaining = (_endUtc - DateTime.UtcNow).TotalSeconds;
            return Mathf.Clamp01((float)(remaining / _durationSec));
        }

        void Update()
        {
            float frac = RemainingFraction();
            if (frac <= 0f)
            {
                if (_visible) SetVisible(false);
                return;
            }

            if (!_built) BuildWheel();
            if (!_visible) SetVisible(true);
            SetArc(_foreground, frac);
        }

        void LateUpdate()
        {
            if (!_visible || _wheelRoot == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            // Face the wheel's plane (+Z normal) toward the camera so the ring reads as a circle.
            _wheelRoot.rotation = Quaternion.LookRotation(
                _wheelRoot.position - cam.transform.position, cam.transform.up);
        }

        // ── Wheel construction ────────────────────────────────────────────────

        private void BuildWheel()
        {
            var rootGO = new GameObject("CooldownWheel");
            rootGO.transform.SetParent(transform, worldPositionStays: false);
            rootGO.transform.localPosition = new Vector3(0f, YOffset, 0f);
            _wheelRoot = rootGO.transform;

            Color tint = Color.white;
            var fi = GetComponent<FieldInstance>();
            if (fi != null && fi.Field != null)
                tint = fi.Field.fieldColor;
            Color bright = new Color(
                Mathf.Clamp01(tint.r + 0.2f), Mathf.Clamp01(tint.g + 0.2f), Mathf.Clamp01(tint.b + 0.2f), 1f);
            Color dim = new Color(1f, 1f, 1f, 0.18f);

            _background = MakeRing("Background", dim, LineWidth * 0.6f, sortingOrder: 2);
            _foreground = MakeRing("Foreground", bright, LineWidth, sortingOrder: 3);

            SetArc(_background, 1f);
            _built = true;
        }

        private LineRenderer MakeRing(string name, Color color, float width, int sortingOrder)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_wheelRoot, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;

            var lr = go.AddComponent<LineRenderer>();
            lr.useWorldSpace   = false;
            lr.alignment       = LineAlignment.TransformZ; // ring lives in the holder's XY plane
            lr.loop            = false;
            lr.widthMultiplier = width;
            lr.numCapVertices  = 2;
            lr.sortingOrder    = sortingOrder;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows  = false;

            lr.material   = MakeMaterial(color);
            lr.startColor = color;
            lr.endColor   = color;
            return lr;
        }

        private static Material MakeMaterial(Color color)
        {
            // URP Unlit keeps it cheap and avoids magenta on Android (per project convention);
            // fall back to Sprites/Default if the URP shader is unavailable in a given build.
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", color);
            return mat;
        }

        /// <summary>Draws the leading <paramref name="frac"/> of the circle, clockwise from the top.</summary>
        private static void SetArc(LineRenderer lr, float frac)
        {
            frac = Mathf.Clamp01(frac);
            int count = Mathf.Max(2, Mathf.CeilToInt(frac * Segments) + 1);
            lr.positionCount = count;
            for (int i = 0; i < count; i++)
            {
                float t     = (count <= 1) ? 0f : (float)i / (count - 1);
                float angle = (90f - 360f * frac * t) * Mathf.Deg2Rad; // top, clockwise
                lr.SetPosition(i, new Vector3(Mathf.Cos(angle) * Radius, Mathf.Sin(angle) * Radius, 0f));
            }
        }

        private void SetVisible(bool visible)
        {
            _visible = visible;
            if (_wheelRoot != null)
                _wheelRoot.gameObject.SetActive(visible);
        }
    }
}
