using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Advances the tutorial step when each step's completion condition is met.
    /// Checks inventory state and entity counts — does NOT drive UI directly.
    /// The UI bridge MonoBehaviour reads TutorialStateData and shows appropriate prompts.
    ///
    /// Item IDs used for condition checks (must match recipes.json):
    ///   1 = Up Quark | 2 = Down Quark | 3 = Electron
    ///   4 = Proton   | 5 = Neutron    | 6 = Hydrogen
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(ProductionSystem))]
    public partial struct TutorialSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<TutorialStateData>();
            state.RequireForUpdate<PlayerInventoryTag>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var tutorial = SystemAPI.GetSingleton<TutorialStateData>();
            if (!tutorial.IsActive) return;

            var inventory = SystemAPI.GetSingletonBuffer<InventorySlot>(true); // read-only

            bool advanced = false;
            switch (tutorial.CurrentStep)
            {
                case TutorialStep.None:
                    tutorial.CurrentStep = TutorialStep.IntroDialogue;
                    advanced = true;
                    break;

                case TutorialStep.IntroDialogue:
                    // Skip intro for returning players who already have research unlocked
                    if (SaveManager.Instance != null &&
                        SaveManager.Instance.Current.unlockedResearch.Contains("recombination_i"))
                    {
                        tutorial.CurrentStep = TutorialStep.CraftFirstQuarks;
                        advanced = true;
                    }
                    // Otherwise TutorialOverlayController plays the dialogue and advances this step
                    break;

                case TutorialStep.BuyRecombinationI:
                    // Advance once Recombination I appears in the persistent unlock list
                    if (SaveManager.Instance != null &&
                        SaveManager.Instance.Current.unlockedResearch.Contains("recombination_i"))
                    {
                        tutorial.CurrentStep = TutorialStep.CraftFirstQuarks;
                        advanced = true;
                    }
                    break;

                case TutorialStep.CraftFirstQuarks:
                    // Condition: player has at least one up quark or down quark
                    if (CountInInventory(inventory, 1) > 0 || CountInInventory(inventory, 2) > 0)
                    {
                        tutorial.CurrentStep = TutorialStep.CraftFirstProton;
                        advanced = true;
                    }
                    break;

                case TutorialStep.CraftFirstProton:
                    if (CountInInventory(inventory, 4) > 0)
                    {
                        tutorial.CurrentStep = TutorialStep.CraftFirstNeutron;
                        advanced = true;
                    }
                    break;

                case TutorialStep.CraftFirstNeutron:
                    if (CountInInventory(inventory, 5) > 0)
                    {
                        tutorial.CurrentStep = TutorialStep.CraftFirstHydrogen;
                        advanced = true;
                    }
                    break;

                case TutorialStep.CraftFirstHydrogen:
                    if (CountInInventory(inventory, 6) > 0)
                    {
                        tutorial.CurrentStep = TutorialStep.PlaceFirstBuilding;
                        advanced = true;
                    }
                    break;

                case TutorialStep.PlaceFirstBuilding:
                    // Condition: at least one BuildingData entity exists
                    int buildingCount = 0;
                    foreach (var _ in SystemAPI.Query<RefRO<BuildingData>>())
                        buildingCount++;
                    if (buildingCount > 0)
                    {
                        tutorial.CurrentStep = TutorialStep.AutomationStarted;
                        advanced = true;
                    }
                    break;

                case TutorialStep.AutomationStarted:
                    // Automation milestone — further steps driven by PlayerProgressData
                    var progress = SystemAPI.GetSingleton<PlayerProgressData>();
                    if (progress.PrestigeAvailable)
                    {
                        tutorial.CurrentStep = TutorialStep.ReachPrestigeWall;
                        advanced = true;
                    }
                    break;

                case TutorialStep.ReachPrestigeWall:
                    // PrestigeSystem sets PrestigeRequested — we just wait for it to complete
                    var prestige = SystemAPI.GetSingleton<PrestigeData>();
                    if (prestige.RunCount >= 1)
                    {
                        tutorial.CurrentStep = TutorialStep.FirstPrestigeComplete;
                        tutorial.FirstRunComplete = true;
                        advanced = true;
                    }
                    break;

                case TutorialStep.FirstPrestigeComplete:
                    tutorial.CurrentStep = TutorialStep.SpendPrestigeCurrency;
                    advanced = true;
                    break;

                case TutorialStep.SpendPrestigeCurrency:
                    // Driven by UI interaction — UI sets this step to Completed externally
                    break;

                case TutorialStep.Completed:
                    tutorial.IsActive = false;
                    SystemAPI.SetSingleton(tutorial);
                    return;
            }

            if (advanced)
            {
                SystemAPI.SetSingleton(tutorial);
                UnityEngine.Debug.Log($"[TutorialSystem] Advanced to step: {tutorial.CurrentStep}");
            }
        }

        private static int CountInInventory(DynamicBuffer<InventorySlot> inv, int itemID)
        {
            for (int i = 0; i < inv.Length; i++)
                if (inv[i].ItemID == itemID) return inv[i].Quantity;
            return 0;
        }
    }
}
