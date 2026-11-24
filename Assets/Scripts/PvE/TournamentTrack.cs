using System;
using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>One upgrade of the tournament track: bought with Strings, permanent.</summary>
    public sealed class TrackDef
    {
        public readonly string id, name, description;
        public readonly float  perLevel;
        public readonly int[]  costs;   // Strings per level (index 0 = level 1)

        public TrackDef(string id, string name, string description, float perLevel, int[] costs)
        {
            this.id = id; this.name = name; this.description = description; this.perLevel = perLevel; this.costs = costs;
        }

        public int MaxLevel => costs.Length;
    }

    /// <summary>
    /// The tournament track (#500, part 2): permanent upgrades bought only with Strings (<see cref="SaveData.pveStrings"/>).
    /// Levels live in <see cref="SaveData.pveTrack"/> as "id:level" and survive prestige and Ascension.
    ///  - Star Resonance: every item star is worth +2% more per level (<see cref="ItemStars.BonusPerStar"/>).
    ///  - Lucky Draw: +1 star on each tournament reward per level (<see cref="TournamentRules.ApplyReward"/>).
    ///  - Surge Luck: +5% Entropy Surge crystal chance per level (<see cref="AdRewardCalculator.SurgeCrystalChance"/>).
    ///  - Harmonics: +10% Strings from each tournament reward per level.
    /// </summary>
    public static class TournamentTrack
    {
        public const string StarResonance = "star_resonance";
        public const string LuckyDraw     = "lucky_draw";
        public const string SurgeLuck     = "surge_luck";
        public const string Harmonics     = "harmonics";

        public static readonly TrackDef[] All =
        {
            new TrackDef(StarResonance, "Star Resonance", "Every item star adds +2% more value per level.",
                         0.02f, new[] { 20, 40, 80, 160, 320 }),
            new TrackDef(LuckyDraw, "Lucky Draw", "+1 extra star on every tournament reward per level.",
                         1f, new[] { 50, 150, 400 }),
            new TrackDef(SurgeLuck, "Surge Luck", "The Entropy Surge pays crystals +5% more often per level.",
                         0.05f, new[] { 30, 90, 270 }),
            new TrackDef(Harmonics, "Harmonics", "+10% Strings from every tournament reward per level.",
                         0.10f, new[] { 40, 120, 360 }),
        };

        /// <summary>Raised after a purchase (main thread).</summary>
        public static event Action Changed;

        public static TrackDef Find(string id)
        {
            foreach (var d in All) if (d.id == id) return d;
            return null;
        }

        public static int Level(SaveData save, string id)
        {
            if (save?.pveTrack == null) return 0;
            string prefix = id + ":";
            foreach (var e in save.pveTrack)
                if (e != null && e.StartsWith(prefix, StringComparison.Ordinal)
                    && int.TryParse(e.Substring(prefix.Length), out int lvl))
                    return Math.Max(0, lvl);
            return 0;
        }

        static float Effect(SaveData save, string id)
        {
            var d = Find(id);
            return d == null ? 0f : d.perLevel * Math.Min(Level(save, id), d.MaxLevel);
        }

        /// <summary>Value added per star on top of the base 20% (Star Resonance).</summary>
        public static float StarBonus(SaveData save) => Effect(save, StarResonance);

        /// <summary>Extra stars on each tournament reward (Lucky Draw).</summary>
        public static int ExtraStars(SaveData save) => (int)Math.Round(Effect(save, LuckyDraw));

        /// <summary>Added to the Entropy Surge's crystal chance (Surge Luck).</summary>
        public static float SurgeCrystalBonus(SaveData save) => Effect(save, SurgeLuck);

        /// <summary>Strings from a reward after Harmonics (+10% per level, rounded).</summary>
        public static int StringsAfterHarmonics(SaveData save, int strings) =>
            strings <= 0 ? strings : (int)Math.Round(strings * (1d + Math.Round(Effect(save, Harmonics), 6)));

        /// <summary>Strings for the next level, or -1 at max.</summary>
        public static int NextCost(SaveData save, string id)
        {
            var d = Find(id);
            if (d == null) return -1;
            int lvl = Level(save, id);
            return lvl >= d.MaxLevel ? -1 : d.costs[lvl];
        }

        public static bool CanBuy(SaveData save, string id)
        {
            int cost = NextCost(save, id);
            return save != null && cost > 0 && save.pveStrings >= cost;
        }

        /// <summary>Buys the next level: spends the Strings and records it. Returns false when it can't.</summary>
        public static bool TryBuy(SaveData save, string id)
        {
            if (!CanBuy(save, id)) return false;
            int cost = NextCost(save, id);
            int lvl  = Level(save, id) + 1;
            save.pveStrings -= cost;
            save.pveTrack ??= new List<string>();
            save.pveTrack.RemoveAll(e => e != null && e.StartsWith(id + ":", StringComparison.Ordinal));
            save.pveTrack.Add($"{id}:{lvl}");
            ItemStars.Invalidate();   // Star Resonance changes every starred price
            Changed?.Invoke();
            return true;
        }
    }
}
