using Unity.Entities;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Bakes the TutorialStateData singleton into the subscene.
    /// Add this component to a GameObject in the game's subscene.
    /// </summary>
    public class TutorialAuthoring : MonoBehaviour
    {
        [Tooltip("Uncheck to disable the tutorial entirely.")]
        public bool tutorialActive = true;

        public class Baker : Baker<TutorialAuthoring>
        {
            public override void Bake(TutorialAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);
                AddComponent(entity, new TutorialStateData
                {
                    CurrentStepIndex = 0,
                    IsActive         = authoring.tutorialActive,
                    FirstRunComplete = false
                });
            }
        }
    }
}
