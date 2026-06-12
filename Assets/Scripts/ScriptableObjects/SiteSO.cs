using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Overrides the spawn density of one field on a given site.
    /// density_multiplier 0 = field absent on this site; 2 = twice the default density.
    /// </summary>
    [Serializable]
    public class SiteFieldOverride
    {
        public FieldSO field;
        [Min(0f)] public float densityMultiplier = 1f;
    }

    /// <summary>
    /// A build site ("Quantum Domain"). Exactly one site is the active, live-ECS grid at a time;
    /// inactive sites keep producing via the idle-snapshot mechanism. Field overrides shape each
    /// site's field distribution. site_origin uses the default field set (no overrides).
    /// </summary>
    [CreateAssetMenu(fileName = "New Site", menuName = "MobileIdleBuilder/Site")]
    public class SiteSO : ScriptableObject
    {
        public string id;
        public string displayName;

        [Tooltip("Entropy cost to unlock this site. 0 = unlocked from start (origin).")]
        public long unlockCost;

        [Tooltip("Per-field density overrides for this site. Empty = default field set.")]
        public List<SiteFieldOverride> fieldOverrides = new();
    }
}
