using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>Which buildings get a progress bar and how full it is (#211). Pure, so tests pin it.</summary>
    public static class ProgressBarRules
    {
        /// <summary>
        /// A harvester's bar follows its collector timer (its RecipeProcessData never advances); a crafter's
        /// follows its craft, shown once a recipe is set (an empty bar reads as "waiting for inputs").
        /// False for anything else (Demon, generators, Filters, a crafter with no recipe).
        /// </summary>
        public static bool TryFraction(bool hasCollector, CollectorData collector,
                                       bool hasRecipe, RecipeProcessData recipe, out float fraction)
        {
            fraction = 0f;
            if (hasCollector)
            {
                float rate = collector.OutputRate > 0f ? collector.OutputRate : 1f;
                fraction = Mathf.Clamp01(collector.Timer * rate);
                return true;
            }
            if (hasRecipe && recipe.RecipeID >= 0 && recipe.CraftTime > 0f)
            {
                fraction = Mathf.Clamp01(recipe.Progress / recipe.CraftTime);
                return true;
            }
            return false;
        }

        /// <summary>
        /// The fill quad's centre offset (along the bar, from the bar's centre) and width, so the fill grows
        /// from the left edge of a bar <paramref name="width"/> wide.
        /// </summary>
        public static (float offset, float width) Fill(float width, float fraction)
        {
            float w = width * Mathf.Clamp01(fraction);
            return (-width * 0.5f + w * 0.5f, w);
        }
    }

    /// <summary>
    /// World-space progress bars above producing buildings (#211): one thin camera-facing bar per crafter
    /// or harvester, drawn as two instanced batches (background, fill) every frame, so cost stays flat
    /// with many buildings and nothing is created per building. Toggled in Settings
    /// (<see cref="SettingsData.showProgressBars"/>). Added at runtime by <see cref="BuildingVisualizer"/>.
    /// </summary>
    public sealed class BuildingProgressBars : MonoBehaviour
    {
        const float BarWidth  = 0.7f;    // in cells
        const float BarHeight = 0.09f;   // in cells
        const float Lift      = 0.95f;   // above the ground, in cells: just over a harvester's tip (the camera is steep,
                                         // so more reads as belonging to the row behind)
        const float LiftPerExtraCell = 0.45f;
        const int   MaxBatch  = 1023;

        static readonly Color BackColor = new(0.05f, 0.07f, 0.1f, 1f);
        static readonly Color FillColor = new(0.30f, 0.80f, 1.00f, 1f);   // crest blue, as the inspector bar

        GridRenderer _grid;
        Mesh _quad;
        Material _back, _fill;
        RenderParams _backParams, _fillParams;
        readonly List<Matrix4x4> _backs = new(), _fills = new();
        EntityQuery _query;
        World _world;   // the world _query belongs to

        public static bool Enabled => SettingsService.Instance?.Current?.showProgressBars ?? true;

        void Awake()
        {
            _grid = GetComponent<BuildingVisualizer>()?.Grid ?? FindAnyObjectByType<GridRenderer>();
            _quad = BuildQuad();
            _back = MakeMaterial(BackColor);
            _fill = MakeMaterial(FillColor);
            _backParams = new RenderParams(_back) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
            _fillParams = new RenderParams(_fill) { shadowCastingMode = ShadowCastingMode.Off, receiveShadows = false };
        }

        void OnDestroy()
        {
            if (_quad != null) Destroy(_quad);
            if (_back != null) Destroy(_back);
            if (_fill != null) Destroy(_fill);
            if (_world != null && _world.IsCreated) _query.Dispose();
        }

        void LateUpdate()
        {
            if (!Enabled || _grid == null) return;
            var cam = Camera.main;
            if (cam == null || !TryQuery(out var em)) return;

            _backs.Clear(); _fills.Clear();
            float cs = _grid.CellSize;
            var gridT = _grid.transform;
            var rot   = cam.transform.rotation;
            var right = rot * Vector3.right;

            using var entities = _query.ToEntityArray(Allocator.Temp);
            foreach (var e in entities)
            {
                bool hasCol = em.HasComponent<CollectorData>(e);
                bool hasRec = em.HasComponent<RecipeProcessData>(e);
                if (!ProgressBarRules.TryFraction(hasCol, hasCol ? em.GetComponentData<CollectorData>(e) : default,
                                                  hasRec, hasRec ? em.GetComponentData<RecipeProcessData>(e) : default,
                                                  out float f)) continue;

                var cell = em.GetComponentData<GridPosition>(e).Cell;
                int fw = 1, fh = 1;
                if (em.HasComponent<BuildingFootprint>(e))
                {
                    var fp = em.GetComponentData<BuildingFootprint>(e);
                    fw = Mathf.Max(1, fp.Width); fh = Mathf.Max(1, fp.Height);
                }
                // Bigger buildings are taller (a 2-wide Combiner's dome): lift the bar with the footprint.
                float lift = Lift + LiftPerExtraCell * (Mathf.Max(fw, fh) - 1);
                var centre = gridT.TransformPoint(new Vector3((cell.x + (fw - 1) * 0.5f) * cs, lift * cs,
                                                              (cell.y + (fh - 1) * 0.5f) * cs));
                float w = BarWidth * cs, h = BarHeight * cs;
                _backs.Add(Matrix4x4.TRS(centre, rot, new Vector3(w, h, 1f)));
                var (offset, fillW) = ProgressBarRules.Fill(w, f);
                if (fillW > 0f)
                    // Nudged toward the camera so the fill never z-fights the background.
                    _fills.Add(Matrix4x4.TRS(centre + right * offset - cam.transform.forward * 0.002f, rot,
                                             new Vector3(fillW, h, 1f)));
            }

            Draw(_backParams, _backs);
            Draw(_fillParams, _fills);
        }

        void Draw(RenderParams rp, List<Matrix4x4> matrices)
        {
            for (int start = 0; start < matrices.Count; start += MaxBatch)
                Graphics.RenderMeshInstanced(rp, _quad, 0, matrices, Mathf.Min(MaxBatch, matrices.Count - start), start);
        }

        bool TryQuery(out EntityManager em)
        {
            em = default;
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return false;
            if (world != _world)
            {
                _world = world;
                _query = world.EntityManager.CreateEntityQuery(new EntityQueryDesc
                {
                    All = new[] { ComponentType.ReadOnly<BuildingData>(), ComponentType.ReadOnly<GridPosition>() },
                    Any = new[] { ComponentType.ReadOnly<CollectorData>(), ComponentType.ReadOnly<RecipeProcessData>() },
                    None = new[] { ComponentType.ReadOnly<EntropySinkTag>() },
                });
            }
            em = world.EntityManager;
            return true;
        }

        static Mesh BuildQuad()
        {
            var m = new Mesh { name = "ProgressBarQuad" };
            m.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                new Vector3(-0.5f,  0.5f, 0f), new Vector3(0.5f,  0.5f, 0f),
            });
            m.SetTriangles(new[] { 0, 3, 1, 3, 0, 2 }, 0);
            m.RecalculateBounds();
            return m;
        }

        static Material MakeMaterial(Color c)
        {
            // URP Unlit so the bars never render magenta on Android (CLAUDE.md rule).
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var mat = new Material(shader) { enableInstancing = true, name = "ProgressBar" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", c);
            if (mat.HasProperty("_Cull"))      mat.SetFloat("_Cull", (float)CullMode.Off);
            mat.color = c;
            return mat;
        }
    }
}
