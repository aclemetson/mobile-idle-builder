using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Procedural output-direction arrow built from Unity primitives.
    /// Always points in local +Z by default; call SetDirection to rotate it.
    ///
    /// Usage:
    ///   var arrow = someGO.AddComponent&lt;OutputArrow&gt;();
    ///   arrow.Initialize(OutputDirection.North, OutputArrow.GhostColor);
    /// </summary>
    public class OutputArrow : MonoBehaviour
    {
        public static readonly Color GhostColor   = Color.white;
        public static readonly Color PlacedColor  = new Color(1f,  0.84f, 0f,  1f); // gold  — output
        public static readonly Color InputColor   = new Color(0f,  0.6f,  1f,  1f); // blue  — input
        public static readonly Color GhostInput   = new Color(0.4f, 0.8f, 1f,  0.8f);
        public static readonly Color GhostOutput  = new Color(1f,  1f,   0.4f, 0.8f);

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public void Initialize(OutputDirection dir, Color color)
        {
            Build(color);
            SetDirection(dir);
        }

        /// <summary>Rotates the arrow to face the given direction (local +Z = North).</summary>
        public void SetDirection(OutputDirection dir)
        {
            float yRot = dir switch
            {
                OutputDirection.North => 0f,
                OutputDirection.East  => 90f,
                OutputDirection.South => 180f,
                OutputDirection.West  => 270f,
                _                    => 0f,
            };
            transform.localRotation = Quaternion.Euler(0f, yRot, 0f);
        }

        // ---- Private ----

        private void Build(Color color)
        {
            // 10% smaller than original
            SpawnPart("Shaft",
                pos:   new Vector3(0f, 0f, 0.135f),
                scale: new Vector3(0.072f, 0.054f, 0.342f),
                rot:   Quaternion.identity,
                color: color);

            SpawnPart("Head",
                pos:   new Vector3(0f, 0f, 0.45f),
                scale: new Vector3(0.198f, 0.054f, 0.198f),
                rot:   Quaternion.Euler(0f, 45f, 0f),
                color: color);
        }

        private void SpawnPart(string partName, Vector3 pos, Vector3 scale, Quaternion rot, Color color)
        {
            var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            part.name = partName;
            Destroy(part.GetComponent<Collider>());

            part.transform.SetParent(transform, worldPositionStays: false);
            part.transform.localPosition = pos;
            part.transform.localScale    = scale;
            part.transform.localRotation = rot;

            var mr = part.GetComponent<MeshRenderer>();
            if (RenderingMaterials.Instance?.Opaque != null)
                mr.sharedMaterial = RenderingMaterials.Instance.Opaque;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows    = false;

            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(BaseColorId, color);
            mr.SetPropertyBlock(mpb);
        }
    }
}
