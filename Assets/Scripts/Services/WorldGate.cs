using System.Collections.Generic;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Pure rules for content that belongs to a World (2026-11-19 Chemistry gate):
    /// <list type="bullet">
    /// <item>Research with <see cref="ResearchSO.requiredWorld"/> can't be started until that world is unlocked
    /// (Physics past silicon and the whole Chemistry tree wait for the Chemistry tasks).</item>
    /// <item>A building with <see cref="BuildingSO.world"/> is only offered in the build menu while that world is
    /// active (the Molecular Synthesizer lives in Chemistry). Empty means shared: belts, splitters, the Demon.</item>
    /// </list>
    /// world_physics (the starting world) is always open.
    /// </summary>
    public static class WorldGate
    {
        public const string Physics   = "world_physics";
        public const string Chemistry = "world_chemistry";

        /// <summary>True if content that needs <paramref name="worldId"/> is open on this save.</summary>
        public static bool IsOpen(string worldId, IReadOnlyCollection<string> unlockedWorlds)
        {
            if (string.IsNullOrEmpty(worldId) || worldId == Physics) return true;
            if (unlockedWorlds == null) return false;
            foreach (var id in unlockedWorlds) if (id == worldId) return true;
            return false;
        }

        /// <summary>True if <paramref name="research"/> may be started on this save as far as worlds go.</summary>
        public static bool ResearchOpen(ResearchSO research, IReadOnlyCollection<string> unlockedWorlds) =>
            research != null && IsOpen(research.requiredWorld, unlockedWorlds);

        /// <summary>True if <paramref name="building"/> is placeable while <paramref name="activeWorldId"/> is active.</summary>
        public static bool BuildingInWorld(BuildingSO building, string activeWorldId)
        {
            if (building == null) return false;
            if (string.IsNullOrEmpty(building.world)) return true;
            return building.world == (string.IsNullOrEmpty(activeWorldId) ? Physics : activeWorldId);
        }

        /// <summary>Player-facing name of a world id for "Opens with …" labels, e.g. world_chemistry → Chemistry.</summary>
        public static string DisplayName(string worldId)
        {
            if (string.IsNullOrEmpty(worldId)) return "";
            string s = worldId.StartsWith("world_") ? worldId.Substring(6) : worldId;
            return s.Length == 0 ? worldId : char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }
}
