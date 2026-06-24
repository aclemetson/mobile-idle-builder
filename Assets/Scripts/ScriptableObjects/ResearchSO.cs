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

        [Header("Cost")]
        public int costBaseCurrency;
        public int costPrestigeCurrency;    // 0 for most research

        [Header("Timer")]
        public int durationSeconds;         // research time in seconds; 0 = instant unlock

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
