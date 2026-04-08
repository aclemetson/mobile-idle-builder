using Unity.Entities;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Singleton component tracking first-run tutorial progress.
    /// TutorialSystem advances CurrentStep when each step's condition is met.
    /// </summary>
    public struct TutorialStateData : IComponentData
    {
        public TutorialStep CurrentStep;
        public bool IsActive;           // false after tutorial is fully complete
        public bool FirstRunComplete;   // true once the player prestiges for the first time
    }
}
