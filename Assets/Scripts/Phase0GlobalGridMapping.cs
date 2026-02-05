// Phase0GlobalGridMapping.cs
// Camera-aligned global grid mapping (world <-> global cell coords).
// The visible rug grid is a subset of this global grid.

using System;
using UnityEngine;

namespace Phase0 {
    [Serializable]
    public sealed class Phase0GlobalGridMapping {
        public int width;
        public int height;

        public Vector2 cellStep;          // world units per cell (x,y)
        public Vector2 cell00World;       // center of global Cell(0,0) in world
        public Vector2 worldMin;          // bottom-left corner of global grid in world
        public Rect gridWorldRect;        // world rect covering global grid

        /// <summary>
        /// Initializes a centered global grid aligned to the camera.
        /// Cell size is taken from the provided cellStep (typically inferred from the rug grid).
        /// </summary>
        public void InitFromCamera(Camera cam, int globalWidth, int globalHeight, Vector2 cellStepWorld) {
            if (cam == null) throw new ArgumentNullException(nameof(cam));

            width = Mathf.Max(1, globalWidth);
            height = Mathf.Max(1, globalHeight);

            cellStep = cellStepWorld;
            float stepX = Mathf.Abs(cellStep.x) > 0.0001f ? cellStep.x : 1f;
            float stepY = Mathf.Abs(cellStep.y) > 0.0001f ? cellStep.y : 1f;
            cellStep = new Vector2(stepX, stepY);

            float cellSizeX = Mathf.Abs(stepX);
            float cellSizeY = Mathf.Abs(stepY);
            float worldW = cellSizeX * width;
            float worldH = cellSizeY * height;

            Vector3 camCenter = cam.transform.position;
            worldMin = new Vector2(camCenter.x - worldW * 0.5f, camCenter.y - worldH * 0.5f);
            cell00World = worldMin + new Vector2(stepX * 0.5f, stepY * 0.5f);
            gridWorldRect = new Rect(worldMin.x, worldMin.y, worldW, worldH);
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
    }
}
