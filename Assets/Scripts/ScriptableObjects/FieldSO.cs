using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    [CreateAssetMenu(fileName = "New Field", menuName = "MobileIdleBuilder/Field")]
    public class FieldSO : ScriptableObject
    {
        public string id;
        public string displayName;
        public FieldType fieldType;
        /// <summary>
        /// Items that can be harvested from this field.
        /// Single-item fields (e.g. Electron) auto-select on placement.
        /// Multi-item fields (e.g. Quark) prompt the player to choose.
        /// </summary>
        public List<ItemSO> outputItems;
        public Color fieldColor;
        public Sprite fieldIcon;
        [TextArea(2, 5)]
        public string codexEntry;
    }
}
