using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Moves a placed building's entity to a new anchor, rotation and flip in place, so everything it holds
    /// (input and output buffers, craft progress, recipe, upgrade levels, a manager's bonus, Filter rules)
    /// stays with it. Only the grid-facing state is rewritten: position, transform, footprint, placed ports,
    /// the legacy output direction, Filter rule facings and the splitter's turn. Grid occupancy, visuals and
    /// manager keys are the caller's job (<see cref="BuildingPlacer.MoveBuilding"/>).
    /// </summary>
    public static class BuildingMove
    {
        /// <summary>
        /// Rewrites <paramref name="e"/>'s grid state for its new placement. <paramref name="outputDirection"/>
        /// is the legacy field collector's output side; pass null for buildings with a port layout.
        /// </summary>
        public static void Apply(EntityManager em, Entity e, BuildingSO building, int x, int y,
                                 int rotation, bool flipped, int? outputDirection)
        {
            int  oldRotation = 0;
            bool oldFlipped  = false;
            if (em.HasComponent<BuildingTransformData>(e))
            {
                var t = em.GetComponentData<BuildingTransformData>(e);
                oldRotation = t.Rotation;
                oldFlipped  = t.Flipped;
            }

            em.SetComponentData(e, new GridPosition { Cell = new int2(x, y) });

            var transform = new BuildingTransformData { Rotation = rotation, Flipped = flipped };
            if (em.HasComponent<BuildingTransformData>(e)) em.SetComponentData(e, transform);
            else                                           em.AddComponentData(e, transform);

            var baseFp = BaseFootprint(building);
            var fp     = PortUtils.RotatedFootprint(baseFp, rotation);
            if (fp.x > 1 || fp.y > 1)
            {
                var footprint = new BuildingFootprint { Width = fp.x, Height = fp.y };
                if (em.HasComponent<BuildingFootprint>(e)) em.SetComponentData(e, footprint);
                else                                       em.AddComponentData(e, footprint);
            }
            else if (em.HasComponent<BuildingFootprint>(e))
            {
                em.RemoveComponent<BuildingFootprint>(e);
            }

            // Ports carry absolute cells, so every one is rebuilt for the new anchor and orientation.
            var ports = em.GetBuffer<PlacedPortData>(e);
            ports.Clear();
            if (building?.ports != null && building.ports.Length > 0)
            {
                foreach (var port in building.ports)
                {
                    var (relCell, dir) = PortUtils.TransformPort(port, baseFp, flipped, rotation);
                    ports.Add(new PlacedPortData
                    {
                        PortType = (int)port.portType,
                        CellX    = x + relCell.x,
                        CellY    = y + relCell.y,
                        Facing   = (int)dir
                    });
                }
            }
            else if (outputDirection.HasValue)
            {
                ports.Add(new PlacedPortData
                {
                    PortType = (int)PortType.Output, CellX = x, CellY = y, Facing = outputDirection.Value
                });
            }

            if (outputDirection.HasValue)
            {
                var od = new OutputDirectionData { Direction = outputDirection.Value };
                if (em.HasComponent<OutputDirectionData>(e)) em.SetComponentData(e, od);
                else                                         em.AddComponentData(e, od);
            }

            // Filter rules name absolute sides; turn them with the building so each item keeps leaving by
            // the same port it did before.
            if (em.HasBuffer<OutputPortFilter>(e) && (rotation != oldRotation || flipped != oldFlipped))
            {
                var buffer = em.GetBuffer<OutputPortFilter>(e);
                FilterSystem.WriteRules(buffer, RemapRules(FilterSystem.ReadRules(buffer),
                                                           oldRotation, oldFlipped, rotation, flipped));
            }

            // The splitter's next side is an absolute facing too; start its turn over.
            if (em.HasComponent<SplitterState>(e))
                em.SetComponentData(e, new SplitterState { NextFacing = -1 });
        }

        /// <summary>
        /// Where a side of the building ends up after it turns from (oldRotation, oldFlipped) to
        /// (newRotation, newFlipped): undo the old orientation back to the building's own frame, then apply
        /// the new one (flip first, then rotate, the same order as <see cref="PortUtils.TransformPort"/>).
        /// </summary>
        public static OutputDirection RemapFacing(OutputDirection facing, int oldRotation, bool oldFlipped,
                                                  int newRotation, bool newFlipped)
        {
            var local = PortUtils.RotateDir(facing, (4 - oldRotation % 4) % 4);
            if (oldFlipped) local = PortUtils.FlipDir(local);
            if (newFlipped) local = PortUtils.FlipDir(local);
            return PortUtils.RotateDir(local, newRotation % 4);
        }

        /// <summary>Filter rules with each facing turned by <see cref="RemapFacing"/>.</summary>
        public static List<(int facing, int itemId)> RemapRules(IReadOnlyList<(int facing, int itemId)> rules,
                                                                int oldRotation, bool oldFlipped,
                                                                int newRotation, bool newFlipped)
        {
            var result = new List<(int, int)>(rules.Count);
            foreach (var (facing, itemId) in rules)
            {
                int turned = facing < 0 ? facing
                    : (int)RemapFacing((OutputDirection)facing, oldRotation, oldFlipped, newRotation, newFlipped);
                result.Add((turned, itemId));
            }
            return result;
        }

        private static Vector2Int BaseFootprint(BuildingSO building) =>
            building == null
                ? Vector2Int.one
                : new Vector2Int(Mathf.Max(1, building.footprint.x), Mathf.Max(1, building.footprint.y));
    }
}
