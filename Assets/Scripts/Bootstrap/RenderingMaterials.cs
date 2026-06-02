using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Scene singleton that vends URP-compatible materials to any script that spawns
    /// procedural primitives (CreatePrimitive) at runtime.
    ///
    /// Wire once in the scene; every new building, conveyor, or FX script just calls
    /// RenderingMaterials.Instance.Opaque (or .Transparent) instead of relying on
    /// Unity's Built-in default material, which renders magenta on Android/URP.
    ///
    /// Inspector fields:
    ///   Opaque      — URP Unlit Opaque      (cubes, spheres, solid arrows)
    ///   Transparent — URP Unlit Transparent  (quads, ghost overlays)
    /// </summary>
    public class RenderingMaterials : SingletonMonoBehaviour<RenderingMaterials>
    {
        [SerializeField] public Material Opaque;
        [SerializeField] public Material Transparent;
    }
}
