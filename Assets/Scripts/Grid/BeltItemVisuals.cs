using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Animated 3D belt visuals for particles and atoms. Any other item keeps its camera-facing icon tile on
    /// the belt (<see cref="ConveyorVisualizer"/>).
    ///
    /// <list type="bullet">
    /// <item>electron: a soft teal cloud around a bright core (<c>ElectronCloud</c> shader)</item>
    /// <item>up / down quark: a glossy bead that shakes and squashes ("unstable")</item>
    /// <item>proton / neutron: a solid shell that turns see-through for a moment every few seconds,
    /// showing its quarks (uud / udd) joined by gluon strings and circling inside</item>
    /// <item>alpha: a bare 2p + 2n nucleus in a violet halo; beta: a bright electron with a comet tail;
    /// neutrino: a tiny point whose halo swells and fades; gamma: a yellow-white wave packet in a glow</item>
    /// <item>every element and isotope (<see cref="AtomStructure"/>): a nucleus, one faint ring per electron
    /// shell and the outer electrons orbiting on the outer ring; noble gases glow, radioactive ones spark</item>
    /// <item>every molecule with a structure (<see cref="BeltMoleculeData"/>): a ball-and-stick model that tumbles</item>
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
        public static readonly Color ShellRing    = Hex(0x9FE8E1);
        public static readonly Color NobleGlow    = Hex(0xC9F7FF);
        public static readonly Color DecaySpark   = Hex(0xE6FF7A);
        public static readonly Color GammaWave    = Hex(0xFFF3B0);
        public static readonly Color GammaGlow    = Hex(0xFFE27A);
        public static readonly Color NeutrinoHalo = Hex(0xDDE6FF);

        /// <summary>How <see cref="ConveyorVisualizer"/> turns an item's visual each frame.</summary>
        public enum Facing
        {
            /// <summary>Left alone: round particles that animate in their own space.</summary>
            Fixed,
            /// <summary>Turned to face the camera (icon tiles, atoms: their rings are built facing the viewer).</summary>
            Camera,
            /// <summary>Local +Z along the item's direction of travel (a comet tail, a wave packet).</summary>
            AlongBelt,
        }

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
            { "alpha_particle", BuildAlpha },
            { "beta_particle",  BuildBeta },
            { "neutrino",       BuildNeutrino },
            { "gamma_photon",   BuildGamma },
        };

        private static readonly Dictionary<string, Facing> s_Facing = new()
        {
            { "alpha_particle", Facing.Camera },
            { "beta_particle",  Facing.AlongBelt },
            { "gamma_photon",   Facing.AlongBelt },
        };

        private static Mesh s_Sphere, s_Cloud, s_ProtonCore, s_NeutronCore;
        private static Material s_UpQuarkMat, s_DownQuarkMat, s_CoreMat, s_NucleonCoreMat,
                                s_ProtonShellMat, s_NeutronShellMat, s_CloudMat;
        private static Material s_NucleusMat, s_MetalNucleusMat, s_RingMat, s_OrbitMat, s_SparkMat, s_NobleGlowMat,
                                s_AlphaHaloMat, s_TailMat, s_WhiteMat, s_NeutrinoHaloMat, s_GammaMat, s_GammaGlowMat,
                                s_MoleculeMat, s_MetalMoleculeMat, s_FrostMat;
        // Atom meshes depend only on Z (and N for light nuclei), so they are shared by every item of an element.
        private static readonly Dictionary<string, Mesh> s_Meshes = new();

        /// <summary>The item ids that get a 3D belt visual.</summary>
        public static IEnumerable<string> RegisteredIds => s_Factories.Keys;

        /// <summary>True when <paramref name="itemId"/> is a registered particle with a 3D belt visual.</summary>
        public static bool Has(string itemId) => itemId != null && s_Factories.ContainsKey(itemId);

        /// <summary>True for an element or isotope: drawn as an atom.</summary>
        public static bool IsAtom(ItemSO item) =>
            item != null && item.atomicNumber > 0 &&
            (item.category == ItemCategory.Element || item.category == ItemCategory.Isotope);

        /// <summary>True for a molecule with a ball-and-stick structure (<see cref="BeltMoleculeData"/>).</summary>
        public static bool IsMolecule(ItemSO item) => item != null && !IsAtom(item) && BeltMoleculeData.Has(item.id);

        /// <summary>True when <paramref name="item"/> gets a 3D belt visual (a registered particle, an atom or a molecule).</summary>
        public static bool Has(ItemSO item) => item != null && (Has(item.id) || IsAtom(item) || IsMolecule(item));

        /// <summary>How the visual for <paramref name="item"/> should be turned each frame.</summary>
        public static Facing FacingOf(ItemSO item)
        {
            if (item == null) return Facing.Camera;
            if (Has(item.id)) return s_Facing.TryGetValue(item.id, out var f) ? f : Facing.Fixed;
            if (IsMolecule(item)) return Facing.Fixed;   // tumbles in its own space
            return Facing.Camera;                         // atoms and icon tiles
        }

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

        /// <summary>
        /// Builds the 3D visual for <paramref name="item"/>: a registered particle by id, otherwise an atom for
        /// any element or isotope. Null when it has none or a shader is missing (the caller uses the icon tile).
        /// </summary>
        public static GameObject TryCreate(ItemSO item, Transform parent)
        {
            if (item == null) return null;
            if (Has(item.id)) return TryCreate(item.id, parent);
            bool atom = IsAtom(item), molecule = !atom && IsMolecule(item);
            if (!atom && !molecule) return null;
            if (Shader.Find(ParticleShader) == null || Shader.Find(CloudShader) == null)
            {
                GameLogger.Warning($"[BeltItemVisuals] Particle shaders missing; '{item.id}' falls back to its icon tile.");
                return null;
            }
            return atom
                ? BuildAtom(parent, item.id, item.atomicNumber, AtomStructure.NeutronCount(item.atomicNumber, item.atomicMass),
                            item.isRadioactive)
                : BuildMolecule(parent, item.id);
        }

        /// <summary>
        /// A ball-and-stick molecule (<see cref="BeltMoleculeData"/>) that tumbles slowly about Y while its bonds
        /// vibrate. Ids in <see cref="MetalMolecules"/> are glossy metal.
        /// </summary>
        internal static GameObject BuildMolecule(Transform parent, string id)
        {
            var mesh = CachedMesh($"molecule_{id}", () => BeltMoleculeData.BuildMesh(id, ToShaderSpace));
            if (mesh == null) return null;
            var root = Root(parent, id);
            StructureParts.AddMesh("Molecule", root.transform, mesh,
                                   MetalMolecules.Contains(id) ? MetalMoleculeMaterial() : MoleculeMaterial(), Vector3.zero);
            return root;
        }

        /// <summary>
        /// Molecules drawn in glossy metal. Empty since the Ni/Fe catalyst left Chemistry (2026-11-23); Metallurgy's
        /// compounds join it when they get belt structures.
        /// </summary>
        internal static readonly System.Collections.Generic.HashSet<string> MetalMolecules = new();

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

        /// <summary>
        /// An atom (see <see cref="AtomStructure"/>). Built facing local -Z, which <see cref="ConveyorVisualizer"/>
        /// turns toward the camera, so every shell ring reads as an orbit.
        /// </summary>
        internal static GameObject BuildAtom(Transform parent, string id, int z, int neutrons, bool radioactive)
        {
            var root = Root(parent, id);
            float core = AtomStructure.NucleusRadius(z, neutrons);

            Mesh nucleus = z <= AtomStructure.MaxClusterZ
                ? CachedMesh($"nucleus_{z}_{neutrons}", () => BeltParticleMeshes.NucleusCluster(
                      z, neutrons, ToShaderSpace(Proton), ToShaderSpace(Neutron)))
                : CachedMesh($"heavy_{z}", () => BeltParticleMeshes.BeadedBall(
                      ToShaderSpace(AtomStructure.RegimeColor(z)), core, z));
            bool metal = z > AtomStructure.MaxClusterZ && AtomStructure.IsMetal(z);
            StructureParts.AddMesh("Nucleus", root.transform, nucleus, metal ? MetalNucleusMaterial() : NucleusMaterial(),
                                   Vector3.zero);

            StructureParts.AddMesh("Shells", root.transform,
                CachedMesh($"rings_{z}_{neutrons}", () => BeltParticleMeshes.AtomRings(z, core, ToShaderSpace(ShellRing))),
                RingMaterial(), Vector3.zero);

            // Outer electrons orbit in the outer ring's plane: their parent turns local Y onto the ring's normal
            // and the shader spins them about it.
            var shells = AtomStructure.ShellOccupancy(z);
            int outer  = shells.Length - 1;
            float r    = AtomStructure.ShellRadius(outer, shells.Length, core);
            var orbit  = new GameObject("Orbit");
            orbit.transform.SetParent(root.transform, worldPositionStays: false);
            orbit.transform.localRotation = Quaternion.FromToRotation(Vector3.up, AtomStructure.ShellNormal(z, outer));
            StructureParts.AddMesh("Electrons", orbit.transform,
                CachedMesh($"electrons_{shells[outer]}_{r:F4}", () => BeltParticleMeshes.BeadsOnCircle(
                    shells[outer], r, ToShaderSpace(Electron))),
                OrbitMaterial(), Vector3.zero);

            if (AtomStructure.IsNoble(z))
            {
                float glow = r * 1.12f;
                StructureParts.AddMesh("Glow", root.transform,
                    CachedMesh($"glow_{glow:F4}", () => BeltParticleMeshes.ElectronCloud(1, 0f, glow, glow, 1)),
                    NobleGlowMaterial(), Vector3.zero);
            }
            if (radioactive)
                StructureParts.AddMesh("Sparks", root.transform,
                    CachedMesh($"sparks_{z}", () => BeltParticleMeshes.DecaySparks(core, ToShaderSpace(DecaySpark), z)),
                    SparkMaterial(), Vector3.zero);
            return root;
        }

        private static GameObject BuildAlpha(Transform parent)
        {
            var root = Root(parent, "alpha_particle");
            StructureParts.AddMesh("Nucleus", root.transform,
                CachedMesh("nucleus_2_2", () => BeltParticleMeshes.NucleusCluster(2, 2, ToShaderSpace(Proton), ToShaderSpace(Neutron))),
                NucleusMaterial(), Vector3.zero);
            StructureParts.AddMesh("Halo", root.transform,
                CachedMesh("halo_0.085", () => BeltParticleMeshes.ElectronCloud(1, 0f, 0.085f, 0.085f, 1)),
                CloudMaterial(ref s_AlphaHaloMat, Proton, 0.32f, 0.08f, 1.4f), Vector3.zero);
            return root;
        }

        private static GameObject BuildBeta(Transform parent)
        {
            var root = Root(parent, "beta_particle");
            var core = StructureParts.AddMesh("Core", root.transform, SphereMesh(), CoreMaterial(), Vector3.zero);
            core.localScale = Vector3.one * 0.06f;
            StructureParts.AddMesh("Tail", root.transform,
                CachedMesh("comet_tail", () => BeltParticleMeshes.CometTail(ToShaderSpace(Electron))),
                TailMaterial(), Vector3.zero);
            return root;
        }

        private static GameObject BuildNeutrino(Transform parent)
        {
            var root = Root(parent, "neutrino");
            var core = StructureParts.AddMesh("Core", root.transform, SphereMesh(), WhiteMaterial(), Vector3.zero);
            core.localScale = Vector3.one * 0.018f;
            StructureParts.AddMesh("Halo", root.transform,
                CachedMesh("halo_0.07", () => BeltParticleMeshes.ElectronCloud(1, 0f, 0.07f, 0.07f, 1)),
                CloudMaterial(ref s_NeutrinoHaloMat, NeutrinoHalo, 0.3f, 0.45f, 2.2f), Vector3.zero);
            return root;
        }

        private static GameObject BuildGamma(Transform parent)
        {
            var root = Root(parent, "gamma_photon");
            StructureParts.AddMesh("Wave", root.transform,
                CachedMesh("wave_packet", () => BeltParticleMeshes.WavePacket(ToShaderSpace(GammaWave))),
                GammaMaterial(), Vector3.zero);
            StructureParts.AddMesh("Glow", root.transform,
                CachedMesh("halo_0.13", () => BeltParticleMeshes.ElectronCloud(1, 0f, 0.13f, 0.13f, 1)),
                CloudMaterial(ref s_GammaGlowMat, GammaGlow, 0.28f, 0.12f, 3.0f), Vector3.zero);
            return root;
        }

        // ----------------------------------------------------------------
        // Shared meshes and materials
        // ----------------------------------------------------------------

        private static Mesh CachedMesh(string key, Func<Mesh> build)
        {
            if (!s_Meshes.TryGetValue(key, out var mesh) || mesh == null)
                s_Meshes[key] = mesh = build();
            return mesh;
        }

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

        private static Material NucleusMaterial()
        {
            if (s_NucleusMat != null) return s_NucleusMat;
            s_NucleusMat = NewParticleMaterial(Color.white);   // colours come from the mesh
            s_NucleusMat.SetFloat("_JitterAmp", 0.004f);
            s_NucleusMat.SetFloat("_JitterFreq", 20f);
            s_NucleusMat.SetFloat("_SpinSpeed", 0.6f);
            return s_NucleusMat;
        }

        private static Material MetalNucleusMaterial()
        {
            if (s_MetalNucleusMat != null) return s_MetalNucleusMat;
            s_MetalNucleusMat = new Material(NucleusMaterial());
            s_MetalNucleusMat.SetFloat("_Metal", 0.8f);
            s_MetalNucleusMat.SetFloat("_Gloss", 1f);
            return s_MetalNucleusMat;
        }

        /// <summary>
        /// See-through particle material: alpha from the vertex colour, no fresnel rim, drawn after the belt. Keeps
        /// the mesh's own orientation (no random per-item turn), which rings, tails and sparks rely on.
        /// </summary>
        private static Material TransparentParticleMaterial(Color color)
        {
            var mat = NewParticleMaterial(color);
            mat.SetFloat("_PhaseSpin", 0f);
            mat.SetFloat("_RimAlpha", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = TransparentQueue;
            return mat;
        }

        private static Material MoleculeMaterial()
        {
            if (s_MoleculeMat != null) return s_MoleculeMat;
            s_MoleculeMat = NewParticleMaterial(Color.white);   // colours come from the mesh
            s_MoleculeMat.SetFloat("_SpinSpeed", 0.5f);          // slow tumble
            s_MoleculeMat.SetFloat("_JitterAmp", 0.003f);        // bond vibration
            s_MoleculeMat.SetFloat("_JitterFreq", 16f);
            return s_MoleculeMat;
        }

        private static Material MetalMoleculeMaterial()
        {
            if (s_MetalMoleculeMat != null) return s_MetalMoleculeMat;
            s_MetalMoleculeMat = new Material(MoleculeMaterial());
            s_MetalMoleculeMat.SetFloat("_Metal", 0.8f);
            s_MetalMoleculeMat.SetFloat("_Gloss", 1f);
            return s_MetalMoleculeMat;
        }

        /// <summary>Liquid hydrogen's droplet: a faint pale-blue bubble whose edge glows (frost).</summary>
        private static Material FrostMaterial()
        {
            if (s_FrostMat != null) return s_FrostMat;
            var frost = Hex(0xCDEBFF); frost.a = 0.2f;
            s_FrostMat = TransparentParticleMaterial(frost);
            s_FrostMat.SetFloat("_Metal", 0f);
            s_FrostMat.SetFloat("_Gloss", 0.6f);
            s_FrostMat.SetFloat("_RimGlow", 0.9f);
            return s_FrostMat;
        }

        private static Material RingMaterial()
        {
            if (s_RingMat != null) return s_RingMat;
            s_RingMat = TransparentParticleMaterial(Color.white);   // colour and alpha come from the mesh
            s_RingMat.SetFloat("_Metal", 0f);
            s_RingMat.SetFloat("_Gloss", 0f);
            s_RingMat.SetFloat("_TopLight", 0f);
            s_RingMat.SetFloat("_Emission", 1.2f);
            return s_RingMat;
        }

        private static Material OrbitMaterial()
        {
            if (s_OrbitMat != null) return s_OrbitMat;
            s_OrbitMat = NewParticleMaterial(Color.white);
            s_OrbitMat.SetFloat("_SpinSpeed", 1.6f);   // the outer electrons circle the nucleus
            return s_OrbitMat;
        }

        private static Material SparkMaterial()
        {
            if (s_SparkMat != null) return s_SparkMat;
            s_SparkMat = TransparentParticleMaterial(Color.white);
            s_SparkMat.SetFloat("_Gloss", 0f);
            s_SparkMat.SetFloat("_Emission", 1.4f);
            s_SparkMat.SetFloat("_JitterAmp", 0.008f);
            s_SparkMat.SetFloat("_JitterFreq", 18f);
            // Flicker: hidden 70% of each 0.9 s, so sparks flash rather than stay lit.
            s_SparkMat.SetFloat("_FadePeriod", 0.9f);
            s_SparkMat.SetFloat("_FadeOpen", 0.7f);
            s_SparkMat.SetFloat("_FadeMin", 0f);
            return s_SparkMat;
        }

        private static Material NobleGlowMaterial() => CloudMaterial(ref s_NobleGlowMat, NobleGlow, 0.22f, 0.05f, 1.2f);

        private static Material TailMaterial()
        {
            if (s_TailMat != null) return s_TailMat;
            s_TailMat = TransparentParticleMaterial(Color.white);
            s_TailMat.SetFloat("_Gloss", 0f);
            s_TailMat.SetFloat("_Emission", 1.25f);
            s_TailMat.SetFloat("_JitterAmp", 0.004f);
            s_TailMat.SetFloat("_JitterFreq", 30f);
            return s_TailMat;
        }

        private static Material WhiteMaterial()
        {
            if (s_WhiteMat != null) return s_WhiteMat;
            s_WhiteMat = NewParticleMaterial(Color.white);
            s_WhiteMat.SetFloat("_Emission", 1.4f);
            return s_WhiteMat;
        }

        private static Material GammaMaterial()
        {
            if (s_GammaMat != null) return s_GammaMat;
            s_GammaMat = NewParticleMaterial(Color.white);   // colour comes from the mesh
            s_GammaMat.SetFloat("_PhaseSpin", 0f);            // the wave points along the belt
            s_GammaMat.SetFloat("_Metal", 0f);
            s_GammaMat.SetFloat("_TopLight", 0f);
            s_GammaMat.SetFloat("_Emission", 1.3f);
            s_GammaMat.SetFloat("_JitterAmp", 0.004f);
            s_GammaMat.SetFloat("_JitterFreq", 14f);
            return s_GammaMat;
        }

        /// <summary>A soft additive halo (ElectronCloud shader) in <paramref name="color"/>.</summary>
        private static Material CloudMaterial(ref Material cache, Color color, float intensity, float breathe, float breatheFreq)
        {
            if (cache != null) return cache;
            cache = new Material(Shader.Find(CloudShader)) { renderQueue = TransparentQueue };
            cache.SetColor("_BaseColor", color);
            cache.SetFloat("_Intensity", intensity);
            cache.SetFloat("_Breathe", breathe);
            cache.SetFloat("_BreatheFreq", breatheFreq);
            cache.SetFloat("_SwirlSpeed", 0.3f);
            cache.SetFloat("_WobbleAmp", 0f);
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
