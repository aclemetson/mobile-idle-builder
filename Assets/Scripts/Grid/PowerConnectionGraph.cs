using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Managed helper that turns the placed power buildings into a connection graph for visualization:
    /// which power nodes link to which (edges), and which of them chain back to a real generator
    /// (connectivity). This is the read-only mirror of the connectivity flood <see cref="PowerGridSystem"/>
    /// runs each frame in Burst — the edge rule routes through <see cref="PowerCoverageMath.NodesLinked"/>
    /// so the dashed lines the player sees always match the grid the simulation computes.
    ///
    /// The core <see cref="BuildEdges"/> / <see cref="ComputeConnectivity"/> functions take plain data and
    /// touch no ECS types, so they are unit-testable without a <see cref="World"/>.
    /// </summary>
    public static class PowerConnectionGraph
    {
        /// <summary>A single placed power building, in grid space, with the data needed to link + draw it.</summary>
        public struct PowerNode
        {
            public int     AnchorX, AnchorY;   // lower-left footprint cell
            public int     Width, Height;      // footprint size in cells
            public float   LinkRange;          // node-to-node connection range (tiles)
            public float   InfluenceRadius;    // coverage radius (tiles) — the powered area this node provides
            public bool    IsGenerator;        // true = real eV source (MaxEV > 0); false = relay/spreader
            public bool    IsGridLinked;       // connectivity as last computed by PowerGridSystem (ECS read)
            public Vector3 WorldCenter;        // footprint centre in world space (for line endpoints)
        }

        /// <summary>
        /// Returns the undirected edges between power nodes that are within connection range of each other
        /// (distance within max of the two link ranges). Each edge is a pair of indices into
        /// <paramref name="nodes"/> with i &lt; j. O(N^2); N is the number of placed power buildings (small).
        /// </summary>
        public static List<(int i, int j)> BuildEdges(IReadOnlyList<PowerNode> nodes)
        {
            var edges = new List<(int, int)>();
            if (nodes == null) return edges;

            for (int i = 0; i < nodes.Count; i++)
            {
                var a = nodes[i];
                for (int j = i + 1; j < nodes.Count; j++)
                {
                    var b = nodes[j];
                    if (PowerCoverageMath.NodesLinked(
                            a.AnchorX, a.AnchorY, a.AnchorX + a.Width - 1, a.AnchorY + a.Height - 1,
                            b.AnchorX, b.AnchorY, b.AnchorX + b.Width - 1, b.AnchorY + b.Height - 1,
                            a.LinkRange, b.LinkRange))
                    {
                        edges.Add((i, j));
                    }
                }
            }
            return edges;
        }

        /// <summary>
        /// Marks each node grid-connected if it is a generator or chains (link by link) to one. Flood over
        /// <paramref name="edges"/> seeded from every generator until stable. Mirrors the Burst pass in
        /// <see cref="PowerGridSystem"/>.
        /// </summary>
        public static bool[] ComputeConnectivity(IReadOnlyList<PowerNode> nodes, List<(int i, int j)> edges)
        {
            int n = nodes?.Count ?? 0;
            var connected = new bool[n];
            if (n == 0) return connected;

            for (int i = 0; i < n; i++)
                connected[i] = nodes[i].IsGenerator;

            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var (i, j) in edges)
                {
                    if (connected[i] == connected[j]) continue;
                    connected[i] = connected[j] = true;
                    changed = true;
                }
            }
            return connected;
        }

        /// <summary>
        /// Snapshots every placed power building (PowerNodeData + GridPosition) from the ECS world into a
        /// managed list, reading the sim-computed <see cref="PowerNodeData.IsGridLinked"/> and converting
        /// footprints to world-space centres with <paramref name="cellSize"/>. Returns an empty list when
        /// the world is null/uncreated. Read-only — allocates and disposes its own Temp arrays.
        /// </summary>
        public static List<PowerNode> GatherFromEcs(EntityManager em, float cellSize)
        {
            var nodes = new List<PowerNode>();
            if (em.World == null || !em.World.IsCreated) return nodes;

            var query = em.CreateEntityQuery(
                ComponentType.ReadOnly<PowerNodeData>(),
                ComponentType.ReadOnly<GridPosition>());
            var entities = query.ToEntityArray(Allocator.Temp);

            for (int e = 0; e < entities.Length; e++)
            {
                var entity = entities[e];
                var node   = em.GetComponentData<PowerNodeData>(entity);
                var cell   = em.GetComponentData<GridPosition>(entity).Cell;

                int w = 1, h = 1;
                if (em.HasComponent<BuildingFootprint>(entity))
                {
                    var fp = em.GetComponentData<BuildingFootprint>(entity);
                    w = fp.Width;
                    h = fp.Height;
                }

                nodes.Add(new PowerNode
                {
                    AnchorX      = cell.x,
                    AnchorY      = cell.y,
                    Width        = w,
                    Height       = h,
                    LinkRange       = node.LinkRadius,
                    InfluenceRadius = node.InfluenceRadius,
                    IsGenerator     = node.MaxEV > 0f,
                    IsGridLinked    = node.IsGridLinked,
                    WorldCenter  = new Vector3(
                        (cell.x + (w - 1) * 0.5f) * cellSize, 0f, (cell.y + (h - 1) * 0.5f) * cellSize)
                });
            }

            entities.Dispose();
            return nodes;
        }
    }
}
