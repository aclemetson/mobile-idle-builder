using System.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Spawns a small procedural dot-burst at the building face where a conveyor belt
    /// deposits an item into the input buffer.
    ///
    /// Auto-instantiates itself after scene load — no manual scene setup required.
    /// ConveyorSystem calls Instance.Trigger() on the main thread.
    /// </summary>
    public class BuildingInputFX : MonoBehaviour
    {
        public static BuildingInputFX Instance { get; private set; }

        private GridRenderer _gridRenderer;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        // ----------------------------------------------------------------
        // Auto-creation — no manual scene wiring needed
        // ----------------------------------------------------------------

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoInstantiate()
        {
            if (Instance != null) return;
            var go = new GameObject("[BuildingInputFX]");
            go.AddComponent<BuildingInputFX>();
        }

        void Awake() { Instance = this; }

        void Start()
        {
            _gridRenderer = FindAnyObjectByType<GridRenderer>();
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        // ----------------------------------------------------------------
        // Public API
        // ----------------------------------------------------------------

        /// <summary>
        /// Fires a burst at the face of the last segment (the building's entry point).
        /// <paramref name="exitDir"/> is the direction the item was travelling when it
        /// left the segment — used to position the burst at the correct face.
        /// </summary>
        public void Trigger(int2 segCell, int exitDir, int itemID)
        {
            if (_gridRenderer == null) return;

            float   cs        = _gridRenderer.CellSize;
            Vector3 segCenter = new Vector3(segCell.x * cs, 0.4f, segCell.y * cs);
            // Burst at the exit face of the segment (= building's input face)
            Vector3 burstPos  = segCenter + DirToVector3(exitDir) * (cs * 0.45f);
            Color   color     = ItemColor(itemID);

            // 5 dots fanning outward in the XZ plane from the impact point
            const int count = 5;
            for (int i = 0; i < count; i++)
            {
                float   angle   = (i / (float)count) * 360f;
                Vector3 dir     = Quaternion.Euler(0f, angle, 0f) * Vector3.forward;
                Vector3 start   = burstPos + dir * (cs * 0.05f);
                Vector3 vel     = dir * cs * 0.35f + Vector3.up * cs * 0.2f;
                SpawnDot(start, vel, color);
            }
        }

        // ----------------------------------------------------------------
        // Private helpers
        // ----------------------------------------------------------------

        private void SpawnDot(Vector3 worldPos, Vector3 velocity, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "InputDot";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform);
            go.transform.position   = worldPos;
            go.transform.localScale = Vector3.one * 0.10f;

            var mr = go.GetComponent<MeshRenderer>();
            if (RenderingMaterials.Instance?.Opaque != null)
                mr.sharedMaterial = RenderingMaterials.Instance.Opaque;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows    = false;

            var mpb = new MaterialPropertyBlock();
            mpb.SetColor(BaseColorId, color);
            mr.SetPropertyBlock(mpb);

            StartCoroutine(AnimateDot(go, velocity, 0.4f));
        }

        private IEnumerator AnimateDot(GameObject go, Vector3 velocity, float duration)
        {
            float   elapsed    = 0f;
            Vector3 startPos   = go.transform.position;
            float   startScale = 0.10f;

            while (elapsed < duration)
            {
                if (go == null) yield break;
                elapsed += Time.deltaTime;
                float t = elapsed / duration;
                go.transform.position   = startPos + velocity * t;
                go.transform.localScale = Vector3.one * Mathf.Lerp(startScale, 0f, t);
                yield return null;
            }

            if (go != null) Destroy(go);
        }

        private static Vector3 DirToVector3(int dir) =>
            (OutputDirection)dir switch
            {
                OutputDirection.North => Vector3.forward,
                OutputDirection.East  => Vector3.right,
                OutputDirection.South => Vector3.back,
                OutputDirection.West  => Vector3.left,
                _                     => Vector3.zero
            };

        // Golden-ratio hue spread — same formula used by ConveyorVisualizer
        private static Color ItemColor(int itemID)
        {
            float hue = (itemID * 0.618034f) % 1f;
            return Color.HSVToRGB(hue, 0.9f, 1f);
        }
    }
}
