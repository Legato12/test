// Phase0BoardPlacedHighlightView.cs
// Placed-on-board outline highlight view (no gameplay logic).

using System.Collections.Generic;
using UnityEngine;

namespace Phase0
{
    public sealed class Phase0BoardPlacedHighlightView : MonoBehaviour
    {
        [Header("Visual")]
        public Sprite outlineSprite;
        public string sortingLayer = "Ghost";
        public int sortingOrder = 40;

        [Header("Pool")]
        [Min(1)] public int maxTiles = 16;

        private readonly List<SpriteRenderer> _tiles = new();

        public void SetCells(Vector2Int[] cells, Phase0BoardMapping mapping, float cellSize, Color color)
        {
            if (cells == null || cells.Length == 0)
            {
                Clear();
                return;
            }

            int count = Mathf.Min(cells.Length, maxTiles);
            EnsureTiles(count, cellSize);

            for (int i = 0; i < count; i++)
            {
                var world = mapping.CellToWorldCenter(cells[i]);
                var tile = _tiles[i];
                tile.transform.position = new Vector3(world.x, world.y, 0f);
                tile.color = color;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < _tiles.Count; i++)
            {
                if (_tiles[i] != null)
                    _tiles[i].gameObject.SetActive(false);
            }
        }

        private void EnsureTiles(int count, float cellSize)
        {
            while (_tiles.Count < count)
            {
                var go = new GameObject($"PlacedTile_{_tiles.Count}");
                go.transform.SetParent(transform, false);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = outlineSprite;
                sr.drawMode = SpriteDrawMode.Sliced;
                sr.size = new Vector2(cellSize, cellSize);
                sr.sortingLayerName = sortingLayer;
                sr.sortingOrder = sortingOrder;

                _tiles.Add(sr);
            }

            for (int i = 0; i < _tiles.Count; i++)
            {
                _tiles[i].gameObject.SetActive(i < count);
                if (i < count)
                {
                    _tiles[i].size = new Vector2(cellSize, cellSize);
                }
            }
        }
    }
}