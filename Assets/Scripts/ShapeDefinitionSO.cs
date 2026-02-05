// ShapeDefinitionSO.cs
// Runtime ScriptableObject definitions for Phase 0.
// Put this file under: Assets/Phase0/Scripts/ (NOT inside an Editor folder)

using UnityEngine;

namespace Phase0
{
    /// <summary>
    /// Defines a polyomino shape in grid cells using a BASE orientation only.
    /// Rotations are computed at runtime in Pure C# core (per test requirements).
    /// </summary>
    [CreateAssetMenu(menuName = "Phase0/Shape Definition", fileName = "ShapeDefinition")]
    public sealed class ShapeDefinitionSO : ScriptableObject
    {
        [Header("Identity")]
        public string shapeId = "L";

        [Header("Base cells (local)")]
        [Tooltip("Cells in BASE orientation relative to local origin (0,0). Example L: (0,0),(0,1),(0,2),(1,0)")]
        public Vector2Int[] baseCells;

        [Header("Rotation / pivot")]
        [Tooltip("Local pivot used for rotation math in the core. Usually (0,0) for polyominoes.")]
        public Vector2Int pivot = new Vector2Int(0, 0);

        [Header("Optional view hints")]
        [Tooltip("Suggested sprite/Spine visual size in world units (purely a hint for view layer).")]
        public float suggestedWorldScale = 1.0f;
    }

    /// <summary>
    /// Scene-level config for the prototype (grid size, cell size etc.).
    /// This is not gameplay logic; just constants / settings.
    /// </summary>
    [CreateAssetMenu(menuName = "Phase0/Scene Config", fileName = "SceneConfig")]
    public sealed class SceneConfigSO : ScriptableObject
    {
        [Header("Rug (Visible Grid)")]
        [Tooltip("Visible rug/board size in cells. This is NOT a placement restriction.")]
        public int gridSize = 4;              // 4x4
        public float cellSize = 1.0f;
        public float cellGap = 0.06f;

        [Header("Global Grid (Invisible Screen Grid)")]
        [Tooltip("Global grid width in cells (camera-aligned). If <= 0, it will be auto-computed.")]
        public int globalGridWidth = 0;
        [Tooltip("Global grid height in cells (camera-aligned). If <= 0, it will be auto-computed.")]
        public int globalGridHeight = 0;

        [Header("Rug Subset (Global Coords)")]
        [Tooltip("If true, use rugOrigin/rugWidth/rugHeight directly. If false, rug origin is auto-derived from GridRoot position in the global grid.")]
        public bool useRugOverride = false;
        public Vector2Int rugOrigin = new Vector2Int(0, 0);
        [Min(1)] public int rugWidth = 4;
        [Min(1)] public int rugHeight = 4;
        [Header("Board Visuals")]
        public Color baseCellColor = Color.white;
        public Color blockedCellColor = new Color(1f, 0.25f, 0.25f, 1f);
        public Color hoverValidCellColor = new Color(0.35f, 0.75f, 1f, 1f);
        public Color hoverInvalidCellColor = new Color(1f, 0.25f, 0.25f, 1f);
        public Color placedCellColor = new Color(0.25f, 0.9f, 0.35f, 1f);

        [Header("Input")]
        [Tooltip("Tap vs drag threshold in pixels (they suggested ~10 px).")]
        public float tapDragThresholdPx = 10f;

        [Header("Hover")]
        [Tooltip("Minimum time between hovered cell switches (anti-flicker).")]
        [Min(0f)] public float hoverDebounceSeconds = 0.05f;

        [Header("Feel (view layer will use these)")]
        public float snapDuration = 0.12f;
        [Range(0f, 1f)] public float snapOvershootRatio = 0.20f;
        [Min(0f)] public float snapOvershootMax = 0.18f;
        [Range(0.1f, 0.9f)] public float snapOvershootPhase = 0.65f;
        [Range(0.1f, 2f)] public float snapSettleOvershootStrength = 1.0f;
        public float bounceBackDuration = 0.3f;
        [Range(0f, 1f)] public float bounceBackOvershootRatio = 0.12f;
        [Min(0f)] public float bounceBackOvershootMax = 0.12f;
        [Range(0.1f, 0.9f)] public float bounceBackOvershootPhase = 0.55f;
        [Range(0.1f, 2f)] public float bounceBackBounceStrength = 0.9f;

        [Header("Rotation Feel")]
        [Min(0.01f)] public float rotateDuration = 0.12f;
        [Range(0.1f, 2f)] public float rotateOvershootStrength = 1.15f;
    }
}
