using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using MobileIdleBuilder;

namespace MobileIdleBuilder.Editor
{
    /// <summary>
    /// Reads Assets/Data/achievements.json and generates or updates AchievementSO assets
    /// inside Assets/Data/achievements/. Then auto-wires all generated assets into the
    /// AchievementDatabase asset at Assets/Data/AchievementDatabase.asset.
    ///
    /// Run via: MobileIdleBuilder → Import Achievements
    /// Safe to re-run — existing assets are updated in place by ID.
    /// </summary>
    public static class AchievementImporter
    {
        private const string JsonPath       = "Assets/Data/achievements.json";
        private const string OutputDir      = "Assets/Data/achievements";
        private const string DatabaseAsset  = "Assets/Data/AchievementDatabase.asset";

        [MenuItem("MobileIdleBuilder/Import Achievements")]
        public static void Import()
        {
            var textAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(JsonPath);
            if (textAsset == null)
            {
                Debug.LogError($"[AchievementImporter] Could not find {JsonPath}");
                return;
            }

            AchievementListJson list;
            try
            {
                list = JsonUtility.FromJson<AchievementListJson>(textAsset.text);
            }
            catch (Exception e)
            {
                Debug.LogError($"[AchievementImporter] JSON parse failed: {e.Message}");
                return;
            }

            if (list?.achievements == null || list.achievements.Length == 0)
            {
                Debug.LogError("[AchievementImporter] No achievements found in JSON.");
                return;
            }

            if (!AssetDatabase.IsValidFolder(OutputDir))
                AssetDatabase.CreateFolder("Assets/Data", "achievements");

            var generated = new List<AchievementSO>();

            foreach (var def in list.achievements)
            {
                if (string.IsNullOrEmpty(def.id))
                {
                    Debug.LogWarning("[AchievementImporter] Skipping entry with empty id.");
                    continue;
                }

                string assetPath = $"{OutputDir}/{def.id}.asset";
                var so = AssetDatabase.LoadAssetAtPath<AchievementSO>(assetPath);

                if (so == null)
                {
                    so = ScriptableObject.CreateInstance<AchievementSO>();
                    AssetDatabase.CreateAsset(so, assetPath);
                }

                so.id                   = def.id;
                so.displayName          = def.displayName;
                so.description          = def.description;
                so.isHidden             = def.isHidden;
                so.category             = ParseEnum<AchievementCategory>(def.category);
                so.triggerType          = ParseEnum<AchievementTrigger>(def.triggerType);
                so.triggerTargetId      = def.triggerTargetId ?? "";
                so.triggerQuantity      = def.triggerQuantity;
                so.paidCurrencyReward   = def.paidCurrencyReward;
                so.prestigeCurrencyReward = def.prestigeCurrencyReward;

                EditorUtility.SetDirty(so);
                generated.Add(so);
            }

            // Create or update the AchievementDatabase asset
            var db = AssetDatabase.LoadAssetAtPath<AchievementDatabase>(DatabaseAsset);
            if (db == null)
            {
                db = ScriptableObject.CreateInstance<AchievementDatabase>();
                AssetDatabase.CreateAsset(db, DatabaseAsset);
            }

            // Inject the achievements array via reflection (matching the existing pattern)
            var dbField = typeof(AchievementDatabase)
                .GetField("achievements", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (dbField != null)
                dbField.SetValue(db, generated.ToArray());

            EditorUtility.SetDirty(db);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[AchievementImporter] Imported {generated.Count} achievements → {DatabaseAsset}");
        }

        static T ParseEnum<T>(string value) where T : struct, Enum
        {
            if (Enum.TryParse<T>(value, ignoreCase: true, out var result))
                return result;
            Debug.LogWarning($"[AchievementImporter] Could not parse '{value}' as {typeof(T).Name}, defaulting to 0.");
            return default;
        }

        // ── JSON model ────────────────────────────────────────────────────────

        [Serializable]
        class AchievementListJson
        {
            public AchievementJson[] achievements;
        }

        [Serializable]
        class AchievementJson
        {
            public string id;
            public string displayName;
            public string description;
            public string category;
            public string triggerType;
            public string triggerTargetId;
            public int    triggerQuantity;
            public int    paidCurrencyReward;
            public int    prestigeCurrencyReward;
            public bool   isHidden;
        }
    }
}
