// Phase0CoreModel.cs
// Put under: Assets/Phase0/Scripts/
// Pure C# core: grid occupancy + shape rotation math.
// No Unity scene refs, no physics, no colliders.
// NOTE: This file is intentionally kept free of MonoBehaviour dependencies.

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Phase0
{
    public static class ShapeRotation
    {
        // Rotate base cells clockwise by 90*k around pivot, then normalize (min x/y -> 0).
        public static Vector2Int[] GetRotatedNormalized(Vector2Int[] baseCells, Vector2Int pivot, int rotationIndexCW)
        {
            rotationIndexCW = Mod4(rotationIndexCW);

            var rotated = new Vector2Int[baseCells.Length];
            for (int i = 0; i < baseCells.Length; i++)
            {
                rotated[i] = RotateCellCW(baseCells[i], pivot, rotationIndexCW);
            }

            // Normalize so that min x,y are 0 (keeps origin anchor consistent).
            int minX = rotated.Min(v => v.x);
            int minY = rotated.Min(v => v.y);

            for (int i = 0; i < rotated.Length; i++)
            {
                rotated[i] = new Vector2Int(rotated[i].x - minX, rotated[i].y - minY);
            }

            return rotated;
        }

        private static int Mod4(int x) => ((x % 4) + 4) % 4;

        // rotationIndexCW: 0,1,2,3 clockwise quarter-turns
        private static Vector2Int RotateCellCW(Vector2Int cell, Vector2Int pivot, int rotationIndexCW)
        {
            var p = cell - pivot;
            Vector2Int r = p;
            // CW: (x,y)->(y,-x)
            for (int i = 0; i < rotationIndexCW; i++)
            {
                r = new Vector2Int(r.y, -r.x);
            }
            return r + pivot;
        }
    }

    public sealed class GridModel
    {
        public readonly int gridSize;

        private readonly HashSet<Vector2Int> _blocked = new();
        private readonly HashSet<Vector2Int> _occupied = new();

        public GridModel(int gridSize)
        {
            this.gridSize = gridSize;
        }

        public void SetBlocked(IEnumerable<Vector2Int> cells)
        {
            _blocked.Clear();
            foreach (var c in cells) _blocked.Add(c);
        }

        public void AddOccupied(IEnumerable<Vector2Int> cells)
        {
            foreach (var c in cells) _occupied.Add(c);
        }

        public void RemoveOccupied(IEnumerable<Vector2Int> cells)
        {
            foreach (var c in cells) _occupied.Remove(c);
        }

        public bool IsInside(Vector2Int c) => c.x >= 0 && c.x < gridSize && c.y >= 0 && c.y < gridSize;

        public bool IsBlockedOrOccupied(Vector2Int c) => _blocked.Contains(c) || _occupied.Contains(c);

        public bool CanPlace(Vector2Int originCell, Vector2Int[] localCells, out Vector2Int firstInvalid)
        {
            for (int i = 0; i < localCells.Length; i++)
            {
                var world = originCell + localCells[i];

                if (!IsInside(world))
                {
                    firstInvalid = world;
                    return false;
                }

                if (IsBlockedOrOccupied(world))
                {
                    firstInvalid = world;
                    return false;
                }
            }

            firstInvalid = default;
            return true;
        }
    }
}
