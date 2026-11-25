using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Conveyor mode: trace a belt by tapping its points, then confirm it as one belt.
    ///
    /// The mode has two sub-modes, toggled by the HUD's Create/Destroy button. It always starts in Create.
    ///
    /// CREATE (a <see cref="ConveyorTrace"/>):
    ///   1. Tap a free cell (or an existing belt) to set the START.
    ///   2. Tap the next point. The belt is routed there from the last point: straight when they line up,
    ///      else an L (Rotate flips the last bend), going around obstacles when the L is blocked. Keep tapping
    ///      points to shape the belt; it never crosses itself. A point on an existing belt merges into it and
    ///      ends the trace. Undo removes the last point.
    ///   3. ✓ places the whole belt and starts over (the next tap starts a new belt). Two taps and ✓ make the
    ///      same belt as the old start/end placement, which the tutorial relies on.
    ///   Tapping the start again while it is the only point makes a single standalone belt; Rotate turns it.
    ///   Done exits the mode; New clears the trace.
    ///
    /// DESTROY:
    ///   Tap any belt to remove that single segment (its chain neighbours are re-linked).
    ///
    /// The camera is NOT pan-locked: the map pans and zooms between taps. Every tap goes through
    /// <see cref="TapGesture"/>, so a pan, a pinch or rotate, a touch that stops a gliding map, or a long rest
    /// never adds a point.
    /// </summary>
    public class ConveyorPlacementController : MonoBehaviour
    {
        public enum Mode { Create, Destroy }

        [Header("Scene references")]
        [SerializeField] private GridRenderer       gridRenderer;
        [SerializeField] private ConveyorPlacer     conveyorPlacer;
        [SerializeField] private CameraController    cameraController;
        [SerializeField] private ConveyorVisualizer conveyorVisualizer;

        // ----------------------------------------------------------------
        // Public state / events
        // ----------------------------------------------------------------

        public bool IsPlacing     { get; private set; }
        public bool IsDestroyMode => _mode == Mode.Destroy;

        /// <summary>True when ✓ can place something: a traced belt of two or more points, or a single belt.</summary>
        public bool HasCandidate  { get; private set; }

        /// <summary>A start point is set (the trace has at least one point).</summary>
        public bool HasStart      => _trace != null && _trace.HasStart;

        /// <summary>Points tapped so far, including the start.</summary>
        public int  NodeCount     => _trace?.Nodes.Count ?? 0;

        /// <summary>The trace ended on an existing belt: only ✓ (or Undo) is left.</summary>
        public bool IsTraceEnded  => _trace != null && _trace.IsEnded;

        /// <summary>Raised when conveyor mode begins (true) or ends (false).</summary>
        public event Action<bool> OnPlacingChanged;
        /// <summary>Raised when the Create/Destroy mode flips. Carries true when Destroy is active.</summary>
        public event Action<bool> OnModeChanged;
        /// <summary>Raised when ✓ becomes possible (true) or not (false).</summary>
        public event Action<bool> OnCandidateChanged;
        /// <summary>Raised when a start point is set (true) or the trace is cleared (false). A false ends
        /// the tutorial's belt chain, so it fires after every placed belt.</summary>
        public event Action<bool> OnStartChanged;
        /// <summary>Raised whenever the traced points change (HUD label and Undo).</summary>
        public event Action OnTraceChanged;

        /// <summary>True while the next tap lays an overpass (the Up button is on).</summary>
        public bool RaiseNext { get; private set; }

        /// <summary>True once the Overpasses research is done: the Up button shows while tracing.</summary>
        public bool CanRaise => MaxSpan() > 0;

        /// <summary>Tiles an overpass may cross now: 0 (locked), 4, or 8 with Overpasses II.</summary>
        public static int MaxSpan() =>
            OverpassRules.MaxSpan(id => ResearchService.Instance != null && ResearchService.Instance.IsUnlocked(id));
        /// <summary>Raised after a belt is confirmed and placed (drives the tutorial's conveyor step).</summary>
        public event Action OnChainPlaced;

        /// <summary>
        /// Direct hit-test for the conveyor toolbar strip, assigned by HUDController (ScreenPointInElement on
        /// the conveyor-bar). Folded into the press UI check alongside UIInputBlocker so taps on the bar never
        /// leak to the grid behind it.
        /// </summary>
        public Func<Vector2, bool> IsPointerOverConveyorUI;

        // ----------------------------------------------------------------
        // Private state
        // ----------------------------------------------------------------

        private Mode _mode = Mode.Create;

        private ConveyorTrace _trace;
        private readonly TapGesture _tap = new();

        // Single-belt candidate: the start tapped again while it is the only point.
        private bool            _isSingle;
        private OutputDirection _singleDir = OutputDirection.North;

        private Vector2Int _hoverCell = new(-1, -1);

        // Identical refusal toasts at most once per 1.5 s (#301a).
        private readonly ToastRateLimiter _toastLimiter = new(1.5);

        // ----------------------------------------------------------------
        // Lifecycle
        // ----------------------------------------------------------------

        void Awake()
        {
            if (gridRenderer       == null) gridRenderer       = FindAnyObjectByType<GridRenderer>();
            if (conveyorPlacer     == null) conveyorPlacer     = FindAnyObjectByType<ConveyorPlacer>();
            if (cameraController    == null) cameraController    = FindAnyObjectByType<CameraController>();
            if (conveyorVisualizer == null) conveyorVisualizer = FindAnyObjectByType<ConveyorVisualizer>();

            _trace = new ConveyorTrace(
                c => IsFreecell(c.x, c.y),
                IsConveyorCell,
                // Router cost (#301b): keep away from free port fronts and from hugging buildings.
                (a, b) => ConveyorPathfinder.MakeExtraCost(CollectPortFronts(), IsBuildingCell, a, b),
                path => conveyorPlacer != null ? conveyorPlacer.RejectReason(path) : null,
                (a, b) =>
                {
                    int max = MaxSpan();
                    var check = OverpassRules.CanSpan(a, b, max, c => IsBuildingCell(c.x, c.y) || !gridRenderer.IsInBounds(c.x, c.y),
                        c => GridOccupancy.Instance != null && GridOccupancy.Instance.IsUnderOverpass(c.x, c.y));
                    return OverpassRules.Message(check, max);
                });
        }

        /// <summary>Up button: the next tap lays an overpass from the last point (tap again to turn it off).</summary>
        public void ToggleRaise()
        {
            if (!IsPlacing || IsDestroyMode || !CanRaise) return;
            RaiseNext = !RaiseNext;
            OnTraceChanged?.Invoke();
        }

        private void SetRaise(bool on)
        {
            if (RaiseNext == on) return;
            RaiseNext = on;
            OnTraceChanged?.Invoke();
        }

        void OnApplicationPause(bool paused) { if (paused) _tap.Cancel(); }

        // ----------------------------------------------------------------
        // Public API (called by HUD buttons)
        // ----------------------------------------------------------------

        public void BeginConveyorMode()
        {
            if (IsPlacing) return;
            ResetAll();
            _toastLimiter.Reset();
            IsPlacing = true;
            _mode     = Mode.Create;
            cameraController?.SetPanLocked(false); // the map pans and zooms between taps
            OnPlacingChanged?.Invoke(true);
            OnModeChanged?.Invoke(false);
            OnCandidateChanged?.Invoke(false);
            OnTraceChanged?.Invoke();
        }

        /// <summary>"Done" button — exits conveyor mode entirely (an unconfirmed trace is dropped).</summary>
        public void CancelConveyorMode()
        {
            ResetAll();
            IsPlacing = false;
            cameraController?.SetPanLocked(false);
            OnPlacingChanged?.Invoke(false);
        }

        /// <summary>Toggles between Create and Destroy. Clears any pending trace.</summary>
        public void ToggleMode()
        {
            if (!IsPlacing) return;
            ClearTrace();
            _mode = _mode == Mode.Create ? Mode.Destroy : Mode.Create;
            OnModeChanged?.Invoke(IsDestroyMode);
        }

        /// <summary>
        /// Rotate button — for a single belt, cycles its facing N→E→S→W; otherwise bends the last segment's L
        /// the other way. Earlier segments stay as drawn.
        /// </summary>
        public void RotatePath()
        {
            if (_isSingle)
            {
                _singleDir = NextDir(_singleDir);
                conveyorVisualizer?.ShowSinglePreview(_trace.Start.x, _trace.Start.y, (int)_singleDir);
                RefreshPreviewSeams();
                return;
            }
            if (_trace.SegmentCount == 0) return;
            if (_trace.FlipLastBend()) Repaint(pulseLast: false);
            else if (_trace.LastRejectReason != null) Toast(_trace.LastRejectReason);
        }

        /// <summary>✓ — places the traced belt (or the single belt) and starts over.</summary>
        public void ConfirmPath()
        {
            if (!HasCandidate) return;

            bool placed;
            string refusal;
            if (_isSingle)
            {
                conveyorVisualizer?.ClearSinglePreview();
                placed = conveyorPlacer.PlaceConveyorChain(new List<Vector2Int> { _trace.Start }, out refusal, _singleDir);
            }
            else
            {
                placed = conveyorPlacer.PlaceConveyorChain(_trace.Path(), out refusal);
            }

            if (!placed)
            {
                // Used to fail silently, so ✓ looked broken. Say why and keep the trace to adjust.
                Toast(refusal ?? BeltFeedback.BadEndMessage);
                if (_isSingle) conveyorVisualizer?.ShowSinglePreview(_trace.Start.x, _trace.Start.y, (int)_singleDir);
                return;
            }

            SaveManager.Instance?.SaveLocal();
            OnChainPlaced?.Invoke();
            ClearTrace(); // the belt is finished: the next tap starts a new one
        }

        /// <summary>"New" button — clears the whole trace without leaving conveyor mode.</summary>
        public void ResetStart()
        {
            if (!IsPlacing || IsDestroyMode) return;
            ClearTrace();
        }

        /// <summary>Undo button — removes the last point (the start too, when it is the only one).</summary>
        public void UndoLastNode()
        {
            if (!IsPlacing || IsDestroyMode || !HasStart) return;
            if (_isSingle) { LeaveSingle(); Repaint(pulseLast: false); return; }

            _trace.Undo();
            if (!_trace.HasStart) { ClearTrace(); return; }
            Repaint(pulseLast: false);
        }

        /// <summary>Kept for the HUD's older wiring: same as <see cref="UndoLastNode"/>.</summary>
        public void ClearCandidate() => UndoLastNode();

        // ----------------------------------------------------------------
        // Update loop
        // ----------------------------------------------------------------

        void Update()
        {
            if (!IsPlacing) return;

            if (InputUtils.WasCancelPressed())
            {
                if (HasStart) UndoLastNode();
                else          CancelConveyorMode();
                return;
            }

            if (InputUtils.PumpTap(_tap, IsPressOverUI, out Vector2 tapPos))
                HandleTap(WorldToCell(tapPos));

            RefreshHover();
        }

        private bool IsPressOverUI(Vector2 p) =>
            UIInputBlocker.IsPointerOverUI(p) || (IsPointerOverConveyorUI?.Invoke(p) ?? false);

        private void HandleTap(Vector2Int cell)
        {
            GameLogger.Develop($"[Conveyor] TAP cell={cell} mode={_mode} nodes={NodeCount} single={_isSingle}");
            if (!gridRenderer.IsInBounds(cell.x, cell.y)) return;

            if (_mode == Mode.Destroy)
            {
                // Belts are free to replace, so removal needs no confirmation (only buildings ask).
                if (IsConveyorCell(cell) && conveyorPlacer.RemoveSegmentAt(cell.x, cell.y))
                {
                    gridRenderer.ClearDeconstructHover();
                    _hoverCell = new(-1, -1);
                    SaveManager.Instance?.SaveLocal();
                }
                return;
            }

            // The start tapped again while it is the only point: one standalone belt.
            if (NodeCount == 1 && cell == _trace.Start)
            {
                if (_isSingle) return;
                if (IsConveyorCell(cell)) { Toast(BeltFeedback.RetapStartMessage); return; }
                BeginSingle();
                return;
            }

            if (_isSingle) LeaveSingle(); // tapping onward turns the single belt back into a trace

            bool raised = RaiseNext && CanRaise;
            var result = _trace.TryAdd(cell, raised);
            if (raised && result == ConveyorTrace.AddResult.Added) SetRaise(false);   // one overpass per Up
            switch (result)
            {
                case ConveyorTrace.AddResult.Started:
                    SetHasStart(true);
                    ClearHover();
                    Repaint(pulseLast: false);
                    break;

                case ConveyorTrace.AddResult.Added:
                case ConveyorTrace.AddResult.Merged:
                    Repaint(pulseLast: true);
                    break;

                case ConveyorTrace.AddResult.NotEndpoint:
                    Toast(BeltFeedback.RefusalReason(HasStart, false, false, false));
                    break;
                case ConveyorTrace.AddResult.CrossesItself: Toast(BeltFeedback.CrossesItselfMessage); break;
                case ConveyorTrace.AddResult.AlreadyEnded:  Toast(BeltFeedback.TraceEndedMessage);    break;
                case ConveyorTrace.AddResult.Rejected:      Toast(_trace.LastRejectReason);          break;

                case ConveyorTrace.AddResult.Unreachable:
                    Toast(BeltFeedback.NoRouteMessage);
                    // Show which existing belts sit on the straight L (#301a).
                    var blockers = BeltFeedback.BlockingBeltCells(_trace.Last, cell, true, (x, y) => IsConveyorCell(new Vector2Int(x, y)));
                    if (blockers != null && blockers.Count > 0) gridRenderer.SetBlockingBelts(blockers);
                    break;

                case ConveyorTrace.AddResult.SameAsLast:
                    break; // a double tap does nothing
            }
        }

        // ----------------------------------------------------------------
        // Single belt
        // ----------------------------------------------------------------

        private void BeginSingle()
        {
            _isSingle  = true;
            _singleDir = OutputDirection.North;
            gridRenderer.ClearConveyorGhost();
            ClearHover();
            conveyorVisualizer?.ShowSinglePreview(_trace.Start.x, _trace.Start.y, (int)_singleDir);
            RefreshPreviewSeams();
            SetHasCandidate(true);
            OnTraceChanged?.Invoke();
        }

        private void LeaveSingle()
        {
            _isSingle = false;
            conveyorVisualizer?.ClearSinglePreview();
            conveyorVisualizer?.ClearPreviewSeams();
        }

        // Seams the belt being drawn would leave (#208): shown while previewing, gone on confirm or cancel.
        private readonly List<(Unity.Mathematics.int2 cell, int exit)> _previewSeams = new();

        private void RefreshPreviewSeams()
        {
            if (conveyorVisualizer == null) return;
            _previewSeams.Clear();
            if (conveyorPlacer != null && _trace != null && _trace.HasStart && !IsDestroyMode)
            {
                if (_isSingle)
                    conveyorPlacer.PreviewSeams(new List<Vector2Int> { _trace.Start }, _previewSeams, _singleDir);
                else if (_trace.SegmentCount > 0)
                    conveyorPlacer.PreviewSeams(_trace.Path(), _previewSeams);
            }
            if (_previewSeams.Count == 0) conveyorVisualizer.ClearPreviewSeams();
            else conveyorVisualizer.ShowPreviewSeams(_previewSeams);
        }

        // ----------------------------------------------------------------
        // Drawing
        // ----------------------------------------------------------------

        /// <summary>Redraws the trace: orange route cells, green points, and the new point pulsing.</summary>
        private void Repaint(bool pulseLast)
        {
            gridRenderer.ClearConveyorGhost(); // also clears any blocking-belt tint
            if (_trace.HasStart)
            {
                // Existing belts on the route keep their belt visual (an existing start, or a merge end).
                foreach (var c in _trace.Path())
                    if (!IsConveyorCell(c)) gridRenderer.AddConveyorGhostCell(c.x, c.y);
                // Tiles under an overpass show too, so the span reads as one piece (belts there keep their look).
                foreach (var c in _trace.CrossedCells())
                    if (!IsConveyorCell(c)) gridRenderer.AddConveyorGhostCell(c.x, c.y);
                gridRenderer.PaintConveyorNodes(_trace.Nodes);
                if (pulseLast) gridRenderer.PulseConveyorEndpoint(_trace.Last.x, _trace.Last.y);
            }
            SetHasCandidate(_isSingle || _trace.SegmentCount > 0);
            RefreshPreviewSeams();
            OnTraceChanged?.Invoke();
        }

        private void Toast(string reason)
        {
            if (reason != null && _toastLimiter.TryAcquire(reason))
                InventoryPopupController.NotifyWarning(reason);
        }

        // ----------------------------------------------------------------
        // Hover feedback (desktop; a touch has no hover)
        // ----------------------------------------------------------------

        private void RefreshHover()
        {
            // Over a trace, hover tints would overwrite the route; only the first point gets one.
            if (HasStart || _isSingle) return;

            Vector2 screenPos = InputUtils.GetPointerPosition();
            if (IsPressOverUI(screenPos)) { ClearHover(); return; }

            Vector2Int cell = WorldToCell(screenPos);
            if (cell == _hoverCell) return;
            ClearHover();
            _hoverCell = cell;

            if (!gridRenderer.IsInBounds(cell.x, cell.y)) return;

            if (_mode == Mode.Destroy)
            {
                if (IsConveyorCell(cell)) gridRenderer.SetDeconstructHover(cell.x, cell.y);
            }
            else if (IsValidEndpoint(cell))
            {
                gridRenderer.SetConveyorHoverCell(cell.x, cell.y);
            }
        }

        private void ClearHover()
        {
            gridRenderer.ClearConveyorHoverCell();
            gridRenderer.ClearDeconstructHover();
            _hoverCell = new(-1, -1);
        }

        // ----------------------------------------------------------------
        // Validity helpers
        // ----------------------------------------------------------------

        /// <summary>Cycles a single belt's facing N→E→S→W→N. Pure helper (unit-tested).</summary>
        public static OutputDirection NextDir(OutputDirection d) => (OutputDirection)(((int)d + 1) % 4);

        private bool IsValidEndpoint(Vector2Int c) => IsFreecell(c.x, c.y) || IsConveyorCell(c);

        private bool IsFreecell(int x, int y)
        {
            if (!gridRenderer.IsInBounds(x, y)) return false;
            if (GridOccupancy.Instance != null && GridOccupancy.Instance.IsOccupied(x, y)) return false;
            return true;
        }

        /// <summary>A building cell: occupied but not a conveyor.</summary>
        private bool IsBuildingCell(int x, int y) =>
            GridOccupancy.Instance != null && GridOccupancy.Instance.IsOccupied(x, y)
            && !GridOccupancy.Instance.IsConveyorCell(x, y);

        /// <summary>
        /// Belt cells in front of every placed building port (outputs and inputs), via the same
        /// TutorialRouteGuide.OutputFront/InputFront the tutorial route guide uses. Connected ports are
        /// not filtered out: their front already holds a belt, so it is blocked and never costed anyway.
        /// </summary>
        private static HashSet<Vector2Int> CollectPortFronts()
        {
            var fronts = new HashSet<Vector2Int>();
            var world = Unity.Entities.World.DefaultGameObjectInjectionWorld;
            if (world == null || !world.IsCreated) return fronts;
            var em = world.EntityManager;

            using var query = em.CreateEntityQuery(
                Unity.Entities.ComponentType.ReadOnly<BuildingData>(),
                Unity.Entities.ComponentType.ReadOnly<PlacedPortData>());
            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            foreach (var entity in entities)
            {
                var ports = em.GetBuffer<PlacedPortData>(entity, isReadOnly: true);
                for (int i = 0; i < ports.Length; i++)
                {
                    var cell   = new Vector2Int(ports[i].CellX, ports[i].CellY);
                    var facing = (OutputDirection)ports[i].Facing;
                    fronts.Add(ports[i].PortType == (int)PortType.Output
                        ? TutorialRouteGuide.OutputFront(cell, facing)
                        : TutorialRouteGuide.InputFront(cell, facing));
                }
            }
            return fronts;
        }

        private bool IsConveyorCell(Vector2Int c) =>
            GridOccupancy.Instance != null && GridOccupancy.Instance.IsConveyorCell(c.x, c.y);

        // ----------------------------------------------------------------
        // Reset helpers
        // ----------------------------------------------------------------

        private void ResetAll()
        {
            _tap.Cancel();
            ClearTrace();
            gridRenderer.ClearConveyorHoverCell();
            gridRenderer.ClearDeconstructHover();
            _hoverCell = new(-1, -1);
        }

        /// <summary>Drops the trace (and any single belt), raising OnStartChanged(false) if one was set.</summary>
        private void ClearTrace()
        {
            if (_isSingle) LeaveSingle();
            _trace?.Clear();
            RaiseNext = false;
            gridRenderer.ClearConveyorGhost();
            conveyorVisualizer?.ClearPreviewSeams();
            ClearHover();
            SetHasCandidate(false);
            SetHasStart(false);
            OnTraceChanged?.Invoke();
        }

        // ----------------------------------------------------------------
        // Event helpers
        // ----------------------------------------------------------------

        private bool _startRaised;

        /// <summary>Raises <see cref="OnStartChanged"/> only on a change.</summary>
        private void SetHasStart(bool value)
        {
            if (_startRaised == value) return;
            _startRaised = value;
            OnStartChanged?.Invoke(value);
        }

        private void SetHasCandidate(bool value)
        {
            if (HasCandidate == value) return;
            HasCandidate = value;
            OnCandidateChanged?.Invoke(value);
        }

        private Vector2Int WorldToCell(Vector2 screenPos)
        {
            var ray = Camera.main.ScreenPointToRay(new Vector3(screenPos.x, screenPos.y, 0));
            if (Mathf.Abs(ray.direction.y) < 0.0001f) return new(-1, -1);

            float t     = -ray.origin.y / ray.direction.y;
            var   world = ray.origin + ray.direction * t;

            return GridRenderer.WorldToCell(world, gridRenderer.CellSize);
        }
    }
}
