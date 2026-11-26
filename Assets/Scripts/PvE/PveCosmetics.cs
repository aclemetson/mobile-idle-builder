using System;

// Shared by the Unity client and the PvE Cloud Code module (CloudCode/PveModule links this file), so the server
// accepts only these ids for the leaderboard look. Plain C# 9 only: no UnityEngine.
namespace MobileIdleBuilder
{
    public enum CosmeticKind { NameColor, Title, BeltTheme }

    /// <summary>One cosmetic bought with Strings (#500, part 3).</summary>
    public sealed class CosmeticDef
    {
        public readonly string       id;
        public readonly CosmeticKind kind;
        public readonly string       label;
        public readonly int          cost;     // Strings
        public readonly string       value;    // NameColor / BeltTheme: "#RRGGBB" (belt: line colour); Title: its text
        public readonly string       value2;   // BeltTheme: glow colour

        public CosmeticDef(string id, CosmeticKind kind, string label, int cost, string value, string value2 = null)
        {
            this.id = id; this.kind = kind; this.label = label; this.cost = cost; this.value = value; this.value2 = value2;
        }
    }

    /// <summary>
    /// The cosmetics catalogue (#500): leaderboard name colours and titles (other players see them), and belt
    /// themes (only on your own grid). Ids are stored in the save and on the server; never rename one.
    /// </summary>
    public static class PveCosmetics
    {
        public static readonly CosmeticDef[] All =
        {
            new CosmeticDef("color_cyan",   CosmeticKind.NameColor, "Cyan",   25, "#00E5FF"),
            new CosmeticDef("color_violet", CosmeticKind.NameColor, "Violet", 25, "#B388FF"),
            new CosmeticDef("color_gold",   CosmeticKind.NameColor, "Gold",   25, "#FFD54F"),
            new CosmeticDef("color_rose",   CosmeticKind.NameColor, "Rose",   25, "#FF6E9C"),
            new CosmeticDef("color_lime",   CosmeticKind.NameColor, "Lime",   25, "#B2FF59"),
            new CosmeticDef("color_ember",  CosmeticKind.NameColor, "Ember",  25, "#FF8A3D"),

            new CosmeticDef("title_string_theorist",    CosmeticKind.Title, "String Theorist",     40, "String Theorist"),
            new CosmeticDef("title_entropy_engineer",   CosmeticKind.Title, "Entropy Engineer",    40, "Entropy Engineer"),
            new CosmeticDef("title_maxwells_favourite", CosmeticKind.Title, "Maxwell's Favourite", 40, "Maxwell's Favourite"),
            new CosmeticDef("title_quantum_tinkerer",   CosmeticKind.Title, "Quantum Tinkerer",    40, "Quantum Tinkerer"),
            new CosmeticDef("title_fusion_fanatic",     CosmeticKind.Title, "Fusion Fanatic",      40, "Fusion Fanatic"),
            new CosmeticDef("title_idle_physicist",     CosmeticKind.Title, "Idle Physicist",      40, "Idle Physicist"),

            new CosmeticDef("belt_aurora", CosmeticKind.BeltTheme, "Aurora", 100, "#B9FFE4", "#2EE6A8"),
            new CosmeticDef("belt_plasma", CosmeticKind.BeltTheme, "Plasma", 100, "#FFC2F4", "#E040FB"),
            new CosmeticDef("belt_ember",  CosmeticKind.BeltTheme, "Ember",  100, "#FFE0B2", "#FF6D00"),
        };

        public static CosmeticDef Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var d in All) if (d.id == id) return d;
            return null;
        }

        /// <summary>True for "" / null (none) or a known id of <paramref name="kind"/>.</summary>
        public static bool IsValid(string id, CosmeticKind kind) =>
            string.IsNullOrEmpty(id) || (Find(id)?.kind == kind);

        /// <summary>The title's text, or "" for none / unknown.</summary>
        public static string TitleText(string id)
        {
            var d = Find(id);
            return d != null && d.kind == CosmeticKind.Title ? d.value : "";
        }

        /// <summary>The name colour's hex, or null for none / unknown.</summary>
        public static string ColorHex(string id)
        {
            var d = Find(id);
            return d != null && d.kind == CosmeticKind.NameColor ? d.value : null;
        }
    }
}
