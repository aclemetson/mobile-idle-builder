using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Runtime balance-override layer. The build always ships the full baked ScriptableObjects as the
    /// offline-safe baseline; this overlays a curated set of <b>scalar</b> values fetched from Remote
    /// Config (<c>gamedata.overrides</c>, delivered via <see cref="FeatureFlagService"/>'s cache so the
    /// precedence is remote &gt; cache &gt; baked and it works offline).
    ///
    /// Values are applied in-place onto the shared loaded SO instances at boot — by the owners that load
    /// them (<see cref="ItemDatabase"/>, <see cref="ResearchService"/>, <see cref="GameBootstrap"/>) — so
    /// every consumer that reads those SOs sees the override with no per-consumer change. We patch only an
    /// allow-listed set of scalar fields via explicit setters (IL2CPP-safe, no reflection); ids, refs, and
    /// arrays are never touched, so the importer's cross-reference integrity is preserved.
    ///
    /// Fresh remote values are fetched late (after UGS init) and cached, so a change takes effect on the
    /// <b>next cold start</b> — the same next-launch model the <c>gamedata.updatedUtc</c> notice assumes.
    /// Empty/blank/garbage blob =&gt; no overrides; never throws.
    /// </summary>
    public static class GameDataOverrides
    {
        // ── JSON DTOs (JsonUtility-friendly: arrays of entries, never id-keyed maps) ────────────
        [Serializable] public class ConfigEntry { public string field; public float value; }
        [Serializable] public class EntityEntry { public string id; public string field; public float value; }

        [Serializable]
        public class Blob
        {
            public ConfigEntry[] config;
            public EntityEntry[] items;
            public EntityEntry[] research;
        }

        // Memoized parse keyed on the raw string so we parse once per distinct blob.
        static string _rawCached;
        static Blob   _blobCached;

        /// <summary>Parses a blob string. Returns null on blank/garbage — never throws.</summary>
        public static Blob Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonUtility.FromJson<Blob>(json); }
            catch (Exception ex)
            {
                GameLogger.Warning($"[GameDataOverrides] Blob parse failed — ignoring overrides. Reason: {ex.Message}");
                return null;
            }
        }

        /// <summary>The active blob, sourced from the feature-flag cache (remote &gt; cache &gt; default).</summary>
        static Blob Current()
        {
            string raw = FeatureFlags.GameDataOverridesJson;
            if (!string.Equals(raw, _rawCached, StringComparison.Ordinal))
            {
                _rawCached  = raw;
                _blobCached = Parse(raw);
            }
            return _blobCached;
        }

        // ── Public apply entry points (called by the SO owners at boot) ─────────────────────────

        public static int ApplyConfig(GameConfigSO cfg) => ApplyConfig(cfg, Current()?.config);

        public static int ApplyItems(IReadOnlyList<ItemSO> items) => ApplyItems(items, Current()?.items);

        public static int ApplyResearch(IReadOnlyList<ResearchSO> research) => ApplyResearch(research, Current()?.research);

        // ── Apply overloads with explicit entries (unit-testable without FeatureFlagService) ─────

        public static int ApplyConfig(GameConfigSO cfg, ConfigEntry[] entries)
        {
            if (cfg == null || entries == null) return 0;
            int applied = 0;
            foreach (var e in entries)
                if (e != null && SetConfigField(cfg, e.field, e.value)) applied++;
            if (applied > 0) GameLogger.Info($"[GameDataOverrides] Applied {applied} config override(s).");
            return applied;
        }

        public static int ApplyItems(IReadOnlyList<ItemSO> items, EntityEntry[] entries)
        {
            if (items == null || entries == null) return 0;
            var byId = new Dictionary<string, ItemSO>(items.Count);
            foreach (var it in items)
                if (it != null && !string.IsNullOrEmpty(it.id)) byId[it.id] = it;

            int applied = 0;
            foreach (var e in entries)
                if (e != null && byId.TryGetValue(e.id ?? "", out var item) && SetItemField(item, e.field, e.value)) applied++;
            if (applied > 0) GameLogger.Info($"[GameDataOverrides] Applied {applied} item override(s).");
            return applied;
        }

        public static int ApplyResearch(IReadOnlyList<ResearchSO> research, EntityEntry[] entries)
        {
            if (research == null || entries == null) return 0;
            var byId = new Dictionary<string, ResearchSO>(research.Count);
            foreach (var r in research)
                if (r != null && !string.IsNullOrEmpty(r.id)) byId[r.id] = r;

            int applied = 0;
            foreach (var e in entries)
                if (e != null && byId.TryGetValue(e.id ?? "", out var r) && SetResearchField(r, e.field, e.value)) applied++;
            if (applied > 0) GameLogger.Info($"[GameDataOverrides] Applied {applied} research override(s).");
            return applied;
        }

        // ── Explicit setters double as the allow-list. Unknown field => ignored (returns false). ─

        static bool SetConfigField(GameConfigSO c, string field, float v)
        {
            switch (field)
            {
                case "baseCraftTimeMultiplier":         c.baseCraftTimeMultiplier = v; return true;
                case "manualCraftTimeBase":             c.manualCraftTimeBase = v; return true;
                case "atomicAssemblerEVPerMassUnit":    c.atomicAssemblerEVPerMassUnit = v; return true;
                case "isotopicManipulatorEVPerNeutron": c.isotopicManipulatorEVPerNeutron = v; return true;
                case "netWorthToPrestigeCurrencyRate":  c.netWorthToPrestigeCurrencyRate = v; return true;
                case "prestigeBaseValue":               c.prestigeBaseValue = v; return true;
                case "prestigeWallMultiplier":          c.prestigeWallMultiplier = v; return true;
                case "prestigeCurrencyScale":           c.prestigeCurrencyScale = v; return true;
                case "alphaParticleEVValue":            c.alphaParticleEVValue = v; return true;
                case "betaParticleEVValue":             c.betaParticleEVValue = v; return true;
                case "startingEntropy":                 c.startingEntropy = (long)v; return true;
                case "idleBaseMaxSeconds":              c.idleBaseMaxSeconds = v; return true;
                case "idleAbsoluteMaxSeconds":          c.idleAbsoluteMaxSeconds = v; return true;
                case "idleBaseCollectionRate":          c.idleBaseCollectionRate = v; return true;
                case "buildingPurchaseMultiplierT1":    c.buildingPurchaseMultiplierT1 = v; return true;
                case "buildingPurchaseMultiplierT2":    c.buildingPurchaseMultiplierT2 = v; return true;
                case "buildingPurchaseMultiplierT3Plus":c.buildingPurchaseMultiplierT3Plus = v; return true;
                default:
                    GameLogger.Warning($"[GameDataOverrides] Unknown config field '{field}' — ignored.");
                    return false;
            }
        }

        static bool SetItemField(ItemSO it, string field, float v)
        {
            switch (field)
            {
                case "baseSellValue":         it.baseSellValue = v; return true;
                case "isotopeSellMultiplier": it.isotopeSellMultiplier = v; return true;
                default:
                    GameLogger.Warning($"[GameDataOverrides] Unknown item field '{field}' (id '{it.id}') — ignored.");
                    return false;
            }
        }

        static bool SetResearchField(ResearchSO r, string field, float v)
        {
            switch (field)
            {
                case "costBaseCurrency":     r.costBaseCurrency = Mathf.RoundToInt(v); return true;
                case "costPrestigeCurrency": r.costPrestigeCurrency = Mathf.RoundToInt(v); return true;
                case "durationSeconds":      r.durationSeconds = Mathf.RoundToInt(v); return true;
                case "fieldCooldownMult":    r.fieldCooldownMult = v; return true;
                default:
                    GameLogger.Warning($"[GameDataOverrides] Unknown research field '{field}' (id '{r.id}') — ignored.");
                    return false;
            }
        }
    }
}
