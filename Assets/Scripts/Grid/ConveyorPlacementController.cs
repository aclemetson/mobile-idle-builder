using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Manages conveyor placement mode with a deliberate, tap-based interaction designed for
    /// mobile (replaces the old press-drag-release model that placed abandoned tracks on stray
    /// touches and offered no confirmation step).
    ///
    /// The mode has two sub-modes, toggled by the HUD's Create/Destroy button. It always starts
    /// in Create when the mode is entered.
    ///
    /// CREATE:
    ///   1. Tap a free cell (or an existing conveyor cell) — it becomes the START anchor.
    ///   2. Tap another free / existing-conveyor cell — the best route between them is computed
    ///      and shown as a ghost. A single-turn L is preferred; if it is blocked the route is
    ///      found around obstacles (ConveyorPathfinder). This is the placement CANDIDATE.
    ///   3. Rotate flips the L-elbow between horizontal-first and vertical-first. Check (confirm)
    ///      places the run and auto-chains: the run's end becomes the next START so the player can
    ///      keep extending. The X clears the candidate (keeping the start) to pick a new end.
    ///   4. Finished (the HUD button that used to read "Cancel") exits the mode entirely.
    ///
    /// DESTROY:
    ///   Tap any track to remove that single segment (its chain neighbours are re-linked). Toggle
    ///   back to Create at any time.
    ///
    /// The camera is NOT pan-locked here: the player can scroll the map between taps. A press is
    /// only treated as a tap when it stays under CameraController.TapThreshold, so drags pan the
    /// camera without placing or destroying anything.
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
        public bool HasCandidate  { get; private set; }

        /// <summary>Raised when conveyor mode begins (true) or ends (false).</summary>
        public event Action<bool> OnPlacingChanged;
        /// <summary>Raised when the Create/Destroy mode flips. Carries true when Destroy is active.</summary>
        public event Action<bool> OnModeChanged;
        /// <summary>Raised when a placement candidate appears (true) or is cleared (false).</summary>
        public event Action<bool> OnCandidateChanged;
        /// <summary>Raised after a run is confirmed and placed (drives the tutorial's conveyor step).</summary>
        public event Action OnChainPlaced;

        /// <summary>
        /// Direct hit-test for the conveyor toolbar strip, assigned by HUDController
        /// (ScreenPointInElement on the conveyor-bar). Folded into the press/hover UI check
        /// alongside UIInputBlocker so taps on the bar never leak to the grid behind it — the
        /// same belt-and-suspenders the building placement bar uses.
        /// </summary>
        public Func<Vector2, bool> IsPointerOverConveyorUI;

        // ----------------------------------------------------------------
        // Private state
        // ----------------------------------------------------------------

        private Mode _mode = Mode.Create;

        private bool       _hasStart;
        private Vector2Int _startCell;

        private Vector2Int       _destCell;
        private List<Vector2Int> _currentPath = new();
        private bool             _pathValid;
        private bool             _elbowHorizontalFirst = true;

        // Single-cell candidate: the player tapped the start cell again to drop one standalone
        // belt whose facing they rotate before confirming.
        private bool            _isSingle;
        private OutputDirection _singleDir = OutputDirection.North;

        // Tap-vs-drag tracking (so map panning does not place/destroy)
        private bool    _pressActive;
        private bool    _pressOverUI;
        private Vector2 _pressPos;
        private float   _dragAccum;

        private Vector2Int _hoverCell = new(-1, -1);

        // ----------------------------------------------------------------
        // Lifecycle
        // ----------------------------------------------------------------

        void Awake()
        {
            if (gridRenderer       == null) gridRenderer       = FindAnyObjectByType<GridRenderer>();
            if (conveyorPlacer     == null) conveyorPlacer     = FindAnyObjectByType<ConveyorPlacer>();
            if (cameraController    == null) cameraController    = FindAnyObjectByType<CameraController>();
            if (conveyorVisualizer == null) conveyorVisualizer = FindAnyObjectByType<ConveyorVisualizer>();
        }

        // ----------------------------------------------------------------
        // Public API (called by HUD buttons)
        // ----------------------------------------------------------------

        public void BeginConveyorMode()
        {
            if (IsPlacing) return;
            ResetAll();
            IsPlacing = true;
            _mode     = Mode.Create;
            cameraController?.SetPanLocked(false); // allow map panning between taps
            OnPlacingChanged?.Invoke(true);
            OnModeChanged?.Invoke(false);
            OnCandidateChanged?.Invoke(false);
        }

        /// <summary>"Finished" button — exits conveyor mode entirely.</summary>
        public void CancelConveyorMode()
        {
            ResetAll();
            IsPlacing = false;
            cameraController?.SetPanLocked(false);
            OnPlacingChanged?.Invoke(false);
        }

        /// <summary>Toggles between Create and Destroy. Clears any pending start/candidate.</summary>
        public void ToggleMode()
        {
            if (!IsPlacing) return;
            ClearStartAndCandidate();
            _mode = _mode == Mode.Create ? Mode.Destroy : Mode.Create;
            OnModeChanged?.Invoke(IsDestroyMode);
        }

        /// <summary>
        /// Rotate button — for a single belt, cycles its facing N→E→S→W; otherwise flips which way
        /// the single-turn L bends, then recomputes.
        /// </summary>
        public void RotatePath()
        {
            if (!HasCandidate) return;
            if (_isSingle)
            {
                _singleDir = NextDir(_singleDir);
                conveyorVisualizer?.ShowSinglePreview(_startCell.x, _startCell.y, (int)_singleDir);
                return;
            }
            _elbowHorizontalFirst = !_elbowHorizontalFirst;
            RecomputePath();
        }

        /// <summary>Check button — commits the candidate run and auto-chains from its end.</summary>
        public void ConfirmPath()
        {
            if (!HasCandidate || !_pathValid || _currentPath.Count < 1) return;

            if (_isSingle)
            {
                conveyorVisualizer?.ClearSinglePreview();
                conveyorPlacer.PlaceConveyorChain(_currentPath, _singleDir);
                SaveManager.Instance?.SaveLocal();
                OnChainPlaced?.Invoke();

                // A standalone belt resets to a fresh start (no auto-chain).
                ClearStartAndCandidate();
                return;
            }

            conveyorPlacer.PlaceConveyorChain(_currentPath);
            SaveManager.Instance?.SaveLocal();
            OnChainPlaced?.Invoke();

            // Auto-chain: the run's end becomes the next start so the player can keep extending.
            Vector2Int newStart = _destCell;
            ClearStartAndCandidate();
            _hasStart  = true;
            _startCell = newStart;
            MarkStart();
        }

        /// <summary>X button — drops the pending end/path but keeps the current start.</summary>
        public void ClearCandidate()
        {
            if (!HasCandidate) return;
            conveyorVisualizer?.ClearSinglePreview();
            gridRenderer.ClearConveyorGhost();
            _currentPath = new List<Vector2Int>();
            _pathValid   = false;
            _isSingle    = false;
            HasCandidate = false;
            OnCandidateChanged?.Invoke(false);
            if (_hasStart) MarkStart();
        }

        // ----------------------------------------------------------------
        // Update loop
        // ----------------------------------------------------------------

        void Update()
        {
            if (!IsPlacing) return;

            if (InputUtils.WasCancelPressed())
            {
                if (HasCandidate) ClearCandidate();
                else              CancelConveyorMode();
                return;
            }

            TrackTap();
            RefreshHover();
        }

        /// <summary>
        /// Detects a tap (press + release that stayed under the pan threshold and did not start
        /// over UI) and routes it to the active mode. Drags fall through to the camera pan.
        /// </summary>
        private void TrackTap()
        {
            if (InputUtils.WasPointerPressed())
            {
                _pressPos    = InputUtils.GetPointerPosition();
                _dragAccum   = 0f;
                _pressActive = true;

                // Develop-tier trace of why a press over the conveyor bar is/ isn't treated as UI.
                // VerboseLogging stays true across BOTH checks so the bar-check + UIBlock detail
                // log only on the press (not every hover frame).
                UIInputBlocker.VerboseLogging = true;
                bool overBlocker  = UIInputBlocker.IsPointerOverUI(_pressPos);
                bool overConveyor = IsPointerOverConveyorUI?.Invoke(_pressPos) ?? false;
                UIInputBlocker.VerboseLogging = false;
                GameLogger.Develop($"[Conveyor] PRESS at {_pressPos} screen={Screen.width}x{Screen.height} mode={_mode} hasStart={_hasStart} hasCandidate={HasCandidate} overBlocker={overBlocker} overConveyor={overConveyor} -> pressOverUI={(overBlocker || overConveyor)}");

                _pressOverUI = overBlocker || overConveyor;
            }

            if (_pressActive && InputUtils.IsPointerHeld())
            {
                Vector2 cur = InputUtils.GetPointerPosition();
                _dragAccum += Vector2.Distance(cur, _pressPos);
                _pressPos   = cur;
            }

            if (InputUtils.WasPointerReleased())
            {
                bool wasTap = _pressActive && !_pressOverUI && _dragAccum <= CameraController.TapThreshold;
                Vector2 relPos = InputUtils.GetPointerPosition();
                GameLogger.Develop($"[Conveyor] RELEASE at {relPos} pressActive={_pressActive} pressOverUI={_pressOverUI} dragAccum={_dragAccum:F1} -> wasTap={wasTap}");
                _pressActive = false;
                if (wasTap)
                    HandleTap(WorldToCell(relPos));
            }
        }

        private void HandleTap(Vector2Int cell)
        {
            GameLogger.Develop($"[Conveyor] HANDLETAP cell={cell} inBounds={gridRenderer.IsInBounds(cell.x, cell.y)} mode={_mode} hasStart={_hasStart} startCell={_startCell} hasCandidate={HasCandidate}");
            if (!gridRenderer.IsInBounds(cell.x, cell.y)) return;

            if (_mode == Mode.Destroy)
            {
                if (IsConveyorCell(cell) && conveyorPlacer.RemoveSegmentAt(cell.x, cell.y))
                {
                    gridRenderer.ClearDeconstructHover();
                    _hoverCell = new(-1, -1);
                    SaveManager.Instance?.SaveLocal();
                }
                return;
            }

            // ---- Create mode ----
            if (!_hasStart)
            {
                if (!IsValidEndpoint(cell)) return;
                _hasStart  = true;
                _startCell = cell;
                ClearHover();
                MarkStart();
                return;
            }

            // Start already chosen — tapping the start cell again drops a single standalone belt
            // (only on a free cell; re-tapping an existing conveyor stays a no-op).
            if (cell == _startCell)
            {
                if (!IsConveyorCell(_startCell)) BeginSingleCandidate();
                return;
            }

            if (!IsValidEndpoint(cell)) return;

            // Leaving a pending single candidate for a real run — drop its directional preview.
            if (_isSingle) conveyorVisualizer?.ClearSinglePreview();

            _isSingle = false;
            _destCell = cell;
            RecomputePath();
            if (_pathValid)
            {
                HasCandidate = true;
                OnCandidateChanged?.Invoke(true);
            }
        }

        /// <summary>
        /// Forms a single-cell candidate at the start: one standalone belt the player can rotate
        /// (RotatePath) before confirming. Shows a directional belt preview rather than a tile ghost.
        /// </summary>
        private void BeginSingleCandidate()
        {
            _isSingle    = true;
            _singleDir   = OutputDirection.North;
            _destCell    = _startCell;
            _currentPath = new List<Vector2Int> { _startCell };
            _pathValid   = true;

            gridRenderer.ClearConveyorGhost();
            ClearHover();
            conveyorVisualizer?.ShowSinglePreview(_startCell.x, _startCell.y, (int)_singleDir);

            HasCandidate = true;
            OnCandidateChanged?.Invoke(true);
        }

        // ----------------------------------------------------------------
        // Path computation
        // ----------------------------------------------------------------

        private void RecomputePath()
        {
            gridRenderer.ClearConveyorGhost();

            Func<int, int, bool> isFree = IsFreecell;

            // Prefer the chosen single-turn elbow, then the other elbow, then route around.
            List<Vector2Int> path =
                ConveyorPathfinder.BuildOneTurnPath(_startCell, _destCell, _elbowHorizontalFirst, isFree)
                ?? ConveyorPathfinder.BuildOneTurnPath(_startCell, _destCell, !_elbowHorizontalFirst, isFree)
                ?? ConveyorPathfinder.FindRoute(_startCell, _destCell, isFree);

            _currentPath = path ?? new List<Vector2Int>();
            _pathValid   = path != null && path.Count >= 1;

            if (!_pathValid)
            {
                // Unreachable end — keep showing the start marker, drop the candidate.
                if (_hasStart) MarkStart();
                return;
            }

            // Paint ghost tiles; skip existing-conveyor endpoints so they keep their belt visual.
            for (int i = 0; i < _currentPath.Count; i++)
            {
                var c = _currentPath[i];
                bool isExistingEndpoint =
                    ((i == 0) || (i == _currentPath.Count - 1)) && IsConveyorCell(c);
                if (!isExistingEndpoint)
                    gridRenderer.AddConveyorGhostCell(c.x, c.y);
            }
            gridRenderer.PaintConveyorEndpoints(_startCell.x, _startCell.y, _destCell.x, _destCell.y);
        }

        // ----------------------------------------------------------------
        // Hover feedback
        // ----------------------------------------------------------------

        private void RefreshHover()
        {
            // While a candidate ghost is shown, do not add hover highlights on top of it.
            if (HasCandidate) return;

            Vector2 screenPos = InputUtils.GetPointerPosition();
            if (UIInputBlocker.IsPointerOverUI(screenPos)
                || (IsPointerOverConveyorUI?.Invoke(screenPos) ?? false)) { ClearHover(); return; }

            Vector2Int cell = WorldToCell(screenPos);
            if (cell == _hoverCell) return;
            ClearHover();
            _hoverCell = cell;

            if (!gridRenderer.IsInBounds(cell.x, cell.y)) return;

            if (_mode == Mode.Destroy)
            {
                if (IsConveyorCell(cell))
                    gridRenderer.SetDeconstructHover(cell.x, cell.y);
            }
            else if (!_hasStart && IsValidEndpoint(cell))
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

        /// <summary>Highlights the current start cell green (tracked so ClearConveyorGhost restores it).</summary>
        private void MarkStart()
        {
            gridRenderer.ClearConveyorGhost();
            gridRenderer.AddConveyorGhostCell(_startCell.x, _startCell.y);
            gridRenderer.PaintConveyorEndpoints(_startCell.x, _startCell.y, _startCell.x, _startCell.y);
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

        private bool IsConveyorCell(Vector2Int c) =>
            GridOccupancy.Instance != null && GridOccupancy.Instance.IsConveyorCell(c.x, c.y);

        // ----------------------------------------------------------------
        // Reset helpers
        // ----------------------------------------------------------------

        private void ResetAll()
        {
            conveyorVisualizer?.ClearSinglePreview();
            gridRenderer.ClearConveyorGhost();
            gridRenderer.ClearConveyorHoverCell();
            gridRenderer.ClearDeconstructHover();
            _hasStart    = false;
            _currentPath = new List<Vector2Int>();
            _pathValid   = false;
            _isSingle    = false;
            _pressActive = false;
            _dragAccum   = 0f;
            _hoverCell   = new(-1, -1);
            _elbowHorizontalFirst = true;
            if (HasCandidate)
            {
                HasCandidate = false;
                OnCandidateChanged?.Invoke(false);
            }
        }

        private void ClearStartAndCandidate()
        {
            conveyorVisualizer?.ClearSinglePreview();
            gridRenderer.ClearConveyorGhost();
            ClearHover();
            _hasStart    = false;
            _currentPath = new List<Vector2Int>();
            _pathValid   = false;
            _isSingle    = false;
            _elbowHorizontalFirst = true;
            if (HasCandidate)
            {
                HasCandidate = false;
                OnCandidateChanged?.Invoke(false);
            }
        }

        // ----------------------------------------------------------------
        // Grid helper
        // ----------------------------------------------------------------

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
