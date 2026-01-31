// Phase0GameController.cs
// Put under: Assets/Phase0/Scripts/
// MonoBehaviour that wires input -> core model -> view.
// Pure C# core is in Phase0CoreModel.cs.

using System.Collections.Generic;
using System.Linq;
using PrimeTween;
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

        [Header("Game Feel FX")]
        public Phase0GameFeelFX gameFeelFx;
        public Phase0BoardPlacedHighlightView placedHighlightView;

        [Header("Spine Offset (per rotation)")]
        [Tooltip("Local offsets for Spine child per CW rotation index (0-3).")]
        public Vector2[] spineOffsets = new Vector2[4];

        [Header("Spine Visual Offset")]
        [Tooltip("Constant local offset for SpineVisualOffset child (not per-rotation).")]
        public Vector2 spineVisualOffset;

        [Header("Hysteresis")]
        [Tooltip("How far finger must move away from current cell center (in world units) before we switch to a neighbor cell.")]
        public float cellSwitchHysteresisWorld = 0.32f;

        private readonly Phase0BoardMapping _mapping = new();
        private readonly Phase0PlacementBrain _brain = new();

        // Placement state (controller only; core brain owns rules)
        private bool _held;
        private bool _movedBeyondThreshold;
        private bool _dragStarted;
        private Vector2 _downScreenPos;
        private Vector3 _pieceOriginBeforeDrag;
        private Vector3 _velocity;                  // SmoothDamp velocity
        private Vector3 _dragOffsetWorld;
        private Sequence _positionTween;
        private Tween _rotationTween;

        private Int2[] _lastPlacedWorldCells; // cached after placement

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

            // Build blocked list
            var blocked = new List<Int2>();
            foreach (Transform cell in gridRoot)
            {
                if (!cell.name.Contains("Cell_")) continue;
                if (!cell.name.Contains("_BLOCKED")) continue;

                if (TryParseCellName(cell.name, out var coord))
                    blocked.Add(new Int2(coord.x, coord.y));
            }

            // Occupied: dummy piece occupies exactly 1 cell (as per requirement).
            var occupied = new List<Int2>();
            if (dummyPiece != null)
            {
                var dummyCell = _mapping.WorldToCellRound(dummyPiece.position);
                if (_mapping.IsInsideGrid(dummyCell))
                    occupied.Add(new Int2(dummyCell.x, dummyCell.y));
            }

            _brain.Initialize(
                gridSize,
                blocked,
                occupied,
                shapeDefinition != null ? ToInt2Array(shapeDefinition.baseCells) : null,
                shapeDefinition != null ? new Int2(shapeDefinition.pivot.x, shapeDefinition.pivot.y) : Int2.zero
            );

            // Init views
            var pieceTiles = activePieceRoot != null ? activePieceRoot.GetComponent<Phase0PieceTilesView>() : null;
            if (pieceTiles != null)
            {
                pieceTiles.AutoCollectTiles();
                pieceTiles.ApplyLocalCells(ToVector2IntArray(_brain.LocalCells), _mapping.cellStep.x, _mapping.cellStep.y);
            }

            if (ghostView != null)
            {
                // Find sprite from one tile (placeholder) if none set
                if (ghostView.tileSprite == null)
                {
                    var anyTile = activePieceRoot.GetComponentsInChildren<SpriteRenderer>().FirstOrDefault();
                    if (anyTile != null) ghostView.tileSprite = anyTile.sprite;
                }

                ghostView.EnsureTiles(_brain.LocalCells.Length, sceneConfig != null ? sceneConfig.cellSize : 1f);
                ghostView.SetVisible(false);
            }

            if (placedHighlightView != null)
            {
                if (placedHighlightView.outlineSprite == null)
                {
                    placedHighlightView.outlineSprite = ResolveDefaultOutlineSprite();
                }

                placedHighlightView.Clear();
            }

            if (gameFeelFx != null && sceneConfig != null)
            {
                gameFeelFx.SetHatchScaleForCellSize(sceneConfig.cellSize);
            }
        }

        // Update loop: input -> core brain -> view updates (FX/ghost).
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

            // View-only smoothing / squash while dragging.
            if (_dragStarted && gameFeelFx != null)
            {
                gameFeelFx.UpdateKinematics(activePieceRoot.position);
            }
        }

        private void OnPointerDown(PointerState pointer)
        {
            _held = true;
            _movedBeyondThreshold = false;
            _dragStarted = false;
            _downScreenPos = pointer.screenPos;

            _positionTween.Stop();
            _rotationTween.Stop();


            _pieceOriginBeforeDrag = activePieceRoot.position;

            // When picking up from board, clear occupied cells temporarily (so we can re-place)
            if (_brain.IsPlacedOnBoard && placedHighlightView != null)
            {
                placedHighlightView.Clear();
            }
            _brain.OnPickup();

            // Compute drag offset so it doesn't jump
            var w = pointer.worldPos;
            _dragOffsetWorld = activePieceRoot.position - new Vector3(w.x, w.y, 0f);

            // Candidate init
            _brain.ResetCandidate();
            if (ghostView != null) ghostView.SetVisible(false);
            if (gameFeelFx != null) gameFeelFx.SetInvalidVisual(false);
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

            if (_movedBeyondThreshold && !_dragStarted)
            {
                _dragStarted = true;
                if (gameFeelFx != null)
                {
                    gameFeelFx.SetDragging(true);
                    gameFeelFx.OnPickup();
                }
            }

            // Direct manipulation: once movement begins, piece follows finger with spring delay.
            if (_movedBeyondThreshold)
            {
                Vector3 target = new Vector3(pointer.worldPos.x, pointer.worldPos.y, 0f) + _dragOffsetWorld;
                target = ClampToCameraBounds(target, mainCamera, activePieceRoot, fallbackPaddingWorld: 0.3f, fallbackExtents: GetFallbackPieceExtents());

                activePieceRoot.position = Vector3.SmoothDamp(
                    activePieceRoot.position,
                    target,
                    ref _velocity,
                    followSmoothTime
                );

                if (_dragStarted && gameFeelFx != null)
                {
                    gameFeelFx.UpdateKinematics(activePieceRoot.position);
                }

                // Use the actual piece position (not raw finger) so ghost aligns with spring-follow.
                UpdateCandidateAndGhost(activePieceRoot.position);
            }
        }

        private void OnPointerUp(PointerState pointer)
        {
            bool wasTap = !_movedBeyondThreshold;

            _held = false;
            _velocity = Vector3.zero;
            if (_dragStarted && gameFeelFx != null)
            {
                gameFeelFx.SetDragging(false);
            }
            if (gameFeelFx != null) gameFeelFx.SetInvalidVisual(false);

            if (wasTap)
            {
                if (!_brain.IsPlacedOnBoard)
                {
                    // Tap: rotate when not placed on board. Revalidate placement if it exists outside board.
                    int previousRotation = _brain.RotationCW;
                    bool hadPlacement = _brain.LastPlacedWorldCells != null && _brain.LastPlacedWorldCells.Length > 0;
                    Int2 originCell = hadPlacement
                        ? _brain.LastPlacedOriginCell
                        : new Int2(_mapping.WorldToCellRound(activePieceRoot.position).x,
                            _mapping.WorldToCellRound(activePieceRoot.position).y);

                    RotateCW();

                    if (hadPlacement)
                    {
                        if (_brain.TryCommitPlacementAt(originCell, out _))
                        {
                            _lastPlacedWorldCells = _brain.LastPlacedWorldCells;
                        }
                        else
                        {
                            _brain.SetRotationCW(previousRotation);
                            ApplyRotationVisuals();
                            _brain.RestorePlacementIfAny();
                        }
                    }

                    if (gameFeelFx != null)
                    {
                        gameFeelFx.OnRotateTap();
                    }
                }

                if (ghostView != null) ghostView.SetVisible(false);
                return;
            }

            // Drag drop: valid if candidate is valid (even outside board).
            if (_brain.HasCandidate && _brain.CandidateValid)
            {
                // Snap to candidate origin cell
                Vector3 snapPos = _mapping.CellToWorldCenter(new Vector2Int(_brain.CandidateOriginCell.x, _brain.CandidateOriginCell.y));

                PlaySnapTween(activePieceRoot.position, snapPos, isValid: true);

                // Mark occupied cells
                _lastPlacedWorldCells = _brain.PlaceCandidate();

                UpdatePlacedHighlight();

                if (gameFeelFx != null)
                {
                    gameFeelFx.OnDropValid();
                }
                Phase0Haptics.Pulse(this, count: 1);
            }
            else
            {
                PlaySnapTween(activePieceRoot.position, _pieceOriginBeforeDrag, isValid: false);

                if (gameFeelFx != null)
                {
                    gameFeelFx.OnDropInvalid();
                }

                Phase0Haptics.Pulse(this, count: 2, intervalSeconds: 0.05f);

                // Re-occupy original cells if it was placed before
                _brain.RestorePlacementIfAny();
                UpdatePlacedHighlight();
            }

            if (ghostView != null) ghostView.SetVisible(false);
        }

        private void UpdatePlacedHighlight()
        {
            if (placedHighlightView == null)
            {
                return;
            }

            if (!_brain.IsPlacedOnBoard || _brain.LastPlacedWorldCells == null || _brain.LastPlacedWorldCells.Length == 0)
            {
                placedHighlightView.Clear();
                return;
            }

            var cellSize = sceneConfig != null ? sceneConfig.cellSize : 1f;
            var color = gameFeelFx != null && gameFeelFx.settings != null
                ? gameFeelFx.settings.placedOutlineColor
                : new Color(0.25f, 0.9f, 0.35f, 0.9f);
            placedHighlightView.SetCells(ToVector2IntArray(_brain.LastPlacedWorldCells), _mapping, cellSize, color);
        }

        private void PlaySnapTween(Vector3 from, Vector3 to, bool isValid)
        {
            _positionTween.Stop();

            float duration = isValid
                ? (sceneConfig != null ? sceneConfig.snapDuration : 0.12f)
                : (sceneConfig != null ? sceneConfig.bounceBackDuration : 0.16f);

            float phase = isValid
                ? (sceneConfig != null ? sceneConfig.snapOvershootPhase : 0.65f)
                : (sceneConfig != null ? sceneConfig.bounceBackOvershootPhase : 0.55f);

            phase = Mathf.Clamp01(phase);
            duration = Mathf.Max(0.01f, duration);

            float dist = Vector3.Distance(from, to);
            float ratio = isValid
                ? (sceneConfig != null ? sceneConfig.snapOvershootRatio : 0.20f)
                : (sceneConfig != null ? sceneConfig.bounceBackOvershootRatio : 0.12f);
            float max = isValid
                ? (sceneConfig != null ? sceneConfig.snapOvershootMax : 0.18f)
                : (sceneConfig != null ? sceneConfig.bounceBackOvershootMax : 0.12f);

            Vector3 overshoot = Vector3.zero;
            if (dist > 0.0001f)
            {
                overshoot = (to - from).normalized * Mathf.Min(max, dist * Mathf.Max(0f, ratio));
            }

            Vector3 over = to + overshoot;
            float first = duration * phase;
            float second = duration - first;

            var sequence = Sequence.Create();
            if (first > 0.0001f)
            {
                sequence.Chain(Tween.Position(activePieceRoot, over, first, Ease.OutCubic));
            }

            if (second > 0.0001f)
            {
                var settleStrength = isValid
                    ? (sceneConfig != null ? sceneConfig.snapSettleOvershootStrength : 1.0f)
                    : (sceneConfig != null ? sceneConfig.bounceBackBounceStrength : 0.9f);

                Easing ease = isValid
                    ? Easing.Overshoot(settleStrength)
                    : Easing.Bounce(settleStrength);

                sequence.Chain(Tween.Position(activePieceRoot, to, second, ease));
            }

            _positionTween = sequence;
        }

        private void UpdateCandidateAndGhost(Vector2 pointerWorld)
        {
            var approxCell = _mapping.WorldToCellRound(pointerWorld);

            bool shouldSwitch = false;
            if (_brain.HasCandidate)
            {
                float d = _mapping.DistanceToCellCenter(pointerWorld, new Vector2Int(_brain.CandidateOriginCell.x, _brain.CandidateOriginCell.y));
                shouldSwitch = d >= cellSwitchHysteresisWorld;
            }

            _brain.UpdateCandidate(new Int2(approxCell.x, approxCell.y), shouldSwitch);

            // Ghost view
            if (ghostView != null)
            {
                if (!_brain.HasCandidate)
                {
                    ghostView.SetVisible(false);
                    return;
                }
                var localCells = _brain.LocalCells;
                var intersecting = new List<Vector2Int>(localCells.Length);
                for (int i = 0; i < localCells.Length; i++)
                {
                    var world = _brain.CandidateOriginCell + localCells[i];
                    if (_mapping.IsInsideGrid(new Vector2Int(world.x, world.y)))
                        intersecting.Add(new Vector2Int(localCells[i].x, localCells[i].y));
                }

                if (intersecting.Count == 0 && _brain.CandidateValid)
                {
                    ghostView.SetVisible(false);
                    return;
                }

                ghostView.SetVisible(true);
                ghostView.transform.position = _mapping.CellToWorldCenter(new Vector2Int(_brain.CandidateOriginCell.x, _brain.CandidateOriginCell.y));

                // Apply footprint (valid: intersecting, invalid: full footprint)
                var cellsToDraw = _brain.CandidateValid
                    ? intersecting.ToArray()
                    : ToVector2IntArray(localCells);

                float cellSize = sceneConfig != null ? sceneConfig.cellSize : 1f;
                ghostView.EnsureTiles(cellsToDraw.Length, cellSize);
                ghostView.ApplyLocalCells(cellsToDraw, _mapping.cellStep.x, _mapping.cellStep.y);

                if (gameFeelFx != null)
                {
                    gameFeelFx.SetHatchScaleForCellSize(cellSize);
                }

                bool candidateValid = _brain.CandidateValid;
                Color validColor = new Color(0.35f, 0.75f, 1f, 0.6f);
                Color invalidColor = new Color(1f, 0.25f, 0.25f, 0.6f);
                if (gameFeelFx != null && gameFeelFx.settings != null)
                {
                    validColor = gameFeelFx.settings.ghostValidOutlineColor;
                    invalidColor = gameFeelFx.settings.ghostInvalidOutlineColor;
                }
                if (candidateValid)
                {
                    ghostView.SetColor(validColor);
                }
                else
                {
                    ghostView.SetColor(invalidColor);
                }

                if (gameFeelFx != null)
                {
                    bool hasGhostCandidate = _brain.HasCandidate;
                    bool ghostIsVisible = ghostView.gameObject.activeSelf;
                    gameFeelFx.SetInvalidVisual(_dragStarted && hasGhostCandidate && !candidateValid && ghostIsVisible);
                }
            }
        }

        private void RotateCW()
        {
            _brain.RotateCW();

            ApplyRotationVisuals();
        }

        private void ApplyRotationVisuals()
        {
            // Update placeholder tiles layout (not rotating transform)
            var pieceTiles = activePieceRoot.GetComponent<Phase0PieceTilesView>();
            if (pieceTiles != null)
            {
                pieceTiles.ApplyLocalCells(ToVector2IntArray(_brain.LocalCells), _mapping.cellStep.x, _mapping.cellStep.y);
            }

            // Visual rotation: rotate the SpineAnchor and apply per-rotation offset to the Spine child.
            if (activePieceRoot != null)
            {
                var spineAnchor = activePieceRoot.Find("SpineAnchor");
                if (spineAnchor != null)
                {
                    if (spineAnchor.localPosition != Vector3.zero)
                    {
                        Debug.LogWarning("Phase0GameController: SpineAnchor.localPosition should be (0,0,0) to avoid pivot drift.", spineAnchor);
                    }

                    float angle = -90f * _brain.RotationCW;
                    _rotationTween.Stop();
                    float duration = sceneConfig != null ? sceneConfig.rotateDuration : 0.12f;
                    float strength = sceneConfig != null ? sceneConfig.rotateOvershootStrength : 1.15f;
                    duration = Mathf.Max(0.01f, duration);
                    strength = Mathf.Clamp(strength, 0.1f, 2f);

                    var target = Quaternion.Euler(0f, 0f, angle);
                    _rotationTween = Tween.LocalRotation(spineAnchor, target, duration, Easing.Overshoot(strength));

                    var visualOffset = spineAnchor.Find("SpineVisualOffset");
                    if (visualOffset != null)
                    {
                        visualOffset.localPosition = new Vector3(spineVisualOffset.x, spineVisualOffset.y, visualOffset.localPosition.z);
                    }
                }
            }
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

            if (placedHighlightView == null)
            {
                var t = root.transform.Find("PlacedHighlight");
                if (t != null)
                {
                    placedHighlightView = t.GetComponent<Phase0BoardPlacedHighlightView>();
                }
            }

            if (placedHighlightView == null)
            {
                var go = new GameObject("PlacedHighlight");
                go.transform.SetParent(root.transform, false);
                placedHighlightView = go.AddComponent<Phase0BoardPlacedHighlightView>();
            }

            if (placedHighlightView != null && placedHighlightView.outlineSprite == null)
            {
                placedHighlightView.outlineSprite = ResolveDefaultOutlineSprite();
            }

            // Ensure piece has placeholder view component
            if (activePieceRoot != null)
            {
                var pv = activePieceRoot.GetComponent<Phase0PieceTilesView>();
                if (pv == null) pv = activePieceRoot.gameObject.AddComponent<Phase0PieceTilesView>();
                pv.AutoCollectTiles();
            }
        }

        private Sprite ResolveDefaultOutlineSprite()
        {
            if (ghostView != null && ghostView.outlineSprite != null) return ghostView.outlineSprite;
            if (ghostView != null && ghostView.tileSprite != null) return ghostView.tileSprite;

            if (activePieceRoot != null)
            {
                var anyTile = activePieceRoot.GetComponentsInChildren<SpriteRenderer>().FirstOrDefault();
                if (anyTile != null) return anyTile.sprite;
            }

            return null;
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

        private static Vector3 ClampToCameraBounds(Vector3 world, Camera cam, Transform root, float fallbackPaddingWorld, Vector2 fallbackExtents)
        {
            // Orthographic bounds
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;

            float minX = cam.transform.position.x - halfW;
            float maxX = cam.transform.position.x + halfW;
            float minY = cam.transform.position.y - halfH;
            float maxY = cam.transform.position.y + halfH;

            if (TryGetRendererBounds(root, out var bounds))
            {
                Vector3 offset = bounds.center - root.position;
                Vector3 extents = bounds.extents;

                float minRootX = minX + extents.x - offset.x;
                float maxRootX = maxX - extents.x - offset.x;
                float minRootY = minY + extents.y - offset.y;
                float maxRootY = maxY - extents.y - offset.y;

                world.x = Mathf.Clamp(world.x, minRootX, maxRootX);
                world.y = Mathf.Clamp(world.y, minRootY, maxRootY);
            }
            else
            {
                float minPadX = minX + Mathf.Max(fallbackPaddingWorld, fallbackExtents.x);
                float maxPadX = maxX - Mathf.Max(fallbackPaddingWorld, fallbackExtents.x);
                float minPadY = minY + Mathf.Max(fallbackPaddingWorld, fallbackExtents.y);
                float maxPadY = maxY - Mathf.Max(fallbackPaddingWorld, fallbackExtents.y);

                world.x = Mathf.Clamp(world.x, minPadX, maxPadX);
                world.y = Mathf.Clamp(world.y, minPadY, maxPadY);
            }

            world.z = 0f;

            return world;
        }

        private static bool TryGetRendererBounds(Transform root, out Bounds bounds)
        {
            bounds = default;
            if (root == null) return false;

            var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
            if (renderers == null || renderers.Length == 0) return false;

            bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return true;
        }

        private Vector2 GetFallbackPieceExtents()
        {
            if (sceneConfig == null) return new Vector2(0.3f, 0.3f);
            float stepX = Mathf.Abs(_mapping.cellStep.x) > 0.0001f ? Mathf.Abs(_mapping.cellStep.x) : sceneConfig.cellSize;
            float stepY = Mathf.Abs(_mapping.cellStep.y) > 0.0001f ? Mathf.Abs(_mapping.cellStep.y) : sceneConfig.cellSize;
            var localCells = _brain.LocalCells;
            if (localCells == null || localCells.Length == 0)
            {
                float half = sceneConfig.cellSize * 0.5f;
                return new Vector2(half, half);
            }

            int minX = localCells.Min(c => c.x);
            int maxX = localCells.Max(c => c.x);
            int minY = localCells.Min(c => c.y);
            int maxY = localCells.Max(c => c.y);

            float width = (maxX - minX + 1) * stepX;
            float height = (maxY - minY + 1) * stepY;
            return new Vector2(width * 0.5f, height * 0.5f);
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

        private static Int2[] ToInt2Array(Vector2Int[] source)
        {
            if (source == null) return null;
            var result = new Int2[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                result[i] = new Int2(source[i].x, source[i].y);
            }
            return result;
        }

        private static Vector2Int[] ToVector2IntArray(Int2[] source)
        {
            if (source == null) return null;
            var result = new Vector2Int[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                result[i] = new Vector2Int(source[i].x, source[i].y);
            }
            return result;
        }
    }
}
