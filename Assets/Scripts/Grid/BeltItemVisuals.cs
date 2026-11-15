using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Animated 3D belt visuals for the basic particles. Any item id not registered here keeps its
    /// camera-facing icon tile on the belt (<see cref="ConveyorVisualizer"/>).
    ///
    /// <list type="bullet">
    /// <item>electron: a soft teal cloud around a bright core (<c>ElectronCloud</c> shader)</item>
    /// <item>up / down quark: a glossy bead that shakes and squashes ("unstable")</item>
    /// <item>proton / neutron: a solid shell that turns see-through for a moment every few seconds,
    /// showing its quarks (uud / udd) joined by gluon strings and circling inside</item>
    /// </list>
    ///
    /// All motion is in the shaders, keyed off each item's world position, so a visual has no
    /// per-instance state: it can be pooled and reused for any item of the same id. Meshes and
    /// materials are shared statics, created on first use.
    /// </summary>
    public static class BeltItemVisuals
    {
        public static readonly Color UpQuark      = Hex(0xFF6B35);
        public static readonly Color DownQuark    = Hex(0x6E7BFF);
        public static readonly Color Electron     = Hex(0x4ECDC4);
        public static readonly Color ElectronCore = Hex(0xE8FFFD);
        public static readonly Color Proton       = Hex(0xD2A8FF);
        public static readonly Color Neutron      = Hex(0x8B949E);
        public static readonly Color Gluon        = Hex(0xF2E86D);

        /// <summary>Belt item diameter in tiles (an icon tile is 0.4).</summary>
        public const float ItemSize  = 0.36f;
        public const float QuarkSize = 0.14f;
        public const float CoreSize  = 0.05f;

        private const string ParticleShader = "MobileIdleBuilder/BeltParticle";
        private const string CloudShader    = "MobileIdleBuilder/ElectronCloud";

        // Draw after the belt lattice (ConveyorField is Transparent+50).
        private const int TransparentQueue = 3100;

        private static readonly Dictionary<string, Func<Transform, GameObject>> s_Factories = new()
        {
            { "electron",   BuildElectron },
            { "up_quark",   p => BuildQuark(p, "up_quark",   QuarkMaterial(ref s_UpQuarkMat, UpQuark, 30f)) },
            { "down_quark", p => BuildQuark(p, "down_quark", QuarkMaterial(ref s_DownQuarkMat, DownQuark, 26f)) },
            { "proton",     p => BuildNucleon(p, "proton",  ref s_ProtonCore,  UpQuark, UpQuark, DownQuark,
                                                ShellMaterial(ref s_ProtonShellMat, Proton, 4.0f)) },
            { "neutron",    p => BuildNucleon(p, "neutron", ref s_NeutronCore, UpQuark, DownQuark, DownQuark,
                                                ShellMaterial(ref s_NeutronShellMat, Neutron, 4.7f)) },
        };

        private static Mesh s_Sphere, s_Cloud, s_ProtonCore, s_NeutronCore;
        private static Material s_UpQuarkMat, s_DownQuarkMat, s_CoreMat, s_NucleonCoreMat,
                                s_ProtonShellMat, s_NeutronShellMat, s_CloudMat;

        /// <summary>The item ids that get a 3D belt visual.</summary>
        public static IEnumerable<string> RegisteredIds => s_Factories.Keys;

        /// <summary>True when <paramref name="itemId"/> has a 3D belt visual.</summary>
        public static bool Has(string itemId) => itemId != null && s_Factories.ContainsKey(itemId);

        /// <summary>
        /// Builds the 3D visual for <paramref name="itemId"/> under <paramref name="parent"/>, or returns null
        /// when the id isn't registered or a shader is missing (the caller then uses the icon tile).
        /// </summary>
        public static GameObject TryCreate(string itemId, Transform parent)
        {
            if (!Has(itemId)) return null;
            if (Shader.Find(ParticleShader) == null || Shader.Find(CloudShader) == null)
            {
                GameLogger.Warning($"[BeltItemVisuals] Particle shaders missing; '{itemId}' falls back to its icon tile.");
                return null;
            }
            return s_Factories[itemId](parent);
        }

        // ----------------------------------------------------------------
        // Builders
        // ----------------------------------------------------------------

        private static GameObject Root(Transform parent, string id)
        {
            var root = new GameObject($"BeltParticle_{id}");
            root.transform.SetParent(parent, worldPositionStays: false);
            return root;
        }

        private static GameObject BuildQuark(Transform parent, string id, Material material)
        {
            var root = Root(parent, id);
            var bead = StructureParts.AddMesh("Bead", root.transform, SphereMesh(), material, Vector3.zero);
            bead.localScale = Vector3.one * QuarkSize;
            return root;
        }

        private static GameObject BuildNucleon(Transform parent, string id, ref Mesh coreMesh,
                                               Color q0, Color q1, Color q2, Material shellMaterial)
        {
            // Vertex colours skip the sRGB->linear conversion that Material.SetColor does, so convert here.
            if (coreMesh == null)
                coreMesh = BeltParticleMeshes.NucleonCore(ToShaderSpace(q0), ToShaderSpace(q1),
                                                          ToShaderSpace(q2), ToShaderSpace(Gluon));
            var root = Root(parent, id);
            StructureParts.AddMesh("Quarks", root.transform, coreMesh, NucleonCoreMaterial(), Vector3.zero);
            var shell = StructureParts.AddMesh("Shell", root.transform, SphereMesh(), shellMaterial, Vector3.zero);
            shell.localScale = Vector3.one * ItemSize;
            return root;
        }

        private static GameObject BuildElectron(Transform parent)
        {
            var root = Root(parent, "electron");
            var core = StructureParts.AddMesh("Core", root.transform, SphereMesh(), CoreMaterial(), Vector3.zero);
            core.localScale = Vector3.one * CoreSize;
            if (s_Cloud == null) s_Cloud = BeltParticleMeshes.ElectronCloud();
            StructureParts.AddMesh("Cloud", root.transform, s_Cloud, CloudMaterial(), Vector3.zero);
            return root;
        }

        // ----------------------------------------------------------------
        // Shared meshes and materials
        // ----------------------------------------------------------------

        private static Mesh SphereMesh()
        {
            if (s_Sphere == null) s_Sphere = BeltParticleMeshes.UnitSphere();
            return s_Sphere;
        }

        private static Material NewParticleMaterial(Color color)
        {
            var mat = new Material(Shader.Find(ParticleShader));
            mat.SetColor("_BaseColor", color);
            // Approved look (2026-11-14, "D3" in the editor look test): glossy, lightly metallic.
            mat.SetFloat("_Metal", 0.4f);
            mat.SetFloat("_Gloss", 0.9f);
            mat.SetFloat("_TopLight", 0.2f);
            mat.SetFloat("_Saturation", 1.15f);
            return mat;
        }

        private static Material QuarkMaterial(ref Material cache, Color color, float frequency)
        {
            if (cache != null) return cache;
            cache = NewParticleMaterial(color);
            cache.SetFloat("_JitterAmp", 0.018f);
            cache.SetFloat("_JitterFreq", frequency);
            cache.SetFloat("_SquashAmp", 0.14f);
            return cache;
        }

        private static Material CoreMaterial()
        {
            if (s_CoreMat != null) return s_CoreMat;
            s_CoreMat = NewParticleMaterial(ElectronCore);
            return s_CoreMat;
        }

        private static Material NucleonCoreMaterial()
        {
            if (s_NucleonCoreMat != null) return s_NucleonCoreMat;
            s_NucleonCoreMat = NewParticleMaterial(Color.white);   // colours come from the mesh
            s_NucleonCoreMat.SetFloat("_JitterAmp", 0.006f);
            s_NucleonCoreMat.SetFloat("_JitterFreq", 24f);
            s_NucleonCoreMat.SetFloat("_SpinSpeed", 1.4f);
            return s_NucleonCoreMat;
        }

        private static Material ShellMaterial(ref Material cache, Color color, float fadePeriod)
        {
            if (cache != null) return cache;
            cache = NewParticleMaterial(color);
            cache.SetFloat("_FadePeriod", fadePeriod);
            cache.SetFloat("_FadeOpen", 0.22f);
            cache.SetFloat("_FadeMin", 0.06f);
            cache.SetFloat("_RimAlpha", 0.85f);
            cache.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            cache.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            cache.SetFloat("_ZWrite", 0f);
            cache.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            cache.SetOverrideTag("RenderType", "Transparent");
            cache.renderQueue = TransparentQueue;
            return cache;
        }

        private static Material CloudMaterial()
        {
            if (s_CloudMat != null) return s_CloudMat;
            s_CloudMat = new Material(Shader.Find(CloudShader)) { renderQueue = TransparentQueue };
            s_CloudMat.SetColor("_BaseColor", Electron);
            return s_CloudMat;
        }

        private static Color ToShaderSpace(Color c) =>
            QualitySettings.activeColorSpace == ColorSpace.Linear ? c.linear : c;

        private static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
