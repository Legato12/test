// Phase0GhostTilesView.cs
// Put under: Assets/Phase0/Scripts/
// Simple ghost footprint visualization (no gameplay logic).

using System.Collections.Generic;
using UnityEngine;

namespace Phase0
{
    public sealed class Phase0GhostTilesView : MonoBehaviour
    {
        [Header("Visual")]
        public Sprite tileSprite;
        public Sprite outlineSprite;
        public string sortingLayer = "Ghost";
        public int sortingOrder = 50;

        private readonly List<SpriteRenderer> _tiles = new();

        public void EnsureTiles(int count, float cellSize)
        {
            // Disable any renderer on the root itself (we use children tiles).
            var rootSr = GetComponent<SpriteRenderer>();
            if (rootSr != null) rootSr.enabled = false;

            while (_tiles.Count < count)
            {
                var go = new GameObject($"GhostTile_{_tiles.Count}");
                go.transform.SetParent(transform, false);

                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = outlineSprite != null ? outlineSprite : tileSprite;
                sr.drawMode = SpriteDrawMode.Sliced;
                sr.size = new Vector2(cellSize, cellSize);
                sr.sortingLayerName = sortingLayer;
                sr.sortingOrder = sortingOrder;

                _tiles.Add(sr);
            }

            for (int i = 0; i < _tiles.Count; i++)
            {
                _tiles[i].gameObject.SetActive(i < count);
            }
        }

        public void ApplyLocalCells(Vector2Int[] localCells, int count, float stepX, float stepY)
        {
            EnsureTiles(count, Mathf.Max(Mathf.Abs(stepX), Mathf.Abs(stepY)));
            for (int i = 0; i < count; i++)
            {
                var c = localCells[i];
                _tiles[i].transform.localPosition = new Vector3(c.x * stepX, c.y * stepY, 0f);
                _tiles[i].enabled = true;
            }

            for (int i = count; i < _tiles.Count; i++)
            {
                _tiles[i].enabled = false;
            }
        }

        public void ApplyLocalCells(Vector2Int[] localCells, float stepX, float stepY)
        {
            ApplyLocalCells(localCells, localCells != null ? localCells.Length : 0, stepX, stepY);
        }

        public void SetVisible(bool visible)
        {
            gameObject.SetActive(visible);
        }

        public void SetColor(Color c)
        {
            for (int i = 0; i < _tiles.Count; i++)
            {
                if (_tiles[i] != null) _tiles[i].color = c;
            }
        }
    }
}
