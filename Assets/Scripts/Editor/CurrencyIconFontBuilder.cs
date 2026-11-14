using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace MobileIdleBuilder.Editor
{
    /// <summary>
    /// Bakes Assets/Fonts/CurrencyIcons.ttf (from scripts/currency_icons/build_font.py) into a static font
    /// asset and makes it the HUD's fallback font, so the currency symbols come from the game on every
    /// device instead of whatever fonts the OS has (#165). Static so play sessions don't dirty the asset.
    /// </summary>
    public static class CurrencyIconFontBuilder
    {
        public const string Symbols         = "◈✦♦⚡";   // entropy, prestige, crystal, power
        public const string FontPath        = "Assets/Fonts/CurrencyIcons.ttf";
        public const string FontAssetPath   = "Assets/Fonts/CurrencyIcons SDF.asset";
        public const string TextSettingsPath = "Assets/Data/settings/gamePanelTextSettings.asset";
        const string PanelSettingsPath      = "Assets/Data/settings/gamePanelSettings.asset";

        [MenuItem("MobileIdleBuilder/Build Currency Icon Font")]
        public static void Build()
        {
            var importer = (TrueTypeFontImporter)AssetImporter.GetAtPath(FontPath);
            if (importer == null)
            {
                Debug.LogError($"[CurrencyIcons] {FontPath} not found. Run scripts/currency_icons/build_font.py first.");
                return;
            }
            importer.includeFontData = true;
            importer.SaveAndReimport();

            var font = AssetDatabase.LoadAssetAtPath<Font>(FontPath);
            if (AssetDatabase.LoadAssetAtPath<FontAsset>(FontAssetPath) != null)
                AssetDatabase.DeleteAsset(FontAssetPath);

            // A high sampling size with wide padding keeps the fine gaps in the gem and the Demon's chamber
            // crisp when the distance field is drawn at 13px.
            var fontAsset = FontAsset.CreateFontAsset(font, 96, 12, GlyphRenderMode.SDFAA, 512, 512,
                                                      AtlasPopulationMode.Dynamic, false);
            fontAsset.name = "CurrencyIcons SDF";
            if (!fontAsset.TryAddCharacters(Symbols, out string missing) || !string.IsNullOrEmpty(missing))
            {
                Debug.LogError($"[CurrencyIcons] The font is missing '{missing}'.");
                return;
            }
            fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;

            AssetDatabase.CreateAsset(fontAsset, FontAssetPath);
            foreach (var atlas in fontAsset.atlasTextures)
            {
                atlas.name = "CurrencyIcons Atlas";
                AssetDatabase.AddObjectToAsset(atlas, fontAsset);
            }
            fontAsset.material.name = "CurrencyIcons Material";
            AssetDatabase.AddObjectToAsset(fontAsset.material, fontAsset);
            EditorUtility.SetDirty(fontAsset);

            var settings = AssetDatabase.LoadAssetAtPath<PanelTextSettings>(TextSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelTextSettings>();
                AssetDatabase.CreateAsset(settings, TextSettingsPath);
            }
            settings.fallbackFontAssets = new System.Collections.Generic.List<FontAsset> { fontAsset };
            EditorUtility.SetDirty(settings);

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            panel.textSettings = settings;
            panel.scale = 1f;   // UIScaler sets the real scale at runtime; don't save a leftover from a play session
            EditorUtility.SetDirty(panel);

            AssetDatabase.SaveAssets();
            Debug.Log($"[CurrencyIcons] Built {FontAssetPath} and wired it into {PanelSettingsPath}.");
        }
    }
}
