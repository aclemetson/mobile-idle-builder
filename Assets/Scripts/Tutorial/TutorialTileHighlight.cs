using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// A flat amber quad that pulses in scale, placed just above a grid tile to draw
    /// the player's attention during a tutorial highlight. Created procedurally by
    /// TutorialHighlighter; destroyed when ClearHighlight() is called.
    /// </summary>
    public class TutorialTileHighlight : MonoBehaviour
    {
        private static readonly Color HighlightColor = new Color(1f, 0.78f, 0.15f, 0.55f);

        private float _baseScaleX;
        private float _baseScaleZ;

        /// <summary>
        /// Spawns a pulsing amber quad centered over the given footprint.
        /// <paramref name="footprintCenter"/> is the XZ centre of the full footprint (Y = 0).
        /// <paramref name="w"/> and <paramref name="h"/> are the footprint dimensions in cells.
        /// </summary>
        public static TutorialTileHighlight Spawn(Vector3 footprintCenter, float cellSize, int w = 1, int h = 1)
        {
            var go  = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "TutorialHighlight";
            Destroy(go.GetComponent<MeshCollider>());

            float scaleX            = cellSize * w * 0.92f;
            float scaleZ            = cellSize * h * 0.92f;
            go.transform.position   = footprintCenter + Vector3.up * 0.02f;
            go.transform.rotation   = Quaternion.Euler(90f, 0f, 0f);
            go.transform.localScale = new Vector3(scaleX, scaleZ, 1f);

            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows    = false;

            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", HighlightColor);
            mr.SetPropertyBlock(mpb);

            var comp         = go.AddComponent<TutorialTileHighlight>();
            comp._baseScaleX = scaleX;
            comp._baseScaleZ = scaleZ;
            return comp;
        }

        void Update()
        {
            // Pulse scale between ~88 % and ~108 % of the base size
            float pulse = 0.98f + Mathf.Sin(Time.time * 4f) * 0.1f;
            transform.localScale = new Vector3(_baseScaleX * pulse, _baseScaleZ * pulse, 1f);
        }
    }
}
