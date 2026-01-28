// Phase0GameController.cs
// Put under: Assets/Phase0/Scripts/
// MonoBehaviour that wires input -> core model -> view.
// Pure C# core is in Phase0CoreModel.cs.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Phase0
{
    public sealed class Phase0GameController : MonoBehaviour
    {
        [Header("Config Assets")]
        public SceneConfigSO sceneConfig;
        public ShapeDefinitionSO shapeDefinition;

        [Header("Scene Refs (auto-found if empty)")]
        public Camera mainCamera;
        public Transform gridRoot;
        public Transform holdingArea;          // origin area (piece start)
        public Transform activePieceRoot;      // "ActivePiece_L"
        public Transform dummyPiece;           // "DummyPiece_Occupied"
        public Phase0GhostTilesView ghostView;

        [Header("Feel (no external tween libs)")]
        public float followSmoothTime = 0.045f;   // spring-follow feel while dragging

        [Header("Spine Offset (per rotation)")]
        [Tooltip("Local offsets for Spine child per CW rotation index (0-3).")]
        public Vector2[] spineOffsets = new Vector2[4];

        [Header("Hysteresis")]
        [Tooltip("How far finger must move away from current cell center (in world units) before we switch to a neighbor cell.")]
        public float cellSwitchHysteresisWorld = 0.32f;

        private readonly Phase0BoardMapping _mapping = new();
        private GridModel _grid;

        // Placement state
        private int _rotationCW;
        private Vector2Int[] _localCells;           // rotated+normalized cells

        private bool _held;
        private bool _movedBeyondThreshold;
        private Vector2 _downScreenPos;
        private Vector3 _pieceOriginBeforeDrag;
        private Vector3 _velocity;                  // SmoothDamp velocity
        private Vector3 _dragOffsetWorld;

        private Vector2Int _candidateOriginCell;
        private bool _hasCandidate;
        private bool _candidateValid;

        private Vector2Int _lastPlacedOriginCell;
        private Vector2Int[] _lastPlacedWorldCells; // cells currently occupying grid
        private bool _isPlacedOnBoard;

        private void Awake()
        {
            AutoFindRefs();
        }

        private void Start()
        {
            if (mainCamera == null) mainCamera = Camera.main;

            if (sceneConfig == null)
            {
                // Optional: try load default config asset by name
                sceneConfig = Resources.FindObjectsOfTypeAll<SceneConfigSO>().FirstOrDefault();
            }

            if (shapeDefinition == null)
            {
                shapeDefinition = Resources.FindObjectsOfTypeAll<ShapeDefinitionSO>().FirstOrDefault();
            }

            int gridSize = sceneConfig != null ? sceneConfig.gridSize : 4;

            if (!_mapping.TryAutoInitFromGridRoot(gridRoot, gridSize))
            {
                Debug.LogError("Phase0GameController: Cannot init mapping from GridRoot. Ensure Cell_0_0 exists.");
                enabled = false;
                return;
            }

            _grid = new GridModel(gridSize);

            // Blocked cells: parse by name suffix "_BLOCKED"
            var blocked = new List<Vector2Int>();
            foreach (Transform cell in gridRoot)
            {
                if (!cell.name.Contains("Cell_")) continue;
                if (!cell.name.Contains("_BLOCKED")) continue;

                if (TryParseCellName(cell.name, out var coord))
                    blocked.Add(coord);
            }
            _grid.SetBlocked(blocked);

            // Occupied: dummy piece occupies exactly 1 cell (as per requirement).
            if (dummyPiece != null)
            {
                var dummyCell = _mapping.WorldToCellRound(dummyPiece.position);
                if (_mapping.IsInsideGrid(dummyCell))
                    _grid.AddOccupied(new[] { dummyCell });
            }

            // Init rotation + cells
            RecomputeLocalCells();

            // Init views
            var pieceTiles = activePieceRoot != null ? activePieceRoot.GetComponent<Phase0PieceTilesView>() : null;
            if (pieceTiles != null)
            {
                pieceTiles.AutoCollectTiles();
                pieceTiles.ApplyLocalCells(_localCells, _mapping.cellStep.x, _mapping.cellStep.y);
            }

            if (ghostView != null)
            {
                // Find sprite from one tile (placeholder) if none set
                if (ghostView.tileSprite == null)
                {
                    var anyTile = activePieceRoot.GetComponentsInChildren<SpriteRenderer>().FirstOrDefault();
                    if (anyTile != null) ghostView.tileSprite = anyTile.sprite;
                }

                ghostView.EnsureTiles(_localCells.Length, sceneConfig != null ? sceneConfig.cellSize : 1f);
                ghostView.SetVisible(false);
            }

            // If piece starts off-board, treat as not placed.
            _isPlacedOnBoard = false;
        }

        private void Update()
        {
            if (mainCamera == null) return;
            if (activePieceRoot == null) return;

            var pointer = PointerState.Get(mainCamera);
            if (pointer.down)
            {
                OnPointerDown(pointer);
            }

            if (_held && pointer.held)
            {
                OnPointerHeld(pointer);
            }

            if (_held && pointer.up)
            {
                OnPointerUp(pointer);
            }
        }

        private void OnPointerDown(PointerState pointer)
        {
            _held = true;
            _movedBeyondThreshold = false;
            _downScreenPos = pointer.screenPos;

            _pieceOriginBeforeDrag = activePieceRoot.position;

            // When picking up from board, clear occupied cells temporarily (so we can re-place)
            if (_isPlacedOnBoard && _lastPlacedWorldCells != null)
            {
                _grid.RemoveOccupied(_lastPlacedWorldCells);
            }

            // Compute drag offset so it doesn't jump
            var w = pointer.worldPos;
            _dragOffsetWorld = activePieceRoot.position - new Vector3(w.x, w.y, 0f);

            // Candidate init
            _hasCandidate = false;
            if (ghostView != null) ghostView.SetVisible(false);
        }

        private void OnPointerHeld(PointerState pointer)
        {
            // Decide tap vs drag based on movement threshold
            float thresholdPx = sceneConfig != null ? sceneConfig.tapDragThresholdPx : 10f;
            if (!_movedBeyondThreshold)
            {
                if ((pointer.screenPos - _downScreenPos).magnitude > thresholdPx)
                    _movedBeyondThreshold = true;
            }

            // Direct manipulation: once movement begins, piece follows finger with spring delay.
            if (_movedBeyondThreshold)
            {
                Vector3 target = new Vector3(pointer.worldPos.x, pointer.worldPos.y, 0f) + _dragOffsetWorld;
                target = ClampToCameraBounds(target, mainCamera, paddingWorld: 0.3f);

                activePieceRoot.position = Vector3.SmoothDamp(
                    activePieceRoot.position,
                    target,
                    ref _velocity,
                    followSmoothTime
                );

                UpdateCandidateAndGhost(pointer.worldPos);
            }
        }

        private void OnPointerUp(PointerState pointer)
        {
            bool wasTap = !_movedBeyondThreshold;

            _held = false;
            _velocity = Vector3.zero;

            if (wasTap)
            {
                // Tap: rotate only when stationary. Do NOT allow rotate while already placed on the board.
                if (!_isPlacedOnBoard)
                {
                    RotateCW();
                }
                if (ghostView != null) ghostView.SetVisible(false);

                // If the piece was previously placed, keep it placed (no movement)
                // and re-occupy cells.
                if (_isPlacedOnBoard && _lastPlacedWorldCells != null)
                {
                    _grid.AddOccupied(_lastPlacedWorldCells);
                }
                return;
            }

            // Drag drop: valid only if candidate is inside grid and CanPlace == true.
            if (_hasCandidate && _candidateValid)
            {
                // Snap to candidate origin cell
                Vector3 snapPos = _mapping.CellToWorldCenter(_candidateOriginCell);

                // Animate with simple overshoot (without external libs)
                StartCoroutine(TweenOvershoot(activePieceRoot, activePieceRoot.position, snapPos, sceneConfig != null ? sceneConfig.snapDuration : 0.12f));

                // Mark occupied cells
                var worldCells = _localCells.Select(c => _candidateOriginCell + c).ToArray();
                _grid.AddOccupied(worldCells);

                _lastPlacedOriginCell = _candidateOriginCell;
                _lastPlacedWorldCells = worldCells;
                _isPlacedOnBoard = true;
            }
            else
            {
                // If we have a candidate (inside grid), invalid = bounce back to origin-before-drag (requirement)
                if (_hasCandidate)
                {
                    StartCoroutine(TweenOvershoot(activePieceRoot, activePieceRoot.position, _pieceOriginBeforeDrag, sceneConfig != null ? sceneConfig.bounceBackDuration : 0.16f));

                    // Re-occupy original cells if it was placed before
                    if (_isPlacedOnBoard && _lastPlacedWorldCells != null)
                    {
                        _grid.AddOccupied(_lastPlacedWorldCells);
                    }
                }
                else
                {
                    // Outside-grid drop: allow "parking" outside the grid anywhere on-screen,
                    // as long as the piece does not overlap the grid area at all.
                    // If it overlaps the grid, push it just outside (small gap) and tween there.
                    var current = activePieceRoot.position;

                    if (DoesPieceOverlapGrid(current, out var pushedTarget))
                    {
                        StartCoroutine(TweenOvershoot(activePieceRoot, current, pushedTarget, sceneConfig != null ? sceneConfig.bounceBackDuration : 0.16f));
                    }
                    else
                    {
                        // Valid outside placement: keep where released; tiny settle for feel
                        StartCoroutine(TweenOvershoot(activePieceRoot, current, current, 0.10f));
                    }

                    _isPlacedOnBoard = false;
                    _lastPlacedWorldCells = null;
                }
            }

            if (ghostView != null) ghostView.SetVisible(false);
        }

        private void UpdateCandidateAndGhost(Vector2 pointerWorld)
        {
            // Outside grid is allowed; off-screen is clamped earlier.
            var approxCell = _mapping.WorldToCellRound(pointerWorld);
            bool inside = _mapping.IsInsideGrid(approxCell);

            if (!inside)
            {
                _hasCandidate = false;
                if (ghostView != null) ghostView.SetVisible(false);
                return;
            }

            // Hysteresis: only switch cell if far enough from current candidate center.
            if (_hasCandidate)
            {
                float d = _mapping.DistanceToCellCenter(pointerWorld, _candidateOriginCell);
                if (d < cellSwitchHysteresisWorld)
                {
                    // keep candidate
                }
                else
                {
                    _candidateOriginCell = approxCell;
                }
            }
            else
            {
                _candidateOriginCell = approxCell;
                _hasCandidate = true;
            }

            // Validate placement at candidate
            _candidateValid = _grid.CanPlace(_candidateOriginCell, _localCells, out _);

            // Ghost view
            if (ghostView != null)
            {
                ghostView.SetVisible(true);
                ghostView.transform.position = _mapping.CellToWorldCenter(_candidateOriginCell);

                // Apply footprint
                ghostView.EnsureTiles(_localCells.Length, sceneConfig != null ? sceneConfig.cellSize : 1f);
                ghostView.ApplyLocalCells(_localCells, _mapping.cellStep.x, _mapping.cellStep.y);

                if (_candidateValid)
                {
                    ghostView.SetColor(new Color(0.25f, 1f, 0.55f, 0.35f));
                }
                else
                {
                    ghostView.SetColor(new Color(1f, 0.2f, 0.25f, 0.35f));
                }
            }
        }

        private void RotateCW()
        {
            _rotationCW = (_rotationCW + 1) & 3;
            RecomputeLocalCells();

            // Update placeholder tiles layout (not rotating transform)
            var pieceTiles = activePieceRoot.GetComponent<Phase0PieceTilesView>();
            if (pieceTiles != null)
            {
                pieceTiles.ApplyLocalCells(_localCells, _mapping.cellStep.x, _mapping.cellStep.y);
            }

            // Visual rotation: rotate the SpineAnchor and apply per-rotation offset to the Spine child.
            if (activePieceRoot != null)
            {
                var spineAnchor = activePieceRoot.Find("SpineAnchor");
                if (spineAnchor != null)
                {
                    float angle = -90f * _rotationCW;
                    spineAnchor.localRotation = Quaternion.Euler(0f, 0f, angle);

                    if (spineAnchor.childCount > 0 && spineOffsets != null && spineOffsets.Length >= 4)
                    {
                        var spineChild = spineAnchor.GetChild(0);
                        var offset = spineOffsets[_rotationCW];
                        spineChild.localPosition = new Vector3(offset.x, offset.y, spineChild.localPosition.z);
                    }
                }
            }
        }

        private void RecomputeLocalCells()
        {
            if (shapeDefinition == null || shapeDefinition.baseCells == null || shapeDefinition.baseCells.Length == 0)
            {
                _localCells = new[] { Vector2Int.zero };
                return;
            }

            _localCells = ShapeRotation.GetRotatedNormalized(shapeDefinition.baseCells, shapeDefinition.pivot, _rotationCW);
        }

        private void AutoFindRefs()
        {
            if (mainCamera == null) mainCamera = Camera.main;

            var root = GameObject.Find("Phase0_Root");
            if (root == null) return;

            if (gridRoot == null)
            {
                var t = root.transform.Find("BoardRoot/GridRoot");
                if (t != null) gridRoot = t;
            }

            if (holdingArea == null)
            {
                var t = root.transform.Find("HoldingArea");
                if (t != null) holdingArea = t;
            }

            if (activePieceRoot == null)
            {
                var t = root.transform.Find("HoldingArea/ActivePiece_L");
                if (t != null) activePieceRoot = t;
            }

            if (dummyPiece == null)
            {
                var t = root.transform.Find("BoardRoot/DummyPiece_Occupied");
                if (t != null) dummyPiece = t;
            }

            if (ghostView == null)
            {
                var g = root.transform.Find("GhostPreview");
                if (g != null)
                {
                    ghostView = g.GetComponent<Phase0GhostTilesView>();
                    if (ghostView == null) ghostView = g.gameObject.AddComponent<Phase0GhostTilesView>();
                }
            }

            // Ensure piece has placeholder view component
            if (activePieceRoot != null)
            {
                var pv = activePieceRoot.GetComponent<Phase0PieceTilesView>();
                if (pv == null) pv = activePieceRoot.gameObject.AddComponent<Phase0PieceTilesView>();
                pv.AutoCollectTiles();
            }
        }

        private static bool TryParseCellName(string name, out Vector2Int cell)
        {
            // Expected: Cell_X_Y or Cell_X_Y_BLOCKED
            cell = default;

            var parts = name.Split('_');
            if (parts.Length < 3) return false;
            if (!parts[0].StartsWith("Cell")) return false;

            if (int.TryParse(parts[1], out int x) && int.TryParse(parts[2], out int y))
            {
                cell = new Vector2Int(x, y);
                return true;
            }

            return false;
        }

        private static Vector3 ClampToCameraBounds(Vector3 world, Camera cam, float paddingWorld)
        {
            // Orthographic bounds
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;

            float minX = cam.transform.position.x - halfW + paddingWorld;
            float maxX = cam.transform.position.x + halfW - paddingWorld;
            float minY = cam.transform.position.y - halfH + paddingWorld;
            float maxY = cam.transform.position.y + halfH - paddingWorld;

            world.x = Mathf.Clamp(world.x, minX, maxX);
            world.y = Mathf.Clamp(world.y, minY, maxY);
            world.z = 0f;

            return world;
        }

        
        private bool DoesPieceOverlapGrid(Vector3 piecePos, out Vector3 pushedTarget)
        {
            pushedTarget = piecePos;

            float cellSize = sceneConfig != null ? sceneConfig.cellSize : 1f;

            // Grid rect in world (inclusive of cell extents)
            float gridMinX = _mapping.cell00World.x - cellSize * 0.5f;
            float gridMinY = _mapping.cell00World.y - cellSize * 0.5f;
            float gridMaxX = _mapping.cell00World.x + (_mapping.gridSize - 1) * _mapping.cellStep.x + cellSize * 0.5f;
            float gridMaxY = _mapping.cell00World.y + (_mapping.gridSize - 1) * _mapping.cellStep.y + cellSize * 0.5f;

            // Piece AABB from footprint tiles (based on localCells)
            float minX = float.PositiveInfinity, minY = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity;

            for (int i = 0; i < _localCells.Length; i++)
            {
                var c = _localCells[i];
                float cx = piecePos.x + c.x * _mapping.cellStep.x;
                float cy = piecePos.y + c.y * _mapping.cellStep.y;

                minX = Mathf.Min(minX, cx - cellSize * 0.5f);
                minY = Mathf.Min(minY, cy - cellSize * 0.5f);
                maxX = Mathf.Max(maxX, cx + cellSize * 0.5f);
                maxY = Mathf.Max(maxY, cy + cellSize * 0.5f);
            }

            bool overlaps = !(maxX <= gridMinX || minX >= gridMaxX || maxY <= gridMinY || minY >= gridMaxY);
            if (!overlaps) return false;

            // Push out by minimal translation + margin
            const float margin = 0.08f;

            float moveLeft = (gridMinX - maxX) - margin;   // negative
            float moveRight = (gridMaxX - minX) + margin;  // positive
            float moveDown = (gridMinY - maxY) - margin;   // negative
            float moveUp = (gridMaxY - minY) + margin;     // positive

            float bestAbs = float.PositiveInfinity;
            Vector3 bestDelta = Vector3.zero;

            void Consider(float move, Vector3 delta)
            {
                float a = Mathf.Abs(move);
                if (a < bestAbs)
                {
                    bestAbs = a;
                    bestDelta = delta;
                }
            }

            Consider(moveLeft, new Vector3(moveLeft, 0f, 0f));
            Consider(moveRight, new Vector3(moveRight, 0f, 0f));
            Consider(moveDown, new Vector3(0f, moveDown, 0f));
            Consider(moveUp, new Vector3(0f, moveUp, 0f));

            pushedTarget = piecePos + bestDelta;
            pushedTarget = ClampToCameraBounds(pushedTarget, mainCamera, paddingWorld: 0.3f);
            return true;
        }

// Lightweight overshoot tween (position only)
        private System.Collections.IEnumerator TweenOvershoot(Transform tr, Vector3 from, Vector3 to, float duration)
        {
            duration = Mathf.Max(0.01f, duration);

            // Overshoot amount proportional to distance
            float dist = Vector3.Distance(from, to);
            Vector3 overshoot = (to - from).normalized * Mathf.Min(0.18f, dist * 0.20f);
            Vector3 over = to + overshoot;

            float t = 0f;
            float half = duration * 0.65f;

            // Phase 1: to overshoot (fast-out)
            while (t < half)
            {
                float a = t / half;
                float eased = 1f - Mathf.Pow(1f - a, 3f);
                tr.position = Vector3.LerpUnclamped(from, over, eased);
                t += Time.deltaTime;
                yield return null;
            }

            // Phase 2: settle back (ease-out)
            t = 0f;
            float rest = duration - half;
            while (t < rest)
            {
                float a = t / rest;
                float eased = 1f - Mathf.Pow(1f - a, 4f);
                tr.position = Vector3.LerpUnclamped(over, to, eased);
                t += Time.deltaTime;
                yield return null;
            }

            tr.position = to;
        }

        private struct PointerState
        {
            public bool down;
            public bool held;
            public bool up;
            public Vector2 screenPos;
            public Vector2 worldPos;

            public static PointerState Get(Camera cam)
            {
                // Touch has priority
                if (Input.touchCount > 0)
                {
                    var t = Input.GetTouch(0);
                    var wp = cam.ScreenToWorldPoint(new Vector3(t.position.x, t.position.y, 10f));
                    return new PointerState
                    {
                        down = t.phase == TouchPhase.Began,
                        held = t.phase == TouchPhase.Moved || t.phase == TouchPhase.Stationary,
                        up = t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled,
                        screenPos = t.position,
                        worldPos = new Vector2(wp.x, wp.y),
                    };
                }

                // Mouse for editor testing
                bool md = Input.GetMouseButtonDown(0);
                bool mh = Input.GetMouseButton(0);
                bool mu = Input.GetMouseButtonUp(0);
                var mpos = (Vector2)Input.mousePosition;
                var mwp = cam.ScreenToWorldPoint(new Vector3(mpos.x, mpos.y, 10f));

                return new PointerState
                {
                    down = md,
                    held = mh,
                    up = mu,
                    screenPos = mpos,
                    worldPos = new Vector2(mwp.x, mwp.y),
                };
            }
        }
    }
}
