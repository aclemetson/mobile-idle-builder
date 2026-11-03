using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The map-wide power-connection overlay toggled by the HUD lightning-bolt button. While visible, it
    /// rebuilds the connection graph from ECS on a light timer and shows two things at once:
    ///   * dashed links between power buildings — bright amber for links that chain back to a generator
    ///     (grid-linked), dim grey for stranded clusters;
    ///   * a coverage ring around every grid-linked node showing the powered area it provides.
    /// Turned off, it hides everything. Reuses <see cref="PowerConnectionRenderer"/>,
    /// <see cref="PlacementRadiusIndicator"/>, and <see cref="PowerConnectionGraph"/> so the overlay matches
    /// the placement/selection previews and the grid the simulation computes.
    /// </summary>
    public class PowerConnectionOverlayController : MonoBehaviour
    {
        [SerializeField] private GridRenderer gridRenderer;

        private const float RefreshInterval = 0.4f; // seconds between rebuilds while visible

        private static readonly Color LinkedColor   = new Color(1f, 0.9f, 0.3f, 0.95f); // wired to a generator
        private static readonly Color StrandedColor = new Color(0.6f, 0.6f, 0.6f, 0.7f); // in range but no generator
        private static readonly Color CoverageColor = new Color(0.35f, 1f, 0.75f, 0.9f); // matches the placement/selection radius ring

        private PowerConnectionRenderer _renderer;
        private readonly List<PlacementRadiusIndicator> _rings = new();
        private bool  _visible;
        private float _timer;

        /// <summary>True while the overlay is showing power connections.</summary>
        public bool IsVisible => _visible;

        private GridRenderer GetGridRenderer()
        {
            if (gridRenderer == null) gridRenderer = FindAnyObjectByType<GridRenderer>();
            return gridRenderer;
        }

        private PowerConnectionRenderer EnsureRenderer()
        {
            if (_renderer == null)
            {
                var gr = GetGridRenderer();
                var parent = gr != null ? gr.transform : transform;
                var go = new GameObject("PowerConnectionOverlay");
                go.transform.SetParent(parent, worldPositionStays: false);
                _renderer = go.AddComponent<PowerConnectionRenderer>();
            }
            return _renderer;
        }

        /// <summary>Shows or hides the whole-map connection overlay. Refreshes immediately when turned on.</summary>
        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (visible) Rebuild();
            else HideAll();
        }

        private void HideAll()
        {
            _renderer?.Hide();
            for (int i = 0; i < _rings.Count; i++)
                if (_rings[i] != null) _rings[i].Hide();
        }

        void Update()
        {
            if (!_visible) return;
            _timer += Time.unscaledDeltaTime;
            if (_timer < RefreshInterval) return;
            _timer = 0f;
            Rebuild();
        }

        private void Rebuild()
        {
            var gr = GetGridRenderer();
            var world = World.DefaultGameObjectInjectionWorld;
            if (gr == null || world == null || !world.IsCreated) { HideAll(); return; }

            float cs    = gr.CellSize;
            var nodes = PowerConnectionGraph.GatherFromEcs(world.EntityManager, cs);
            var edges = PowerConnectionGraph.BuildEdges(nodes);

            // Dashed links.
            var lines = new List<(Vector3, Vector3, Color)>(edges.Count);
            foreach (var (i, j) in edges)
            {
                // Edges only exist between in-range nodes, so both endpoints share connectivity; colour by it.
                var color = nodes[i].IsGridLinked ? LinkedColor : StrandedColor;
                lines.Add((nodes[i].WorldCenter, nodes[j].WorldCenter, color));
            }
            EnsureRenderer().Show(lines);

            // Coverage rings for every node that actually provides power (grid-linked).
            int ringCount = 0;
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i];
                if (!n.IsGridLinked || n.InfluenceRadius <= 0f) continue;
                float worldRadius = n.InfluenceRadius * cs + Mathf.Max(n.Width, n.Height) * 0.5f * cs;
                EnsureRing(ringCount).Show(n.WorldCenter, worldRadius, CoverageColor);
                ringCount++;
            }
            for (int i = ringCount; i < _rings.Count; i++)
                if (_rings[i] != null) _rings[i].Hide();
        }

        private PlacementRadiusIndicator EnsureRing(int idx)
        {
            while (_rings.Count <= idx)
            {
                var gr = GetGridRenderer();
                var parent = gr != null ? gr.transform : transform;
                var go = new GameObject($"PowerCoverageRing_{_rings.Count}");
                go.transform.SetParent(parent, worldPositionStays: false);
                _rings.Add(go.AddComponent<PlacementRadiusIndicator>());
            }
            return _rings[idx];
        }
    }
}
