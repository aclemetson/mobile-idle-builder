using UnityEngine;

namespace MobileIdleBuilder
{
    [CreateAssetMenu(fileName = "New Achievement", menuName = "MobileIdleBuilder/Achievement")]
    public class AchievementSO : ScriptableObject
    {
        [Header("Identity")]
        public string id;
        public string displayName;
        [TextArea(1, 3)]
        public string description;
        public Sprite icon;
        public bool isHidden;               // hidden until unlocked

        [Header("Trigger")]
        public AchievementTrigger triggerType;
        public string triggerTargetId;      // item/building/research id if applicable
        public int triggerQuantity;         // e.g. craft 100 of item

        [Header("Rewards")]
        public CosmeticSO[] rewards;        // cosmetics unlocked on completion

        [Header("Platform")]
        public string platformAchievementId; // Apple Game Center / Google Play id
    }
}
