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

        public int RotationCW => _rotationCW;
        public Int2[] LocalCells => _localCells;
        public bool HasCandidate => _hasCandidate;
        public Int2 CandidateOriginCell => _candidateOriginCell;
        public bool CandidateValid => _candidateValid;
        public bool IsPlacedOnBoard => _isPlacedOnBoard;
        public Int2[] LastPlacedWorldCells => _lastPlacedWorldCells;

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
            _grid.AddOccupied(worldCells);

            _lastPlacedWorldCells = worldCells;
            _isPlacedOnBoard = true;
            return worldCells;
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