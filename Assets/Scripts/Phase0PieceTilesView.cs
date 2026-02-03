// Phase0PieceTilesView.cs
// Put under: Assets/Phase0/Scripts/
// Placeholder tile-based piece view.
// Later you can replace visuals with Spine under SpineAnchor.

using System.Collections.Generic;
using UnityEngine;

namespace Phase0
{
    public sealed class Phase0PieceTilesView : MonoBehaviour
    {
        [Header("Tiles (optional placeholder)")]
        public List<Transform> tiles = new();

        public void AutoCollectTiles()
        {
            tiles.Clear();
            foreach (Transform ch in transform)
            {
                if (ch.name.StartsWith("L_Tile_"))
                    tiles.Add(ch);
            }
            tiles.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        public void ApplyLocalCells(Vector2Int[] localCells, int count, float stepX, float stepY)
        {
            if (tiles == null || tiles.Count == 0) AutoCollectTiles();
            if (tiles == null) return;

            int n = Mathf.Min(tiles.Count, count);
            for (int i = 0; i < n; i++)
            {
                var c = localCells[i];
                tiles[i].localPosition = new Vector3(c.x * stepX, c.y * stepY, 0f);
            }
        }

        public void ApplyLocalCells(Vector2Int[] localCells, float stepX, float stepY)
        {
            ApplyLocalCells(localCells, localCells != null ? localCells.Length : 0, stepX, stepY);
        }
    }
}
