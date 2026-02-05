// Phase0GlobalGridMapping.cs
// Lattice-aligned global grid mapping (world <-> global cell coords).
// Global grid is aligned to rug lattice to prevent half-cell drift.

using System;
using UnityEngine;

namespace Phase0 {
    [Serializable]
    public sealed class Phase0GlobalGridMapping {
        public int width;
        public int height;

        public Vector2 cellStep;          // world units per cell (x,y) - should match rug
        public Vector2 cell00World;       // center of global Cell(0,0) in world
        public Vector2 worldMin;          // bottom-left corner of global grid in world
        public Rect gridWorldRect;        // world rect covering global grid

        // Lattice alignment fields
        private Vector2Int _globalOriginAligned; // global(0,0) in aligned coords
        private Phase0BoardMapping _rugMapping; // reference to rug lattice

        /// <summary>
        /// Origin of rug in global coordinates: global = rugOriginGlobal + rugLocal
        /// </summary>
        public Vector2Int RugOriginGlobal => AlignedToGlobal(Vector2Int.zero);

        /// <summary>
        /// Initializes a lattice-aligned global grid.
        /// Uses rug mapping as the lattice reference to prevent half-cell drift.
        /// </summary>
        public void InitLatticeAligned(Camera cam, int globalWidth, int globalHeight, Phase0BoardMapping rugMapping) {
            if (cam == null) throw new ArgumentNullException(nameof(cam));
            if (rugMapping == null) throw new ArgumentNullException(nameof(rugMapping));

            _rugMapping = rugMapping;
            width = Mathf.Max(1, globalWidth);
            height = Mathf.Max(1, globalHeight);

            cellStep = rugMapping.cellStep;
            float stepX = Mathf.Abs(cellStep.x) > 0.0001f ? cellStep.x : 1f;
            float stepY = Mathf.Abs(cellStep.y) > 0.0001f ? cellStep.y : 1f;
            cellStep = new Vector2(stepX, stepY);

            // Calculate global bounds: find aligned indices where cell centers fit in camera
            CalculateGlobalBoundsFromCamera(cam);

            float cellSizeX = Mathf.Abs(stepX);
            float cellSizeY = Mathf.Abs(stepY);
            float worldW = cellSizeX * width;
            float worldH = cellSizeY * height;

            cell00World = GlobalCellToWorldCenter(Vector2Int.zero);
            worldMin = cell00World - new Vector2(cellSizeX * 0.5f, cellSizeY * 0.5f);
            gridWorldRect = new Rect(worldMin.x, worldMin.y, worldW, worldH);
        }

        /// <summary>
        /// Calculates global bounds ensuring cell centers fit within camera rect.
        /// Uses Ceil/Floor with epsilon for float safety.
        /// </summary>
        private void CalculateGlobalBoundsFromCamera(Camera cam) {
            // Camera world bounds (orthographic)
            float camHeight = cam.orthographicSize * 2f;
            float camWidth = camHeight * cam.aspect;
            Vector2 camCenter = cam.transform.position;
            Vector2 camMin = camCenter - new Vector2(camWidth * 0.5f, camHeight * 0.5f);
            Vector2 camMax = camCenter + new Vector2(camWidth * 0.5f, camHeight * 0.5f);

            // Half cell size for center-fitting
            float halfCellX = Mathf.Abs(cellStep.x) * 0.5f;
            float halfCellY = Mathf.Abs(cellStep.y) * 0.5f;

            // Find min/max aligned indices where cell centers are fully within camera
            // min + halfCell <= center <= max - halfCell
            const float epsilon = 0.001f;

            Vector2 minCellCenter = camMin + new Vector2(halfCellX, halfCellY);
            Vector2 maxCellCenter = camMax - new Vector2(halfCellX, halfCellY);

            Vector2Int minAligned = _rugMapping.WorldToCellRound(minCellCenter);
            Vector2Int maxAligned = _rugMapping.WorldToCellRound(maxCellCenter);

            // Conservative bounds calculation
            int minX = Mathf.CeilToInt((_rugMapping.WorldToCellFloor(camMin + new Vector2(halfCellX, 0)).x + epsilon));
            int maxX = Mathf.FloorToInt((_rugMapping.WorldToCellFloor(camMax - new Vector2(halfCellX, 0)).x - epsilon));
            int minY = Mathf.CeilToInt((_rugMapping.WorldToCellFloor(camMin + new Vector2(0, halfCellY)).y + epsilon));
            int maxY = Mathf.FloorToInt((_rugMapping.WorldToCellFloor(camMax - new Vector2(0, halfCellY)).y - epsilon));

            // Calculate global origin to align with lattice
            // Choose an aligned origin that puts (0,0) global at a reasonable position
            Vector2Int alignedOrigin = new Vector2Int(minX, minY);
            _globalOriginAligned = alignedOrigin;

            // Recalculate width/height based on actual bounds
            int actualWidth = Math.Max(1, maxX - minX + 1);
            int actualHeight = Math.Max(1, maxY - minY + 1);

            // If requested size is smaller, use requested size but align to lattice
            if (actualWidth > width) {
                // Expand if needed, but keep alignment
                int extra = actualWidth - width;
                _globalOriginAligned = new Vector2Int(minX - extra / 2, _globalOriginAligned.y);
            }
            if (actualHeight > height) {
                int extra = actualHeight - height;
                _globalOriginAligned = new Vector2Int(_globalOriginAligned.x, minY - extra / 2);
            }
        }

        public Vector2 CellToWorldCenter(Vector2Int cell) {
            return cell00World + new Vector2(cell.x * cellStep.x, cell.y * cellStep.y);
        }

        /// <summary>
        /// Cell under point using floor against the grid min bounds.
        /// </summary>
        public Vector2Int WorldToCellFloor(Vector2 world) {
            float dx = (world.x - worldMin.x) / cellStep.x;
            float dy = (world.y - worldMin.y) / cellStep.y;
            return new Vector2Int(Mathf.FloorToInt(dx), Mathf.FloorToInt(dy));
        }

        /// <summary>
        /// Nearest cell center (round).
        /// </summary>
        public Vector2Int WorldToCellRound(Vector2 world) {
            float dx = (world.x - cell00World.x) / cellStep.x;
            float dy = (world.y - cell00World.y) / cellStep.y;
            return new Vector2Int(Mathf.RoundToInt(dx), Mathf.RoundToInt(dy));
        }

        public bool IsInside(Vector2Int cell) {
            return cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height;
        }

        public float DistanceToCellCenter(Vector2 world, Vector2Int cell) {
            return Vector2.Distance(world, CellToWorldCenter(cell));
        }

        /// <summary>
        /// Converts aligned coordinates to global coordinates.
        /// Aligned coords come from rugMapping.WorldToCellRound/CellToWorldCenter.
        /// </summary>
        public Vector2Int AlignedToGlobal(Vector2Int aligned) {
            return new Vector2Int(aligned.x - _globalOriginAligned.x, aligned.y - _globalOriginAligned.y);
        }

        /// <summary>
        /// Converts global coordinates to aligned coordinates.
        /// Aligned coords can be used with rugMapping.CellToWorldCenter.
        /// </summary>
        public Vector2Int GlobalToAligned(Vector2Int global) {
            return new Vector2Int(global.x + _globalOriginAligned.x, global.y + _globalOriginAligned.y);
        }

        /// <summary>
        /// Gets the nearest global cell from world position.
        /// world -> aligned -> global (prevents half-cell drift).
        /// </summary>
        public Vector2Int WorldToGlobalCellRound(Vector2 world) {
            Vector2Int aligned = _rugMapping.WorldToCellRound(world);
            return AlignedToGlobal(aligned);
        }

        /// <summary>
        /// Gets world center of global cell.
        /// global -> aligned -> world center (ensures alignment with rug).
        /// </summary>
        public Vector2 GlobalCellToWorldCenter(Vector2Int global) {
            Vector2Int aligned = GlobalToAligned(global);
            return _rugMapping.CellToWorldCenter(aligned);
        }

        /// <summary>
        /// Clamps a cell to global grid bounds.
        /// </summary>
        public Vector2Int ClampToGlobalBounds(Vector2Int cell) {
            return new Vector2Int(
                Mathf.Clamp(cell.x, 0, width - 1),
                Mathf.Clamp(cell.y, 0, height - 1)
            );
        }
    }
}
