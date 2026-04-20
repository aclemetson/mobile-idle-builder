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
        public FieldType fieldType;

        [Tooltip("Items produced when the player taps or stands near this field.\n" +
                 "Weights are relative — equal weights = equal probability.\n" +
                 "Single entry = always that item.")]
        public List<FieldDropEntry> drops = new();

        [Tooltip("Items per second automatically collected while the player is within the proximity radius.")]
        [Min(0.1f)] public float collectionRate = 1f;

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
