// Phase0BoardMapping.cs
// Put under: Assets/Phase0/Scripts/
// World-space grid mapping helpers (no gameplay logic).

using System;
using System.Linq;
using UnityEngine;

namespace Phase0
{
    [Serializable]
    public sealed class Phase0BoardMapping
    {
        public int gridSize = 4;

        public Vector2 cellStep;          // (cellSize + gap, cellSize + gap) inferred from scene
        public Vector2 cell00World;       // center of Cell_0_0 in world

        public bool TryAutoInitFromGridRoot(Transform gridRoot, int expectedGridSize = 4)
        {
            if (gridRoot == null) return false;

            gridSize = expectedGridSize;

            var c00 = gridRoot.Find("Cell_0_0") ?? gridRoot.Find("Cell_0_0_BLOCKED");
            var c10 = gridRoot.Find("Cell_1_0") ?? gridRoot.Find("Cell_1_0_BLOCKED");
            var c01 = gridRoot.Find("Cell_0_1") ?? gridRoot.Find("Cell_0_1_BLOCKED");

            if (c00 == null || c10 == null || c01 == null) return false;

            cell00World = c00.position;
            cellStep = new Vector2(
                (c10.position - c00.position).x,
                (c01.position - c00.position).y
            );

            return true;
        }

        public Vector2 CellToWorldCenter(Vector2Int cell)
        {
            return cell00World + new Vector2(cell.x * cellStep.x, cell.y * cellStep.y);
        }

        public Vector2Int WorldToCellRound(Vector2 world)
        {
            var dx = (world.x - cell00World.x) / cellStep.x;
            var dy = (world.y - cell00World.y) / cellStep.y;
            return new Vector2Int(Mathf.RoundToInt(dx), Mathf.RoundToInt(dy));
        }

        public bool IsInsideGrid(Vector2Int cell)
        {
            return cell.x >= 0 && cell.x < gridSize && cell.y >= 0 && cell.y < gridSize;
        }

        public float DistanceToCellCenter(Vector2 world, Vector2Int cell)
        {
            return Vector2.Distance(world, CellToWorldCenter(cell));
        }
    }
}
