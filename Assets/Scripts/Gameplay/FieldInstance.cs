using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Attached to each field GameObject spawned by FieldGenerator.
    /// Provides identity and world-position access for proximity detection.
    /// </summary>
    public class FieldInstance : MonoBehaviour
    {
        public FieldSO Field { get; private set; }

        private FieldEffect _effect;
        private FieldCooldownIndicator _cooldown;

        /// <summary>
        /// This field's tap-cooldown + radial wheel. Resolved lazily because the indicator is added
        /// to the GameObject after Initialize() runs.
        /// </summary>
        public FieldCooldownIndicator Cooldown => _cooldown ??= GetComponent<FieldCooldownIndicator>();

        public void Initialize(FieldSO field)
        {
            Field = field;
            gameObject.name = $"Field_{field.displayName}";
        }

        /// <summary>
        /// Dims or restores this field's visual based on whether it is accessible in the current
        /// tutorial step. FieldEffect is resolved lazily because it is added to the GameObject
        /// after FieldInstance.Initialize() is called by FieldGenerator.
        /// </summary>
        public void SetLocked(bool locked)
        {
            _effect ??= GetComponent<FieldEffect>();
            _effect?.SetLocked(locked);
        }
    }
}
