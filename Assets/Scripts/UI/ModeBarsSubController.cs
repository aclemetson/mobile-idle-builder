using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace MobileIdleBuilder
{
    /// <summary>
    /// The mode bars (#286, moved out of HUDController): the building placement overlay with its bottom bar,
    /// the world-anchored ✓ / ✕ / ↻ confirm popup and the output selector, the conveyor overlay and bar
    /// (Create/Destroy, Bend, Place, Undo, New, Up), and the deconstruct overlay. It follows the three
    /// controllers' events and reports which mode is active for the back button. HUDController keeps the
    /// building-placed charge, the tutorial's OnConveyorPlaced, and the pure helpers (ClampPopup, IsTap,
    /// ConveyorBarLabel). Sibling MonoBehaviour on the HUD GameObject (added at runtime when missing).
    /// </summary>
    [RequireComponent(typeof(HUDController))]
    public class ModeBarsSubController : MonoBehaviour
    {
        BuildingPlacementController _placement;
        ConveyorPlacementController _conveyor;
        DeconstructController       _deconstruct;

        // Placement
        VisualElement _placementOverlay, _placementBar, _confirmPopup;
        Label         _placementLabel;
        Button        _btnRotate, _btnFlip, _btnRemove, _btnConfirm, _btnCancelCandidate, _btnRotateCandidate;
        // Output selector (placing a building with several outputs)
        VisualElement _outputSelector, _outputOptions;
        // Conveyor
        VisualElement _conveyorOverlay, _conveyorBar;
        Label         _conveyorLabel;
        Button        _btnBend, _btnPlace, _btnUndo, _btnNew, _btnUp, _btnMode;
        // Deconstruct
        VisualElement _deconstructOverlay;

        // Mode flags mirror the controllers' events. _modeEndedFrame lets the back handler yield on the frame a
        // controller cancelled its mode on the same Escape press, so one press never closes two layers.
        bool _placingMode, _conveyorMode, _deconstructMode;
        int  _modeEndedFrame = -1;

        /// <summary>A placement, conveyor or deconstruct mode is on, or one ended this frame (back button).</summary>
        public bool ModeActiveOrJustEnded =>
            _placingMode || _conveyorMode || _deconstructMode || _modeEndedFrame == Time.frameCount;

        public void Initialize(VisualElement root)
        {
            _placementOverlay   = root.Q("placement-overlay");
            _placementBar       = root.Q("placement-bar");
            _placementLabel     = root.Q<Label>("placement-label");
            _btnRotate          = root.Q<Button>("btn-rotate-output");
            _btnFlip            = root.Q<Button>("btn-flip-building");
            _btnRemove          = root.Q<Button>("btn-remove-building");
            _confirmPopup       = root.Q("placement-confirm-popup");
            _btnConfirm         = root.Q<Button>("btn-confirm-place");
            _btnCancelCandidate = root.Q<Button>("btn-cancel-candidate");
            _btnRotateCandidate = root.Q<Button>("btn-rotate-candidate");
            _outputSelector     = root.Q("output-selector");
            _outputOptions      = root.Q("output-selector__options");

            _conveyorOverlay = root.Q("conveyor-overlay");
            _conveyorBar     = root.Q("conveyor-bar");
            _conveyorLabel   = root.Q<Label>("conveyor-label");
            _btnBend         = root.Q<Button>("btn-conveyor-rotate");
            _btnPlace        = root.Q<Button>("btn-conveyor-confirm");
            _btnUndo         = root.Q<Button>("btn-conveyor-cancel-candidate");
            _btnNew          = root.Q<Button>("btn-conveyor-new-start");
            _btnUp           = root.Q<Button>("btn-conveyor-up");
            _btnMode         = root.Q<Button>("btn-conveyor-mode");

            _deconstructOverlay = root.Q("deconstruct-overlay");

            foreach (var el in new[] { _placementOverlay, _confirmPopup, _conveyorOverlay, _deconstructOverlay, _outputSelector })
                HUDController.SetElementVisible(el, false);

            BindButtons(root);
        }

        void BindButtons(VisualElement root)
        {
            // Building placement
            var cancelPlacement = root.Q<Button>("btn-cancel-placement");
            if (cancelPlacement != null) cancelPlacement.clicked += () => _placement?.CancelPlacement();
            if (_btnRotate          != null) _btnRotate.clicked          += () => _placement?.Rotate();
            if (_btnFlip            != null) _btnFlip.clicked            += () => _placement?.Flip();
            if (_btnRemove          != null) _btnRemove.clicked          += () => _placement?.RequestRemoveEditedBuilding();
            if (_btnConfirm         != null) _btnConfirm.clicked         += () => _placement?.ConfirmCandidate();
            if (_btnCancelCandidate != null) _btnCancelCandidate.clicked += () => _placement?.ClearCandidate();
            if (_btnRotateCandidate != null) _btnRotateCandidate.clicked += () => _placement?.Rotate();

            // Conveyor "Finished" (inside the conveyor overlay), trace controls and the Create/Destroy toggle
            root.Q<Button>("btn-cancel-conveyor")?.RegisterCallback<ClickEvent>(_ => _conveyor?.CancelConveyorMode());
            if (_btnBend  != null) _btnBend.clicked  += () => _conveyor?.RotatePath();
            if (_btnPlace != null) _btnPlace.clicked += () => _conveyor?.ConfirmPath();
            if (_btnUndo  != null) _btnUndo.clicked  += () => _conveyor?.UndoLastNode();
            if (_btnNew   != null) _btnNew.clicked   += () => _conveyor?.ResetStart();
            if (_btnUp    != null) _btnUp.clicked    += () => _conveyor?.ToggleRaise();
            if (_btnMode  != null) _btnMode.clicked  += () => _conveyor?.ToggleMode();

            // Deconstruct cancel
            root.Q<Button>("btn-cancel-deconstruct")?.RegisterCallback<ClickEvent>(_ => _deconstruct?.CancelDeconstructMode());

            // Output selector cancel (wired once)
            var cancelOutput = _outputSelector?.Q<Button>("btn-cancel-output");
            if (cancelOutput != null) cancelOutput.clicked += OnCancelOutputSelector;
        }

        /// <summary>Follows the three mode controllers. Called from HUDController.OnEnable.</summary>
        public void Attach(BuildingPlacementController placement, ConveyorPlacementController conveyor,
                           DeconstructController deconstruct)
        {
            Detach();
            _placement = placement; _conveyor = conveyor; _deconstruct = deconstruct;
            if (_placement != null)
            {
                _placement.OnPlacingChanged          += OnPlacingChanged;
                _placement.OnOutputSelectionRequired += ShowOutputSelector;
                _placement.OnCandidateChanged        += OnCandidateChanged;
                _placement.IsPointerOverPlacementUI   = IsPointerOverPlacementUI;
            }
            if (_conveyor != null)
            {
                _conveyor.OnPlacingChanged   += OnConveyorPlacingChanged;
                _conveyor.OnModeChanged      += OnConveyorModeChanged;
                _conveyor.OnCandidateChanged += OnConveyorBoolChanged;
                _conveyor.OnStartChanged     += OnConveyorBoolChanged;
                _conveyor.OnTraceChanged     += UpdateConveyorBar;
                _conveyor.IsPointerOverConveyorUI = IsPointerOverConveyorUI;
            }
            if (_deconstruct != null) _deconstruct.OnDeconstructingChanged += OnDeconstructingChanged;
        }

        public void Detach()
        {
            if (_placement != null)
            {
                _placement.OnPlacingChanged          -= OnPlacingChanged;
                _placement.OnOutputSelectionRequired -= ShowOutputSelector;
                _placement.OnCandidateChanged        -= OnCandidateChanged;
                _placement.IsPointerOverPlacementUI   = null;
            }
            if (_conveyor != null)
            {
                _conveyor.OnPlacingChanged   -= OnConveyorPlacingChanged;
                _conveyor.OnModeChanged      -= OnConveyorModeChanged;
                _conveyor.OnCandidateChanged -= OnConveyorBoolChanged;
                _conveyor.OnStartChanged     -= OnConveyorBoolChanged;
                _conveyor.OnTraceChanged     -= UpdateConveyorBar;
                _conveyor.IsPointerOverConveyorUI = null;
            }
            if (_deconstruct != null) _deconstruct.OnDeconstructingChanged -= OnDeconstructingChanged;
            _placement = null; _conveyor = null; _deconstruct = null;
        }

        void OnDestroy() => Detach();

        /// <summary>Per-frame: keeps the confirm popup over the candidate cell as the camera moves.</summary>
        public void Tick() => UpdateConfirmPopup();

        void TrackMode(ref bool flag, bool active)
        {
            if (flag && !active) _modeEndedFrame = Time.frameCount;
            flag = active;
        }

        // ── Building placement ───────────────────────────────────────────────

        void OnPlacingChanged(bool isPlacing)
        {
            TrackMode(ref _placingMode, isPlacing);
            HUDController.SetElementVisible(_placementOverlay, isPlacing);
            if (!isPlacing)
            {
                HUDController.SetElementVisible(_confirmPopup, false);
                // The output selector only exists inside placement; a cancel from Escape/back used to leave it up.
                HUDController.SetElementVisible(_outputSelector, false);
            }

            bool canRotate = isPlacing && (_placement?.CanRotate ?? false);
            bool canFlip   = isPlacing && (_placement?.CanFlip   ?? false);
            bool editing   = isPlacing && (_placement?.IsEditing ?? false);
            HUDController.SetElementVisible(_btnRotate, canRotate);
            HUDController.SetElementVisible(_btnFlip,   canFlip);
            HUDController.SetElementVisible(_btnRemove, editing);

            if (isPlacing && _placementLabel != null)
            {
                string rotateHint = canRotate ? (canFlip ? "  ·  R rotate  ·  F flip" : "  ·  R rotate") : "";
                bool moving = editing || (_placement?.IsMovingField ?? false);
                _placementLabel.text = moving
                    ? $"Tap a tile to move it  ·  confirm to keep it there{rotateHint}"
                    : $"Tap a tile to position  ·  drag to pan  ·  confirm to place{rotateHint}";
            }
        }

        // The world-anchored ✓ / ✕ at the candidate cell, with ↻ for rotatable buildings so the player can
        // re-orient in context instead of reaching for the bottom bar.
        void OnCandidateChanged(bool hasCandidate)
        {
            HUDController.SetElementVisible(_confirmPopup, hasCandidate);
            HUDController.SetElementVisible(_btnRotateCandidate, hasCandidate && (_placement?.CanRotate ?? false));
            if (hasCandidate) UpdateConfirmPopup();
        }

        void UpdateConfirmPopup()
        {
            if (_confirmPopup == null || _placement == null || !_placement.HasCandidate) return;

            var panel = _confirmPopup.panel;
            var cam   = Camera.main;
            if (panel == null || cam == null) return;

            Vector2 panelPoint = RuntimePanelUtils.CameraTransformWorldToPanel(panel, _placement.CandidateWorldPosition, cam);

            float w = _confirmPopup.resolvedStyle.width;
            float h = _confirmPopup.resolvedStyle.height;
            if (float.IsNaN(w)) w = 0f;
            if (float.IsNaN(h)) h = 0f;

            // Centre horizontally over the cell and float above it (below it if that would clip the top).
            const float margin = 8f;
            float topAbove   = panelPoint.y - h - 24f;
            float desiredTop = topAbove < margin ? panelPoint.y + 24f : topAbove;
            Vector2 panelSize = panel.visualTree.layout.size;
            Vector2 pos = panelSize.x > 0f && panelSize.y > 0f
                ? HudText.ClampPopup(new Vector2(panelPoint.x - w * 0.5f, desiredTop), new Vector2(w, h), panelSize, margin)
                : new Vector2(panelPoint.x - w * 0.5f, desiredTop);
            _confirmPopup.style.left = pos.x;
            _confirmPopup.style.top  = pos.y;

            _btnConfirm?.SetEnabled(_placement.CandidateValid);
        }

        void ShowOutputSelector(List<RecipeSO> options)
        {
            if (_outputSelector == null || _outputOptions == null) return;
            _outputOptions.Clear();
            foreach (var recipe in options)
            {
                var captured = recipe;
                var item     = recipe.outputItem;
                string label = item != null ? (item.displayName ?? item.name) : recipe.displayName;
                var btn = new Button { text = label };
                btn.AddToClassList("output-option-btn");
                btn.clicked += () =>
                {
                    HUDController.SetElementVisible(_outputSelector, false);
                    _placement?.SelectOutput(captured);
                };
                _outputOptions.Add(btn);
            }
            HUDController.SetElementVisible(_outputSelector, true);
        }

        void OnCancelOutputSelector()
        {
            HUDController.SetElementVisible(_outputSelector, false);
            _placement?.CancelPlacement();
        }

        // ── Pointer tests (so a tap on the bars is not also a map tap) ────────

        /// <summary>Over the confirm popup or the placement bar strip (NOT the full-screen overlay).</summary>
        bool IsPointerOverPlacementUI(Vector2 screenPos) =>
            ScreenPointInElement(_confirmPopup, screenPos) || ScreenPointInElement(_placementBar, screenPos);

        /// <summary>Over the conveyor bar strip (NOT the full-screen overlay).</summary>
        bool IsPointerOverConveyorUI(Vector2 screenPos) => ScreenPointInElement(_conveyorBar, screenPos);

        static bool ScreenPointInElement(VisualElement el, Vector2 screenPos)
        {
            if (el == null || el.ClassListContains("hidden")) return false;
            var panel = el.panel;
            if (panel == null) return false;
            // Input System screen coords are bottom-left origin; panel coords are top-left. In this project
            // RuntimePanelUtils.ScreenToPanel scales but does NOT flip Y, so we flip here. Without this,
            // top-of-screen UI maps to the bottom of the panel and never registers as "over UI".
            Vector2 flipped  = new Vector2(screenPos.x, Screen.height - screenPos.y);
            Vector2 panelPos = RuntimePanelUtils.ScreenToPanel(panel, flipped);
            return el.worldBound.Contains(panelPos);
        }

        // ── Conveyor ──────────────────────────────────────────────────────────

        void OnConveyorPlacingChanged(bool isPlacing)
        {
            TrackMode(ref _conveyorMode, isPlacing);
            HUDController.SetElementVisible(_conveyorOverlay, isPlacing);
            // Mode always re-enters in Create with no pending trace (this also refreshes the bar).
            if (isPlacing) OnConveyorModeChanged(false);
        }

        /// <summary>Updates the Create/Destroy toggle look and the bar's instruction.</summary>
        void OnConveyorModeChanged(bool isDestroy)
        {
            if (_btnMode != null)
            {
                _btnMode.text = isDestroy ? "Destroy" : "Create";
                _btnMode.EnableInClassList("conveyor-mode-btn--destroy", isDestroy);
            }
            UpdateConveyorBar();
        }

        void OnConveyorBoolChanged(bool _) => UpdateConveyorBar();

        /// <summary>
        /// The conveyor bar for the current trace: Bend and Place once there is a belt to place, Undo and New
        /// once a start is set, and a short instruction that never repeats the button names.
        /// </summary>
        void UpdateConveyorBar()
        {
            var  cc        = _conveyor;
            bool destroy   = cc != null && cc.IsDestroyMode;
            bool hasStart  = cc != null && !destroy && cc.HasStart;
            bool candidate = cc != null && !destroy && cc.HasCandidate;

            HUDController.SetElementVisible(_btnBend,  candidate);
            HUDController.SetElementVisible(_btnPlace, candidate);
            HUDController.SetElementVisible(_btnUndo,  hasStart);
            HUDController.SetElementVisible(_btnNew,   hasStart);

            // Up (overpass): once researched, while the trace can go on. Lit while the next tap lays one.
            bool ended = cc != null && cc.IsTraceEnded;
            bool raise = hasStart && cc.RaiseNext;
            HUDController.SetElementVisible(_btnUp, hasStart && !ended && cc.CanRaise);
            _btnUp?.EnableInClassList("conveyor-mode-btn--on", raise);

            if (_conveyorLabel != null)
                _conveyorLabel.text = raise ? HudText.ConveyorRaiseLabel
                                            : HudText.ConveyorBarLabel(destroy, hasStart, candidate, ended);
        }

        // ── Deconstruct ───────────────────────────────────────────────────────

        void OnDeconstructingChanged(bool isDeconstructing)
        {
            TrackMode(ref _deconstructMode, isDeconstructing);
            HUDController.SetElementVisible(_deconstructOverlay, isDeconstructing);
        }
    }
}
