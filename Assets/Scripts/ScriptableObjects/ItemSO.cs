using UnityEngine;

namespace MobileIdleBuilder
{
    [CreateAssetMenu(fileName = "New Item", menuName = "MobileIdleBuilder/Item")]
    public class ItemSO : ScriptableObject
    {
        [Header("Identity")]
        public string id;               // matches recipes.json id
        public string displayName;
        public string symbol;           // chemical symbol or notation
        public Sprite icon;

        [Header("ECS Reference")]
        public int itemId;              // integer ID used in ECS components

        [Header("Classification")]
        public int tier;                // 1 = subatomic, 2 = atomic, etc.
        public ItemCategory category;
        public TierSO tierData;

        [Header("Scientific Data")]
        public int atomicNumber;        // 0 if not an element
        public int atomicMass;          // 0 if not an element
        public string charge;           // e.g. "+1", "-1", "0", "+2/3"
        public bool isRadioactive;
        public DecayType decayType;
        public string halfLifeNote;     // display only, not simulated
        public bool isFissile;

        [Header("Harvesting")]
        public bool isHarvested;        // true = raw resource, no recipe
        public string fieldType = FieldTypes.None;   // free-form field-type id (see FieldTypes)

        [Header("Secondary Particle")]
        public bool isSecondaryParticle;
        public ParticleUse particleUses;

        [Header("Codex")]
        [TextArea(2, 5)]
        public string codexEntry;
        public bool codexUnlocked;      // runtime — false by default, set on first craft

        [Header("Economy")]
        public float baseSellValue;
        public float isotopeSellMultiplier;
    }
}
