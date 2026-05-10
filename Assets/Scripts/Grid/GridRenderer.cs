using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MobileIdleBuilder
{
    /// <summary>
    /// Creates a flat grid of placeholder tile quads in world space on the XZ plane.
    /// The camera should be orthographic, looking straight down (-Y), centred above the grid.
    /// Replace with proper sprite/tilemap art in the Alpha phase.
    /// </summary>
    public class GridRenderer : MonoBehaviour
    {
        [Header("Grid size")]
        [SerializeField] private int   width    = 10;
        [SerializeField] private int   height   = 10;
        [SerializeField] private float cellSize = 1f;

        [Header("Colours")]
        [SerializeField] private Color tileColor             = new Color(0.067f, 0.094f, 0.102f, 1f); // #111827 default tile
        [SerializeField] private Color occupiedColor         = new Color(0f,     0.565f, 0.612f, 1f); // #009090 placed building
        [SerializeField] private Color ghostValidColor       = new Color(0f,     0.898f, 1f,    0.6f); // #00e5ff semi-transparent valid
        [SerializeField] private Color ghostInvalidColor     = new Color(1f,     0.09f,  0.267f, 0.6f); // #ff1744 semi-transparent invalid
        [SerializeField] private Color deconstructHoverColor = new Color(0.9f,   0.15f,  0.15f, 0.9f); // solid danger red
        [SerializeField] private Color fieldHoverColor       = new Color(0.15f,  0.85f,  0.35f, 0.55f); // soft green — interactable field
        [SerializeField] private Color tutorialHighlightColor = new Color(1f,    0.78f,  0.15f, 0.75f); // amber/gold — tutorial focus
        [SerializeField] private Color tutorialHoverColor    = new Color(1f,    0.97f,  0.70f, 1.00f); // bright pale-yellow — hover over tutorial tile

        [Header("Tile Material")]
        [SerializeField] private Material tileMaterial;   // Must be URP Unlit Transparent — assign in Inspector

        [Header("Tile gap (0 = flush, 0.05 = small gap)")]
        [SerializeField] [Range(0f, 0.5f)] private float gap = 0.05f;

        public float CellSize => cellSize;
        public int   Width    => width;
        public int   Height   => height;

        private static readonly Color ConveyorGhostColor    = new Color(1f,    0.5f,  0f,    0.7f); // orange
        private static readonly Color ConveyorEndpointColor = new Color(0.25f, 0.88f, 0.35f, 0.9f); // green

        private GameObject[,]        _tiles;
        private Vector2Int           _ghostCell              = new(-1, -1);
        private readonly Dictionary<Vector2Int, Color> _fieldTileColors = new();
        private readonly HashSet<Vector2Int> _tutorialHighlightCells = new();
        private readonly List<Vector2Int> _ghostCells             = new();
        private readonly List<Vector2Int> _conveyorGhostCells    = new();
        private readonly List<Vector2Int> _deconstructHoverCells = new();
        private Vector2Int               _conveyorHoverCell      = new(-1, -1);
        private Vector2Int               _fieldHoverCell         = new(-1, -1);

        void Awake()  => BuildGrid();
        void Start()
        {
            // Skip if CameraController is driving the camera (it initialises its own position)
            if (Camera.main == null || Camera.main.GetComponent<CameraController>() == null)
                CentreCamera();
        }

        void BuildGrid()
        {
            _tiles = new GameObject[width, height];
            float tileScale = cellSize - gap;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    var tile = GameObject.CreatePrimitive(PrimitiveType.Quad);
                    tile.name = $"Tile_{x}_{y}";
                    tile.transform.SetParent(transform, worldPositionStays: false);
                    tile.transform.localPosition = new Vector3(x * cellSize, 0f, y * cellSize);
                    tile.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    tile.transform.localScale    = new Vector3(tileScale, tileScale, 1f);

                    Destroy(tile.GetComponent<MeshCollider>());

                    var mr = tile.GetComponent<MeshRenderer>();
                    if (tileMaterial != null)
                        mr.sharedMaterial = tileMaterial;
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows    = false;

                    // PresenceReceiver owns colour state so the presence ripple
                    // can be blended on top of whatever the tile's current colour is.
                    var pr = tile.AddComponent<PresenceReceiver>();
                    pr.SetBaseColor(tileColor);

                    _tiles[x, y] = tile;
                }
            }
        }

        private void CentreCamera()
        {
            var cam = Camera.main;
            if (cam == null) return;

            float centreX = (width  - 1) * cellSize * 0.5f;
            float centreZ = (height - 1) * cellSize * 0.5f;

            cam.transform.position = new Vector3(centreX, cam.transform.position.y, centreZ);
            cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            cam.orthographicSize   = (Mathf.Max(width, height) * cellSize * 0.5f) + cellSize;
        }

        public bool IsInBounds(int x, int y) => x >= 0 && x < width && y >= 0 && y < height;

        /// <summary>Marks a cell as permanently occupied (building placed).</summary>
        public void SetTileHighlight(int x, int y, bool highlighted)
        {
            if (!IsInBounds(x, y)) return;
            SetColor(_tiles[x, y].GetComponent<MeshRenderer>(),
                     highlighted ? occupiedColor : tileColor);
        }

        /// <summary>Shows a single-cell ghost. Convenience overload for 1x1 buildings.</summary>
        public void ShowGhost(int x, int y, bool isValid) => ShowGhost(x, y, 1, 1, isValid);

        /// <summary>Shows a ghost footprint of (w x h) cells starting at (x, y). Pass isValid=false for red.</summary>
        public void ShowGhost(int x, int y, int w, int h, bool isValid)
        {
            foreach (var c in _ghostCells)
                RestoreCell(c.x, c.y);
            _ghostCells.Clear();
            _ghostCell = new(-1, -1);

            var color = isValid ? ghostValidColor : ghostInvalidColor;
            for (int dx = 0; dx < w; dx++)
            {
                for (int dy = 0; dy < h; dy++)
                {
                    int cx = x + dx, cy = y + dy;
                    if (!IsInBounds(cx, cy)) continue;
                    SetColor(_tiles[cx, cy].GetComponent<MeshRenderer>(), color);
                    _ghostCells.Add(new Vector2Int(cx, cy));
                }
            }
        }

        /// <summary>Clears all ghost tiles, restoring cells to their normal colour.</summary>
        public void HideGhost()
        {
            foreach (var c in _ghostCells)
                RestoreCell(c.x, c.y);
            _ghostCells.Clear();
            _ghostCell = new(-1, -1);
        }

        // ---- Conveyor ghost helpers ----

        /// <summary>
        /// Highlights a single cell with the conveyor-belt ghost colour without disturbing other ghosts.
        /// Call ClearConveyorGhost() to undo all conveyor highlights.
        /// </summary>
        public void AddConveyorGhostCell(int x, int y)
        {
            if (!IsInBounds(x, y)) return;
            SetColor(_tiles[x, y].GetComponent<MeshRenderer>(), ConveyorGhostColor);
            _conveyorGhostCells.Add(new Vector2Int(x, y));
        }

        /// <summary>Clears all conveyor ghost highlights, restoring each cell to its normal colour.</summary>
        public void ClearConveyorGhost()
        {
            foreach (var c in _conveyorGhostCells)
                RestoreCell(c.x, c.y);
            _conveyorGhostCells.Clear();
        }

        /// <summary>
        /// Highlights the cell under the pointer green before the player starts dragging.
        /// Automatically restores the previous hover cell.
        /// </summary>
        public void SetConveyorHoverCell(int x, int y)
        {
            if (_conveyorHoverCell.x >= 0)
                RestoreCell(_conveyorHoverCell.x, _conveyorHoverCell.y);

            _conveyorHoverCell = new(x, y);
            if (IsInBounds(x, y))
                SetColor(_tiles[x, y].GetComponent<MeshRenderer>(), ConveyorEndpointColor);
        }

        /// <summary>Clears the pre-drag hover highlight.</summary>
        public void ClearConveyorHoverCell()
        {
            if (_conveyorHoverCell.x >= 0)
                RestoreCell(_conveyorHoverCell.x, _conveyorHoverCell.y);
            _conveyorHoverCell = new(-1, -1);
        }

        /// <summary>
        /// Paints the start and end anchor cells green on top of the already-drawn orange path.
        /// The cells must already be tracked in _conveyorGhostCells so ClearConveyorGhost restores them.
        /// </summary>
        public void PaintConveyorEndpoints(int sx, int sy, int ex, int ey)
        {
            if (IsInBounds(sx, sy))
                SetColor(_tiles[sx, sy].GetComponent<MeshRenderer>(), ConveyorEndpointColor);
            if ((ex != sx || ey != sy) && IsInBounds(ex, ey))
                SetColor(_tiles[ex, ey].GetComponent<MeshRenderer>(), ConveyorEndpointColor);
        }

        // ---- Deconstruct hover ----

        /// <summary>
        /// Highlights a building footprint in danger-red to indicate it will be deconstructed.
        /// Call ClearDeconstructHover() first if a previous highlight is active.
        /// </summary>
        public void SetDeconstructHover(int x, int y, int w = 1, int h = 1)
        {
            for (int dx = 0; dx < w; dx++)
            {
                for (int dy = 0; dy < h; dy++)
                {
                    int cx = x + dx, cy = y + dy;
                    if (!IsInBounds(cx, cy)) continue;
                    SetColor(_tiles[cx, cy].GetComponent<MeshRenderer>(), deconstructHoverColor);
                    _deconstructHoverCells.Add(new Vector2Int(cx, cy));
                }
            }
        }

        /// <summary>Clears the deconstruct hover highlight, restoring each cell to its normal colour.</summary>
        public void ClearDeconstructHover()
        {
            foreach (var c in _deconstructHoverCells)
                RestoreCell(c.x, c.y);
            _deconstructHoverCells.Clear();
        }

        // ---- Field hover ----

        /// <summary>
        /// Highlights the cell under the pointer to indicate an interactable field.
        /// Automatically restores the previous hover cell.
        /// </summary>
        public void SetFieldHoverCell(int x, int y)
        {
            if (_fieldHoverCell.x == x && _fieldHoverCell.y == y) return;
            if (_fieldHoverCell.x >= 0)
                RestoreCell(_fieldHoverCell.x, _fieldHoverCell.y);

            _fieldHoverCell = new(x, y);
            if (IsInBounds(x, y))
            {
                bool isTutorialCell = _tutorialHighlightCells.Contains(new Vector2Int(x, y));
                SetColor(_tiles[x, y].GetComponent<MeshRenderer>(),
                         isTutorialCell ? tutorialHoverColor : fieldHoverColor);
            }
        }

        /// <summary>Clears the field hover highlight, restoring the cell to its normal colour.</summary>
        public void ClearFieldHoverCell()
        {
            if (_fieldHoverCell.x < 0) return;
            RestoreCell(_fieldHoverCell.x, _fieldHoverCell.y);
            _fieldHoverCell = new(-1, -1);
        }

        // ---- Field tile colour ----

        /// <summary>
        /// Paints a cell permanently with the field's identity colour.
        /// This sits below tutorial and ghost layers in RestoreCell priority.
        /// </summary>
        public void SetFieldTileColor(int x, int y, Color color)
        {
            if (!IsInBounds(x, y)) return;
            _fieldTileColors[new Vector2Int(x, y)] = color;
            RestoreCell(x, y);
        }

        // ---- Tutorial highlight ----

        /// <summary>
        /// Highlights a rect of cells amber/gold to draw attention during a tutorial step.
        /// Replaces any previously active tutorial highlight. Defaults to 1×1 for fields.
        /// </summary>
        public void SetTutorialHighlightRect(int x, int y, int w = 1, int h = 1)
        {
            ClearTutorialHighlightCell();
            for (int dx = 0; dx < w; dx++)
            for (int dy = 0; dy < h; dy++)
            {
                int cx = x + dx, cy = y + dy;
                if (!IsInBounds(cx, cy)) continue;
                SetColor(_tiles[cx, cy].GetComponent<MeshRenderer>(), tutorialHighlightColor);
                _tutorialHighlightCells.Add(new Vector2Int(cx, cy));
            }
        }

        /// <summary>Clears all tutorial highlight cells, restoring each to its normal colour.</summary>
        public void ClearTutorialHighlightCell()
        {
            foreach (var c in _tutorialHighlightCells)
                RestoreCell(c.x, c.y);
            _tutorialHighlightCells.Clear();
        }

        // ---- Helpers ----

        private void RestoreCell(int x, int y)
        {
            if (!IsInBounds(x, y)) return;
            var tile = _tiles[x, y];
            if (tile == null) return;   // already destroyed (e.g. during scene shutdown)

            var key = new Vector2Int(x, y);
            Color color;
            if (_tutorialHighlightCells.Contains(key))
                color = tutorialHighlightColor;
            else if (GridOccupancy.Instance != null && GridOccupancy.Instance.IsOccupied(x, y))
                color = occupiedColor;
            else if (_fieldTileColors.TryGetValue(key, out var fieldColor))
                color = fieldColor;
            else
                color = tileColor;
            SetColor(tile.GetComponent<MeshRenderer>(), color);
        }

        private static void SetColor(MeshRenderer mr, Color color)
        {
            // Route through PresenceReceiver so the presence ripple is preserved on top
            var pr = mr.GetComponent<PresenceReceiver>();
            if (pr != null) { pr.SetBaseColor(color); return; }

            // Fallback for any renderer without a PresenceReceiver (shouldn't happen in practice)
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", color);
            mr.SetPropertyBlock(mpb);
        }
    }
}
