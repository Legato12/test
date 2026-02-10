// Phase0GameController.cs
// Put under: Assets/Phase0/Scripts/
// MonoBehaviour that wires input -> core model -> view.
// Pure C# core is in Phase0CoreModel.cs.

using System.Collections.Generic;
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

        [Header("Hover Debounce")]
        [Tooltip("Minimum time between hovered cell switches (anti-flicker).")]
        public float hoverDebounceSeconds = 0.05f;

        // Rug mapping is used only for the visible board cell renderers.
        private readonly Phase0BoardMapping _rugMapping = new();
        // Global mapping drives hovered cell / snapping / lock positions.
        private readonly Phase0GlobalGridMapping _globalMapping = new();
        private readonly Phase0PlacementBrain _brain = new();

        private Vector2Int _rugOriginGlobal;
        private int _rugWidthCells;
        private int _rugHeightCells;

        private bool _hasEverLocked;
        private Vector3 _spawnWorldPos;
        private int _spawnRotationCW;

        private Int2 _lastLockedOriginCell;
        private int _lastLockedRotationCW;

        // Placement state (controller only; core brain owns rules)
        private bool _held;
        private bool _movedBeyondThreshold;
        private bool _dragStarted;
        private bool _pickedUpFromBoardThisTouch;
        private Vector2 _downScreenPos;
        private Vector3 _pieceOriginBeforeDrag;
        private Vector3 _velocity;                  // SmoothDamp velocity
        private Vector3 _dragOffsetWorld;
        private Sequence _positionTween;
        private Tween _rotationTween;

        private Renderer[] _cachedPieceRenderers;
        private Phase0PieceTilesView _cachedPieceTilesView;
        private readonly Dictionary<Vector2Int, SpriteRenderer> _cellRenderers = new();
        private readonly HashSet<Vector2Int> _blockedCells = new();
        private readonly Vector2Int[] _hoverTintedCells = new Vector2Int[4];
        private int _hoverTintedCellsCount;
        private readonly Vector2Int[] _placedCells = new Vector2Int[4];
        private int _placedCellsCount;

        private Vector2Int[] _scratchCandidateAll;
        private Vector2Int[] _scratchCandidateInside;
        private Vector2Int[] _scratchCandidateLocalAll;
        private Vector2Int[] _scratchCandidateLocalInside;
        private int _scratchInsideCount;
        private Vector2Int[] _scratchLocalCellsV2;
        private Vector2Int[] _scratchPlacedCellsV2;

        private float _lastHoverSwitchTime;

        private float _lastCameraAspect;
        private int _lastScreenWidth;
        private int _lastScreenHeight;

        private void Awake()
        {
            AutoFindRefs();
        }

        private void Start()
        {
            if (mainCamera == null) mainCamera = Camera.main;

            CachePieceRenderers();
            _cachedPieceTilesView = activePieceRoot != null ? activePieceRoot.GetComponent<Phase0PieceTilesView>() : null;

            if (sceneConfig == null)
            {
                sceneConfig = Resources.Load<SceneConfigSO>("SceneConfig");
            }

            if (shapeDefinition == null)
            {
                shapeDefinition = FindFirstResource<ShapeDefinitionSO>();
            }

            int rugSize = sceneConfig != null ? sceneConfig.gridSize : 4;
            int rugW = sceneConfig != null ? sceneConfig.rugWidth : rugSize;
            int rugH = sceneConfig != null ? sceneConfig.rugHeight : rugSize;

            if (!_rugMapping.TryAutoInitFromGridRoot(gridRoot, rugW, rugH))
            {
                enabled = false;
                return;
            }

            // Global grid dims: either explicit config or auto-computed from camera extents + rug cell step.
            int globalW = sceneConfig != null ? sceneConfig.globalGridWidth : 0;
            int globalH = sceneConfig != null ? sceneConfig.globalGridHeight : 0;
            if (globalW <= 0 || globalH <= 0)
            {
                float step = Mathf.Max(Mathf.Abs(_rugMapping.cellStep.x), Mathf.Abs(_rugMapping.cellStep.y));
                step = Mathf.Max(0.0001f, step);

                float camH = mainCamera.orthographicSize * 2f;
                float camW = camH * mainCamera.aspect;

                globalW = Mathf.Max(rugW, Mathf.FloorToInt(camW / step));
                globalH = Mathf.Max(rugH, Mathf.FloorToInt(camH / step));

                globalW = Mathf.Max(1, globalW);
                globalH = Mathf.Max(1, globalH);
            }

            _globalMapping.InitLatticeAligned(mainCamera, globalW, globalH, _rugMapping);

            // Rug origin in global coords: now calculated from lattice alignment
            _rugOriginGlobal = _globalMapping.RugOriginGlobal;
            _rugWidthCells = rugW;
            _rugHeightCells = rugH;

            CacheCellRenderers();

            // Build blocked list (local rug coords for visuals, global coords for core).
            var blocked = new List<Int2>();
            _blockedCells.Clear();
            foreach (Transform cell in gridRoot)
            {
                if (!cell.name.Contains("Cell_")) continue;
                if (!cell.name.Contains("_BLOCKED")) continue;

                if (TryParseCellName(cell.name, out var coord))
                {
                    // Store local coords for the rug visuals.
                    _blockedCells.Add(coord);

                    // Convert to global coords for placement validation.
                    var global = coord + _rugOriginGlobal;
                    blocked.Add(new Int2(global.x, global.y));
                }
            }

            // Occupied: dummy piece occupies exactly 1 cell (global coords).
            var occupied = new List<Int2>();
            if (dummyPiece != null)
            {
                var dummyCell = _globalMapping.WorldToGlobalCellRound(dummyPiece.position);
                if (_globalMapping.IsInside(dummyCell))
                    occupied.Add(new Int2(dummyCell.x, dummyCell.y));
            }

            _brain.Initialize(
                globalW,
                globalH,
                blocked,
                occupied,
                shapeDefinition != null ? ToInt2Array(shapeDefinition.baseCells) : null,
                shapeDefinition != null ? new Int2(shapeDefinition.pivot.x, shapeDefinition.pivot.y) : Int2.zero,
                new Int2(_rugOriginGlobal.x, _rugOriginGlobal.y),
                _rugWidthCells,
                _rugHeightCells
            );

            EnsureScratch(_brain.LocalCells.Length);

            // Spawn placement is treated as the initial LastValidPlacement (before first lock).
            // Snap the spawn position onto the global grid for determinism.
            _spawnWorldPos = activePieceRoot != null ? activePieceRoot.position : default;
            if (activePieceRoot != null)
            {
                var spawnCell = _globalMapping.WorldToCellRound(activePieceRoot.position);
                var spawnPos2 = _globalMapping.CellToWorldCenter(spawnCell);
                _spawnWorldPos = new Vector3(spawnPos2.x, spawnPos2.y, 0f);
                activePieceRoot.position = _spawnWorldPos;
            }
            _spawnRotationCW = _brain.RotationCW;
            _hasEverLocked = false;

            // Init views
            var pieceTiles = activePieceRoot != null ? activePieceRoot.GetComponent<Phase0PieceTilesView>() : null;
            if (pieceTiles != null)
            {
                pieceTiles.AutoCollectTiles();
                int localCount = _brain.LocalCells != null ? _brain.LocalCells.Length : 0;
                EnsureLocalCellsScratch(localCount);
                FillVector2IntScratch(_brain.LocalCells, localCount, _scratchLocalCellsV2);
                pieceTiles.ApplyLocalCells(_scratchLocalCellsV2, localCount, _globalMapping.cellStep.x, _globalMapping.cellStep.y);
            }

            if (ghostView != null)
            {
                // Find sprite from one tile (placeholder) if none set
                if (ghostView.tileSprite == null)
                {
                    var anyTile = FindFirstChildSpriteRenderer(activePieceRoot);
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

            // Pre-warm arrays to prevent first-drag GC allocations
            // Shape L has 4 cells, so pre-allocate that capacity
            for (int i = 0; i < 4; i++)
            {
                _hoverTintedCells[i] = new Vector2Int(-999, -999);
                _placedCells[i] = new Vector2Int(-999, -999);
            }
            _hoverTintedCellsCount = 4;
            _placedCellsCount = 4;
            ClearHoverTint();
            ClearPlacedCells();

            ApplyBaseCellColors();

            _lastCameraAspect = mainCamera != null ? mainCamera.aspect : 0f;
            _lastScreenWidth = Screen.width;
            _lastScreenHeight = Screen.height;
        }

        // Update loop: input -> core brain -> view updates (FX/ghost).
        private void Update()
        {
            if (mainCamera == null) return;
            if (activePieceRoot == null) return;

            bool aspectChanged = Mathf.Abs(mainCamera.aspect - _lastCameraAspect) > 0.01f;
            bool resolutionChanged = Screen.width != _lastScreenWidth || Screen.height != _lastScreenHeight;
            if (aspectChanged || resolutionChanged)
            {
                ReinitializeGlobalGrid();
                _lastCameraAspect = mainCamera.aspect;
                _lastScreenWidth = Screen.width;
                _lastScreenHeight = Screen.height;
            }

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

        private void ReinitializeGlobalGrid()
        {
            if (mainCamera == null || gridRoot == null)
            {
                return;
            }

            int rugW = sceneConfig != null ? sceneConfig.rugWidth : 4;
            int rugH = sceneConfig != null ? sceneConfig.rugHeight : 4;

            float step = Mathf.Max(Mathf.Abs(_rugMapping.cellStep.x), Mathf.Abs(_rugMapping.cellStep.y));
            step = Mathf.Max(0.0001f, step);

            float camH = mainCamera.orthographicSize * 2f;
            float camW = camH * mainCamera.aspect;

            int globalW = Mathf.Max(rugW, Mathf.FloorToInt(camW / step));
            int globalH = Mathf.Max(rugH, Mathf.FloorToInt(camH / step));
            globalW = Mathf.Max(1, globalW);
            globalH = Mathf.Max(1, globalH);

            _globalMapping.InitLatticeAligned(mainCamera, globalW, globalH, _rugMapping);
            _rugOriginGlobal = _globalMapping.RugOriginGlobal;

            var blocked = new List<Int2>();
            foreach (Transform cell in gridRoot)
            {
                if (!cell.name.Contains("Cell_") || !cell.name.Contains("_BLOCKED")) continue;
                if (TryParseCellName(cell.name, out var coord))
                {
                    var global = coord + _rugOriginGlobal;
                    blocked.Add(new Int2(global.x, global.y));
                }
            }

            var occupied = new List<Int2>();
            if (dummyPiece != null)
            {
                var dummyCell = _globalMapping.WorldToGlobalCellRound(dummyPiece.position);
                if (_globalMapping.IsInside(dummyCell))
                    occupied.Add(new Int2(dummyCell.x, dummyCell.y));
            }

            _brain.Initialize(
                globalW,
                globalH,
                blocked,
                occupied,
                shapeDefinition != null ? ToInt2Array(shapeDefinition.baseCells) : null,
                shapeDefinition != null ? new Int2(shapeDefinition.pivot.x, shapeDefinition.pivot.y) : Int2.zero,
                new Int2(_rugOriginGlobal.x, _rugOriginGlobal.y),
                _rugWidthCells,
                _rugHeightCells
            );

            EnsureScratch(_brain.LocalCells.Length);

            _hasEverLocked = false;
            _pickedUpFromBoardThisTouch = false;
            _held = false;
            _dragStarted = false;
            _movedBeyondThreshold = false;
            _velocity = Vector3.zero;

            _brain.ResetCandidate();
            if (ghostView != null)
            {
                ghostView.SetVisible(false);
            }
            if (gameFeelFx != null)
            {
                gameFeelFx.SetInvalidVisual(false);
            }

            ClearHoverTint();
            ClearPlacedCells();
            ApplyBaseCellColors();
            UpdatePlacedHighlight();
        }

        private void OnPointerDown(PointerState pointer)
        {
            _held = true;
            _movedBeyondThreshold = false;
            _dragStarted = false;
            _downScreenPos = pointer.screenPos;
            _lastHoverSwitchTime = Time.unscaledTime - hoverDebounceSeconds;

            _positionTween.Stop();
            _rotationTween.Stop();


            _pieceOriginBeforeDrag = activePieceRoot.position;

            // When picking up from board, clear occupied cells temporarily (so we can re-place)
            _pickedUpFromBoardThisTouch = _brain.IsPlacedOnBoard && _brain.LastPlacedWorldCells != null;
            if (_pickedUpFromBoardThisTouch && placedHighlightView != null)
            {
                placedHighlightView.Clear();
            }
            _brain.OnPickup();
            ClearPlacedCells();

            // Compute drag offset so it doesn't jump
            var w = pointer.worldPos;
            _dragOffsetWorld = activePieceRoot.position - new Vector3(w.x, w.y, 0f);

            // Candidate init
            _brain.ResetCandidate();
            if (ghostView != null) ghostView.SetVisible(false);
            if (gameFeelFx != null) gameFeelFx.SetInvalidVisual(false);
            ClearHoverTint();
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
                Phase0Haptics.OnGrab();
            }

            // Direct manipulation: once movement begins, piece follows finger with spring delay.
            if (_movedBeyondThreshold)
            {
                Vector3 target = new Vector3(pointer.worldPos.x, pointer.worldPos.y, 0f) + _dragOffsetWorld;
                target = ClampToCameraBounds(target, mainCamera, activePieceRoot, _cachedPieceRenderers, fallbackPaddingWorld: 0.3f, fallbackExtents: GetFallbackPieceExtents());

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
                // Tap: rotate and revalidate placement (including locked pieces on board).
                int previousRotation = _brain.RotationCW;
                bool hadPlacement = _brain.IsLocked;
                Int2 originCell;
                if (hadPlacement)
                {
                    originCell = _brain.LastPlacedOriginCell;
                }
                else
                {
                    var approx = _globalMapping.WorldToCellFloor(activePieceRoot.position);
                    // Clamp to global grid bounds
                    approx.x = Mathf.Clamp(approx.x, 0, _globalMapping.width - 1);
                    approx.y = Mathf.Clamp(approx.y, 0, _globalMapping.height - 1);
                    originCell = new Int2(approx.x, approx.y);
                }

                RotateCW();

                if (hadPlacement)
                {
                    if (_brain.TryCommitPlacementAt(originCell, out _))
                    {
                        _hasEverLocked = true;
                        _lastLockedOriginCell = _brain.LastPlacedOriginCell;
                        _lastLockedRotationCW = _brain.RotationCW;
                        SyncPlacedCellsFromBrain();
                        ApplyBaseCellColors();
                        UpdatePlacedHighlight();
                    }
                    else
                    {
                        _brain.SetRotationCW(previousRotation);
                        ApplyRotationVisuals();
                        _brain.RestorePlacementIfAny();
                        SyncPlacedCellsFromBrain();
                        ApplyBaseCellColors();
                        UpdatePlacedHighlight();
                    }
                }
                else if (_pickedUpFromBoardThisTouch)
                {
                    _brain.RestorePlacementIfAny();
                    SyncPlacedCellsFromBrain();
                    ApplyBaseCellColors();
                    UpdatePlacedHighlight();
                }

                if (gameFeelFx != null)
                {
                    gameFeelFx.OnRotateTap();
                }
                Phase0Haptics.OnRotate();

                if (ghostView != null) ghostView.SetVisible(false);
                ClearHoverTint();
                return;
            }

            // Drag drop: valid if candidate is valid (even outside board).
            if (_brain.HasCandidate && _brain.CandidateValid)
            {
                // Snap to candidate origin cell
                Vector3 snapPos = _globalMapping.CellToWorldCenter(new Vector2Int(_brain.CandidateOriginCell.x, _brain.CandidateOriginCell.y));

                PlaySnapTween(activePieceRoot.position, snapPos, isValid: true);

                // Mark occupied cells
                _brain.PlaceCandidateReuse(out _);
                _hasEverLocked = true;
                _lastLockedOriginCell = _brain.LastPlacedOriginCell;
                _lastLockedRotationCW = _brain.RotationCW;
                SyncPlacedCellsFromBrain();
                ApplyBaseCellColors();

                UpdatePlacedHighlight();

                if (gameFeelFx != null)
                {
                    gameFeelFx.OnDropValid();
                }
                Phase0Haptics.OnValidPlace();
            }
            else
            {
                Vector3 returnPos;
                int returnRotationCW;

                if (_hasEverLocked)
                {
                    returnPos = _globalMapping.CellToWorldCenter(new Vector2Int(_lastLockedOriginCell.x, _lastLockedOriginCell.y));
                    returnRotationCW = _lastLockedRotationCW;
                }
                else
                {
                    returnPos = _spawnWorldPos;
                    returnRotationCW = _spawnRotationCW;
                }

                _brain.RestorePlacementIfAny();

                _brain.SetRotationCW(returnRotationCW);
                ApplyRotationVisuals(returnRotationCW, immediate: false);

                if (gameFeelFx != null)
                {
                    gameFeelFx.PlayReleaseInvalid(activePieceRoot, _cachedPieceRenderers);
                }

                PlaySnapTween(activePieceRoot.position, returnPos, isValid: false);

                Phase0Haptics.OnReject();

                SyncPlacedCellsFromBrain();
                ApplyBaseCellColors();
                UpdatePlacedHighlight();
            }

            if (ghostView != null) ghostView.SetVisible(false);
            ClearHoverTint();
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

            // LastPlacedWorldCells is now already rug-local, so use directly
            int localCount = _brain.LastPlacedWorldCells != null ? _brain.LastPlacedWorldCells.Length : 0;
            EnsurePlacedCellsScratch(localCount);
            FillVector2IntScratch(_brain.LastPlacedWorldCells, localCount, _scratchPlacedCellsV2);

            placedHighlightView.SetCells(_scratchPlacedCellsV2, localCount, _rugMapping, cellSize, color);
        }

        private void PlaySnapTween(Vector3 from, Vector3 to, bool isValid)
        {
            _positionTween.Stop();

            float duration = isValid
                ? (sceneConfig != null ? sceneConfig.snapDuration : 0.12f)
                : (sceneConfig != null ? sceneConfig.bounceBackDuration : 0.3f);

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
            var approxCell = _globalMapping.WorldToCellFloor(pointerWorld);

            // Clamp to global grid bounds
            approxCell.x = Mathf.Clamp(approxCell.x, 0, _globalMapping.width - 1);
            approxCell.y = Mathf.Clamp(approxCell.y, 0, _globalMapping.height - 1);

            bool shouldSwitch = false;
            if (_brain.HasCandidate)
            {
                var currentCell = new Vector2Int(_brain.CandidateOriginCell.x, _brain.CandidateOriginCell.y);
                bool cellChanged = approxCell != currentCell;
                float d = _globalMapping.DistanceToCellCenter(pointerWorld, currentCell);
                float debounce = sceneConfig != null ? sceneConfig.hoverDebounceSeconds : hoverDebounceSeconds;
                bool debounceReady = Time.unscaledTime - _lastHoverSwitchTime >= debounce;

                shouldSwitch = cellChanged && debounceReady && d >= cellSwitchHysteresisWorld;
            }
            else
            {
                shouldSwitch = true;
            }

    var beforeOrigin = _brain.HasCandidate ? _brain.CandidateOriginCell : default;
    _brain.UpdateCandidate(new Int2(approxCell.x, approxCell.y), shouldSwitch);
    if (shouldSwitch && _brain.HasCandidate && _brain.CandidateOriginCell != beforeOrigin)
    {
        _lastHoverSwitchTime = Time.unscaledTime;
        Phase0Haptics.OnHoverSnap();
    }

    // Compute validity flags for invalid visual (independent of ghost visibility)
    bool candidateValid = _brain.HasCandidate && _brain.CandidateValid;
    bool invalidVisual = _dragStarted && _brain.HasCandidate && !candidateValid;

    UpdateHoverTint();

            // Ghost view
            if (ghostView != null)
            {
                if (!_brain.HasCandidate)
                {
                    ghostView.SetVisible(false);
                    if (gameFeelFx != null) gameFeelFx.SetInvalidVisual(invalidVisual);
                    return;
                }
                var localCells = _brain.LocalCells;
                int worldCount = localCells != null ? localCells.Length : 0;

                for (int i = 0; i < worldCount; i++)
                {
                    var local = localCells[i];
                    var world = _brain.CandidateOriginCell + local;
                    _scratchCandidateLocalAll[i] = new Vector2Int(local.x, local.y);
                    _scratchCandidateAll[i] = new Vector2Int(world.x, world.y);
                }

                _scratchInsideCount = 0;
                for (int i = 0; i < worldCount; i++)
                {
                    var wc = _scratchCandidateAll[i];
                    if (!IsInsideRugGlobal(wc))
                        continue;

                    _scratchCandidateInside[_scratchInsideCount] = wc;
                    _scratchCandidateLocalInside[_scratchInsideCount] = _scratchCandidateLocalAll[i];
                    _scratchInsideCount++;
                }

                if (_scratchInsideCount == 0 && _brain.CandidateValid)
                {
                    ghostView.SetVisible(false);
                    if (gameFeelFx != null) gameFeelFx.SetInvalidVisual(invalidVisual);
                    return;
                }

                ghostView.SetVisible(true);
                ghostView.transform.position = _globalMapping.CellToWorldCenter(new Vector2Int(_brain.CandidateOriginCell.x, _brain.CandidateOriginCell.y));

                // Apply footprint (valid: intersecting, invalid: full footprint)
                UnityEngine.Vector2Int[] cellsToDraw = _scratchCandidateLocalAll;
                int drawCount = worldCount;

                float cellSize = sceneConfig != null ? sceneConfig.cellSize : 1f;
                ghostView.EnsureTiles(drawCount, cellSize);
                ghostView.ApplyLocalCells(cellsToDraw, drawCount, _globalMapping.cellStep.x, _globalMapping.cellStep.y);

                if (_cachedPieceTilesView != null)
                {
                    _cachedPieceTilesView.ApplyLocalCells(cellsToDraw, drawCount, _globalMapping.cellStep.x, _globalMapping.cellStep.y);
                }

                if (gameFeelFx != null)
                {
                    gameFeelFx.SetHatchScaleForCellSize(cellSize);
                }

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
                    gameFeelFx.SetInvalidVisual(invalidVisual);
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
            ApplyRotationVisuals(_brain.RotationCW, immediate: false);
        }

        private void ApplyRotationVisuals(int rotationCW, bool immediate)
        {
            // Update placeholder tiles layout (not rotating transform)
            if (_cachedPieceTilesView != null)
            {
                int localCount = _brain.LocalCells != null ? _brain.LocalCells.Length : 0;
                EnsureLocalCellsScratch(localCount);
                FillVector2IntScratch(_brain.LocalCells, localCount, _scratchLocalCellsV2);
                _cachedPieceTilesView.ApplyLocalCells(_scratchLocalCellsV2, localCount, _globalMapping.cellStep.x, _globalMapping.cellStep.y);
            }

            // Visual rotation: rotate the SpineAnchor and apply per-rotation offset to the Spine child.
            if (activePieceRoot != null)
            {
                var spineAnchor = activePieceRoot.Find("SpineAnchor");
                if (spineAnchor != null)
                {
                    float angle = -90f * rotationCW;
                    _rotationTween.Stop();
                    float duration = sceneConfig != null ? sceneConfig.rotateDuration : 0.12f;
                    float strength = sceneConfig != null ? sceneConfig.rotateOvershootStrength : 1.15f;
                    duration = Mathf.Max(0.01f, duration);
                    strength = Mathf.Clamp(strength, 0.1f, 2f);

                    var target = Quaternion.Euler(0f, 0f, angle);
                    if (immediate || duration <= 0.0001f)
                    {
                        spineAnchor.localRotation = target;
                    }
                    else if (Quaternion.Dot(spineAnchor.localRotation, target) <= 0.999999f)
                    {
                        _rotationTween = Tween.LocalRotation(spineAnchor, target, duration, Easing.Overshoot(strength));
                    }

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

        private void CacheCellRenderers()
        {
            _cellRenderers.Clear();
            if (gridRoot == null) return;

            foreach (Transform cell in gridRoot)
            {
                if (!cell.name.Contains("Cell_")) continue;
                if (!TryParseCellName(cell.name, out var coord)) continue;

                var sr = cell.GetComponent<SpriteRenderer>();
                if (sr != null)
                {
                    _cellRenderers[coord] = sr;
                }
            }
        }

        public void ApplyBaseCellColors()
        {
            if (_cellRenderers.Count == 0) return;

            Color baseColor = sceneConfig != null ? sceneConfig.baseCellColor : Color.white;
            Color blockedColor = sceneConfig != null ? sceneConfig.blockedCellColor : new Color(1f, 0.25f, 0.25f, 1f);
            Color placedColor = sceneConfig != null ? sceneConfig.placedCellColor : new Color(0.25f, 0.9f, 0.35f, 1f);

            foreach (var kvp in _cellRenderers)
            {
                var coord = kvp.Key;
                var sr = kvp.Value;
                if (sr == null) continue;

                bool isBlocked = _blockedCells.Contains(coord);
                sr.color = isBlocked ? blockedColor : baseColor;
            }

            for (int i = 0; i < _placedCellsCount; i++)
            {
                var coord = _placedCells[i];
                if (_cellRenderers.TryGetValue(coord, out var sr) && sr != null)
                {
                    sr.color = placedColor;
                }
            }
        }

        private bool IsInsideRugGlobal(Vector2Int globalCell)
        {
            // Rug is a subset of the global grid, expressed in global grid-space.
            // IMPORTANT: rug is NOT a placement restriction; it's only for UI subset (tint/highlight/ghost).
            return globalCell.x >= _rugOriginGlobal.x
                && globalCell.x < _rugOriginGlobal.x + _rugWidthCells
                && globalCell.y >= _rugOriginGlobal.y
                && globalCell.y < _rugOriginGlobal.y + _rugHeightCells;
        }

        private bool TryGlobalToRugLocal(Vector2Int globalCell, out Vector2Int rugLocal)
        {
            if (!IsInsideRugGlobal(globalCell))
            {
                rugLocal = default;
                return false;
            }

            rugLocal = new Vector2Int(globalCell.x - _rugOriginGlobal.x, globalCell.y - _rugOriginGlobal.y);
            return true;
        }

        private void UpdateHoverTint()
        {
            if (_cellRenderers.Count == 0 || !_brain.HasCandidate) return;

            var localCells = _brain.LocalCells;
            if (localCells == null || localCells.Length == 0) return;

            var candidateOrigin = _brain.CandidateOriginCell;
            bool isValid = _brain.CandidateValid;

            Color validColor = sceneConfig != null ? sceneConfig.hoverValidCellColor : new Color(0.35f, 0.75f, 1f, 1f);
            Color invalidColor = sceneConfig != null ? sceneConfig.hoverInvalidCellColor : new Color(1f, 0.25f, 0.25f, 1f);
            Color tintColor = isValid ? validColor : invalidColor;

            ClearHoverTint();
            _hoverTintedCellsCount = 0;
            for (int i = 0; i < localCells.Length; i++)
            {
                var world = candidateOrigin + localCells[i];
                var globalCoord = new Vector2Int(world.x, world.y);
                if (!TryGlobalToRugLocal(globalCoord, out var localCoord)) continue;
                if (!_cellRenderers.TryGetValue(localCoord, out var sr) || sr == null) continue;

                sr.color = tintColor;
                if (_hoverTintedCellsCount < _hoverTintedCells.Length)
                    _hoverTintedCells[_hoverTintedCellsCount++] = localCoord;
            }
        }

        private void ClearHoverTint()
        {
            if (_hoverTintedCellsCount == 0) return;
            for (int i = 0; i < _hoverTintedCellsCount; i++)
            {
                ApplyDefaultCellColor(_hoverTintedCells[i]);
            }
            _hoverTintedCellsCount = 0;
        }

        private void SyncPlacedCellsFromBrain()
        {
            _placedCellsCount = 0;
            if (!_brain.IsPlacedOnBoard || _brain.LastPlacedWorldCells == null) return;

            for (int i = 0; i < _brain.LastPlacedWorldCells.Length; i++)
            {
                var cell = _brain.LastPlacedWorldCells[i];
                var localCoord = new Vector2Int(cell.x, cell.y);
                if (_placedCellsCount < _placedCells.Length)
                    _placedCells[_placedCellsCount++] = localCoord;
            }
        }

        private void ClearPlacedCells()
        {
            if (_placedCellsCount == 0) return;
            for (int i = 0; i < _placedCellsCount; i++)
            {
                ApplyDefaultCellColor(_placedCells[i]);
            }
            _placedCellsCount = 0;
        }

        private void ApplyDefaultCellColor(Vector2Int coord)
        {
            if (!_cellRenderers.TryGetValue(coord, out var sr) || sr == null) return;

            Color baseColor = sceneConfig != null ? sceneConfig.baseCellColor : Color.white;
            Color blockedColor = sceneConfig != null ? sceneConfig.blockedCellColor : new Color(1f, 0.25f, 0.25f, 1f);
            Color placedColor = sceneConfig != null ? sceneConfig.placedCellColor : new Color(0.25f, 0.9f, 0.35f, 1f);

            if (IsPlacedCell(coord))
            {
                sr.color = placedColor;
                return;
            }

            bool isBlocked = _blockedCells.Contains(coord);
            sr.color = isBlocked ? blockedColor : baseColor;
        }

        private bool IsPlacedCell(Vector2Int coord)
        {
            for (int i = 0; i < _placedCellsCount; i++)
            {
                if (_placedCells[i] == coord) return true;
            }
            return false;
        }

        private void CachePieceRenderers()
        {
            if (activePieceRoot == null)
            {
                _cachedPieceRenderers = null;
                return;
            }

            _cachedPieceRenderers = activePieceRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
        }

        private Sprite ResolveDefaultOutlineSprite()
        {
            if (ghostView != null && ghostView.outlineSprite != null) return ghostView.outlineSprite;
            if (ghostView != null && ghostView.tileSprite != null) return ghostView.tileSprite;

            if (activePieceRoot != null)
            {
                var anyTile = FindFirstChildSpriteRenderer(activePieceRoot);
                if (anyTile != null) return anyTile.sprite;
            }

            return null;
        }

        private static T FindFirstResource<T>() where T : Object
        {
            var items = Resources.FindObjectsOfTypeAll<T>();
            if (items == null || items.Length == 0) return null;
            return items[0];
        }

        private static SpriteRenderer FindFirstChildSpriteRenderer(Transform root)
        {
            if (root == null) return null;

            var renderers = root.GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
            if (renderers == null || renderers.Length == 0) return null;
            return renderers[0];
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

        private void EnsureScratch(int maxCells)
        {
            if (_scratchCandidateAll == null || _scratchCandidateAll.Length != maxCells)
                _scratchCandidateAll = new Vector2Int[maxCells];

            if (_scratchCandidateInside == null || _scratchCandidateInside.Length != maxCells)
                _scratchCandidateInside = new Vector2Int[maxCells];

            if (_scratchCandidateLocalAll == null || _scratchCandidateLocalAll.Length != maxCells)
                _scratchCandidateLocalAll = new Vector2Int[maxCells];

            if (_scratchCandidateLocalInside == null || _scratchCandidateLocalInside.Length != maxCells)
                _scratchCandidateLocalInside = new Vector2Int[maxCells];

            EnsureLocalCellsScratch(maxCells);
            EnsurePlacedCellsScratch(maxCells);
        }

        private void EnsureLocalCellsScratch(int maxCells)
        {
            if (maxCells < 0) maxCells = 0;
            if (_scratchLocalCellsV2 == null || _scratchLocalCellsV2.Length != maxCells)
                _scratchLocalCellsV2 = new Vector2Int[maxCells];
        }

        private void EnsurePlacedCellsScratch(int maxCells)
        {
            if (maxCells < 0) maxCells = 0;
            if (_scratchPlacedCellsV2 == null || _scratchPlacedCellsV2.Length != maxCells)
                _scratchPlacedCellsV2 = new Vector2Int[maxCells];
        }

        private static void FillVector2IntScratch(Int2[] source, int count, Vector2Int[] destination)
        {
            if (source == null || destination == null || count <= 0) return;

            int n = Mathf.Min(Mathf.Min(source.Length, destination.Length), count);
            for (int i = 0; i < n; i++)
            {
                destination[i] = new Vector2Int(source[i].x, source[i].y);
            }
        }

        private static Vector3 ClampToCameraBounds(Vector3 world, Camera cam, Transform root, Renderer[] cachedRenderers, float fallbackPaddingWorld, Vector2 fallbackExtents)
        {
            // Orthographic bounds
            float halfH = cam.orthographicSize;
            float halfW = halfH * cam.aspect;

            float minX = cam.transform.position.x - halfW;
            float maxX = cam.transform.position.x + halfW;
            float minY = cam.transform.position.y - halfH;
            float maxY = cam.transform.position.y + halfH;

            if (TryGetRendererBounds(cachedRenderers, out var bounds))
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

        private static bool TryGetRendererBounds(Renderer[] cachedRenderers, out Bounds bounds)
        {
            bounds = default;
            if (cachedRenderers == null || cachedRenderers.Length == 0) return false;

            bounds = cachedRenderers[0].bounds;
            for (int i = 1; i < cachedRenderers.Length; i++)
            {
                var renderer = cachedRenderers[i];
                if (renderer == null) continue;
                bounds.Encapsulate(renderer.bounds);
            }

            return true;
        }

        private Vector2 GetFallbackPieceExtents()
        {
            if (sceneConfig == null) return new Vector2(0.3f, 0.3f);
            float stepX = Mathf.Abs(_globalMapping.cellStep.x) > 0.0001f ? Mathf.Abs(_globalMapping.cellStep.x) : sceneConfig.cellSize;
            float stepY = Mathf.Abs(_globalMapping.cellStep.y) > 0.0001f ? Mathf.Abs(_globalMapping.cellStep.y) : sceneConfig.cellSize;
            var localCells = _brain.LocalCells;
            if (localCells == null || localCells.Length == 0)
            {
                float half = sceneConfig.cellSize * 0.5f;
                return new Vector2(half, half);
            }

            int minX = localCells[0].x;
            int maxX = localCells[0].x;
            int minY = localCells[0].y;
            int maxY = localCells[0].y;
            for (int i = 1; i < localCells.Length; i++)
            {
                var cell = localCells[i];
                if (cell.x < minX) minX = cell.x;
                if (cell.x > maxX) maxX = cell.x;
                if (cell.y < minY) minY = cell.y;
                if (cell.y > maxY) maxY = cell.y;
            }

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

    }
}
