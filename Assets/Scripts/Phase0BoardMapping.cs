// Phase0BoardMapping.cs
// Put under: Assets/Phase0/Scripts/
// World-space grid mapping helpers (no gameplay logic).

using System;
using UnityEngine;

namespace Phase0
{
    [Serializable]
    public sealed class Phase0BoardMapping
    {
        public int width = 4;
        public int height = 4;

        public Vector2 cellStep;          // (cellSize + gap, cellSize + gap) inferred from scene
        public Vector2 cell00World;       // center of Cell_0_0 in world

        public Rect gridWorldRect;        // world rect covering full grid footprint

        public bool TryAutoInitFromGridRoot(Transform gridRoot, int expectedWidth = 4, int expectedHeight = 4)
        {
            if (gridRoot == null) return false;

            width = expectedWidth;
            height = expectedHeight;

            var c00 = gridRoot.Find("Cell_0_0") ?? gridRoot.Find("Cell_0_0_BLOCKED");
            var c10 = gridRoot.Find("Cell_1_0") ?? gridRoot.Find("Cell_1_0_BLOCKED");
            var c01 = gridRoot.Find("Cell_0_1") ?? gridRoot.Find("Cell_0_1_BLOCKED");

            if (c00 == null || c10 == null || c01 == null) return false;

            cell00World = c00.position;
            cellStep = new Vector2(
                (c10.position - c00.position).x,
                (c01.position - c00.position).y
            );

            float cellSizeX = Mathf.Abs(cellStep.x);
            float cellSizeY = Mathf.Abs(cellStep.y);
            float minX = cell00World.x - cellSizeX * 0.5f;
            float minY = cell00World.y - cellSizeY * 0.5f;
            float sizeX = cellSizeX * width;
            float sizeY = cellSizeY * height;
            gridWorldRect = new Rect(minX, minY, sizeX, sizeY);

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

        public Vector2Int WorldToCellFloor(Vector2 world)
        {
            var dx = (world.x - cell00World.x) / cellStep.x;
            var dy = (world.y - cell00World.y) / cellStep.y;
            return new Vector2Int(Mathf.FloorToInt(dx), Mathf.FloorToInt(dy));
        }

        public bool IsInsideGrid(Vector2Int cell)
        {
            return cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height;
        }

        public bool IsInsideGridRect(Vector2 world)
        {
            return world.x >= gridWorldRect.xMin && world.x <= gridWorldRect.xMax
                && world.y >= gridWorldRect.yMin && world.y <= gridWorldRect.yMax;
        }

        public bool IsInsideGridRectHysteresis(Vector2 world, bool wasInside, float hysteresisWorld)
        {
            if (hysteresisWorld <= 0f) return IsInsideGridRect(world);

            Rect rect = gridWorldRect;
            if (wasInside)
            {
                rect = new Rect(
                    rect.xMin - hysteresisWorld,
                    rect.yMin - hysteresisWorld,
                    rect.width + hysteresisWorld * 2f,
                    rect.height + hysteresisWorld * 2f);
            }
            else
            {
                rect = new Rect(
                    rect.xMin + hysteresisWorld,
                    rect.yMin + hysteresisWorld,
                    rect.width - hysteresisWorld * 2f,
                    rect.height - hysteresisWorld * 2f);

                if (rect.width <= 0f || rect.height <= 0f)
                    return IsInsideGridRect(world);
            }

            return world.x >= rect.xMin && world.x <= rect.xMax
                && world.y >= rect.yMin && world.y <= rect.yMax;
        }

        public float DistanceToCellCenter(Vector2 world, Vector2Int cell)
        {
            return Vector2.Distance(world, CellToWorldCenter(cell));
        }
    }
}
