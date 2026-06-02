using System.Collections;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Reusable tutorial highlight system.
    ///
    /// ShowHighlight(id)  — pan camera to target + amber tile + pulsing quad
    /// ShowVisualOnly(id) — amber tile + pulsing quad only, camera stays on player
    /// ClearHighlight()   — return camera to character + remove all visuals
    ///
    /// Because field positions are randomised at runtime by FieldGenerator (a coroutine),
    /// ShowHighlight starts a background search that retries every frame until the field
    /// exists in the scene, then applies the highlight. This makes it safe to call at any
    /// point during startup without worrying about spawn order.
    ///
    /// Supported target IDs:
    ///   FieldInstance.Field.id — e.g. "electron_field", "quark_field"
    ///   (ECS building IDs can be added to FindWorldPosition when needed)
    ///
    /// Inspector wiring required:
    ///   cameraFollow  → CameraController on Main Camera
    ///   gridRenderer  → GridRenderer in scene
    /// </summary>
    public class TutorialHighlighter : MonoBehaviour
    {
        [SerializeField] private CameraController cameraFollow;
        [SerializeField] private GridRenderer     gridRenderer;
        [SerializeField] private Material         _highlightMaterial;  // URP Unlit Transparent — assign in Inspector

        private TutorialTileHighlight _activeHighlight;
        private Coroutine             _searchRoutine;

        /// <summary>Everything ApplyHighlight needs: the anchor grid cell, footprint size, and camera target.</summary>
        private struct WorldTarget
        {
            public int     AnchorX;
            public int     AnchorZ;
            public int     FootprintW;
            public int     FootprintH;
            public Vector3 CameraTarget; // world-space point to pan the camera to
        }

        // ── Public API ────────────────────────────────────────────────────────

        /// <summary>Pan camera to the named target and show the amber tile + pulse.</summary>
        public void ShowHighlight(string targetId)  => BeginSearch(targetId, panCamera: true);

        /// <summary>Show the amber tile + pulse without moving the camera.</summary>
        public void ShowVisualOnly(string targetId) => BeginSearch(targetId, panCamera: false);

        /// <summary>Return camera to the character and remove all highlight visuals.</summary>
        public void ClearHighlight()
        {
            if (_searchRoutine != null)
            {
                StopCoroutine(_searchRoutine);
                _searchRoutine = null;
            }

            cameraFollow?.ResumeFollow();
            gridRenderer?.ClearTutorialHighlightCell();
            ClearVisual();
        }

        // ── Internals ─────────────────────────────────────────────────────────

        private void BeginSearch(string targetId, bool panCamera)
        {
            if (string.IsNullOrEmpty(targetId)) return;

            // Cancel any in-progress search for a previous target
            if (_searchRoutine != null)
            {
                StopCoroutine(_searchRoutine);
                _searchRoutine = null;
            }

            _searchRoutine = StartCoroutine(SearchAndHighlight(targetId, panCamera));
        }

        /// <summary>
        /// Retries every frame until FieldGenerator has spawned the target,
        /// then applies the camera pan and/or visual highlight.
        /// Times out after 15 seconds to avoid an infinite loop on bad IDs.
        /// </summary>
        private IEnumerator SearchAndHighlight(string targetId, bool panCamera)
        {
            float        timeout = 15f;
            WorldTarget? target  = null;

            while (target == null)
            {
                target   = FindWorldTarget(targetId);
                timeout -= Time.deltaTime;

                if (timeout <= 0f)
                {
                    GameLogger.Warning($"[TutorialHighlighter] Timed out searching for target '{targetId}'. " +
                                     "Check that the FieldSO id matches exactly.");
                    _searchRoutine = null;
                    yield break;
                }

                if (target == null)
                    yield return null;
            }

            _searchRoutine = null;
            ApplyHighlight(target.Value, panCamera);
        }

        private void ApplyHighlight(WorldTarget t, bool panCamera)
        {
            if (panCamera)
                cameraFollow?.PanTo(t.CameraTarget);

            gridRenderer?.SetTutorialHighlightRect(t.AnchorX, t.AnchorZ, t.FootprintW, t.FootprintH);

            float cs = gridRenderer != null ? gridRenderer.CellSize : 1f;
            ClearVisual();
            _activeHighlight = TutorialTileHighlight.Spawn(t.CameraTarget, cs, t.FootprintW, t.FootprintH, _highlightMaterial);
        }

        private WorldTarget? FindWorldTarget(string targetId)
        {
            // Scene-based field instances
            var fields = FindObjectsByType<FieldInstance>(FindObjectsSortMode.None);
            foreach (var f in fields)
            {
                if (f.Field == null || f.Field.id != targetId) continue;

                float cs = gridRenderer != null ? gridRenderer.CellSize : 1f;
                int   ax = Mathf.RoundToInt(f.transform.position.x / cs);
                int   az = Mathf.RoundToInt(f.transform.position.z / cs);
                // Camera target = tile centre on the ground plane
                var camTarget = new Vector3(ax * cs, 0f, az * cs);
                return new WorldTarget { AnchorX = ax, AnchorZ = az,
                                         FootprintW = 1, FootprintH = 1,
                                         CameraTarget = camTarget };
            }

            // ECS buildings
            if (targetId == "maxwells_demon")
                return FindEntropySinkTarget();

            return null;
        }

        /// <summary>
        /// Returns the highlight target for the first entity with EntropySinkTag, including
        /// its full footprint from BuildingFootprint (defaults to 1×1 if absent).
        /// </summary>
        private WorldTarget? FindEntropySinkTarget()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return null;

            var em = world.EntityManager;
            using var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<EntropySinkTag>(),
                ComponentType.ReadOnly<GridPosition>()
            );

            if (query.IsEmpty) return null;

            using var entities = query.ToEntityArray(Allocator.Temp);
            var entity = entities[0];
            var pos    = em.GetComponentData<GridPosition>(entity);
            float cs   = gridRenderer != null ? gridRenderer.CellSize : 1f;

            int w = 1, h = 1;
            if (em.HasComponent<BuildingFootprint>(entity))
            {
                var fp = em.GetComponentData<BuildingFootprint>(entity);
                w = fp.Width;
                h = fp.Height;
            }

            // Camera target = world centre of the full footprint
            var camTarget = new Vector3(
                (pos.Cell.x + (w - 1) * 0.5f) * cs, 0f,
                (pos.Cell.y + (h - 1) * 0.5f) * cs);

            return new WorldTarget
            {
                AnchorX      = pos.Cell.x,
                AnchorZ      = pos.Cell.y,
                FootprintW   = w,
                FootprintH   = h,
                CameraTarget = camTarget
            };
        }

        private void ClearVisual()
        {
            if (_activeHighlight != null)
            {
                Destroy(_activeHighlight.gameObject);
                _activeHighlight = null;
            }
        }

        void OnDestroy() => ClearHighlight();
    }
}
