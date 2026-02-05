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
}
