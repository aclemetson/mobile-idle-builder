using System;
using System.Collections.Generic;
using System.IO;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

namespace MobileIdleBuilder.Editor
{
    /// <summary>
    /// Renders each building's in-game 3D model to a transparent icon for the Builder cards (#487).
    ///
    /// Builds the model the same way BuildingVisualizer does (a cube host plus the bespoke *Structure component
    /// for its structure kind, the Demon's torus, or the field collector spindle), without ECS: every structure
    /// handles a missing world. Renders it offscreen with PreviewRenderUtility from the game's camera angle and
    /// writes Assets/Art/Icons/Buildings/{id}.png. GameDataImporter picks those up as BuildingSO.icon while the
    /// building's icon_path is "TODO", so run "MobileIdleBuilder/Import Game Data" after generating.
    ///
    /// Buildings with no bespoke model (still a placeholder cube in game) are skipped: the Builder card shows
    /// their main product's icon instead (HUDController.BuildingCardSprite).
    /// </summary>
    public static class BuildingIconRenderer
    {
        const int Size = 256;
        public const string OutDir = GameDataImporter.BuildingIconsDir;

        /// <summary>True when the building has a model worth rendering (not the placeholder cube).</summary>
        /// The Molecular Synthesizer's form is shared by eight Chemistry/Biology buildings and is a plain sphere when
        /// still, so those show their product too.
        public static bool HasModel(BuildingSO b) =>
            b != null && b.structureKind != BuildingStructureKind.MolecularSynthesizer
            && (b.structureKind != BuildingStructureKind.None || b.isEntropySink || IsCollector(b));

        // Field harvesters: placed on fields, drawn as the collector spindle in the field's colour.
        static bool IsCollector(BuildingSO b) =>
            b.compatibleFields != null && b.compatibleFields.Length > 0 && b.structureKind == BuildingStructureKind.None;

        static Material s_flow;

        // Stand-in for the belts' flow / stream material (ConveyorVisualizer owns the real one at runtime).
        static Material FlowMaterial()
        {
            if (s_flow != null) return s_flow;
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            s_flow = new Material(shader) { color = new Color(0.32f, 0.62f, 1f) };
            if (s_flow.HasProperty("_BaseColor")) s_flow.SetColor("_BaseColor", new Color(0.32f, 0.62f, 1f));
            return s_flow;
        }

        [MenuItem("MobileIdleBuilder/Generate Building Icons")]
        public static void GenerateAll()
        {
            Directory.CreateDirectory(OutDir);
            var guids = AssetDatabase.FindAssets("t:BuildingSO");
            int made = 0, skipped = 0, failed = 0;
            var preview = new PreviewRenderUtility();
            try
            {
                Setup(preview);
                foreach (var guid in guids)
                {
                    var b = AssetDatabase.LoadAssetAtPath<BuildingSO>(AssetDatabase.GUIDToAssetPath(guid));
                    if (b == null || string.IsNullOrEmpty(b.id)) continue;
                    if (!HasModel(b))
                    {
                        skipped++;
                        var stale = $"{OutDir}/{GameDataImporter.Sanitize(b.id)}.png";
                        if (File.Exists(stale)) AssetDatabase.DeleteAsset(stale);
                        continue;
                    }
                    try
                    {
                        var png = Render(preview, b);
                        if (png == null) { failed++; continue; }
                        File.WriteAllBytes($"{OutDir}/{GameDataImporter.Sanitize(b.id)}.png", png);
                        made++;
                    }
                    catch (Exception e)
                    {
                        failed++;
                        Debug.LogWarning($"[BuildingIconRenderer] {b.id}: {e.Message}");
                    }
                }
            }
            finally
            {
                preview.Cleanup();
            }
            AssetDatabase.Refresh();
            ConfigureImports();
            Debug.Log($"[BuildingIconRenderer] {made} icons, {skipped} without a model (product icon), {failed} failed. Run Import Game Data.");
        }

        static void Setup(PreviewRenderUtility preview)
        {
            var cam = preview.camera;
            cam.fieldOfView     = 26f;
            cam.nearClipPlane   = 0.05f;
            cam.farClipPlane    = 100f;
            cam.clearFlags      = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            preview.lights[0].intensity          = 1.25f;
            preview.lights[0].transform.rotation = Quaternion.Euler(45f, 30f, 0f);
            preview.lights[1].intensity          = 0.6f;
            preview.ambientColor                 = new Color(0.35f, 0.38f, 0.45f);
        }

        static byte[] Render(PreviewRenderUtility preview, BuildingSO b)
        {
            var root = BuildModel(b);
            if (root == null) return null;
            try
            {
                preview.AddSingleGO(root);
                var bounds = Bounds(root);
                // The game's view: looking down at ~50 degrees, turned 35 degrees.
                var dir = Quaternion.Euler(50f, -35f, 0f) * Vector3.back;
                float radius = Mathf.Max(0.01f, bounds.extents.magnitude);
                float dist = radius / Mathf.Sin(preview.camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 1.08f;
                preview.camera.transform.position = bounds.center + dir * dist;
                preview.camera.transform.LookAt(bounds.center);

                // Read the camera's own target (EndStaticPreview copies to RGB and loses the transparent background).
                preview.BeginStaticPreview(new Rect(0, 0, Size, Size));
                preview.Render(true);
                var rt = preview.camera.targetTexture;
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
                tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                UnityEngine.Object.DestroyImmediate(preview.EndStaticPreview());
                var png = tex.EncodeToPNG();
                UnityEngine.Object.DestroyImmediate(tex);
                return png;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        /// <summary>The building's model, built as BuildingVisualizer builds it (cell size 1, no ECS entity).</summary>
        static GameObject BuildModel(BuildingSO b)
        {
            int fw = Mathf.Max(1, b.footprint.x), fh = Mathf.Max(1, b.footprint.y);
            var kind  = b.structureKind;
            var color = BuildingVisualizer.BaseColorFor(kind, b.isEntropySink);
            var host  = GameObject.CreatePrimitive(PrimitiveType.Cube);
            host.name = $"Icon_{b.id}";
            UnityEngine.Object.DestroyImmediate(host.GetComponent<Collider>());
            host.transform.localScale = Vector3.one;
            var pr = host.AddComponent<PresenceReceiver>();
            pr.SetBaseColor(color);

            if (b.isEntropySink)
            {
                host.transform.localScale = new Vector3(fw - 0.2f, fw - 0.2f, fh - 0.2f);
                host.AddComponent<EntropySinkStructure>().Initialize(Entity.Null);
                return host;
            }
            switch (kind)
            {
                case BuildingStructureKind.AtomGenerator:
                    host.transform.localScale = Vector3.one * (1f + 0.25f * (Mathf.Max(fw, fh) - 1));
                    host.AddComponent<AtomGeneratorStructure>().Initialize(Entity.Null); break;
                case BuildingStructureKind.IsotopicManipulator:
                    host.AddComponent<IsotopicManipulatorStructure>().Initialize(Entity.Null); break;
                case BuildingStructureKind.BasicGenerator:
                    host.AddComponent<BasicGeneratorStructure>().Initialize(color); break;
                case BuildingStructureKind.PowerRelay:
                    host.AddComponent<PowerRelayStructure>().Initialize(fw, fh); break;
                case BuildingStructureKind.StrongForceCombiner:
                    host.AddComponent<StrongForceCombinerStructure>().Initialize(Entity.Null, fw, fh, color); break;
                case BuildingStructureKind.MolecularSynthesizer:
                    host.AddComponent<MolecularSynthesizerStructure>().Initialize(Entity.Null); break;
                case BuildingStructureKind.MaterialsForge:
                    host.AddComponent<MaterialsForgeStructure>().Initialize(Entity.Null); break;
                case BuildingStructureKind.ComponentFabricator:
                    host.AddComponent<ComponentFabricatorStructure>().Initialize(Entity.Null); break;
                case BuildingStructureKind.RadioactiveContainment:
                    host.AddComponent<RadioactiveContainmentStructure>().Initialize(fw, fh); break;
                case BuildingStructureKind.FusionReactor:
                    host.AddComponent<FusionReactorStructure>().Initialize(Entity.Null, color); break;
                case BuildingStructureKind.NucleonHarvester:
                    host.AddComponent<NucleonHarvesterStructure>().Initialize(Entity.Null, fw, fh,
                        new Vector2(0f, -fh * 0.5f), FieldColor(b)); break;
                case BuildingStructureKind.BeltSplitter:
                    host.AddComponent<SplitterStructure>().Initialize(Entity.Null, (int)OutputDirection.North,
                        new List<int> { (int)OutputDirection.West, (int)OutputDirection.East }, color, FlowMaterial()); break;
                case BuildingStructureKind.Spring:
                    host.AddComponent<SpringStructure>().Initialize((int)OutputDirection.South, color, FlowMaterial()); break;
                default:
                    if (IsCollector(b)) { host.AddComponent<CollectorStructure>().Initialize(Entity.Null, FieldColor(b)); break; }
                    UnityEngine.Object.DestroyImmediate(host);
                    return null;
            }
            return host;
        }

        // A collector looks like the field it sits on: the colour of the first field type it accepts.
        static Color FieldColor(BuildingSO b)
        {
            if (b.compatibleFields != null && b.compatibleFields.Length > 0)
                foreach (var guid in AssetDatabase.FindAssets("t:FieldSO"))
                {
                    var f = AssetDatabase.LoadAssetAtPath<FieldSO>(AssetDatabase.GUIDToAssetPath(guid));
                    if (f != null && Array.IndexOf(b.compatibleFields, f.fieldType) >= 0) return f.fieldColor;
                }
            return new Color(0.35f, 0.8f, 0.9f);
        }

        static Bounds Bounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(root.transform.position, Vector3.one);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers) if (r.enabled) bounds.Encapsulate(r.bounds);
            return bounds;
        }

        static void ConfigureImports()
        {
            foreach (var path in Directory.GetFiles(OutDir, "*.png"))
            {
                var p = path.Replace('\\', '/');
                if (AssetImporter.GetAtPath(p) is TextureImporter ti
                    && (ti.textureType != TextureImporterType.Sprite || ti.spriteImportMode != SpriteImportMode.Single))
                {
                    ti.textureType = TextureImporterType.Sprite;
                    ti.spriteImportMode = SpriteImportMode.Single;   // one sprite per file, loadable as Sprite
                    ti.alphaIsTransparency = true;
                    ti.mipmapEnabled = false;
                    ti.SaveAndReimport();
                }
            }
        }
    }
}
