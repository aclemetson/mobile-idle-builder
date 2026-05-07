using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Detects when PrestigeSystem increments RunCount, then clears the current-run
    /// save data and triggers a save. This bridges the Burst-compiled PrestigeSystem
    /// to managed save logic.
    /// </summary>
    public class PrestigeSaveWatcher : MonoBehaviour
    {
        EntityQuery _prestigeQuery;
        int         _lastRunCount = -1;

        void Start()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return;
            _prestigeQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<PrestigeData>());
        }

        void Update()
        {
            if (_prestigeQuery == null || _prestigeQuery.IsEmpty) return;

            int current = _prestigeQuery.GetSingleton<PrestigeData>().RunCount;

            // Track every frame; skip init frame to avoid spurious trigger on load
            if (_lastRunCount == -1)
            {
                _lastRunCount = current;
                return;
            }

            if (current == _lastRunCount) return;
            _lastRunCount = current;

            // Prestige just completed — wipe run state and permanently retire the tutorial
            var save = SaveManager.Instance?.Current;
            if (save != null)
            {
                save.currentRun = new CurrentRunData();
                save.currentRun.grid = new GridSaveData();
                save.tutorial.hasCompletedFirstRun = true;
                save.tutorial.isActive             = false;
            }

            ECSLoadBridge.Instance?.FlushToSave();  // writes updated prestige totals
            SaveManager.Instance?.SaveLocal();

            Debug.Log($"[PrestigeSaveWatcher] Run {current} saved after prestige.");
        }
    }
}
