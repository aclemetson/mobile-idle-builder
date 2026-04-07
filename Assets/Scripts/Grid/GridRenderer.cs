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
        [SerializeField] private Color tileColor      = new Color(0.067f, 0.094f, 0.102f, 1f); // #111827 default tile
        [SerializeField] private Color occupiedColor  = new Color(0f,     0.565f, 0.612f, 1f); // #009090 placed building
        [SerializeField] private Color ghostValidColor = new Color(0f,    0.898f, 1f,    0.6f); // #00e5ff semi-transparent valid
        [SerializeField] private Color ghostInvalidColor = new Color(1f,  0.09f,  0.267f, 0.6f); // #ff1744 semi-transparent invalid

        [Header("Tile gap (0 = flush, 0.05 = small gap)")]
        [SerializeField] [Range(0f, 0.5f)] private float gap = 0.05f;

        public float CellSize => cellSize;
        public int   Width    => width;
        public int   Height   => height;

        private GameObject[,] _tiles;
        private Vector2Int    _ghostCell = new(-1, -1);

        void Awake()  => BuildGrid();
        void Start()  => CentreCamera();

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
                    mr.shadowCastingMode = ShadowCastingMode.Off;
                    mr.receiveShadows    = false;

                    SetColor(mr, tileColor);
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

        /// <summary>Shows a ghost tile at the given cell. Pass isValid=false for red (occupied/OOB).</summary>
        public void ShowGhost(int x, int y, bool isValid)
        {
            // Clear previous ghost if it moved
            if (_ghostCell.x >= 0)
                RestoreCell(_ghostCell.x, _ghostCell.y);

            if (!IsInBounds(x, y)) { _ghostCell = new(-1, -1); return; }

            SetColor(_tiles[x, y].GetComponent<MeshRenderer>(),
                     isValid ? ghostValidColor : ghostInvalidColor);
            _ghostCell = new(x, y);
        }

        /// <summary>Clears the ghost tile, restoring the cell to its normal colour.</summary>
        public void HideGhost()
        {
            if (_ghostCell.x < 0) return;
            RestoreCell(_ghostCell.x, _ghostCell.y);
            _ghostCell = new(-1, -1);
        }

        // ---- Helpers ----

        private void RestoreCell(int x, int y)
        {
            if (!IsInBounds(x, y)) return;
            bool occupied = GridOccupancy.Instance != null && GridOccupancy.Instance.IsOccupied(x, y);
            SetColor(_tiles[x, y].GetComponent<MeshRenderer>(),
                     occupied ? occupiedColor : tileColor);
        }

        private static void SetColor(MeshRenderer mr, Color color)
        {
            var mpb = new MaterialPropertyBlock();
            mpb.SetColor("_BaseColor", color);
            mr.SetPropertyBlock(mpb);
        }
    }
}
