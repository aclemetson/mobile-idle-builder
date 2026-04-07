using UnityEngine;

namespace MobileIdleBuilder
{
    // Placeholder for building-specific special upgrades (e.g. Nucleon Harvester merge).
    // Concrete special upgrade types will subclass or extend this.
    [CreateAssetMenu(fileName = "New Special Upgrade", menuName = "MobileIdleBuilder/Upgrade/Special")]
    public class SpecialUpgradeSO : ScriptableObject
    {
        public string id;
        public string displayName;
        [TextArea(1, 3)]
        public string description;
        public int costBaseCurrency;
        public int costPrestigeCurrency;
    }
}
