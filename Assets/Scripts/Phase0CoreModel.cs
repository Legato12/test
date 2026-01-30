// Phase0CoreModel.cs
// Put under: Assets/Phase0/Scripts/
// Pure C# core: grid occupancy + shape rotation math.
// No Unity scene refs, no physics, no colliders.
// NOTE: This file is intentionally kept free of MonoBehaviour dependencies.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Phase0
{
    public readonly struct Int2 : IEquatable<Int2>
    {
        public readonly int x;
        public readonly int y;

        public static readonly Int2 zero = new Int2(0, 0);

        public Int2(int x, int y)
        {
            this.x = x;
            this.y = y;
        }

        public static Int2 operator +(Int2 a, Int2 b) => new Int2(a.x + b.x, a.y + b.y);
        public static Int2 operator -(Int2 a, Int2 b) => new Int2(a.x - b.x, a.y - b.y);

        public bool Equals(Int2 other) => x == other.x && y == other.y;
        public override bool Equals(object obj) => obj is Int2 other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(x, y);

        public static bool operator ==(Int2 left, Int2 right) => left.Equals(right);
        public static bool operator !=(Int2 left, Int2 right) => !left.Equals(right);

        public override string ToString() => $"({x},{y})";
    }

    public static class ShapeRotation
    {
        // Rotate base cells clockwise by 90*k around pivot. Do NOT normalize; allow negative coords.
        public static Int2[] GetRotated(Int2[] baseCells, Int2 pivot, int rotationIndexCW)
        {
            rotationIndexCW = Mod4(rotationIndexCW);

            var rotated = new Int2[baseCells.Length];
            for (int i = 0; i < baseCells.Length; i++)
            {
                rotated[i] = RotateCellCW(baseCells[i], pivot, rotationIndexCW);
            }

            return rotated;
        }

        private static int Mod4(int x) => ((x % 4) + 4) % 4;

        // rotationIndexCW: 0,1,2,3 clockwise quarter-turns
        private static Int2 RotateCellCW(Int2 cell, Int2 pivot, int rotationIndexCW)
        {
            var p = cell - pivot;
            Int2 r = p;
            // CW: (x,y)->(y,-x)
            for (int i = 0; i < rotationIndexCW; i++)
            {
                r = new Int2(r.y, -r.x);
            }
            return r + pivot;
        }
    }

    public sealed class GridModel
    {
        public readonly int gridSize;

        private readonly HashSet<Int2> _blocked = new();
        private readonly HashSet<Int2> _occupied = new();

        public GridModel(int gridSize)
        {
            this.gridSize = gridSize;
        }

        public void SetBlocked(IEnumerable<Int2> cells)
        {
            _blocked.Clear();
            foreach (var c in cells) _blocked.Add(c);
        }

        public void AddOccupied(IEnumerable<Int2> cells)
        {
            foreach (var c in cells) _occupied.Add(c);
        }

        public void RemoveOccupied(IEnumerable<Int2> cells)
        {
            foreach (var c in cells) _occupied.Remove(c);
        }

        public bool IsInside(Int2 c) => c.x >= 0 && c.x < gridSize && c.y >= 0 && c.y < gridSize;

        public bool IsBlockedOrOccupied(Int2 c) => _blocked.Contains(c) || _occupied.Contains(c);

        public bool CanPlace(Int2 originCell, Int2[] localCells, out Int2 firstInvalid)
        {
            for (int i = 0; i < localCells.Length; i++)
            {
                var world = originCell + localCells[i];

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
