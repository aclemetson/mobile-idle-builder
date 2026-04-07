using UnityEngine;

namespace MobileIdleBuilder
{
    [CreateAssetMenu(fileName = "New Cosmetic", menuName = "MobileIdleBuilder/Cosmetic")]
    public class CosmeticSO : ScriptableObject
    {
        [Header("Identity")]
        public string id;
        public string displayName;
        [TextArea(1, 3)]
        public string description;
        public Sprite previewImage;

        [Header("Type")]
        public CosmeticType type;           // BuildingSkin, ParticleEffect, GridTheme, HUDAccent, MusicTrack, CodexCover, ProfileBadge
        public Object cosmeticAsset;        // material, prefab, audio clip, etc.

        [Header("Unlock")]
        public UnlockMethod unlockMethod;
        public AchievementSO unlockAchievement;  // if UnlockMethod = Achievement
        public int premiumCurrencyCost;          // if UnlockMethod = PremiumCurrency
        public string iapProductId;              // if UnlockMethod = IAP

        [Header("Music")]
        public bool isScreenMusic;          // true if this is a music track cosmetic
        public ScreenTarget musicScreenTarget;   // which screen this music plays on by default
    }
}
