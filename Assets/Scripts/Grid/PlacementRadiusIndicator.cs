using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// A flat circular outline drawn on the grid's ground plane (XZ) to preview a power building's
    /// influence radius while it is being positioned. Replaces the old square tile wash: a ring reads
    /// as the true circular coverage, and the placed buildings that fall inside light up separately
    /// (see <see cref="BuildingVisualizer.HighlightBuildingsInRange"/>).
    ///
    /// Built entirely in code (one looped <see cref="LineRenderer"/>, no scene wiring) and created at
    /// runtime by <see cref="BuildingPlacementController"/>, the same way the ghost output arrow is.
    /// Mirrors the ring construction in <see cref="FieldCooldownIndicator"/>, but flat (in XZ, not
    /// billboarded) and a full closed loop.
    /// </summary>
    [RequireComponent(typeof(LineRenderer))]
    public class PlacementRadiusIndicator : MonoBehaviour
    {
        private const int   Segments  = 64;
        private const float GroundY   = 0.05f; // sit just above the tiles so it isn't z-fought
        private const float LineWidth = 0.06f;

        private LineRenderer _line;
        private readonly Vector3[] _points = new Vector3[Segments];

        void Awake()
        {
            _line = GetComponent<LineRenderer>();
            _line.useWorldSpace     = false;      // points are in the holder's local XZ plane
            _line.loop              = true;
            _line.widthMultiplier   = LineWidth;
            _line.numCornerVertices = 0;
            _line.positionCount     = Segments;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows    = false;
            _line.material          = MakeMaterial(Color.white);
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Positions the ring at <paramref name="centerWorld"/> (world space) and lays out
        /// <see cref="Segments"/> points at <paramref name="worldRadius"/> in the XZ plane, tinted
        /// <paramref name="color"/>. Activates the indicator.
        /// </summary>
        public void Show(Vector3 centerWorld, float worldRadius, Color color)
        {
            if (_line == null) return;
            transform.position = new Vector3(centerWorld.x, GroundY, centerWorld.z);

            for (int i = 0; i < Segments; i++)
            {
                float a = (float)i / Segments * Mathf.PI * 2f;
                _points[i] = new Vector3(Mathf.Cos(a) * worldRadius, 0f, Mathf.Sin(a) * worldRadius);
            }
            _line.SetPositions(_points);
            _line.startColor = color;
            _line.endColor   = color;
            if (_line.material.HasProperty("_BaseColor")) _line.material.SetColor("_BaseColor", color);

            gameObject.SetActive(true);
        }

        /// <summary>Hides the ring (kept alive for the next placement, like the ghost arrow).</summary>
        public void Hide()
        {
            if (gameObject != null) gameObject.SetActive(false);
        }

        private static Material MakeMaterial(Color color)
        {
            // URP Unlit keeps it cheap and Android-safe (per project convention); fall back to
            // Sprites/Default if the URP shader is stripped in a given build.
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))     mat.SetColor("_Color", color);
            return mat;
        }

        void OnDestroy()
        {
            if (_line != null && _line.material != null) Destroy(_line.material);
        }
    }
}
