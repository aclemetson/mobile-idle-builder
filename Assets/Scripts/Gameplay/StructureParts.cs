using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Shared helpers for composing a building structure from child parts. A <see cref="StructureParts"/>
    /// instance owns the materials and meshes it creates, so a structure calls <see cref="Dispose"/> in
    /// OnDestroy and needs no per-part cleanup. Every part is visual only: no colliders, shadows or probes.
    /// </summary>
    public sealed class StructureParts
    {
        private readonly List<Object> _owned = new();

        /// <summary>Registers a mesh or material so <see cref="Dispose"/> destroys it.</summary>
        public T Own<T>(T asset) where T : Object
        {
            if (asset != null) _owned.Add(asset);
            return asset;
        }

        /// <summary>A flat unlit colour, or null if the shared URP material isn't available.</summary>
        public Material Flat(Color color)
        {
            var opaque = RenderingMaterials.Instance != null ? RenderingMaterials.Instance.Opaque : null;
            if (opaque == null) return null;
            var mat = Own(new Material(opaque));
            mat.SetColor("_BaseColor", color);
            return mat;
        }

        /// <summary>
        /// Unlit colour shaded along <c>uv.y</c>: darker low down, with a highlight at the top. On a sphere
        /// primitive this reads as lit from above at the cost of one registered shader. Falls back to
        /// <see cref="Flat"/> if the shader is missing.
        /// </summary>
        public Material Shaded(Color color, Color highlight, float highlightStrength = 0.6f)
        {
            var shader = Shader.Find("MobileIdleBuilder/CollectorStructure");
            if (shader == null) return Flat(color);
            var mat = Own(new Material(shader));
            mat.SetColor("_BaseColor", color);
            mat.SetColor("_TipColor", highlight);
            mat.SetFloat("_TipGlow", highlightStrength);
            return mat;
        }

        /// <summary>Adds a child rendering <paramref name="mesh"/> with <paramref name="material"/>.</summary>
        public static Transform AddMesh(string name, Transform parent, Mesh mesh, Material material,
                                        Vector3 localPosition)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPosition;
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            Plain(mr);
            return go.transform;
        }

        /// <summary>Adds a child primitive with its collider removed, so it never blocks raycasts or placement.</summary>
        public static Transform AddPrimitive(PrimitiveType type, string name, Transform parent, Material material,
                                             Vector3 localPosition, Vector3 localScale)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);
            go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.localPosition = localPosition;
            go.transform.localScale    = localScale;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = material;
            Plain(mr);
            return go.transform;
        }

        /// <summary>Stretches a cylinder primitive (2 units tall along Y) between two local points.</summary>
        public static void StretchBetween(Transform cylinder, Vector3 from, Vector3 to, float diameter)
        {
            Vector3 span = to - from;
            float length = span.magnitude;
            cylinder.localPosition = (from + to) * 0.5f;
            cylinder.localRotation = length > 1e-5f ? Quaternion.FromToRotation(Vector3.up, span / length) : Quaternion.identity;
            cylinder.localScale    = new Vector3(diameter, length * 0.5f, diameter);
        }

        /// <summary>Turns off the lighting work an unlit part doesn't need.</summary>
        public static void Plain(Renderer renderer)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows    = false;
            renderer.lightProbeUsage   = LightProbeUsage.Off;
        }

        public void Dispose()
        {
            foreach (var asset in _owned)
                if (asset != null) Object.Destroy(asset);
            _owned.Clear();
        }
    }

    /// <summary>
    /// Flat rings on the ground that travel between two radii and fade: outward for a generator broadcasting
    /// power, inward for a harvester drawing a field in. Owns its mesh and material through the given parts.
    /// </summary>
    public sealed class GroundRipples
    {
        private const float BaseRadius = 0.5f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private readonly Transform[]    _rings;
        private readonly MeshRenderer[] _renderers;
        private readonly MaterialPropertyBlock _block = new();

        public GroundRipples(StructureParts parts, Transform parent, int count, float tubeRadius)
        {
            _rings     = new Transform[count];
            _renderers = new MeshRenderer[count];

            var transparent = RenderingMaterials.Instance != null ? RenderingMaterials.Instance.Transparent : null;
            if (transparent == null) return;

            // Instanced so the rings draw AFTER the floor tiles (same queue 3000, no depth write); otherwise
            // the floor sometimes sorts on top of them.
            var material = parts.Own(new Material(transparent) { renderQueue = 3200 });
            var mesh = parts.Own(TorusMeshBuilder.Build(40, 5,
                new TorusMeshBuilder.TorusProfile { MajorRadius = BaseRadius, MinorRadius = tubeRadius }));

            for (int i = 0; i < count; i++)
            {
                _rings[i] = StructureParts.AddMesh($"Ripple_{i}", parent, mesh, material, new Vector3(0f, 0.02f, 0f));
                _renderers[i] = _rings[i].GetComponent<MeshRenderer>();
            }
        }

        /// <summary>
        /// Positions every ring. Each travels from scale <paramref name="startScale"/> to
        /// <paramref name="endScale"/> over <paramref name="period"/> seconds, staggered evenly, with alpha
        /// fading from <paramref name="startAlpha"/> to <paramref name="endAlpha"/>.
        /// </summary>
        public void Animate(float time, float period, float startScale, float endScale,
                            float startAlpha, float endAlpha, Color color)
        {
            for (int i = 0; i < _rings.Length; i++)
            {
                if (_rings[i] == null) continue;
                float phase = (time / period + (float)i / _rings.Length) % 1f;
                float scale = Mathf.Lerp(startScale, endScale, phase);
                _rings[i].localScale = new Vector3(scale, scale, scale);
                _block.SetColor(BaseColorId, new Color(color.r, color.g, color.b, Mathf.Lerp(startAlpha, endAlpha, phase)));
                _renderers[i].SetPropertyBlock(_block);
            }
        }
    }
}
