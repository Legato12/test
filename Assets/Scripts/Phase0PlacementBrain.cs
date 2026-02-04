// Phase0PlacementBrain.cs
// Pure C# placement/rotation brain (no MonoBehaviour). Unity layer provides world mapping + visuals.

using System;
using System.Collections.Generic;

namespace Phase0
{
    public sealed class Phase0PlacementBrain
    {
        private GridModel _grid;
        private Int2[] _baseCells = new Int2[0];
        private Int2 _pivot;

        private int _rotationCW;
        private Int2[] _localCells;

        private bool _hasCandidate;
        private Int2 _candidateOriginCell;
        private bool _candidateValid;

        private bool _isPlacedOnBoard;
        private Int2[] _lastPlacedWorldCells;
        private Int2 _lastPlacedOriginCell;

        private Int2[] _scratchWorldCells;
        private Int2[] _scratchInBoardCells;
        private int _scratchInBoardCount;

        public int RotationCW => _rotationCW;
        public Int2[] LocalCells => _localCells;
        public bool HasCandidate => _hasCandidate;
        public Int2 CandidateOriginCell => _candidateOriginCell;
        public bool CandidateValid => _candidateValid;
        public bool IsPlacedOnBoard => _isPlacedOnBoard;
        public Int2[] LastPlacedWorldCells => _lastPlacedWorldCells;
        public Int2 LastPlacedOriginCell => _lastPlacedOriginCell;

        public void Initialize(int gridSize,
            IEnumerable<Int2> blockedCells,
            IEnumerable<Int2> occupiedCells,
            Int2[] baseCells,
            Int2 pivot)
        {
            _grid = new GridModel(gridSize);
            _grid.SetBlocked(blockedCells ?? new List<Int2>());
            if (occupiedCells != null) _grid.AddOccupied(occupiedCells);

            _baseCells = baseCells ?? new Int2[0];
            _pivot = pivot;
            _rotationCW = 0;
            RecomputeLocalCells();

            _hasCandidate = false;
            _candidateValid = false;
            _isPlacedOnBoard = false;
            _lastPlacedWorldCells = null;
            _lastPlacedOriginCell = Int2.zero;

            EnsureScratch(_localCells != null ? _localCells.Length : 0);
        }

        public void SetShape(Int2[] baseCells, Int2 pivot)
        {
            _baseCells = baseCells ?? new Int2[0];
            _pivot = pivot;
            RecomputeLocalCells();
        }

        public void RotateCW()
        {
            _rotationCW = (_rotationCW + 1) & 3;
            RecomputeLocalCells();
        }

        public void SetRotationCW(int rotationCW)
        {
            _rotationCW = rotationCW & 3;
            RecomputeLocalCells();
        }

        public void ResetCandidate()
        {
            _hasCandidate = false;
            _candidateValid = false;
        }

        public void UpdateCandidate(Int2 approxCell, bool shouldSwitchCandidate)
        {
            if (_hasCandidate)
            {
                if (shouldSwitchCandidate)
                    _candidateOriginCell = approxCell;
            }
            else
            {
                _candidateOriginCell = approxCell;
                _hasCandidate = true;
            }

            _candidateValid = _grid.CanPlace(_candidateOriginCell, _localCells, out _);
        }

        public void OnPickup()
        {
            if (_isPlacedOnBoard && _lastPlacedWorldCells != null)
                _grid.RemoveOccupied(_lastPlacedWorldCells);
        }

        public void RestorePlacementIfAny()
        {
            if (_isPlacedOnBoard && _lastPlacedWorldCells != null)
                _grid.AddOccupied(_lastPlacedWorldCells);
        }

        public Int2[] GetCandidateWorldCells()
        {
            if (!_hasCandidate) return null;

            int count = _localCells != null ? _localCells.Length : 0;
            if (count == 0) return Array.Empty<Int2>();

            EnsureScratch(count);
            BuildWorldCells(_candidateOriginCell, count);
            return CloneScratchWorldCells(count);
        }

        public Int2[] PlaceCandidate()
        {
            int worldCount;
            var worldCells = PlaceCandidateReuse(out worldCount);
            if (worldCells == null) return null;

            if (worldCount == 0) return Array.Empty<Int2>();

            return CloneScratchWorldCells(worldCount);
        }

        public bool TryCommitPlacementAt(Int2 originCell, out Int2 firstInvalid)
        {
            if (!_grid.CanPlace(originCell, _localCells, out firstInvalid))
                return false;

            int count = _localCells != null ? _localCells.Length : 0;
            if (count == 0)
            {
                _lastPlacedWorldCells = null;
                _isPlacedOnBoard = false;
                _lastPlacedOriginCell = originCell;
                return true;
            }

            EnsureScratch(count);
            BuildWorldCells(originCell, count);
            BuildInBoardCells(count);

            if (_scratchInBoardCount > 0)
            {
                _grid.AddOccupied(_scratchInBoardCells, _scratchInBoardCount);
                StoreLastPlacedFromScratch(_scratchInBoardCount);
                _isPlacedOnBoard = true;
            }
            else
            {
                _lastPlacedWorldCells = null;
                _isPlacedOnBoard = false;
            }

            _lastPlacedOriginCell = originCell;
            return true;
        }

        private void RecomputeLocalCells()
        {
            if (_baseCells == null || _baseCells.Length == 0)
            {
                _localCells = new[] { Int2.zero };
                EnsureScratch(_localCells.Length);
                return;
            }

            _localCells = ShapeRotation.GetRotated(_baseCells, _pivot, _rotationCW);
            EnsureScratch(_localCells.Length);
        }

        private void EnsureScratch(int size)
        {
            if (size <= 0) size = 1;

            if (_scratchWorldCells == null || _scratchWorldCells.Length != size)
            {
                _scratchWorldCells = new Int2[size];
            }

            if (_scratchInBoardCells == null || _scratchInBoardCells.Length != size)
            {
                _scratchInBoardCells = new Int2[size];
            }
        }

        private void BuildWorldCells(Int2 originCell, int count)
        {
            for (int i = 0; i < count; i++)
            {
                _scratchWorldCells[i] = originCell + _localCells[i];
            }

        }

        private void BuildInBoardCells(int count)
        {
            _scratchInBoardCount = 0;
            for (int i = 0; i < count; i++)
            {
                var world = _scratchWorldCells[i];
                if (!_grid.IsInside(world))
                    continue;

                _scratchInBoardCells[_scratchInBoardCount] = world;
                _scratchInBoardCount++;
            }
        }

        private Int2[] CloneScratchWorldCells(int count)
        {
            var result = new Int2[count];
            for (int i = 0; i < count; i++)
            {
                result[i] = _scratchWorldCells[i];
            }
            return result;
        }

        private void StoreLastPlacedFromScratch(int count)
        {
            if (_lastPlacedWorldCells == null || _lastPlacedWorldCells.Length != count)
            {
                _lastPlacedWorldCells = new Int2[count];
            }

            for (int i = 0; i < count; i++)
            {
                _lastPlacedWorldCells[i] = _scratchInBoardCells[i];
            }
        }

        public Int2[] PlaceCandidateReuse(out int worldCount)
        {
            worldCount = 0;
            if (!_hasCandidate || !_candidateValid) return null;

            int count = _localCells != null ? _localCells.Length : 0;
            if (count == 0) return Array.Empty<Int2>();

            EnsureScratch(count);
            BuildWorldCells(_candidateOriginCell, count);
            BuildInBoardCells(count);

            if (_scratchInBoardCount > 0)
            {
                _grid.AddOccupied(_scratchInBoardCells, _scratchInBoardCount);
                StoreLastPlacedFromScratch(_scratchInBoardCount);
                _isPlacedOnBoard = true;
            }
            else
            {
                _lastPlacedWorldCells = null;
                _isPlacedOnBoard = false;
            }

            _lastPlacedOriginCell = _candidateOriginCell;
            worldCount = count;
            return _scratchWorldCells;
        }
    }
}