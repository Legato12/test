// Phase0PlacementBrain.cs
// Pure C# placement/rotation brain (no MonoBehaviour). Unity layer provides world mapping + visuals.

using System.Collections.Generic;
using System.Linq;

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
            return _localCells.Select(c => _candidateOriginCell + c).ToArray();
        }

        public Int2[] PlaceCandidate()
        {
            if (!_hasCandidate || !_candidateValid) return null;

            var worldCells = _localCells.Select(c => _candidateOriginCell + c).ToArray();
            var inBoardCells = worldCells.Where(_grid.IsInside).ToArray();
            if (inBoardCells.Length > 0)
            {
                _grid.AddOccupied(inBoardCells);
                _lastPlacedWorldCells = inBoardCells;
                _isPlacedOnBoard = true;
            }
            else
            {
                _lastPlacedWorldCells = null;
                _isPlacedOnBoard = false;
            }

            _lastPlacedOriginCell = _candidateOriginCell;
            return worldCells;
        }

        public bool TryCommitPlacementAt(Int2 originCell, out Int2 firstInvalid)
        {
            if (!_grid.CanPlace(originCell, _localCells, out firstInvalid))
                return false;

            var worldCells = _localCells.Select(c => originCell + c).ToArray();
            var inBoardCells = worldCells.Where(_grid.IsInside).ToArray();
            if (inBoardCells.Length > 0)
            {
                _grid.AddOccupied(inBoardCells);
                _lastPlacedWorldCells = inBoardCells;
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
                return;
            }

            _localCells = ShapeRotation.GetRotated(_baseCells, _pivot, _rotationCW);
        }
    }
}