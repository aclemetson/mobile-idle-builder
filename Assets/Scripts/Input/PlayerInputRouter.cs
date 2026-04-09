using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Routes tap input to character movement when not in building placement mode.
    /// Taps that land on the HUD are ignored — the UI intercepts them first.
    /// When IsPlacing is true, BuildingPlacementController owns the tap — this router
    /// does nothing so there's no conflict.
    ///
    /// Scene setup: attach to any GameObject, wire placementController, characterMover,
    /// and hudDocument (the UIDocument used by the HUD).
    /// </summary>
    [DefaultExecutionOrder(-10)]
    public class PlayerInputRouter : MonoBehaviour
    {
        [SerializeField] private BuildingPlacementController  placementController;
        [SerializeField] private ConveyorPlacementController  conveyorController;
        [SerializeField] private DeconstructController        deconstructController;
        [SerializeField] private CharacterMover               characterMover;
        [SerializeField] private UIDocument                   hudDocument;

        void Awake()
        {
            if (deconstructController == null)
                deconstructController = FindAnyObjectByType<DeconstructController>();
        }

        void Update()
        {
            if (!InputUtils.WasPointerPressed()) return;
            if (placementController   != null && placementController.IsPlacing)      return;
            if (conveyorController    != null && conveyorController.IsPlacing)       return;
            if (deconstructController != null && deconstructController.IsDeconstructing) return;

            Vector2 screenPos = InputUtils.GetPointerPosition();
            if (IsPointerOverUI(screenPos)) return;

            var ray = Camera.main.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0f));

            // Intersect the ray with the Y=0 ground plane
            if (Mathf.Abs(ray.direction.y) < 0.0001f) return;
            float   t        = (0f - ray.origin.y) / ray.direction.y;
            Vector3 worldPos = ray.origin + ray.direction * t;

            characterMover.SetMoveTarget(worldPos);
        }

        private bool IsPointerOverUI(Vector2 screenPos)
        {
            if (hudDocument == null) return false;
            var panel = hudDocument.rootVisualElement?.panel;
            if (panel == null) return false;

            // UIToolkit panel space has Y=0 at the top; Unity screen space has Y=0 at the bottom
            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(
                panel, new Vector2(screenPos.x, Screen.height - screenPos.y));

            return panel.Pick(panelPos) != null;
        }
    }
}
