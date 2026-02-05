// IntRect.cs
// Pure C# integer rect for grid-space calculations (no UnityEngine dependency).

using System;

namespace Phase0 {
    /// <summary>
    /// Integer axis-aligned rectangle in grid-space.
    /// Origin is bottom-left (min) corner in cells.
    /// Width/Height are in cells.
    /// </summary>
    public readonly struct IntRect : IEquatable<IntRect> {
        public readonly Int2 origin;
        public readonly int width;
        public readonly int height;

        public int xMin => origin.x;
        public int yMin => origin.y;
        public int xMax => origin.x + width;   // exclusive
        public int yMax => origin.y + height;  // exclusive

        public IntRect(Int2 origin, int width, int height) {
            this.origin = origin;
            this.width = width;
            this.height = height;
        }

        public IntRect(int x, int y, int width, int height) : this(new Int2(x, y), width, height) {
        }

        public bool Contains(Int2 c) {
            return c.x >= xMin && c.x < xMax && c.y >= yMin && c.y < yMax;
        }

        public bool Equals(IntRect other) {
            return origin == other.origin && width == other.width && height == other.height;
        }

        public override bool Equals(object obj) => obj is IntRect other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(origin, width, height);
        public static bool operator ==(IntRect a, IntRect b) => a.Equals(b);
        public static bool operator !=(IntRect a, IntRect b) => !a.Equals(b);

        public override string ToString() => $"IntRect(origin={origin}, w={width}, h={height})";
    }
}
