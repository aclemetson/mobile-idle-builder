using System.Collections.Generic;
using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder.Editor
{
    // Generates periodic-table-style tile sprites for every ItemSO:
    // a regime-coloured rounded rectangle showing the atomic number (top-left),
    // the element symbol (large, centred) and the atomic mass (below). Isotopes
    // therefore read distinctly from their parent element via the mass line.
    //
    // Output: Assets/Art/Icons/Elements/{id}.png, imported as Sprites. The tiles
    // are wired onto ItemSO.icon by GameDataImporter's convention fallback (it
    // picks up this path when icon_path is still "TODO"), so run
    // "MobileIdleBuilder/Import Game Data" after generating.
    public static class ElementIconGenerator
    {
        const int Size = 256;
        const string FontPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset";

        // Nuclear creation-regime palette, mirrored from docs/gameplay_loop_data.js
        // (nuclear.regimes) so the in-game tiles match the docs periodic table.
        static readonly Color Genesis   = Hex("#d2a8ff");
        static readonly Color Fusion    = Hex("#3fb950");
        static readonly Color Fission   = Hex("#f0883e");
        static readonly Color Field     = Hex("#58a6ff");
        static readonly Color Breeding  = Hex("#db61a2");
        static readonly Color Synthesis = Hex("#e3b341");
        static readonly Color NonElement = Hex("#6e7681"); // particles / molecules / alloys / components
        static readonly Color BaseDark  = Hex("#0d1117");  // dark backdrop the regime tint sits over

        // Batch entrypoint (-executeMethod): generate all tiles, then re-import game data so
        // the convention fallback assigns them to ItemSO.icon. Exits Unity with a status code.
        // Must run WITHOUT -nographics (TMP text needs a graphics device to render).
        public static void GenerateAndImportBatch()
        {
            int code = 0;
            try
            {
                GenerateAll();
                GameDataImporter.Import();
                AssetDatabase.SaveAssets();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ElementIconGenerator] Batch generation failed: {e}");
                code = 1;
            }
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        [MenuItem("MobileIdleBuilder/Generate Element Icons")]
        public static void GenerateAll()
        {
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontPath);
            if (font == null)
            {
                Debug.LogError($"[ElementIconGenerator] Font not found at '{FontPath}'. Aborting.");
                return;
            }

            var items = LoadAllItems();
            if (items.Count == 0)
            {
                Debug.LogWarning("[ElementIconGenerator] No ItemSO assets found under Assets/Resources/Items.");
                return;
            }

            Directory.CreateDirectory(GameDataImporter.GeneratedIconsDir);

            var rig = new IconRig(font);
            var writtenPaths = new List<string>(items.Count);
            try
            {
                for (int i = 0; i < items.Count; i++)
                {
                    var it = items[i];
                    if (EditorUtility.DisplayCancelableProgressBar(
                            "Generating element icons",
                            $"{it.displayName} ({i + 1}/{items.Count})",
                            (i + 1f) / items.Count))
                    {
                        Debug.LogWarning("[ElementIconGenerator] Cancelled.");
                        return;
                    }

                    var tex = rig.Render(it);
                    string path = $"{GameDataImporter.GeneratedIconsDir}/{GameDataImporter.Sanitize(it.id)}.png";
                    File.WriteAllBytes(path, tex.EncodeToPNG());
                    writtenPaths.Add(path);
                    Object.DestroyImmediate(tex);
                }
            }
            finally
            {
                rig.Dispose();
                EditorUtility.ClearProgressBar();
            }

            AssetDatabase.Refresh();
            foreach (var path in writtenPaths)
                ConfigureAsSprite(path);
            AssetDatabase.SaveAssets();

            Debug.Log($"[ElementIconGenerator] Generated {writtenPaths.Count} tile icons into " +
                      $"{GameDataImporter.GeneratedIconsDir}. Run 'MobileIdleBuilder/Import Game Data' to assign them.");
        }

        static List<ItemSO> LoadAllItems()
        {
            var items = new List<ItemSO>();
            foreach (var guid in AssetDatabase.FindAssets("t:ItemSO", new[] { "Assets/Resources/Items" }))
            {
                var so = AssetDatabase.LoadAssetAtPath<ItemSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (so != null) items.Add(so);
            }
            // Deterministic order: elements by Z, then everything else by itemId.
            items.Sort((a, b) =>
            {
                int za = a.atomicNumber > 0 ? a.atomicNumber : int.MaxValue;
                int zb = b.atomicNumber > 0 ? b.atomicNumber : int.MaxValue;
                return za != zb ? za.CompareTo(zb) : a.itemId.CompareTo(b.itemId);
            });
            return items;
        }

        static Color RegimeColor(ItemSO it)
        {
            int z = it.atomicNumber;
            if (z <= 0) return NonElement;
            if (z == 1) return Genesis;
            if (z <= 26) return Fusion;
            if (z <= 80) return Fission;
            if (z <= 92) return Field;
            if (z <= 100) return Breeding;
            return Synthesis;
        }

        static void ConfigureAsSprite(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 256;
            importer.SaveAndReimport();
        }

        static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out var c);
            return c;
        }

        // ── Offscreen render rig (reused across all tiles) ────────────────────
        // A temporary orthographic camera captures TMP 3D text over a transparent
        // background; the coloured rounded-rect backdrop is drawn procedurally and
        // the text is alpha-composited on top. Rendered via SubmitRenderRequest,
        // the URP-correct on-demand render path in Unity 6.
        sealed class IconRig
        {
            const int Layer = 31;            // dedicated, isolates the rig from scene geometry
            const float Margin = 12f;        // tile inset from the texture edge (px)
            const float CornerRadius = 34f;  // rounded-corner radius (px)
            const float BorderThickness = 6f;

            readonly GameObject _root;
            readonly Camera _cam;
            readonly RenderTexture _rt;
            readonly TextMeshPro _number, _symbol, _mass;

            public IconRig(TMP_FontAsset font)
            {
                _root = new GameObject("~ElementIconRig") { hideFlags = HideFlags.HideAndDontSave };

                var camGo = new GameObject("Cam") { hideFlags = HideFlags.HideAndDontSave };
                camGo.transform.SetParent(_root.transform);
                // 1 world unit == 1 pixel; tile spans [0,Size] on both axes.
                camGo.transform.position = new Vector3(Size / 2f, Size / 2f, -10f);
                _cam = camGo.AddComponent<Camera>();
                _cam.orthographic = true;
                _cam.orthographicSize = Size / 2f;
                _cam.clearFlags = CameraClearFlags.SolidColor;
                _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
                _cam.cullingMask = 1 << Layer;
                _cam.nearClipPlane = 0.1f;
                _cam.farClipPlane = 100f;
                _cam.enabled = false;

                // No MSAA: SDF text is already smoothly anti-aliased and the CPU-drawn
                // backdrop has its own edge AA, so we avoid MSAA-resolve issues with ReadPixels.
                _rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32)
                {
                    antiAliasing = 1
                };

                // NOTE: TMP 3D fontSize is in world units; at this camera scale ~1pt ≈ 0.3px,
                // so sizes are large to read at 256px. Number/mass ~40px, symbol ~100px.
                _number = MakeLabel(font, "Number", TextAlignmentOptions.TopLeft, 118f, Hex("#e6edf3"),
                    new Vector4(26f, 22f, 0f, 0f));
                _symbol = MakeLabel(font, "Symbol", TextAlignmentOptions.Center, 320f, Hex("#ffffff"),
                    new Vector4(16f, 0f, 16f, 10f));
                // Auto-shrink long formulas (e.g. molecule/component symbols like "SiO2", "UF6")
                // so they fit the tile width, while 1-2 letter element symbols stay large.
                _symbol.enableAutoSizing = true;
                _symbol.fontSizeMax = 320f;
                _symbol.fontSizeMin = 90f;
                _mass = MakeLabel(font, "Mass", TextAlignmentOptions.Bottom, 132f, Hex("#e6edf3"),
                    new Vector4(0f, 0f, 0f, 26f));
            }

            TextMeshPro MakeLabel(TMP_FontAsset font, string name, TextAlignmentOptions align,
                float fontSize, Color color, Vector4 margin)
            {
                var go = new GameObject(name) { layer = Layer, hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetParent(_root.transform);
                go.transform.position = new Vector3(Size / 2f, Size / 2f, 0f);
                var tmp = go.AddComponent<TextMeshPro>();
                tmp.font = font;
                tmp.fontSize = fontSize;
                tmp.color = color;
                tmp.alignment = align;
                tmp.fontStyle = FontStyles.Bold;
                tmp.enableWordWrapping = false;
                tmp.margin = margin;
                var rt = tmp.rectTransform;
                rt.sizeDelta = new Vector2(Size, Size);
                return tmp;
            }

            public Texture2D Render(ItemSO it)
            {
                bool isElement = it.atomicNumber > 0;
                SetText(_number, isElement ? it.atomicNumber.ToString() : string.Empty);
                SetText(_symbol, NormalizeSymbol(string.IsNullOrEmpty(it.symbol) ? it.displayName : it.symbol));
                SetText(_mass, isElement && it.atomicMass > 0 ? it.atomicMass.ToString() : string.Empty);

                // Render the text layer over a transparent background.
                var request = new RenderPipeline.StandardRequest { destination = _rt };
                if (RenderPipeline.SupportsRenderRequest(_cam, request))
                    RenderPipeline.SubmitRenderRequest(_cam, request);
                else
                    _cam.Render(); // built-in RP fallback

                var textLayer = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                var prev = RenderTexture.active;
                RenderTexture.active = _rt;
                textLayer.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                textLayer.Apply();
                RenderTexture.active = prev;

                var result = Composite(RegimeColor(it), textLayer);
                Object.DestroyImmediate(textLayer);
                return result;
            }

            static void SetText(TextMeshPro tmp, string text)
            {
                tmp.text = text;
                tmp.ForceMeshUpdate();
            }

            // The LiberationSans SDF atlas has no subscript/superscript glyphs, so molecule and
            // component formulas (H2O, SiO2, UF6, Fe2O3, Fe-C) would render as boxes. Fold those
            // decorative code points down to ASCII so every symbol renders.
            static string NormalizeSymbol(string s)
            {
                if (string.IsNullOrEmpty(s)) return s;
                var sb = new StringBuilder(s.Length);
                foreach (char c in s)
                {
                    if (c >= '₀' && c <= '₉') sb.Append((char)('0' + (c - '₀')));      // subscript 0-9
                    else if (c == '⁰') sb.Append('0');
                    else if (c == '¹') sb.Append('1');
                    else if (c == '²') sb.Append('2');
                    else if (c == '³') sb.Append('3');
                    else if (c >= '⁴' && c <= '⁹') sb.Append((char)('4' + (c - '⁴'))); // superscript 4-9
                    else if (c == '·' || c == '•' || c == '°') sb.Append('-');         // middle dot / bullet / degree
                    else sb.Append(c);
                }
                return sb.ToString();
            }

            // Draws the rounded-rect backdrop and alpha-composites the text over it.
            static Texture2D Composite(Color regime, Texture2D textLayer)
            {
                Color inner = Color.Lerp(BaseDark, regime, 0.35f); // muted regime tint
                Color border = regime;
                var pixels = new Color32[Size * Size];
                var textPixels = textLayer.GetPixels32();

                Vector2 center = new Vector2(Size / 2f, Size / 2f);
                Vector2 half = new Vector2((Size - 2f * Margin) / 2f, (Size - 2f * Margin) / 2f);

                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        float sd = RoundedRectSD(new Vector2(x + 0.5f, y + 0.5f), center, half, CornerRadius);

                        // Coverage: 1 well inside, fading to 0 across the outer edge (AA).
                        float coverage = Mathf.Clamp01(0.5f - sd);
                        Color bg;
                        if (-sd <= BorderThickness)
                        {
                            // Blend border->inner across a 1px seam for a clean inner edge.
                            float t = Mathf.Clamp01(-sd - BorderThickness + 1f);
                            bg = Color.Lerp(border, inner, t);
                        }
                        else
                        {
                            bg = inner;
                        }
                        bg.a = coverage;

                        Color txt = textPixels[y * Size + x];
                        // Alpha-over: text on top of the backdrop.
                        float ta = txt.a;
                        Color outc;
                        outc.r = txt.r * ta + bg.r * (1f - ta);
                        outc.g = txt.g * ta + bg.g * (1f - ta);
                        outc.b = txt.b * ta + bg.b * (1f - ta);
                        outc.a = ta + bg.a * (1f - ta);
                        pixels[y * Size + x] = outc;
                    }
                }

                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                tex.SetPixels32(pixels);
                tex.Apply();
                return tex;
            }

            // Signed distance to a rounded rectangle (negative inside).
            static float RoundedRectSD(Vector2 p, Vector2 center, Vector2 half, float radius)
            {
                Vector2 q = new Vector2(Mathf.Abs(p.x - center.x), Mathf.Abs(p.y - center.y)) - (half - Vector2.one * radius);
                return Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
            }

            public void Dispose()
            {
                if (_rt != null) _rt.Release();
                if (_root != null) Object.DestroyImmediate(_root);
            }
        }
    }
}
