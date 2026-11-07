using UnityEngine;

namespace MobileIdleBuilder
{
    [CreateAssetMenu(fileName = "New Research", menuName = "MobileIdleBuilder/Research")]
    public class ResearchSO : ScriptableObject
    {
        [Header("Identity")]
        public string id;
        public string displayName;
        public string description;
        public Sprite icon;

        [Header("Tree Position")]
        public ResearchBranch branch;
        public ResearchSO[] prerequisites;  // must be completed before this unlocks
        public int depthInTree;             // used for UI layout
        public string block;                // research block (game phase) id, ResearchDatabaseSO.blocks
        public int treeX = -1;              // research tree position from the editor (docs/research-tree.html); -1 = auto
        public int treeY = -1;

        [Header("Cost")]
        public long costBaseCurrency;       // long: late research costs pass 2^31 under the derived economy
        public int costPrestigeCurrency;    // 0 for most research

        [Header("Timer")]
        public int durationSeconds;         // research time in seconds; 0 = instant unlock

        [Header("Modifiers")]
        [Tooltip("Multiplier applied to the field tap cooldown while this research is unlocked.\n" +
                 "1 = no effect; 0.85 = 15% shorter. Stacks multiplicatively across unlocked research.")]
        public float fieldCooldownMult = 1f;

        [Tooltip("Typed effects (e.g. ConveyorSpeed 0.10 = +10% belt speed). Same-type values add up across unlocked research.")]
        public ResearchEffect[] effects;

        [Header("Unlocks")]
        public ItemSO[] unlocksItems;
        public RecipeSO[] unlocksRecipes;
        public BuildingSO[] unlocksBuildings;
        public bool unlocksGridExpansion;
        public ResearchSO[] unlocksResearch;

        [Header("Prestige Behavior")]
        public bool resetsOnPrestige;       // always true currently
        public float prestigeMemoryDiscount; // % cost reduction on repeat runs

        [Header("Codex")]
        [TextArea(2, 5)]
        public string codexEntry;
    }
}
