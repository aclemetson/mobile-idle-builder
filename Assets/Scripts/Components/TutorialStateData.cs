using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Singleton component tracking first-run tutorial progress.
    /// TutorialSystem advances CurrentStepIndex when each step's condition is met.
    /// Step definitions live in TutorialFlowSO (generated from game_data.json).
    /// </summary>
    public struct TutorialStateData : IComponentData
    {
        /// <summary>Index into TutorialFlowSO.steps. -1 = not yet started.</summary>
        public int  CurrentStepIndex;
        public bool IsActive;           // false after tutorial is fully complete
        public bool FirstRunComplete;   // true once the player prestiges for the first time
    }
}
