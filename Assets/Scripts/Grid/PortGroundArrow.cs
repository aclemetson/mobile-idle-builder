using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Whether a port still needs a belt, using the same rules as ConveyorSystem. Pure, so tests pin it.
    /// A belt merely passing the port moves nothing, so its arrow stays.
    /// </summary>
    public static class PortArrowRules
    {
        /// <summary>
        /// True when the port's arrow should show. <paramref name="tileFree"/> is false when the tile in
        /// front of the port is off the grid or holds a building, leaving no room for a belt or an arrow.
        /// An output feeds a belt that STARTS on that tile; an input is fed by a belt that ENDS on it.
        /// </summary>
        public static bool ShouldShow(bool isOutput, bool tileFree, bool hasBelt, bool beltIsChainHead, bool beltIsTail)
        {
            if (!tileFree) return false;
            if (!hasBelt) return true;
            return isOutput ? !beltIsChainHead : !beltIsTail;
        }
    }

    /// <summary>
    /// Builds one flat chevron lying in the XZ plane, pointing along +Z and facing up. Pure construction, so
    /// EditMode tests can assert it without a scene. Sizes are in world units.
    /// </summary>
    public static class ChevronMeshBuilder
    {
        public static Mesh Build(float halfWidth, float depth, float thickness, float y)
        {
            float front = depth * 0.5f, back = -depth * 0.5f;

            var mesh = new Mesh { name = "Chevron" };
            // Clockwise from above, starting at the tip. A fan from the tip covers this concave outline.
            mesh.vertices = new[]
            {
                new Vector3(0f, y, front),
                new Vector3(halfWidth, y, back),
                new Vector3(halfWidth, y, back - thickness),
                new Vector3(0f, y, front - thickness),
                new Vector3(-halfWidth, y, back - thickness),
                new Vector3(-halfWidth, y, back),
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 5 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    /// <summary>
    /// Two chevrons on the ground in front of a port, on the neighbouring tile: gold pointing away from the
    /// building for an output, blue pointing at it for an input. The far chevron is larger and dimmer, so an
    /// input reads as a funnel in and an output as a spray out, and the pair glides gently along the flow.
    /// The host sits at the port's cell centre on the ground; local +Z points out through the port's edge.
    /// </summary>
    public class PortGroundArrow : MonoBehaviour
    {
        public static readonly Color OutputColor = new Color(1f, 0.82f, 0.25f);
        public static readonly Color InputColor  = new Color(0.22f, 0.71f, 1f);
        private static readonly Color Outline    = new Color(0.04f, 0.055f, 0.086f);

        // All in cells, measured from the port's cell centre.
        private const float NearDistance  = 0.78f;
        private const float FarDistance   = 1.14f;
        private const float NearHalfWidth = 0.2f;
        private const float NearDepth     = 0.15f;
        private const float FarHalfWidth  = 0.28f;
        private const float FarDepth      = 0.18f;
        private const float Thickness     = 0.1f;
        private const float OutlinePad    = 0.028f;
        private const float GlideDistance = 0.035f;
        private const float GlideSpeed    = 3f;
        private const float DimFarBy      = 0.55f;

        // Just above belt water, so an arrow over a passing belt still shows it isn't connected.
        private const float FillY    = 0.075f;
        private const float OutlineY = 0.07f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly Dictionary<float, Mesh[]> SharedMeshes = new();

        private Transform _near;
        private Transform _far;
        private float     _travel;
        private float     _cell;

        /// <param name="edge">The side of the cell the port is on.</param>
        public void Initialize(OutputDirection edge, bool isOutput, float cellSize)
        {
            _travel = isOutput ? 1f : -1f;
            _cell   = cellSize;
            SetEdge(edge);

            var meshes = MeshesFor(cellSize);
            Color color = isOutput ? OutputColor : InputColor;
            _near = BuildChevron("Near", NearDistance * cellSize, meshes[0], meshes[1], color);
            _far  = BuildChevron("Far", FarDistance * cellSize, meshes[2], meshes[3],
                                 new Color(color.r * DimFarBy, color.g * DimFarBy, color.b * DimFarBy));
        }

        /// <summary>Turns the arrow to the given side of its cell (local +Z = North).</summary>
        public void SetEdge(OutputDirection edge) =>
            transform.localRotation = Quaternion.Euler(0f, (int)edge * 90f, 0f);

        private Transform BuildChevron(string chevronName, float distance, Mesh fill, Mesh outline, Color color)
        {
            var pivot = new GameObject(chevronName);
            pivot.transform.SetParent(transform, worldPositionStays: false);
            pivot.transform.localPosition = new Vector3(0f, 0f, distance);
            // The mesh points along +Z, so an input's chevrons turn round to point at the building.
            pivot.transform.localRotation = _travel > 0f ? Quaternion.identity : Quaternion.Euler(0f, 180f, 0f);

            AddLayer(pivot.transform, outline, Outline);
            AddLayer(pivot.transform, fill, color);
            return pivot.transform;
        }

        private static void AddLayer(Transform parent, Mesh mesh, Color color)
        {
            var opaque = RenderingMaterials.Instance != null ? RenderingMaterials.Instance.Opaque : null;
            if (opaque == null) return; // no URP material available: skip rather than render magenta
            var part = StructureParts.AddMesh("Layer", parent, mesh, opaque, Vector3.zero);
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, color);
            part.GetComponent<MeshRenderer>().SetPropertyBlock(block);
        }

        // Shared by every arrow: the shapes never change, and each arrow only tints them.
        private static Mesh[] MeshesFor(float cell)
        {
            if (SharedMeshes.TryGetValue(cell, out var meshes) && meshes[0] != null) return meshes;

            meshes = new[]
            {
                ChevronMeshBuilder.Build(NearHalfWidth * cell, NearDepth * cell, Thickness * cell, FillY),
                ChevronMeshBuilder.Build((NearHalfWidth + OutlinePad) * cell, (NearDepth + OutlinePad) * cell,
                                         (Thickness + OutlinePad * 2f) * cell, OutlineY),
                ChevronMeshBuilder.Build(FarHalfWidth * cell, FarDepth * cell, Thickness * cell, FillY),
                ChevronMeshBuilder.Build((FarHalfWidth + OutlinePad) * cell, (FarDepth + OutlinePad) * cell,
                                         (Thickness + OutlinePad * 2f) * cell, OutlineY),
            };
            SharedMeshes[cell] = meshes;
            return meshes;
        }

        void Update()
        {
            if (_near == null) return;
            float glide = Mathf.Sin(Time.time * GlideSpeed) * GlideDistance * _travel;
            _near.localPosition = new Vector3(0f, 0f, (NearDistance + glide) * _cell);
            _far.localPosition  = new Vector3(0f, 0f, (FarDistance + glide) * _cell);
        }
    }
}
