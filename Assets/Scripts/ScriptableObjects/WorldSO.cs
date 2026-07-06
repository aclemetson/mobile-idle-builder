using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Per-world map theming (palette / tint). A placeholder in Phase 1 — the schema is present and
    /// round-trips through the importer, but nothing consumes it yet. Phase 5 wires it into GridRenderer
    /// so each World's map looks distinct. Defaults reproduce the current (Physics) look.
    /// </summary>
    [Serializable]
    public class WorldTheme
    {
        public Color backgroundColor = new(0.06f, 0.06f, 0.09f, 1f);
        public Color tileColor       = new(0.12f, 0.12f, 0.16f, 1f);
        public Color fieldTint       = Color.white;
    }

    /// <summary>
    /// A World ("track") — the layer ABOVE the Site system. Physics / Chemistry / Biology are three Worlds;
    /// each owns a set of member Sites (<see cref="siteIds"/>), its own research branch, and its own map theme.
    /// Worlds are a logical grouping over the FLAT global site list: <see cref="siteIds"/> names which sites
    /// belong to this world, while SaveData.currentRun.grids[]/siteSnapshots[] stay flat and global.
    /// <para>
    /// Unlock is pure economic: pay <see cref="unlockCost"/> entropy and hold every id in
    /// <see cref="prereqUnlockIds"/> (research/item ids unlocked in the prior world). world_physics has
    /// unlockCost 0 and is implicitly unlocked (like site_origin). World unlocks survive prestige.
    /// </para>
    /// </summary>
    [CreateAssetMenu(fileName = "New World", menuName = "MobileIdleBuilder/World")]
    public class WorldSO : ScriptableObject
    {
        public string id;
        public string displayName;

        [Tooltip("Entropy cost to unlock this world. 0 = unlocked from start (Physics).")]
        public long unlockCost;

        [Tooltip("Research/item ids that must be unlocked before this world can be unlocked (pure economic gate).")]
        public List<string> prereqUnlockIds = new();

        [Tooltip("Ids of the Sites that belong to this world, in order. May be empty until sites are authored.")]
        public List<string> siteIds = new();

        [Tooltip("Map palette/tint for this world. Consumed by GridRenderer starting in Phase 5.")]
        public WorldTheme theme = new();
    }
}
