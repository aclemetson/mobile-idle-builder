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
        private static readonly Color PowerCoverageColor    = new Color(0.25f, 0.6f,  1f,    0.45f); // blue — power radius

        private GameObject[,]        _tiles;
        private Vector2Int           _ghostCell              = new(-1, -1);
        private readonly Dictionary<Vector2Int, Color> _fieldTileColors = new();
        private readonly HashSet<Vector2Int> _tutorialHighlightCells = new();
        private readonly List<Vector2Int> _ghostCells             = new();
        private readonly List<Vector2Int> _conveyorGhostCells    = new();
        private readonly List<Vector2Int> _deconstructHoverCells = new();
        private readonly List<Vector2Int> _powerCoverageCells    = new();
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

        /// <summary>
        /// Maps a world-space ground point to the grid cell whose CENTER is nearest.
        /// Tiles are rendered centered at (x*cellSize, z*cellSize) (see BuildGrid), so cell
        /// boundaries fall at (x ± 0.5) * cellSize — this is round-to-nearest, NOT floor.
        /// Plain FloorToInt(world/cellSize) selects the cell a half-tile down-left of the point,
        /// which is the classic "touch is a bit off" placement bug. All screen->cell call sites
        /// must go through this helper so the convention can never diverge again.
        /// </summary>
        public static Vector2Int WorldToCell(Vector3 world, float cellSize) =>
            new(Mathf.FloorToInt(world.x / cellSize + 0.5f),
                Mathf.FloorToInt(world.z / cellSize + 0.5f));

        /// <summary>Marks a cell as permanently occupied (building placed).</summary>
        public void SetTileHighlight(int x, int y, bool highlighted)
        {
            if (!IsInBounds(x, y)) return;
            if (highlighted)
                SetColor(_tiles[x, y].GetComponent<MeshRenderer>(), occupiedColor);
            else
                RestoreCell(x, y);
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

        /// <summary>Clears all ghost tiles (and any power-coverage preview), restoring cells to normal.</summary>
        public void HideGhost()
        {
            foreach (var c in _ghostCells)
                RestoreCell(c.x, c.y);
            _ghostCells.Clear();
            _ghostCell = new(-1, -1);
            ClearPowerCoverage();
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

        // ---- Power coverage ----

        /// <summary>
        /// Tints every cell within <paramref name="radius"/> tiles (Euclidean edge-to-edge gap) of the
        /// footprint rect (gridX, gridY, w, h) with the power-coverage colour. Mirrors the connection
        /// test in PowerGridSystem so the highlighted area equals the powered area. Replaces any
        /// previous coverage highlight.
        /// </summary>
        public void ShowPowerCoverage(int gridX, int gridY, int w, int h, float radius)
        {
            ClearPowerCoverage();
            if (radius <= 0f) return;

            int rad  = Mathf.CeilToInt(radius);
            int maxX = gridX + w - 1;
            int maxY = gridY + h - 1;
            for (int cx = gridX - rad; cx <= maxX + rad; cx++)
            for (int cy = gridY - rad; cy <= maxY + rad; cy++)
            {
                if (!IsInBounds(cx, cy)) continue;
                int gapX = Mathf.Max(0, Mathf.Max(gridX - cx, cx - maxX));
                int gapY = Mathf.Max(0, Mathf.Max(gridY - cy, cy - maxY));
                if (gapX * gapX + gapY * gapY > radius * radius) continue;
                SetColor(_tiles[cx, cy].GetComponent<MeshRenderer>(), PowerCoverageColor);
                _powerCoverageCells.Add(new Vector2Int(cx, cy));
            }
        }

        /// <summary>Clears the power-coverage highlight, restoring each cell to its normal colour.</summary>
        public void ClearPowerCoverage()
        {
            foreach (var c in _powerCoverageCells)
                RestoreCell(c.x, c.y);
            _powerCoverageCells.Clear();
        }

        // ---- Field tile colour ----

        /// <summary>How far the field tile is tinted from the base tile colour toward the field
        /// colour (0 = plain tile, 1 = full field colour). Kept low so the wire-mesh overlay is the
        /// dominant visual and the tile only hints at the field type.</summary>
        private const float FieldTileTintStrength = 0.22f;

        /// <summary>
        /// Paints a cell with a muted hint of the field's identity colour.
        /// This sits below tutorial and ghost layers in RestoreCell priority.
        /// </summary>
        public void SetFieldTileColor(int x, int y, Color color)
        {
            if (!IsInBounds(x, y)) return;
            _fieldTileColors[new Vector2Int(x, y)] = Color.Lerp(tileColor, color, FieldTileTintStrength);
            RestoreCell(x, y);
        }

        public void ClearFieldTileColor(int x, int y)
        {
            if (_fieldTileColors.Remove(new Vector2Int(x, y)))
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
