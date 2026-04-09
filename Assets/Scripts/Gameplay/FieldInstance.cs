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

        public void Initialize(FieldSO field)
        {
            Field = field;
            gameObject.name = $"Field_{field.displayName}";
        }
    }
}
