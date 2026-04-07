using UnityEngine;

namespace MobileIdleBuilder
{
    [CreateAssetMenu(fileName = "New Persistent Upgrade", menuName = "MobileIdleBuilder/Upgrade/Persistent")]
    public class PersistentUpgradeSO : ScriptableObject
    {
        [Header("Identity")]
        public string id;
        public string displayName;
        [TextArea(1, 3)]
        public string description;
        public Sprite icon;

        [Header("Effect")]
        public UpgradeEffectType effectType;
        public float effectValue;       // amount per level
        public int maxLevel;
        public int[] costPerLevel;      // prestige currency cost per level

        [Header("Unlock")]
        public PersistentUpgradeSO[] prerequisites;

        [Header("Codex")]
        [TextArea(2, 5)]
        public string codexEntry;
    }
}
