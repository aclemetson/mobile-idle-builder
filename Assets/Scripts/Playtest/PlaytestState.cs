#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MobileIdleBuilder.Playtest
{
    /// <summary>
    /// One-call snapshot of the game state an AI playtester needs to judge a screenshot: what is
    /// really happening (currency, tutorial step, buildings, power, research) next to what the
    /// player is being shown (open panels, dialogue). Read-only; safe to call every turn.
    /// </summary>
    public static class PlaytestState
    {
        private const int MaxBuildings = 80;
        private const int MaxInventory = 40;

        [Serializable] public sealed class Snapshot
        {
            public string scene;
            public bool   loaded;            // ECSLoadBridge.IsLoaded: gameplay state is valid
            public bool   saveApplied;
            public bool   transitioning;     // SceneLoader is mid scene change
            public float  timeScale;
            public float  playtestTimeScale;
            public float  realtimeSinceStartup;
            public int    screenWidth, screenHeight;

            public Economy   economy;
            public Tutorial  tutorial;
            public UiState   ui;
            public Research  research;
            public Grid      grid;
            public Power     power;
            public List<InventoryEntry> inventory = new();
            public List<string> notes = new();   // anything that could not be read
        }

        [Serializable] public sealed class Economy
        {
            public long  entropy;
            public long  totalEntropySpent;
            public float netWorth;
            public int   tier;
            public float prestigeWall;
            public bool  prestigeAvailable;
            public int   prestigeRuns;
            public long  prestigeCurrencyUnspent;
            public long  crystals;
        }

        [Serializable] public sealed class Tutorial
        {
            public bool   active;
            public int    stepIndex;
            public int    stepCount;
            public string stepId;
            public string hintText;
            public bool   firstRunComplete;
        }

        [Serializable] public sealed class UiState
        {
            public List<string> openPanels;
            public bool dialoguePlaying;
            public bool placingBuilding;
            public bool placementCandidateValid;
            public bool conveyorMode;
            public bool deconstructing;
        }

        [Serializable] public sealed class Research
        {
            public string activeId;
            public string activeCompleteUtc;
            public List<string> unlocked = new();
            public List<string> purchasableNow = new();   // prereqs met and affordable
        }

        [Serializable] public sealed class Grid
        {
            public int    activeSite;
            public int    activeWorld;
            public int    width, height;
            public float  cellSize;
            public int    buildingCount;
            public List<BuildingEntry> buildings = new();
            public List<FieldEntry> fields = new();
        }

        [Serializable] public sealed class FieldEntry
        {
            public string id;            // FieldSO id, e.g. quark_field (paired tiles share it)
            public int    x, y;
            public float  cooldownLeft;  // seconds of game time; 0 = ready to tap
        }

        [Serializable] public sealed class BuildingEntry
        {
            public string type;
            public int    x, y;
            public int    level;
            public int    recipeId;
            public bool   crafting;
            public bool   inputsSatisfied;
            public float  progress;      // 0..1 of the current craft
            public string power;         // "ok" | "brownout" | "unpowered" | null (needs none)
        }

        [Serializable] public sealed class Power
        {
            public float supply, draw, ratio;
            public int   connected, disconnected;
        }

        [Serializable] public sealed class InventoryEntry
        {
            public string id;
            public string name;
            public int    quantity;
        }

        public static Snapshot Capture()
        {
            var bridge = ECSLoadBridge.Instance;
            var save   = SaveManager.Instance?.Current;
            var snap = new Snapshot
            {
                scene                = SceneManager.GetActiveScene().name,
                loaded               = bridge != null && bridge.IsLoaded,
                saveApplied          = bridge != null && bridge.SaveApplied,
                transitioning        = SceneLoader.IsTransitioning,
                timeScale            = Time.timeScale,
                playtestTimeScale    = PlaytestDriver.Instance != null ? PlaytestDriver.Instance.TimeScale : 1f,
                realtimeSinceStartup = Time.realtimeSinceStartup,
                screenWidth          = Screen.width,
                screenHeight         = Screen.height,
            };

            snap.ui = CaptureUi();
            if (!snap.loaded)
            {
                snap.notes.Add("ECS not loaded yet; gameplay fields omitted.");
                return snap;
            }

            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) { snap.notes.Add("No ECS world."); return snap; }
            var em = world.EntityManager;

            snap.economy  = CaptureEconomy(em, save, snap.notes);
            snap.tutorial = CaptureTutorial(em, save);
            snap.research = CaptureResearch(save);
            snap.grid     = CaptureGrid(em, save);
            snap.power    = CapturePower(em);
            CaptureInventory(em, snap.inventory);
            return snap;
        }

        private static UiState CaptureUi()
        {
            var placement   = UnityEngine.Object.FindAnyObjectByType<BuildingPlacementController>();
            var conveyor    = UnityEngine.Object.FindAnyObjectByType<ConveyorPlacementController>();
            var deconstruct = UnityEngine.Object.FindAnyObjectByType<DeconstructController>();
            var dialogue    = UnityEngine.Object.FindAnyObjectByType<DialogueController>();
            return new UiState
            {
                openPanels              = PlaytestUi.OpenPanels(),
                dialoguePlaying         = dialogue != null && dialogue.IsPlaying,
                placingBuilding         = placement != null && placement.IsPlacing,
                placementCandidateValid = placement != null && placement.CandidateValid,
                conveyorMode            = conveyor != null && conveyor.IsPlacing,
                deconstructing          = deconstruct != null && deconstruct.IsDeconstructing,
            };
        }

        private static Economy CaptureEconomy(EntityManager em, SaveData save, List<string> notes)
        {
            var e = new Economy { crystals = save?.paidCurrency ?? 0 };
            if (TryGetSingleton(em, out PlayerProgressData p))
            {
                e.entropy           = p.BaseCurrency;
                e.totalEntropySpent = p.TotalEntropySpent;
                e.netWorth          = p.NetWorth;
                e.tier              = p.CurrentTier;
                e.prestigeWall      = p.PrestigeWallValue;
                e.prestigeAvailable = p.PrestigeAvailable;
            }
            else notes.Add("PlayerProgressData missing.");

            if (TryGetSingleton(em, out PrestigeData pr))
            {
                e.prestigeRuns            = pr.RunCount;
                e.prestigeCurrencyUnspent = pr.PrestigeCurrency - pr.PrestigeCurrencySpent;
            }
            return e;
        }

        private static Tutorial CaptureTutorial(EntityManager em, SaveData save)
        {
            var t = new Tutorial { firstRunComplete = save?.tutorial?.hasCompletedFirstRun ?? false };
            if (!TryGetSingleton(em, out TutorialStateData ts)) return t;

            t.active    = ts.IsActive;
            t.stepIndex = ts.CurrentStepIndex;
            var steps   = TutorialFlowSO.Current?.steps;
            t.stepCount = steps?.Length ?? 0;
            if (steps != null && ts.CurrentStepIndex >= 0 && ts.CurrentStepIndex < steps.Length)
            {
                t.stepId   = steps[ts.CurrentStepIndex].id;
                t.hintText = steps[ts.CurrentStepIndex].hintText;
            }
            return t;
        }

        private static Research CaptureResearch(SaveData save)
        {
            var r = new Research
            {
                activeId          = string.IsNullOrEmpty(save?.activeResearchId) ? null : save.activeResearchId,
                activeCompleteUtc = string.IsNullOrEmpty(save?.activeResearchCompleteUtc) ? null : save.activeResearchCompleteUtc,
            };
            var svc = ResearchService.Instance;
            if (svc?.AllResearch == null) return r;
            foreach (var node in svc.AllResearch)
            {
                if (node == null) continue;
                if (svc.IsUnlocked(node.id)) r.unlocked.Add(node.id);
                else if (svc.CanPurchase(node)) r.purchasableNow.Add(node.id);
            }
            return r;
        }

        private static Grid CaptureGrid(EntityManager em, SaveData save)
        {
            var renderer = UnityEngine.Object.FindAnyObjectByType<GridRenderer>();
            var g = new Grid
            {
                activeSite  = save?.currentRun?.activeSiteIndex ?? 0,
                activeWorld = save?.currentRun?.activeWorldIndex ?? 0,
                width       = renderer != null ? renderer.Width : 0,
                height      = renderer != null ? renderer.Height : 0,
                cellSize    = renderer != null ? renderer.CellSize : 0f,
            };

            var names = BuildingNames();
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<BuildingData>(), ComponentType.ReadOnly<GridPosition>());
            using var entities = query.ToEntityArray(Allocator.Temp);
            g.buildingCount = entities.Length;

            for (int i = 0; i < entities.Length && i < MaxBuildings; i++)
            {
                var entity = entities[i];
                var data   = em.GetComponentData<BuildingData>(entity);
                var cell   = em.GetComponentData<GridPosition>(entity).Cell;
                var entry  = new BuildingEntry
                {
                    type  = names.TryGetValue(data.BuildingType, out var n) ? n : $"#{data.BuildingType}",
                    x     = cell.x,
                    y     = cell.y,
                    level = data.UpgradeLevel,
                    recipeId = -1,
                };
                if (em.HasComponent<RecipeProcessData>(entity))
                {
                    var rp = em.GetComponentData<RecipeProcessData>(entity);
                    entry.recipeId        = rp.RecipeID;
                    entry.crafting        = rp.IsCrafting;
                    entry.inputsSatisfied = rp.InputsSatisfied;
                    entry.progress        = rp.CraftTime > 0f ? Mathf.Clamp01(rp.Progress / rp.CraftTime) : 0f;
                }
                if (em.HasComponent<PowerStatus>(entity))
                {
                    var ps = em.GetComponentData<PowerStatus>(entity);
                    entry.power = ps.IsConnected == 0 ? "unpowered" : ps.ThrottleRatio < 0.999f ? "brownout" : "ok";
                }
                g.buildings.Add(entry);
            }

            foreach (var field in Fields())
            {
                var cell = renderer != null
                    ? GridRenderer.WorldToCell(field.transform.position, renderer.CellSize)
                    : Vector2Int.zero;
                g.fields.Add(new FieldEntry { id = field.Field.id, x = cell.x, y = cell.y, cooldownLeft = CooldownLeft(field) });
            }
            return g;
        }

        /// <summary>Every field tile on the active site that has a FieldSO.</summary>
        public static List<FieldInstance> Fields()
        {
            var list = new List<FieldInstance>();
            foreach (var f in UnityEngine.Object.FindObjectsByType<FieldInstance>(FindObjectsSortMode.None))
                if (f != null && f.Field != null) list.Add(f);
            return list;
        }

        public static float CooldownLeft(FieldInstance field)
        {
            var cd = field.Cooldown;
            if (cd == null || cd.IsReady) return 0f;
            return Mathf.Round((float)(cd.EndUtc - GameClock.UtcNow).TotalSeconds * 10f) / 10f;
        }

        private static Power CapturePower(EntityManager em)
        {
            if (!TryGetSingleton(em, out PowerGridState s)) return null;
            return new Power
            {
                supply = s.Supply, draw = s.Draw, ratio = s.Ratio,
                connected = s.ConnectedCount, disconnected = s.DisconnectedCount,
            };
        }

        private static void CaptureInventory(EntityManager em, List<InventoryEntry> into)
        {
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<PlayerInventoryTag>(), ComponentType.ReadOnly<InventorySlot>());
            if (query.IsEmpty) return;
            var buffer = em.GetBuffer<InventorySlot>(query.GetSingletonEntity(), isReadOnly: true);
            var items  = ItemDatabase.Instance;
            for (int i = 0; i < buffer.Length && into.Count < MaxInventory; i++)
            {
                var slot = buffer[i];
                if (slot.Quantity <= 0) continue;
                var so = items != null ? items.Get(slot.ItemID) : null;
                into.Add(new InventoryEntry
                {
                    id       = so != null ? so.id : $"#{slot.ItemID}",
                    name     = so != null ? so.displayName : null,
                    quantity = slot.Quantity,
                });
            }
        }

        private static Dictionary<int, string> BuildingNames()
        {
            var names = new Dictionary<int, string>();
            var pc = UnityEngine.Object.FindAnyObjectByType<BuildingPlacementController>();
            if (pc?.availableBuildings == null) return names;
            foreach (var entry in pc.availableBuildings)
                if (entry.building != null) names[entry.building.buildingId] = entry.building.name;
            return names;
        }

        private static bool TryGetSingleton<T>(EntityManager em, out T value) where T : unmanaged, IComponentData
        {
            using var query = em.CreateEntityQuery(ComponentType.ReadOnly<T>());
            if (query.IsEmpty) { value = default; return false; }
            value = query.GetSingleton<T>();
            return true;
        }
    }
}
#endif
