using UnityEngine;

namespace MobileIdleBuilder
{
    [CreateAssetMenu(fileName = "New Item", menuName = "MobileIdleBuilder/Item")]
    public class ItemSO : ScriptableObject
    {
        public int itemId;
        public string itemName;
        public int tierLevel;
        public Sprite icon;
        [TextArea(2, 5)]
        public string codexDescription;
    }
}
