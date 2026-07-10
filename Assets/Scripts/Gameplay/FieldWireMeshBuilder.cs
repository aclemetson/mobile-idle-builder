using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Builds the line-topology mesh used by <see cref="FieldWireMesh"/> — a flat grid of shared
    /// vertices on the local XZ plane, connected by horizontal and vertical line segments so it
    /// reads as a "mathematical field" wire surface. Vertices stay flat (Y = 0) at rest; the
    /// fluctuation and tap bounce are applied per-vertex in the shader, not baked into the mesh.
    ///
    /// Pure construction (no scene needed) so EditMode tests can assert vertex/line counts and bounds.
    /// </summary>
    public static class FieldWireMeshBuilder
    {
        /// <summary>
        /// Builds a <paramref name="resolution"/> x <paramref name="resolution"/> cell wire grid
        /// spanning <paramref name="size"/> units in X and Z, centred on the local origin.
        /// Vertex count is (resolution + 1)^2; the mesh uses <see cref="MeshTopology.Lines"/>.
        /// </summary>
        public static Mesh Build(int resolution, float size)
        {
            resolution = Mathf.Max(1, resolution);
            int   n    = resolution + 1;       // vertices per side
            float half = size * 0.5f;
            float step = size / resolution;

            var verts = new Vector3[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
                verts[z * n + x] = new Vector3(-half + x * step, 0f, -half + z * step);

            // 2 * res * (res + 1) segments => twice as many indices.
            var indices = new List<int>(4 * resolution * (resolution + 1));

            // Horizontal segments (constant z, stepping x).
            for (int z = 0; z < n; z++)
            for (int x = 0; x < resolution; x++)
            {
                indices.Add(z * n + x);
                indices.Add(z * n + x + 1);
            }

            // Vertical segments (constant x, stepping z).
            for (int x = 0; x < n; x++)
            for (int z = 0; z < resolution; z++)
            {
                indices.Add(z * n + x);
                indices.Add((z + 1) * n + x);
            }

            var mesh = new Mesh { name = "FieldWireMesh" };
            if (verts.Length > 65535)
                mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = verts;
            mesh.SetIndices(indices.ToArray(), MeshTopology.Lines, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
