using UnityEngine;

namespace MobileIdleBuilder
{
    [CreateAssetMenu(fileName = "New Field", menuName = "MobileIdleBuilder/Field")]
    public class FieldSO : ScriptableObject
    {
        public string id;
        public string displayName;      // e.g. "Positive Quark Field"
        public FieldType fieldType;
        public ItemSO outputItem;       // what this field produces when harvested
        public Color fieldColor;        // grid tile tint
        public Sprite fieldIcon;
        [TextArea(2, 5)]
        public string codexEntry;
    }
}
