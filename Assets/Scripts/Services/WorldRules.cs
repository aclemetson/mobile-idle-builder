using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Applies the rules of the world whose site is live (WorldSO flags such as streamTransport) to the
    /// ECS world. <see cref="GridSaveService.LoadGrid"/> calls it before restoring anything, which covers
    /// scene load and every site or world switch (SiteService.SwitchTo reloads the grid).
    /// <para>
    /// Stream worlds (Chemistry): <see cref="ActiveWorldRules.StreamTransport"/> = 1, so ConveyorPlacer
    /// tags new belts as <see cref="StreamSegment"/>s, and Maxwell's Demon is disabled: it is a baked
    /// fixture present on every site, but Chemistry's sink is the placed Calorimeter. The Demon gets the
    /// <see cref="Disabled"/> tag (every system and query skips it), its cells are freed and its visual
    /// removed. Leaving the stream world re-enables it.
    /// </para>
    /// </summary>
    public static class WorldRules
    {
        static WorldDatabaseSO _worlds;

        /// <summary>The live world from the save's activeWorldIndex (null = Physics / no database).</summary>
        public static WorldSO ActiveWorld(SaveData save)
        {
            _worlds ??= Resources.Load<WorldDatabaseSO>("WorldDatabase");
            int i = save?.currentRun?.activeWorldIndex ?? 0;
            if (_worlds?.allWorlds == null || i < 0 || i >= _worlds.allWorlds.Length) return null;
            return _worlds.allWorlds[i];
        }

        /// <summary>True when the live world's belts are streams (pure; for UI and placement checks).</summary>
        public static bool StreamTransport(SaveData save) => ActiveWorld(save)?.streamTransport == true;

        /// <summary>Writes <see cref="ActiveWorldRules"/> and enables or disables the Demon to match.</summary>
        public static void Apply(EntityManager em, bool streamTransport, BuildingVisualizer visualizer = null)
        {
            SetRules(em, streamTransport);
            SetDemonEnabled(em, !streamTransport, visualizer);
        }

        /// <summary>Creates the <see cref="ActiveWorldRules"/> singleton on first use and sets it.</summary>
        public static void SetRules(EntityManager em, bool streamTransport)
        {
            using var q = em.CreateEntityQuery(ComponentType.ReadWrite<ActiveWorldRules>());
            var rules = new ActiveWorldRules { StreamTransport = (byte)(streamTransport ? 1 : 0) };
            if (q.IsEmpty) em.CreateEntity(typeof(ActiveWorldRules));
            q.SetSingleton(rules);
        }

        /// <summary>Reads the singleton (false when it was never written).</summary>
        public static bool IsStreamWorld(EntityManager em)
        {
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<ActiveWorldRules>());
            return !q.IsEmpty && q.GetSingleton<ActiveWorldRules>().StreamTransport != 0;
        }

        /// <summary>Disables or re-enables every Maxwell's Demon entity (<see cref="DemonTag"/>).</summary>
        public static void SetDemonEnabled(EntityManager em, bool enabled, BuildingVisualizer visualizer = null)
        {
            using var q = em.CreateEntityQuery(new EntityQueryDesc
            {
                All     = new[] { ComponentType.ReadOnly<DemonTag>() },
                Options = EntityQueryOptions.IncludeDisabledEntities,
            });
            var demons = q.ToEntityArray(Allocator.Temp);
            bool changed = false;
            foreach (var e in demons)
            {
                bool isDisabled = em.HasComponent<Disabled>(e);
                if (enabled == !isDisabled) continue;
                changed = true;

                int fw = 1, fh = 1;
                if (em.HasComponent<BuildingFootprint>(e))
                {
                    var fp = em.GetComponentData<BuildingFootprint>(e);
                    fw = fp.Width; fh = fp.Height;
                }
                var cell = em.HasComponent<GridPosition>(e) ? em.GetComponentData<GridPosition>(e).Cell : default;

                if (enabled)
                {
                    em.RemoveComponent<Disabled>(e);
                    GridOccupancy.Instance?.RegisterRect(cell.x, cell.y, fw, fh);
                }
                else
                {
                    if (em.HasBuffer<BuildingInputSlot>(e))  em.GetBuffer<BuildingInputSlot>(e).Clear();
                    if (em.HasBuffer<BuildingOutputSlot>(e)) em.GetBuffer<BuildingOutputSlot>(e).Clear();
                    em.AddComponent<Disabled>(e);
                    GridOccupancy.Instance?.ReleaseRect(cell.x, cell.y, fw, fh);
                    if (visualizer != null) visualizer.RemoveBuilding(cell.x, cell.y);
                }
            }
            demons.Dispose();
            if (changed && enabled && visualizer != null) visualizer.Refresh();
        }
    }
}
