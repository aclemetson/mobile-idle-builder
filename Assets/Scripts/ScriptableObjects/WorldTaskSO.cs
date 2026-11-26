using System;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>One delivery stage: the items Maxwell's Demon must receive while it is the active stage.</summary>
    [Serializable]
    public class WorldTaskStage
    {
        public string   id;
        public string   displayName;
        public string   description;
        public ItemSO[] items;          // parallel to quantities
        public int[]    quantities;     // parallel to items
        public string   dialogueId;     // played when the stage completes ("" = none)
    }

    /// <summary>
    /// The delivery tasks that unlock a world (2026-11-19: the Chemistry gate). Stages run in order and,
    /// like the megastructure, survive prestige: progress lives in the persistent part of SaveData
    /// (<c>worldTaskStage</c> / <c>worldTaskProgress*</c>). Finishing the last stage unlocks
    /// <see cref="worldId"/> for good. Generated from game_data.json <c>world_tasks</c> by GameDataImporter.
    /// </summary>
    [CreateAssetMenu(fileName = "WorldTasks", menuName = "MobileIdleBuilder/World Tasks")]
    public class WorldTaskSO : ScriptableObject
    {
        public string worldId;
        [Tooltip("World where the deliveries are made (Maxwell's Demon lives there). The task strip shows only in it.")]
        public string deliverIn = "world_physics";
        public string displayName;
        public WorldTaskStage[] stages = Array.Empty<WorldTaskStage>();
    }
}
