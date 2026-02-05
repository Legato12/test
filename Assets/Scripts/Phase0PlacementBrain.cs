// Phase0PlacementBrain.cs
// Pure C# placement/rotation brain (no MonoBehaviour). Unity layer provides world mapping + visuals.
//
// Key rules implemented:
// - Global grid bounds are enforced by GridModel (rect width/height).
// - Rug (board) is a subset in global coords; it is NOT a placement restriction.
// - Occupancy is tracked for ALL locked cells globally.
// - IsPlacedOnBoard + LastPlacedWorldCells refer to rug-intersection only (for UI).
// - LastPlacedOriginCell and IsLocked work globally (for LastValidPlacement behavior).

using System;
using System.Collections.Generic;

namespace Phase0 {
    public sealed class Phase0PlacementBrain {
        private GridModel _grid;

        private Int2[] _baseCells = Array.Empty<Int2>();
        private Int2[] _baseOffsets = Array.Empty<Int2>();
        private Int2 _pivot;
        private int _rotationCW;
        private Int2[] _localCells = Array.Empty<Int2>();

        // Rug subset (global coords)
        private Int2 _rugOriginGlobal;
        private int _rugWidth;
        private int _rugHeight;

        // Candidate
        private bool _hasCandidate;
        private Int2 _candidateOriginCell;
        private bool _candidateValid;

        // Locked placement
        private bool _isLocked;
        private bool _isPlacedOnBoard; // intersects rug
        private Int2 _lastLockedOriginCell;
        private Int2[] _lastLockedWorldCellsAll;   // global cells (for occupancy)
        private Int2[] _lastLockedWorldCellsInRug; // global cells intersecting rug (for UI)
        private Int2[] _lastLockedRugLocalCells;   // rug-local cells (UI subset)

        // Scratch
        private Int2[] _scratchWorldCells;
        private Int2[] _scratchWorldCellsInRug;
        private Int2[] _scratchInRugCells;
        private int _scratchInRugCount;

        public int RotationCW => _rotationCW;
        public Int2[] LocalCells => _localCells;
        public bool HasCandidate => _hasCandidate;
        public Int2 CandidateOriginCell => _candidateOriginCell;
        public bool CandidateValid => _candidateValid;

        public bool IsLocked => _isLocked;
        public bool IsPlacedOnBoard => _isPlacedOnBoard;

        // Test/back-compat helpers (do not mutate state)
        public bool HasLockedPlacement => _isLocked;
        public Int2 LastLockedAnchorCell => _lastLockedOriginCell;
        public Int2[] LastLockedWorldCells => _lastLockedWorldCellsAll;
        public int LastLockedRugCellCount => _lastLockedWorldCellsInRug != null ? _lastLockedWorldCellsInRug.Length : 0;
        public Int2[] LastLockedRugCells => _lastLockedWorldCellsInRug;

        /// <summary>
        /// Cells intersecting the rug (rug-local coords). Use for UI only.
        /// </summary>
        public Int2[] LastPlacedWorldCells => _lastLockedRugLocalCells;

        /// <summary>
        /// Last locked anchor cell (global coords). Valid even when locked outside rug.
        /// </summary>
        public Int2 LastPlacedOriginCell => _lastLockedOriginCell;

        // Back-compat name used by controller.
        public Int2 LastPlacedOriginCell_BackCompat => _lastLockedOriginCell;

        public void Initialize(
            int globalWidth,
            int globalHeight,
            IEnumerable<Int2> blockedCellsGlobal,
            IEnumerable<Int2> occupiedCellsGlobal,
            Int2[] baseCells,
            Int2 pivot,
            Int2 rugOriginGlobal,
            int rugWidth,
            int rugHeight
        ) {
            _grid = new GridModel(globalWidth, globalHeight);
            _grid.SetBlocked(blockedCellsGlobal ?? Array.Empty<Int2>());
            if (occupiedCellsGlobal != null) {
                _grid.AddOccupied(occupiedCellsGlobal);
            }

            _rugOriginGlobal = rugOriginGlobal;
            _rugWidth = Math.Max(0, rugWidth);
            _rugHeight = Math.Max(0, rugHeight);

            _baseCells = baseCells ?? Array.Empty<Int2>();
            _pivot = pivot;
            _rotationCW = 0;
            RecomputeBaseOffsets();
            RecomputeLocalCells();

            _hasCandidate = false;
            _candidateValid = false;

            _isLocked = false;
            _isPlacedOnBoard = false;
            _lastLockedOriginCell = Int2.zero;
            _lastLockedWorldCellsAll = null;
            _lastLockedWorldCellsInRug = null;
            _lastLockedRugLocalCells = null;

            EnsureScratch(_localCells != null ? _localCells.Length : 0);
        }

        /// <summary>
        /// Initialize using a rug rectangle in global grid-space.
        /// (This overload exists so tests can pass a named argument: rugRect: ...)
        /// </summary>
        public void Initialize(
            int globalWidth,
            int globalHeight,
            IEnumerable<Int2> blockedCellsGlobal,
            IEnumerable<Int2> occupiedCellsGlobal,
            Int2[] baseCells,
            Int2 pivot,
            IntRect rugRect
        ) {
            Initialize(
                globalWidth,
                globalHeight,
                blockedCellsGlobal,
                occupiedCellsGlobal,
                baseCells,
                pivot,
                rugRect.origin,
                rugRect.width,
                rugRect.height
            );
        }

        // Back-compat (treat rug == grid, origin == (0,0)).
        public void Initialize(
            int gridSize,
            IEnumerable<Int2> blockedCells,
            IEnumerable<Int2> occupiedCells,
            Int2[] baseCells,
            Int2 pivot
        ) {
            Initialize(
                gridSize,
                gridSize,
                blockedCells,
                occupiedCells,
                baseCells,
                pivot,
                Int2.zero,
                gridSize,
                gridSize
            );
        }

        /// <summary>
        /// Back-compat initialize with explicit rug rectangle.
        /// </summary>
        public void Initialize(
            int gridSize,
            IEnumerable<Int2> blockedCells,
            IEnumerable<Int2> occupiedCells,
            Int2[] baseCells,
            Int2 pivot,
            IntRect rugRect
        ) {
            Initialize(
                gridSize,
                gridSize,
                blockedCells,
                occupiedCells,
                baseCells,
                pivot,
                rugRect.origin,
                rugRect.width,
                rugRect.height
            );
        }

        public void SetShape(Int2[] baseCells, Int2 pivot) {
            _baseCells = baseCells ?? Array.Empty<Int2>();
            _pivot = pivot;
            RecomputeBaseOffsets();
            RecomputeLocalCells();
        }

        public void RotateCW() {
            _rotationCW = (_rotationCW + 1) & 3;
            RecomputeLocalCells();
        }

        public void SetRotationCW(int rotationCW) {
            _rotationCW = rotationCW & 3;
            RecomputeLocalCells();
        }

        public void ResetCandidate() {
            _hasCandidate = false;
            _candidateValid = false;
        }

        public void UpdateCandidate(Int2 approxCell, bool shouldSwitchCandidate) {
            if (_hasCandidate) {
                if (shouldSwitchCandidate) {
                    _candidateOriginCell = approxCell;
                }
            } else {
                _candidateOriginCell = approxCell;
                _hasCandidate = true;
            }

            _candidateValid = _grid.CanPlace(_candidateOriginCell, _localCells, out _);
        }

        public void OnPickup() {
            // When the piece is picked up from a locked placement, remove its occupancy so it can be moved/rotated.
            if (_isLocked && _lastLockedWorldCellsAll != null) {
                _grid.RemoveOccupied(_lastLockedWorldCellsAll, _lastLockedWorldCellsAll.Length);
            }
        }

        public void RestorePlacementIfAny() {
            if (_isLocked && _lastLockedWorldCellsAll != null) {
                _grid.AddOccupied(_lastLockedWorldCellsAll, _lastLockedWorldCellsAll.Length);
            }
        }

        public Int2[] GetCandidateWorldCells() {
            if (!_hasCandidate) return null;

            int count = _localCells != null ? _localCells.Length : 0;
            if (count == 0) return Array.Empty<Int2>();

            EnsureScratch(count);
            BuildWorldCells(_candidateOriginCell, count);
            return CloneScratchWorldCells(count);
        }

        public Int2[] PlaceCandidate() {
            int worldCount;
            var worldCells = PlaceCandidateReuse(out worldCount);
            if (worldCells == null) return null;

            if (worldCount == 0) return Array.Empty<Int2>();
            return CloneScratchWorldCells(worldCount);
        }

        public bool TryCommitPlacementAt(Int2 originCell, out Int2 firstInvalid) {
            if (!_grid.CanPlace(originCell, _localCells, out firstInvalid)) {
                return false;
            }

            int count = _localCells != null ? _localCells.Length : 0;
            if (count == 0) {
                _isLocked = true;
                _isPlacedOnBoard = false;
                _lastLockedOriginCell = originCell;
                _lastLockedWorldCellsAll = null;
                _lastLockedWorldCellsInRug = null;
                _lastLockedRugLocalCells = null;
                return true;
            }

            EnsureScratch(count);
            BuildWorldCells(originCell, count);

            // Occupy all cells globally.
            _grid.AddOccupied(_scratchWorldCells, count);

            StoreLastLockedAll(count);
            BuildInRugCells(count);
            StoreLastLockedInRug(_scratchInRugCount);

            _isLocked = true;
            _isPlacedOnBoard = _scratchInRugCount > 0;
            _lastLockedOriginCell = originCell;
            return true;
        }

        public Int2[] PlaceCandidateReuse(out int worldCount) {
            worldCount = 0;
            if (!_hasCandidate || !_candidateValid) return null;

            // CandidateValid is derived from CanPlace(). Still re-check at commit time for safety.
            if (!TryCommitPlacementAt(_candidateOriginCell, out _)) {
                return null;
            }

            worldCount = _localCells != null ? _localCells.Length : 0;
            return _scratchWorldCells;
        }

        private void RecomputeBaseOffsets() {
            if (_baseCells == null || _baseCells.Length == 0) {
                _baseOffsets = Array.Empty<Int2>();
                return;
            }

            _baseOffsets = new Int2[_baseCells.Length];
            for (int i = 0; i < _baseCells.Length; i++) {
                _baseOffsets[i] = _baseCells[i] - _pivot;
            }
        }

        private void RecomputeLocalCells() {
            if (_baseCells == null || _baseCells.Length == 0) {
                _localCells = new[] { Int2.zero };
                EnsureScratch(_localCells.Length);
                return;
            }

            _localCells = ShapeRotation.GetRotatedOffsets(_baseOffsets, _rotationCW);
            EnsureScratch(_localCells.Length);
        }

        private void EnsureScratch(int size) {
            if (size <= 0) size = 1;

            if (_scratchWorldCells == null || _scratchWorldCells.Length != size) {
                _scratchWorldCells = new Int2[size];
            }
            if (_scratchWorldCellsInRug == null || _scratchWorldCellsInRug.Length != size) {
                _scratchWorldCellsInRug = new Int2[size];
            }
            if (_scratchInRugCells == null || _scratchInRugCells.Length != size) {
                _scratchInRugCells = new Int2[size];
            }
        }

        private void BuildWorldCells(Int2 originCell, int count) {
            for (int i = 0; i < count; i++) {
                _scratchWorldCells[i] = originCell + _localCells[i];
            }
        }

        private void BuildInRugCells(int count) {
            _scratchInRugCount = 0;
            if (_rugWidth <= 0 || _rugHeight <= 0) return;

            for (int i = 0; i < count; i++) {
                var world = _scratchWorldCells[i];
                if (!IsInsideRug(world)) {
                    continue;
                }

                // Store both global and rug-local coordinates
                _scratchWorldCellsInRug[_scratchInRugCount] = world;
                _scratchInRugCells[_scratchInRugCount] = world - _rugOriginGlobal;
                _scratchInRugCount++;
            }
        }

        private bool IsInsideRug(Int2 globalCell) {
            int rx = _rugOriginGlobal.x;
            int ry = _rugOriginGlobal.y;
            return globalCell.x >= rx && globalCell.x < rx + _rugWidth
                && globalCell.y >= ry && globalCell.y < ry + _rugHeight;
        }

        private Int2[] CloneScratchWorldCells(int count) {
            var result = new Int2[count];
            for (int i = 0; i < count; i++) {
                result[i] = _scratchWorldCells[i];
            }
            return result;
        }

        private void StoreLastLockedAll(int count) {
            if (_lastLockedWorldCellsAll == null || _lastLockedWorldCellsAll.Length != count) {
                _lastLockedWorldCellsAll = new Int2[count];
            }
            for (int i = 0; i < count; i++) {
                _lastLockedWorldCellsAll[i] = _scratchWorldCells[i];
            }
        }

        private void StoreLastLockedInRug(int count) {
            if (count <= 0) {
                _lastLockedWorldCellsInRug = null;
                _lastLockedRugLocalCells = null;
                return;
            }

            if (_lastLockedWorldCellsInRug == null || _lastLockedWorldCellsInRug.Length != count) {
                _lastLockedWorldCellsInRug = new Int2[count];
            }
            if (_lastLockedRugLocalCells == null || _lastLockedRugLocalCells.Length != count) {
                _lastLockedRugLocalCells = new Int2[count];
            }
            for (int i = 0; i < count; i++) {
                _lastLockedWorldCellsInRug[i] = _scratchWorldCellsInRug[i];
                _lastLockedRugLocalCells[i] = _scratchInRugCells[i];
            }
        }
    }
}
