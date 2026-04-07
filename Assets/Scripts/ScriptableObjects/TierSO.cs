using UnityEngine;

namespace MobileIdleBuilder
{
    [CreateAssetMenu(fileName = "New Tier", menuName = "MobileIdleBuilder/Tier")]
    public class TierSO : ScriptableObject
    {
        public int tierNumber;
        public string displayName;      // e.g. "Subatomic", "Atomic"
        public string description;
        public Color tierColor;
        public Sprite tierIcon;
        public ResearchSO[] unlockResearch;  // research required to access this tier
        public ItemSO[] starterItems;        // items visible (but locked) at tier start
    }
}
