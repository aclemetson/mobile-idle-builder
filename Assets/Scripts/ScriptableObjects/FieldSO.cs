using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// One possible drop from a field when the player manually taps it.
    /// Weights are relative — e.g. two entries both at 1.0 means 50/50.
    /// </summary>
    [Serializable]
    public class FieldDropEntry
    {
        public ItemSO item;
        [Min(0f)] public float weight = 1f;
    }

    [CreateAssetMenu(fileName = "New Field", menuName = "MobileIdleBuilder/Field")]
    public class FieldSO : ScriptableObject
    {
        public string id;
        public string displayName;
        [Tooltip("Free-form field-type id (data-driven). Buildings' compatibleFields match on this. See FieldTypes.")]
        public string fieldType = FieldTypes.None;

        [Tooltip("Item produced each time the player taps this field.\n" +
                 "Weights are relative — equal weights = equal probability.\n" +
                 "Single entry = always that item.")]
        public List<FieldDropEntry> drops = new();

        [Tooltip("Seconds the field is on cooldown after a tap before it can be tapped again.\n" +
                 "Shortened by research and the 'Quick Hands' prestige upgrade.")]
        [Min(0.1f)] public float tapCooldownSeconds = 1.5f;

        [Header("Purchase (Build menu)")]
        [Tooltip("Research that lets the player buy this field. Empty = never for sale.")]
        public string purchaseResearchId;
        [Tooltip("Price of the first purchase. 0 = never for sale.")]
        [Min(0)] public long purchaseCostBase;
        [Tooltip("Each purchase multiplies the next price by this.")]
        [Min(1f)] public float purchaseCostMultiplier = 10f;
        [Tooltip("Fields placed per purchase, side by side (2 for quark pairs).")]
        [Min(1)] public int purchaseGroupSize = 1;

        public Color fieldColor;
        public Sprite fieldIcon;
        [TextArea(2, 5)]
        public string codexEntry;

        /// <summary>Flat list of items for building-placement compatibility checks.</summary>
        public List<ItemSO> outputItems
        {
            get
            {
                var list = new List<ItemSO>(drops.Count);
                foreach (var d in drops)
                    if (d?.item != null) list.Add(d.item);
                return list;
            }
        }
    }
}
