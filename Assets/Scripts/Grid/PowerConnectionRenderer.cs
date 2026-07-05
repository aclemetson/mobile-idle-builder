using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Draws dashed lines between power buildings that are within grid-connection range, flat on the grid's
    /// ground plane. Used for all three connection views: the placement preview (ghost -> in-range nodes),
    /// the selection preview (selected node -> in-range nodes), and the global overlay (every edge). Each
    /// dash line is one pooled <see cref="LineRenderer"/>; call <see cref="Show"/> with the segment list to
    /// (re)draw and <see cref="Hide"/> to clear. Built entirely in code (no scene wiring), created lazily by
    /// its owner the same way <see cref="PlacementRadiusIndicator"/> is.
    ///
    /// Dashes come from a repeating alternating-alpha texture tiled along each segment; the material uses
    /// Sprites/Default (alpha-blended, Android-safe) so the transparent half of the texture reads as a gap.
    /// </summary>
    public class PowerConnectionRenderer : MonoBehaviour
    {
        private const float GroundY         = 0.07f; // sit just above the radius ring so both read clearly
        private const float LineWidth       = 0.05f;
        private const float DashWorldLength = 0.5f;   // approx world length of one dash+gap cycle

        // The floor tiles are URP Unlit Transparent (queue 3000) and write no depth, so a decal at the same
        // queue sorts ambiguously and the floor sometimes draws over it. Drawing after the floor (higher
        // queue) makes the dashes reliably sit on top of the ground.
        private const int   DecalRenderQueue = 3200;

        private readonly List<LineRenderer> _pool = new();
        private Texture2D _dashTex;
        private int _active;

        void Awake()
        {
            _dashTex = BuildDashTexture();
        }

        /// <summary>
        /// (Re)draws one dashed line per connection. Endpoints are taken as world positions and flattened to
        /// the ground plane. Reuses pooled line renderers and hides any left over from a previous call.
        /// </summary>
        public void Show(IEnumerable<(Vector3 a, Vector3 b, Color color)> connections)
        {
            if (connections == null) { Hide(); return; }

            _active = 0;
            foreach (var (a, b, color) in connections)
            {
                var lr = GetOrCreate(_active);
                var pa = new Vector3(a.x, GroundY, a.z);
                var pb = new Vector3(b.x, GroundY, b.z);
                lr.positionCount = 2;
                lr.SetPosition(0, pa);
                lr.SetPosition(1, pb);
                lr.startColor = color;
                lr.endColor   = color;
                float len = Vector3.Distance(pa, pb);
                lr.material.mainTextureScale = new Vector2(Mathf.Max(1f, len / DashWorldLength), 1f);
                lr.gameObject.SetActive(true);
                _active++;
            }

            for (int i = _active; i < _pool.Count; i++)
                if (_pool[i] != null) _pool[i].gameObject.SetActive(false);
        }

        /// <summary>Hides all dash lines (kept alive for the next Show, like the radius ring).</summary>
        public void Hide()
        {
            for (int i = 0; i < _pool.Count; i++)
                if (_pool[i] != null) _pool[i].gameObject.SetActive(false);
            _active = 0;
        }

        private LineRenderer GetOrCreate(int idx)
        {
            while (_pool.Count <= idx)
            {
                var go = new GameObject($"PowerConnDash_{_pool.Count}");
                go.transform.SetParent(transform, worldPositionStays: false);
                var lr = go.AddComponent<LineRenderer>();
                lr.useWorldSpace     = true;
                lr.widthMultiplier   = LineWidth;
                lr.numCornerVertices = 0;
                lr.numCapVertices    = 0;
                lr.textureMode       = LineTextureMode.Tile;
                lr.alignment         = LineAlignment.View;
                lr.shadowCastingMode = ShadowCastingMode.Off;
                lr.receiveShadows    = false;
                lr.material          = MakeMaterial(_dashTex);
                go.SetActive(false);
                _pool.Add(lr);
            }
            return _pool[idx];
        }

        private static Texture2D BuildDashTexture()
        {
            const int w = 16;
            var tex = new Texture2D(w, 1, TextureFormat.RGBA32, false)
            {
                wrapMode   = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point
            };
            var px = new Color[w];
            for (int i = 0; i < w; i++)
                px[i] = i < w / 2 ? Color.white : new Color(1f, 1f, 1f, 0f);
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        private static Material MakeMaterial(Texture2D dashTex)
        {
            // Sprites/Default is alpha-blended (so the texture's clear half becomes a gap) and Android-safe;
            // fall back to URP Unlit if it is stripped. Vertex colour (start/end colour) tints the dashes.
            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
            var mat = new Material(shader) { mainTexture = dashTex, renderQueue = DecalRenderQueue };
            return mat;
        }

        void OnDestroy()
        {
            for (int i = 0; i < _pool.Count; i++)
                if (_pool[i] != null && _pool[i].material != null) Destroy(_pool[i].material);
            if (_dashTex != null) Destroy(_dashTex);
        }
    }
}
